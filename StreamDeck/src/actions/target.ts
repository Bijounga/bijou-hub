import { action, type KeyAction, type KeyUpEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";
import type { HubClient } from "../hub.js";
import { type KeyArt, renderKey, toDataUrl } from "../key-art.js";
import { BREAK_COLOR, formatSpan } from "../session-art.js";

const COLOR = "#33E1FF";
const NEUTRAL_COLOR = "#8A93A6";

type Entry = {
	action: KeyAction;
	lastImage?: string;
};

/**
 * Today's worked time against the daily target: the ring fills as the day's sessions add up and
 * turns green once it's hit. Tap to set or change the target in BijouHub.
 */
@action({ UUID: "com.bijounga.bijouhub.target" })
export class TargetAction extends SingletonAction {
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
		if (!(await this.#hub.ensureRunning()) || (await this.#hub.request("target")).error) await entry.action.showAlert();
	}

	#renderAll(): void {
		for (const entry of this.#keys.values()) void this.#render(entry);
	}

	// The picture only changes when the minute does, so redraws stay rare.
	async #render(entry: Entry): Promise<void> {
		const image = toDataUrl(renderKey(this.#art()));
		if (image === entry.lastImage) return;
		entry.lastImage = image;
		await entry.action.setImage(image);
	}

	#art(): KeyArt {
		const state = this.#hub.connected ? this.#hub.state : null;
		if (!state) return { color: NEUTRAL_COLOR, fraction: null, label: "TARGET", big: "—", caption: "tap to open", dim: true };

		const target = state.dailyTargetSeconds;
		if (!target) return { color: COLOR, fraction: null, label: "TARGET", big: formatSpan(state.todaySeconds), caption: "tap to set" };

		const fraction = Math.min(1, state.todaySeconds / target);
		if (fraction >= 1) {
			return { color: BREAK_COLOR, fraction: 1, label: "TARGET HIT", big: formatSpan(state.todaySeconds), caption: `of ${formatSpan(target)}` };
		}
		return { color: COLOR, fraction, label: `${Math.floor(fraction * 100)}%`, big: formatSpan(state.todaySeconds), caption: `of ${formatSpan(target)}` };
	}
}
