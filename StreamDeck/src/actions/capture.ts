import { action, type KeyAction, type KeyUpEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";
import type { HubClient } from "../hub.js";
import { type KeyArt, renderKey, toDataUrl } from "../key-art.js";

const COLOR = "#33E1FF";

type Entry = {
	action: KeyAction;
	lastImage?: string;
};

/**
 * Quick Capture: tap to get a task out of your head without leaving what you're doing. BijouHub
 * opens a small box on top; type, Enter, and it's on today's list. Shows how many are left.
 */
@action({ UUID: "com.bijounga.bijouhub.capture" })
export class CaptureAction extends SingletonAction {
	readonly #hub: HubClient;
	readonly #keys = new Map<string, Entry>();

	constructor(hub: HubClient) {
		super();
		this.#hub = hub;
		hub.onChange(() => this.#renderAll());
	}

	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (!ev.action.isKey()) return;
		const entry: Entry = { action: ev.action };
		this.#keys.set(ev.action.id, entry);
		await this.#render(entry);
	}

	override onWillDisappear(ev: WillDisappearEvent): void {
		this.#keys.delete(ev.action.id);
	}

	override async onKeyUp(ev: KeyUpEvent): Promise<void> {
		const entry = this.#keys.get(ev.action.id);
		if (!entry) return;
		if (!(await this.#hub.ensureRunning()) || (await this.#hub.request("capture")).error) await entry.action.showAlert();
	}

	#renderAll(): void {
		for (const entry of this.#keys.values()) void this.#render(entry);
	}

	async #render(entry: Entry): Promise<void> {
		const image = toDataUrl(renderKey(this.#art()));
		if (image === entry.lastImage) return;
		entry.lastImage = image;
		await entry.action.setImage(image);
	}

	#art(): KeyArt {
		const goals = this.#hub.connected ? this.#hub.goals : null;
		const caption = goals ? (goals.open === 0 ? "all done" : `${goals.open} to do`) : "new task";
		return { color: COLOR, fraction: null, label: "CAPTURE", big: "+", caption };
	}
}
