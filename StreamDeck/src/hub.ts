import { spawn } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { Socket } from "node:net";
import { homedir } from "node:os";
import { join } from "node:path";

/** Session state BijouHub pushes every second (see MainWindow.DeckState). */
export type HubState = {
	active: boolean;
	keyId: string | null;
	title: string | null;
	targetSeconds: number | null;
	activeSeconds: number;
	paused: boolean;
	idle: boolean;
	/** Time logged today, including the running session. */
	todaySeconds: number;
	/** The always-on-top mini timer is open. */
	poppedOut: boolean;
	/** A Pomodoro session's current focus or break. */
	pomodoro: Pomodoro | null;
	/** How long the user wants to work each day, if they set a target. */
	dailyTargetSeconds: number | null;
};

export type Pomodoro = {
	phase: "focus" | "break";
	round: number;
	/** How many rounds it runs for; null until stopped. */
	rounds: number | null;
	remaining: number;
	phaseSeconds: number;
};

/** Open goals across every tab, starred first (see MainWindow.DeckGoals). */
export type HubGoals = {
	open: number;
	done: number;
	items: { id: string; text: string; starred: boolean; label: string | null }[];
};

export type Catalog = {
	modes: { id: string; name: string }[];
	projects: { id: string; name: string }[];
};

type Reply = Record<string, unknown> & { error?: string };

const DEFAULT_PORT = 47823;
const RECONNECT_MS = 2000;
const REQUEST_TIMEOUT_MS = 5000;
const LAUNCH_TIMEOUT_MS = 30000;

/**
 * Line-delimited JSON link to BijouHub's StreamDeckBridge on 127.0.0.1. Reconnects on its own,
 * and can launch BijouHub (path from the bridge.json it writes) when a key needs it.
 */
export class HubClient {
	#socket: Socket | null = null;
	#connected = false;
	#buffer = "";
	#nextId = 1;
	#pending = new Map<number, { resolve: (reply: Reply) => void; timer: NodeJS.Timeout }>();
	#listeners = new Set<() => void>();
	#reconnectTimer: NodeJS.Timeout | null = null;

	state: HubState | null = null;
	goals: HubGoals | null = null;

	get connected(): boolean {
		return this.#connected;
	}

	/** Called on every state push and on connect/disconnect. */
	onChange(listener: () => void): void {
		this.#listeners.add(listener);
	}

	start(): void {
		this.#connect();
	}

