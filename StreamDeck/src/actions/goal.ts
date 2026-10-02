import type { JsonObject } from "@elgato/utils";
import { action, type KeyAction, type KeyDownEvent, type KeyUpEvent, SingletonAction, type WillAppearEvent, type WillDisappearEvent } from "@elgato/streamdeck";
import type { HubClient } from "../hub.js";
import { type KeyArt, renderKey, renderText, type TextArt, toDataUrl } from "../key-art.js";

const COLOR = "#33E1FF";
const STAR_COLOR = "#FFB547";
const DONE_COLOR = "#4ADE80";
const NEUTRAL_COLOR = "#8A93A6";
const HOLD_MS = 600;
const FLASH_MS = 1500;

type Entry = {
	action: KeyAction<JsonObject>;
	/** The goal this key is showing, followed across list changes. */
	goalId?: string;
	holdTimer?: NodeJS.Timeout;
	held: boolean;
	flash?: { art: KeyArt; until: number };
	lastImage?: string;
};

/**
 * Today's goals, one at a time: starred first, from every tab. Tap shows the next one; hold
 * checks off the one on screen (synced to Google Tasks like a tick in the app).
 */
@action({ UUID: "com.bijounga.bijouhub.goal" })
export class GoalAction extends SingletonAction {
	readonly #hub: HubClient;
	readonly #keys = new Map<string, Entry>();

	constructor(hub: HubClient) {
		super();
		this.#hub = hub;
		hub.onChange(() => this.#renderAll());
	}

	override async onWillAppear(ev: WillAppearEvent): Promise<void> {
		if (!ev.action.isKey()) return;
		const entry: Entry = { action: ev.action, held: false };
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
		if (!this.#current(entry)) return;

		entry.holdTimer = setTimeout(() => {
			entry.held = true;
			entry.holdTimer = undefined;
			void this.#complete(entry);
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
			if (!(await this.#hub.ensureRunning())) await entry.action.showAlert();
			return;
		}

		// Tap: next goal.
		const items = this.#hub.goals?.items ?? [];
		if (items.length === 0) return;
		const index = this.#indexOf(entry);
		entry.goalId = items[(index + 1) % items.length].id;
		await this.#render(entry);
	}

	async #complete(entry: Entry): Promise<void> {
		const goal = this.#current(entry);
		if (!goal) return;

		// Land on the goal after this one once the list updates.
		const items = this.#hub.goals?.items ?? [];
		const next = items[(this.#indexOf(entry) + 1) % items.length];
		const reply = await this.#hub.request("completeGoal", { goalId: goal.id });
		if (reply.error) {
			await entry.action.showAlert();
			return;
		}
		entry.goalId = next && next.id !== goal.id ? next.id : undefined;
		entry.flash = {
			art: { color: DONE_COLOR, fraction: 1, label: "DONE", check: true, big: "", caption: goal.text },
			until: Date.now() + FLASH_MS
		};
		await this.#render(entry);
		setTimeout(() => void this.#render(entry), FLASH_MS + 50);
	}

	#indexOf(entry: Entry): number {
		const items = this.#hub.goals?.items ?? [];
		const index = items.findIndex((g) => g.id === entry.goalId);
		return index < 0 ? 0 : index;
	}

	#current(entry: Entry) {
		const items = this.#hub.goals?.items ?? [];
		return items.length ? items[this.#indexOf(entry)] : undefined;
	}

	#renderAll(): void {
		for (const entry of this.#keys.values()) void this.#render(entry);
	}

	async #render(entry: Entry): Promise<void> {
		const image = toDataUrl(this.#svg(entry));
		if (image === entry.lastImage) return;
		entry.lastImage = image;
		await entry.action.setImage(image);
	}

	#svg(entry: Entry): string {
		if (entry.flash && Date.now() < entry.flash.until) return renderKey(entry.flash.art);
		entry.flash = undefined;

		const goals = this.#hub.goals;
		if (!this.#hub.connected || !goals) {
			return renderText({ color: NEUTRAL_COLOR, label: "GOALS", text: this.#hub.connected ? "Loading…" : "Open BijouHub" });
		}
		if (goals.items.length === 0) {
			return goals.done > 0
				? renderKey({ color: DONE_COLOR, fraction: 1, label: "ALL DONE", check: true, big: "", caption: `${goals.done} today` })
				: renderText({ color: NEUTRAL_COLOR, label: "GOALS", text: "Nothing planned yet" });
		}

		const index = this.#indexOf(entry);
		const goal = goals.items[index];
		entry.goalId = goal.id;
		const art: TextArt = {
			color: goal.starred ? STAR_COLOR : COLOR,
			label: `${goal.starred ? "★ " : ""}${index + 1} / ${goals.items.length}`,
			text: goal.text
		};
		return renderText(art);
	}
}
