import { action, type KeyAction, type KeyUpEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";
import type { HubClient } from "../hub.js";
import { type KeyArt, renderKey, toDataUrl } from "../key-art.js";

const COLOR = "#33E1FF";

type Entry = {
	action: KeyAction;
	lastImage?: string;
};

/**
 * Widgets On Top: flips BijouHub's "Keep widgets on top of other windows" setting. Lit when the
 * widgets sit above everything, dim when they stay behind. It doesn't launch BijouHub: with it
 * closed there are no widgets to move.
 */
@action({ UUID: "com.bijounga.bijouhub.widgetsontop" })
export class OnTopAction extends SingletonAction {
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
		if (!this.#hub.connected || (await this.#hub.request("widgetsOnTop")).error) await entry.action.showAlert();
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
		if (!state) return { color: COLOR, fraction: null, glyph: "behind", label: "WIDGETS", big: "", caption: "BijouHub off", dim: true };
		if (state.widgetsOnTop) return { color: COLOR, fraction: 1, glyph: "ontop", label: "ON TOP", big: "", caption: "widgets" };
		return { color: COLOR, fraction: null, glyph: "behind", label: "BEHIND", big: "", caption: "widgets" };
	}
}