	request(type: string, payload: Record<string, unknown> = {}): Promise<Reply> {
		if (!this.#socket || !this.#connected) return Promise.resolve({ error: "BijouHub isn't running" });

		const id = this.#nextId++;
		return new Promise((resolve) => {
			const timer = setTimeout(() => {
				this.#pending.delete(id);
				resolve({ error: "BijouHub didn't answer" });
			}, REQUEST_TIMEOUT_MS);
			this.#pending.set(id, { resolve, timer });
			this.#socket!.write(JSON.stringify({ ...payload, type, id }) + "\n");
		});
	}

	/** Connects, launching BijouHub first if it isn't running. Resolves false if it never shows up. */
	async ensureRunning(): Promise<boolean> {
		if (this.#connected) return true;

		const exePath = readBridgeInfo()?.exePath;
		if (!exePath || !existsSync(exePath)) return false;

		// On a Mac the path is the binary inside BijouHub.app: open the bundle, the way Finder would.
		const bundle = exePath.match(/^(.*?\.app)\/Contents\/MacOS\//)?.[1];
		if (bundle) spawn("/usr/bin/open", [bundle], { detached: true, stdio: "ignore" }).unref();
		else spawn(exePath, [], { detached: true, stdio: "ignore", windowsHide: false }).unref();

		const deadline = Date.now() + LAUNCH_TIMEOUT_MS;
		while (Date.now() < deadline) {
			this.#connect();
			await delay(500);
			if (this.#connected) return true;
		}
		return false;
	}

	#connect(): void {
		if (this.#socket) return;
		if (this.#reconnectTimer) {
			clearTimeout(this.#reconnectTimer);
			this.#reconnectTimer = null;
		}

		const socket = new Socket();
		this.#socket = socket;
		socket.setEncoding("utf8");
		socket.setNoDelay(true);

		socket.on("connect", async () => {
			this.#connected = true;
			this.#emit();
			const reply = await this.request("state");
			if (!reply.error) this.#applyState(reply);
			const goals = await this.request("goals");
			if (!goals.error) this.#applyGoals(goals);
		});
		socket.on("data", (chunk: string) => this.#receive(chunk));
		socket.on("error", () => {
			/* close follows */
		});
		socket.on("close", () => {
			const wasConnected = this.#connected;
			this.#socket = null;
			this.#connected = false;
			this.#buffer = "";
			this.state = null;
			this.goals = null;
			for (const [, pending] of this.#pending) {
				clearTimeout(pending.timer);
				pending.resolve({ error: "BijouHub closed" });
			}
			this.#pending.clear();
			if (wasConnected) this.#emit();
			this.#reconnectTimer = setTimeout(() => this.#connect(), RECONNECT_MS);
		});

		socket.connect(hubPort(), "127.0.0.1");
	}

	#receive(chunk: string): void {
		this.#buffer += chunk;
		let newline: number;
		while ((newline = this.#buffer.indexOf("\n")) >= 0) {
			const line = this.#buffer.slice(0, newline).trim();
			this.#buffer = this.#buffer.slice(newline + 1);
			if (!line) continue;

			let message: Reply;
			try {
				message = JSON.parse(line);
			} catch {
				continue;
			}

			if (message.type === "reply" && typeof message.id === "number") {
				const pending = this.#pending.get(message.id);
				if (pending) {
					clearTimeout(pending.timer);
					this.#pending.delete(message.id);
					pending.resolve(message);
				}
				// pause/finish answer with the new state — apply it without waiting for the next tick.
				if ("active" in message) this.#applyState(message);
				if ("items" in message) this.#applyGoals(message);
			} else if (message.type === "state") {
				this.#applyState(message);
			} else if (message.type === "goals") {
				this.#applyGoals(message);
			}
		}
	}

	#applyState(message: Record<string, unknown>): void {
		this.state = {
			active: message.active === true,
			keyId: typeof message.keyId === "string" ? message.keyId : null,
			title: typeof message.title === "string" ? message.title : null,
			targetSeconds: typeof message.targetSeconds === "number" ? message.targetSeconds : null,
			activeSeconds: typeof message.activeSeconds === "number" ? message.activeSeconds : 0,
			paused: message.paused === true,
			idle: message.idle === true,
			todaySeconds: typeof message.todaySeconds === "number" ? message.todaySeconds : 0,
			poppedOut: message.poppedOut === true,
			pomodoro: parsePomodoro(message.pomodoro),
			dailyTargetSeconds: typeof message.dailyTargetSeconds === "number" && message.dailyTargetSeconds > 0 ? message.dailyTargetSeconds : null
		};
		this.#emit();
	}

	#applyGoals(message: Record<string, unknown>): void {
		const items = Array.isArray(message.items) ? message.items : [];
		this.goals = {
			open: typeof message.open === "number" ? message.open : 0,
			done: typeof message.done === "number" ? message.done : 0,
			items: items
				.filter((i): i is Record<string, unknown> => typeof i === "object" && i !== null && typeof i.id === "string")
				.map((i) => ({
					id: i.id as string,
					text: typeof i.text === "string" ? i.text : "",
					starred: i.starred === true,
					label: typeof i.label === "string" ? i.label : null
				}))
		};
		this.#emit();
	}

	#emit(): void {
		for (const listener of this.#listeners) listener();
	}
}

function parsePomodoro(value: unknown): Pomodoro | null {
	if (!value || typeof value !== "object") return null;
	const p = value as Record<string, unknown>;
	return {
		phase: p.phase === "break" ? "break" : "focus",
		round: typeof p.round === "number" ? p.round : 1,
		rounds: typeof p.rounds === "number" && p.rounds > 0 ? p.rounds : null,
		remaining: typeof p.remaining === "number" ? p.remaining : 0,
		phaseSeconds: typeof p.phaseSeconds === "number" && p.phaseSeconds > 0 ? p.phaseSeconds : 1
	};
}

function dataDir(): string {
	if (process.env.BIJOUHUB_DATA_DIR) return process.env.BIJOUHUB_DATA_DIR;
	// .NET's ApplicationData folder: %APPDATA% on Windows, ~/.config on a Mac.
	return process.platform === "win32" ? join(process.env.APPDATA ?? "", "BijouHub") : join(homedir(), ".config", "BijouHub");
}

function readBridgeInfo(): { port?: number; exePath?: string } | null {
	try {
		return JSON.parse(readFileSync(join(dataDir(), "bridge.json"), "utf8"));
	} catch {
		return null;
	}
}

function hubPort(): number {
	const fromEnv = Number(process.env.BIJOUHUB_DECK_PORT);
	if (fromEnv > 0) return fromEnv;
	return readBridgeInfo()?.port ?? DEFAULT_PORT;
}

function delay(ms: number): Promise<void> {
	return new Promise((resolve) => setTimeout(resolve, ms));
}
