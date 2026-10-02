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
import { AWAY_COLOR, type KeyArt, OVER_COLOR, PAUSED_COLOR, renderKey, toDataUrl } from "../key-art.js";

export type TimerSettings = {
	/** Stable id for this key, so BijouHub can say which key owns the running session. */
	keyId?: string;
	modeId?: string;
	modeName?: string;
	projectId?: string;
	projectName?: string;
	/** As typed: "45", "1:30", "2h". Empty counts up. */
	duration?: string;
	color?: string;
};

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
 */
@action({ UUID: "com.bijounga.bijouhub.timer" })
export class TimerAction extends SingletonAction<TimerSettings> {
	readonly #hub: HubClient;
	readonly #keys = new Map<string, KeyEntry>();

	constructor(hub: HubClient) {
		super();
		this.#hub = hub;
		hub.onChange(() => this.#renderAll());
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

		const minutes = parseDuration(settings.duration);
		if (minutes === undefined) {
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
				minutes
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

		const minutes = parseDuration(settings.duration);
		if (minutes === undefined) {
			return { color: OVER_COLOR, fraction: null, big: "?", label: "DURATION", caption: settings.duration };
		}

		const preset = minutes === null ? "0:00" : formatPreset(minutes);
		if (entry.starting) {
			return { color, fraction: null, label: "STARTING", big: preset, caption: caption(settings) };
		}

		const state = this.#hub.state;
		if (!state || !this.#ownsSession(entry)) {
			const otherRunning = this.#hub.connected && !!state?.active;
			return { color, fraction: 1, play: true, big: preset, caption: caption(settings), dim: otherRunning };
		}

		// This key's session is live.
		const status = state.paused ? { color: PAUSED_COLOR, label: "PAUSED" } : state.idle ? { color: AWAY_COLOR, label: "AWAY" } : null;

		if (state.targetSeconds === null) {
			return {
				color: status?.color ?? color,
				fraction: (state.activeSeconds % 3600) / 3600,
				label: status?.label ?? "ELAPSED",
				labelColor: status ? undefined : color,
				big: formatClock(state.activeSeconds),
				caption: caption(settings)
			};
		}

		const remaining = state.targetSeconds - state.activeSeconds;
		if (remaining <= 0) {
			return {
				color: status?.color ?? OVER_COLOR,
				fraction: 1,
				label: status?.label ?? "OVER",
				big: "+" + formatClock(-remaining),
				caption: caption(settings)
			};
		}

		return {
			color: status?.color ?? color,
			fraction: remaining / state.targetSeconds,
			label: status?.label,
			big: formatClock(remaining),
			caption: caption(settings)
		};
	}
}

function caption(settings: TimerSettings): string {
	return settings.modeName || settings.projectName || "BijouHub";
}
