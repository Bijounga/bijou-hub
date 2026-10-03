import { randomUUID } from "node:crypto";
import streamDeck, {
	action,
	type DidReceiveSettingsEvent,
	type KeyAction,
	type KeyDownEvent,
	type KeyUpEvent,
	type SendToPluginEvent,
	SingletonAction,
	type WillAppearEvent,
	type WillDisappearEvent
} from "@elgato/streamdeck";
import { formatClock, formatPreset, parseDuration } from "../duration.js";
import type { HubClient } from "../hub.js";
import { type KeyArt, OVER_COLOR, renderKey, toDataUrl } from "../key-art.js";
import { liveSessionArt } from "../session-art.js";

export type TimerSettings = {
	/** Stable id for this key, so BijouHub can say which key owns the running session. */
	keyId?: string;
	modeId?: string;
	modeName?: string;
	projectId?: string;
	projectName?: string;
	/** As typed: "45", "1:30", "2h". Empty counts up (on a Pomodoro key: 25 minutes of focus). */
	duration?: string;
	/** Pomodoro keys only: the break, as typed. Empty is 5 minutes. */
	breakDuration?: string;
	color?: string;
};

/** What a key starts: a countdown (or count-up when minutes is null), or a Pomodoro with a break. */
type Lengths = { minutes: number | null; breakMinutes?: number };

const POMODORO_FOCUS = 25;
const POMODORO_BREAK = 5;

const DEFAULT_COLOR = "#33E1FF";
const LOGGED_COLOR = "#4ADE80";
const NEUTRAL_COLOR = "#8A93A6";
const HOLD_MS = 600;
const FLASH_MS = 1800;

type KeyEntry = {
	action: KeyAction<TimerSettings>;
	settings: TimerSettings;
	holdTimer?: NodeJS.Timeout;
	held: boolean;
	starting: boolean;
	flash?: { art: KeyArt; until: number };
	lastImage?: string;
};

/**
 * A timer key: tap to start a BijouHub session in the key's mode with its duration, tap again
 * to pause/resume, hold to finish and log it. The running key draws a live countdown ring.
 * The Pomodoro key is the same key with a break: focus, break, repeat.
 */
class TimerKey extends SingletonAction<TimerSettings> {
	readonly #hub: HubClient;
	readonly #pomodoro: boolean;
	readonly #keys = new Map<string, KeyEntry>();

