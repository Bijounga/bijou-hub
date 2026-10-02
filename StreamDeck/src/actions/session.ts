import type { JsonObject } from "@elgato/utils";
import streamDeck, { action, type KeyAction, type KeyDownEvent, type KeyUpEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";
import { formatClock } from "../duration.js";
import type { HubClient } from "../hub.js";
import { type KeyArt, renderKey, toDataUrl } from "../key-art.js";
import { formatSpan, liveSessionArt } from "../session-art.js";

const COLOR = "#33E1FF";
const LOGGED_COLOR = "#4ADE80";
const NEUTRAL_COLOR = "#8A93A6";
const HOLD_MS = 600;
const FLASH_MS = 1800;

type Entry = {
	action: KeyAction<JsonObject>;
	holdTimer?: NodeJS.Timeout;
	held: boolean;
	starting: boolean;
	flash?: { art: KeyArt; until: number };
	lastImage?: string;
};

/**
 * Whatever session is running, wherever it was started (the app or any Timer key): live
 * countdown, tap to pause/resume, hold to finish. With nothing running it shows today's total,
 * and a tap brings BijouHub forward.
 */
@action({ UUID: "com.bijounga.bijouhub.session" })
export class SessionAction extends SingletonAction {
	readonly #hub: HubClient;
	readonly #keys = new Map<string, Entry>();

	constructor(hub: HubClient) {
		super();
		this.#hub = hub;
		hub.onChange(() => this.#renderAll());
	}

	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (!ev.action.isKey()) return;
		const entry: Entry = { action: ev.action, held: false, starting: false };
		this.#keys.set(ev.action.id, entry);
		await this.#render(entry);
	}

	override onWillDisappear(ev: WillDisappearEvent): void {
		const entry = this.#keys.get(ev.action.id);
		if (entry?.holdTimer) clearTimeout(entry.holdTimer);
		this.#keys.delete(ev.action.id);
	}

	override onKeyDown(ev: KeyDownEvent): void {
		const entry = this.#keys.get(ev.action.id);
		if (!entry) return;
		entry.held = false;
		if (!this.#hub.connected || !this.#hub.state?.active) return;

		entry.holdTimer = setTimeout(() => {
			entry.held = true;
			entry.holdTimer = undefined;
			void this.#finish(entry);
		}, HOLD_MS);
	}

	override async onKeyUp(ev: KeyUpEvent): Promise<void> {
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

		if (!this.#hub.connected) {
			entry.starting = true;
			await this.#render(entry);
			const running = await this.#hub.ensureRunning();
			entry.starting = false;
			if (!running) await entry.action.showAlert();
			await this.#render(entry);
			return;
		}

		const reply = await this.#hub.request(this.#hub.state?.active ? "pause" : "focus");
		if (reply.error) {
			streamDeck.logger.warn(`Session key: ${reply.error}`);
			await entry.action.showAlert();
		}
	}

	async #finish(entry: Entry): Promise<void> {
		const logged = this.#hub.state?.activeSeconds ?? 0;
		const title = this.#hub.state?.title ?? "Session";
		const reply = await this.#hub.request("finish");
		if (reply.error) {
			await entry.action.showAlert();
			return;
		}
		entry.flash = {
			art: { color: LOGGED_COLOR, fraction: 1, label: "LOGGED", big: formatClock(logged), caption: title },
			until: Date.now() + FLASH_MS
		};
		await this.#render(entry);
		setTimeout(() => void this.#render(entry), FLASH_MS + 50);
	}

	#renderAll(): void {
		for (const entry of this.#keys.values()) void this.#render(entry);
	}

	async #render(entry: Entry): Promise<void> {
		const image = toDataUrl(renderKey(this.#art(entry)));
		if (image === entry.lastImage) return;
		entry.lastImage = image;
		await entry.action.setImage(image);
	}

	#art(entry: Entry): KeyArt {
		if (entry.flash && Date.now() < entry.flash.until) return entry.flash.art;
		entry.flash = undefined;

		if (entry.starting) return { color: COLOR, fraction: null, label: "STARTING", big: "…", caption: "BijouHub" };

		const state = this.#hub.state;
		if (!this.#hub.connected || !state) {
			return { color: NEUTRAL_COLOR, fraction: null, label: "OFFLINE", big: "—", caption: "tap to open" };
		}

		if (state.active) return liveSessionArt(state, COLOR, state.title ?? "Session");

		return { color: COLOR, fraction: null, label: "TODAY", big: formatSpan(state.todaySeconds), caption: "no session" };
	}
}
