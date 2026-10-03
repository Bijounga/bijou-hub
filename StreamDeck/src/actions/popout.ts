import { action, type KeyAction, type KeyUpEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";
import type { HubClient } from "../hub.js";
import { type KeyArt, renderKey, toDataUrl } from "../key-art.js";

const COLOR = "#33E1FF";

type Entry = {
	action: KeyAction;
	lastImage?: string;
};

/**
 * Pops BijouHub's mini timer out (a small always-on-top window, handy while editing in another
 * app) and docks it again. Shows which way the next tap goes; dimmed while nothing is running.
 */
@action({ UUID: "com.bijounga.bijouhub.popout" })
export class PopoutAction extends SingletonAction {
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
		if (!this.#hub.connected || !this.#hub.state?.active) {
			await entry.action.showAlert();
			return;
		}
		const reply = await this.#hub.request("popout");
		if (reply.error) await entry.action.showAlert();
		// The new state comes back in the reply and the broadcast; the key redraws from that.
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
		const state = this.#hub.connected ? this.#hub.state : null;
		if (!state?.active) return { color: COLOR, fraction: null, glyph: "popout", label: "POP OUT", big: "", caption: "no session", dim: true };
		if (state.poppedOut) return { color: COLOR, fraction: 1, glyph: "dock", label: "DOCK", big: "", caption: state.title ?? "timer" };
		return { color: COLOR, fraction: null, glyph: "popout", label: "POP OUT", big: "", caption: state.title ?? "timer" };
	}
}