	constructor(hub: HubClient, pomodoro: boolean) {
		super();
		this.#hub = hub;
		this.#pomodoro = pomodoro;
		hub.onChange(() => this.#renderAll());
	}

	/** Undefined when something typed in the key's settings can't be read. */
	#lengths(settings: TimerSettings): Lengths | undefined {
		const minutes = parseDuration(settings.duration);
		if (!this.#pomodoro) return minutes === undefined ? undefined : { minutes };

		const rest = parseDuration(settings.breakDuration);
		if (minutes === undefined || rest === undefined) return undefined;
		return { minutes: minutes ?? POMODORO_FOCUS, breakMinutes: rest ?? POMODORO_BREAK };
	}

	override async onWillAppear(ev: WillAppearEvent<TimerSettings>): Promise<void> {
		if (!ev.action.isKey()) return;

		const settings = { ...ev.payload.settings };
		// A duplicated key arrives with the original's keyId — give the copy its own.
		const taken = [...this.#keys.values()].some((k) => k.settings.keyId === settings.keyId);
		if (!settings.keyId || taken) {
			settings.keyId = randomUUID();
			await ev.action.setSettings(settings);
		}

		const entry: KeyEntry = { action: ev.action, settings, held: false, starting: false };
		this.#keys.set(ev.action.id, entry);
		await this.#render(entry);
	}

	override onWillDisappear(ev: WillDisappearEvent<TimerSettings>): void {
		const entry = this.#keys.get(ev.action.id);
		if (entry?.holdTimer) clearTimeout(entry.holdTimer);
		this.#keys.delete(ev.action.id);
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<TimerSettings>): Promise<void> {
		const entry = this.#keys.get(ev.action.id);
		if (!entry) return;

		const settings = { ...ev.payload.settings };
		// The settings panel can save a copy that predates our keyId; keep the id stable.
		if (!settings.keyId && entry.settings.keyId) {
			settings.keyId = entry.settings.keyId;
			await entry.action.setSettings(settings);
		}
		entry.settings = settings;
		entry.lastImage = undefined;
		await this.#render(entry);
	}

	override onKeyDown(ev: KeyDownEvent<TimerSettings>): void {
		const entry = this.#keys.get(ev.action.id);
		if (!entry) return;

		entry.held = false;
		if (!this.#ownsSession(entry)) return;

		// Holding the running key finishes the session — fires while still held, so it feels immediate.
		entry.holdTimer = setTimeout(() => {
			entry.held = true;
			entry.holdTimer = undefined;
			void this.#finish(entry);
		}, HOLD_MS);
	}

	override async onKeyUp(ev: KeyUpEvent<TimerSettings>): Promise<void> {
		const entry = this.#keys.get(ev.action.id);
		if (!entry) return;

		if (entry.holdTimer) {
			clearTimeout(entry.holdTimer);
			entry.holdTimer = undefined;
		}
		if (entry.held) {
			entry.held = false;
			return;
		}

		await this.#press(entry);
	}

	override async onSendToPlugin(ev: SendToPluginEvent<{ event?: string }, TimerSettings>): Promise<void> {
		const event = ev.payload?.event;
		if (event === "launchHub") {
			await this.#hub.ensureRunning();
		} else if (event !== "getCatalog") {
			return;
		}

		const reply = await this.#hub.request("catalog");
		await streamDeck.ui.sendToPropertyInspector({
			event: "catalog",
			connected: this.#hub.connected,
			modes: (reply.modes as { id: string; name: string }[] | undefined) ?? [],
			projects: (reply.projects as { id: string; name: string }[] | undefined) ?? []
		});
	}

	async #press(entry: KeyEntry): Promise<void> {
		const { settings } = entry;
		if (!settings.modeId && !settings.projectId) {
			await entry.action.showAlert();
			return;
		}

		const lengths = this.#lengths(settings);
		if (lengths === undefined) {
			await entry.action.showAlert();
			return;
		}

		if (this.#ownsSession(entry)) {
			await this.#hub.request("pause");
			return;
		}

		entry.starting = true;
		await this.#render(entry);
		try {
			if (!(await this.#hub.ensureRunning())) {
				streamDeck.logger.warn("BijouHub isn't running and couldn't be launched");
				await entry.action.showAlert();
				return;
			}

			const reply = await this.#hub.request("start", {
				keyId: settings.keyId,
				modeId: settings.modeId,
				modeName: settings.modeName,
				projectId: settings.projectId,
				minutes: lengths.minutes,
				breakMinutes: lengths.breakMinutes
			});
			if (reply.error) {
				streamDeck.logger.warn(`Start refused: ${reply.error}`);
				await entry.action.showAlert();
			}
		} finally {
			entry.starting = false;
			await this.#render(entry);
		}
	}

	async #finish(entry: KeyEntry): Promise<void> {
		const logged = this.#hub.state?.activeSeconds ?? 0;
		const reply = await this.#hub.request("finish");
		if (reply.error) {
			await entry.action.showAlert();
			return;
		}

		entry.flash = {
			art: { color: LOGGED_COLOR, fraction: 1, label: "LOGGED", big: formatClock(logged), caption: caption(entry.settings) },
			until: Date.now() + FLASH_MS
		};
		await this.#render(entry);
		setTimeout(() => void this.#render(entry), FLASH_MS + 50);
	}

	#ownsSession(entry: KeyEntry): boolean {
		const state = this.#hub.state;
		return this.#hub.connected && !!state?.active && !!state.keyId && state.keyId === entry.settings.keyId;
	}

	#renderAll(): void {
		for (const entry of this.#keys.values()) void this.#render(entry);
	}

	async #render(entry: KeyEntry): Promise<void> {
		const image = toDataUrl(renderKey(this.#art(entry)));
		if (image === entry.lastImage) return;
		entry.lastImage = image;
		await entry.action.setImage(image);
	}

	#art(entry: KeyEntry): KeyArt {
		const { settings } = entry;
		const color = settings.color || DEFAULT_COLOR;

		if (entry.flash && Date.now() < entry.flash.until) return entry.flash.art;
		entry.flash = undefined;

		if (!settings.modeId && !settings.projectId) {
			return { color: NEUTRAL_COLOR, fraction: null, big: "SET UP", caption: "pick a mode" };
		}

		const lengths = this.#lengths(settings);
		if (lengths === undefined) {
			return { color: OVER_COLOR, fraction: null, big: "?", label: "DURATION", caption: settings.duration || settings.breakDuration };
		}

		const preset =
			lengths.breakMinutes !== undefined ? `${lengths.minutes}/${lengths.breakMinutes}`
			: lengths.minutes === null ? "0:00"
			: formatPreset(lengths.minutes);
		if (entry.starting) {
			return { color, fraction: null, label: "STARTING", big: preset, caption: caption(settings) };
		}

		const state = this.#hub.state;
		if (!state || !this.#ownsSession(entry)) {
			const otherRunning = this.#hub.connected && !!state?.active;
			return { color, fraction: 1, play: true, big: preset, caption: caption(settings), dim: otherRunning };
		}

		// This key's session is live.
		return liveSessionArt(state, color, caption(settings));
	}
}

@action({ UUID: "com.bijounga.bijouhub.timer" })
export class TimerAction extends TimerKey {
	constructor(hub: HubClient) {
		super(hub, false);
	}
}

@action({ UUID: "com.bijounga.bijouhub.pomodoro" })
export class PomodoroAction extends TimerKey {
	constructor(hub: HubClient) {
		super(hub, true);
	}
}

function caption(settings: TimerSettings): string {
	return settings.modeName || settings.projectName || "BijouHub";
}
