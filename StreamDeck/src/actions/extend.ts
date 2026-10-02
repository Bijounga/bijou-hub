import { action, type DidReceiveSettingsEvent, type KeyAction, type KeyUpEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";
import type { HubClient } from "../hub.js";
import { type KeyArt, renderKey, toDataUrl } from "../key-art.js";

export type ExtendSettings = {
	/** Minutes to add, as typed. Defaults to 15. */
	minutes?: string;
};

const COLOR = "#33E1FF";
const ADDED_COLOR = "#4ADE80";
const FLASH_MS = 1400;

type Entry = {
	action: KeyAction<ExtendSettings>;
	settings: ExtendSettings;
	flash?: { art: KeyArt; until: number };
	lastImage?: string;
};

function minutesOf(settings: ExtendSettings): number {
	const value = Number.parseInt(settings.minutes ?? "", 10);
	return Number.isFinite(value) && value > 0 && value <= 240 ? value : 15;
}

/**
 * Adds time to the running session — more countdown, or a countdown from now if it was
 * counting up. Clears BijouHub's "time's up" alert. Dimmed while nothing is running.
 */
@action({ UUID: "com.bijounga.bijouhub.extend" })
export class ExtendAction extends SingletonAction<ExtendSettings> {
	readonly #hub: HubClient;
	readonly #keys = new Map<string, Entry>();

	constructor(hub: HubClient) {
		super();
		this.#hub = hub;
		hub.onChange(() => this.#renderAll());
	}

	override async onWillAppear(ev: WillAppearEvent<ExtendSettings>): Promise<void> {
		if (!ev.action.isKey()) return;
		const entry: Entry = { action: ev.action, settings: { ...ev.payload.settings } };
		this.#keys.set(ev.action.id, entry);
		await this.#render(entry);
	}

	override onWillDisappear(ev: WillDisappearEvent<ExtendSettings>): void {
		this.#keys.delete(ev.action.id);
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<ExtendSettings>): Promise<void> {
		const entry = this.#keys.get(ev.action.id);
		if (!entry) return;
		entry.settings = { ...ev.payload.settings };
		await this.#render(entry);
	}

	override async onKeyUp(ev: KeyUpEvent<ExtendSettings>): Promise<void> {
		const entry = this.#keys.get(ev.action.id);
		if (!entry) return;
		if (!this.#hub.connected || !this.#hub.state?.active) {
			await entry.action.showAlert();
			return;
		}

		const minutes = minutesOf(entry.settings);
		const reply = await this.#hub.request("extend", { minutes });
		if (reply.error) {
			await entry.action.showAlert();
			return;
		}
		entry.flash = {
			art: { color: ADDED_COLOR, fraction: 1, label: "ADDED", check: true, big: "", caption: `+${minutes} min` },
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

		const running = this.#hub.connected && !!this.#hub.state?.active;
		return { color: COLOR, fraction: null, label: "ADD TIME", big: `+${minutesOf(entry.settings)}`, caption: "minutes", dim: !running };
	}
}
