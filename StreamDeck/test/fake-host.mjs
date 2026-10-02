// End-to-end test: stands in for the Stream Deck app. Runs the bundled plugin (on Stream Deck's
// own Node 20 when present), presses keys, and records every image the plugin draws. BijouHub
// itself is launched by the plugin on the first press, against a throwaway data folder, so
// real projects and session history are never touched.
//
//   HUB_EXE=D:\LauncherApp\bin\Debug\net8.0-windows\BijouHub.exe npm test
//
// Frames land in test/out (frames.json + index.html for eyeballing).

import { spawn } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { WebSocketServer } from "ws";

const HERE = dirname(fileURLToPath(import.meta.url));
const PLUGIN_DIR = join(HERE, "..", "com.bijounga.bijouhub.sdPlugin");
const OUT = join(HERE, "out");
const ACTION = "com.bijounga.bijouhub.timer";
const DEVICE = "test-device";
const HUB_PORT = 47901;
const HUB_EXE = process.env.HUB_EXE ?? join(HERE, "..", "..", "bin", "Debug", "net8.0-windows", "BijouHub.exe");
const SD_NODE = join(process.env.APPDATA ?? "", "Elgato", "StreamDeck", "NodeJS", "20.20.0", "node.exe");
const RUN_OVERTIME = process.env.SKIP_OVERTIME !== "1";

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let failures = 0;
function check(condition, message) {
	console.log(`${condition ? "  ok " : "  FAIL"}  ${message}`);
	if (!condition) failures++;
}

// ---------- Scratch BijouHub data ----------
const dataDir = mkdtempSync(join(tmpdir(), "bijouhub-deck-test-"));
writeFileSync(join(dataDir, "settings.json"), JSON.stringify({ ZoomLevel: 1, ThemeName: "Dark" }));
writeFileSync(join(dataDir, "modes.json"), JSON.stringify([
	{ Id: "m-edit", Name: "Editing", LaunchItems: [], BlockItems: [] },
	{ Id: "m-write", Name: "Writing", LaunchItems: [], BlockItems: [] }
]));
writeFileSync(join(dataDir, "projects.json"), JSON.stringify([{ Id: "p-film", Name: "Short Film", Goals: [], Notes: [] }]));
// What a previous BijouHub run would have left behind — lets the plugin launch it.
writeFileSync(join(dataDir, "bridge.json"), JSON.stringify({ port: HUB_PORT, exePath: HUB_EXE }));

// ---------- Fake Stream Deck ----------
const images = new Map(); // context -> latest svg
const frames = [];
const events = [];
let step = "start";
let plugin = null;

const server = new WebSocketServer({ port: 0, host: "127.0.0.1" });
await new Promise((r) => server.once("listening", r));
const port = server.address().port;

const connected = new Promise((resolve) => {
	server.on("connection", (socket) => {
		socket.on("message", (raw) => {
			const message = JSON.parse(raw.toString());
			if (message.event === "registerPlugin") {
				plugin = socket;
				resolve();
				return;
			}
			events.push({ step, ...message });
			if (message.event === "setImage") {
				const svg = decodeURIComponent(message.payload.image.replace(/^data:image\/svg\+xml;charset=utf8,/, ""));
				images.set(message.context, svg);
				frames.push({ step, context: message.context, svg });
			}
		});
	});
});

const info = {
	application: { font: "Segoe UI", language: "en", platform: "windows", platformVersion: "10.0.26300", version: "7.5.1.22901" },
	colors: {},
	devicePixelRatio: 2,
	devices: [{ id: DEVICE, name: "Stream Deck", size: { columns: 5, rows: 3 }, type: 0 }],
	plugin: { uuid: "com.bijounga.bijouhub", version: "1.0.0.0" }
};

const node = existsSync(SD_NODE) ? SD_NODE : process.execPath;
console.log(`plugin runtime: ${node}`);
const child = spawn(node, ["bin/plugin.js", "-port", String(port), "-pluginUUID", "test-plugin", "-registerEvent", "registerPlugin", "-info", JSON.stringify(info)], {
	cwd: PLUGIN_DIR,
	env: { ...process.env, BIJOUHUB_DATA_DIR: dataDir, BIJOUHUB_DECK_PORT: String(HUB_PORT), BIJOUHUB_IDLE_MINUTES: "1440" },
	stdio: ["ignore", "inherit", "inherit"]
});

const registered = await Promise.race([connected.then(() => true), sleep(10000).then(() => false)]);
if (!registered) {
	console.log("FAIL  plugin never registered — see com.bijounga.bijouhub.sdPlugin/logs");
	child.kill();
	process.exit(1);
}

const keys = {};
let row = 0;
function send(event, context, payload = {}) {
	plugin.send(JSON.stringify({ action: ACTION, event, context, device: DEVICE, payload }));
}
function appear(name, settings) {
	keys[name] = { context: `ctx-${name}`, settings, coordinates: { column: row++, row: 0 } };
	send("willAppear", keys[name].context, { settings, coordinates: keys[name].coordinates, controller: "Keypad", isInMultiAction: false, state: 0 });
}
function keyPayload(name) {
	return { settings: keys[name].settings, coordinates: keys[name].coordinates, isInMultiAction: false, state: 0 };
}
async function tap(name) {
	send("keyDown", keys[name].context, keyPayload(name));
	await sleep(80);
	send("keyUp", keys[name].context, keyPayload(name));
}
async function hold(name) {
	send("keyDown", keys[name].context, keyPayload(name));
	await sleep(900);
	send("keyUp", keys[name].context, keyPayload(name));
}
const svgOf = (name) => images.get(keys[name].context) ?? "";
const textOf = (name) => [...svgOf(name).matchAll(/<text[^>]*>([^<]*)<\/text>/g)].map((m) => m[1]);
async function waitFor(predicate, ms, label) {
	const deadline = Date.now() + ms;
	while (Date.now() < deadline) {
		if (predicate()) return true;
		await sleep(100);
	}
	console.log(`  (timed out waiting for: ${label})`);
	return false;
}
function snapshot(label) {
	step = label;
	console.log(`- ${label}: ` + Object.keys(keys).map((k) => `${k}=[${textOf(k).join(" | ")}]`).join("  "));
}
function settingsWrites(context) {
	return events.filter((e) => e.event === "setSettings" && e.context === context);
}
function alerts(context) {
	return events.filter((e) => e.event === "showAlert" && e.context === context).length;
}

try {
	// ---------- Keys appear ----------
	step = "appear";
	appear("A", { keyId: "key-a", modeId: "m-edit", modeName: "Editing", duration: "1", color: "#33E1FF" });
	appear("B", { keyId: "key-b", modeId: "m-write", modeName: "Writing", duration: "", color: "#A78BFA" });
	appear("C", {});
	appear("D", { keyId: "key-d", modeId: "m-edit", modeName: "Editing", duration: "abc" });
	appear("E", { keyId: "key-a", modeId: "m-edit", modeName: "Editing", duration: "45" }); // duplicated key
	await waitFor(() => Object.keys(keys).every((k) => images.has(keys[k].context)), 3000, "first images");
	snapshot("appeared");
	check(textOf("A").includes("1m"), "A shows its 1m preset");
	check(textOf("B").includes("0:00"), "B (count up) shows 0:00");
	check(textOf("C").includes("SET UP"), "unconfigured key asks to be set up");
	check(textOf("D").includes("?"), "unreadable duration is flagged");
	const dupe = settingsWrites("ctx-E").at(-1);
	check(dupe && dupe.payload.keyId && dupe.payload.keyId !== "key-a", "duplicated key gets its own keyId");
	check(settingsWrites("ctx-C").length === 1, "unconfigured key is given a keyId");

	// ---------- Settings panel while BijouHub is closed ----------
	send("propertyInspectorDidAppear", keys.A.context);
	send("sendToPlugin", keys.A.context, { event: "getCatalog" });
	await waitFor(() => events.some((e) => e.event === "sendToPropertyInspector"), 3000, "catalog reply");
	const offlineCatalog = events.filter((e) => e.event === "sendToPropertyInspector").at(-1)?.payload;
	check(offlineCatalog?.connected === false, "panel learns BijouHub isn't running");

	// ---------- First press launches BijouHub and starts the countdown ----------
	step = "press A";
	await tap("A");
	await waitFor(() => textOf("A").includes("STARTING"), 2000, "starting frame");
	check(frames.some((f) => f.context === keys.A.context && f.svg.includes("STARTING")), "A shows STARTING while BijouHub launches");
	const started = await waitFor(() => textOf("A").some((t) => /^0:5\d$/.test(t) || t === "1:00"), 40000, "countdown on A");
	check(started, "BijouHub launched and A counts down");
	await sleep(2500);
	snapshot("A running");
	check(textOf("A").some((t) => /^0:5\d$/.test(t)), "A ticks below a minute");
	check(svgOf("B").includes('opacity="0.42"'), "other keys dim while A runs");

	send("sendToPlugin", keys.A.context, { event: "getCatalog" });
	await waitFor(() => events.filter((e) => e.event === "sendToPropertyInspector").at(-1)?.payload?.connected === true, 3000, "online catalog");
	const catalog = events.filter((e) => e.event === "sendToPropertyInspector").at(-1)?.payload;
	check(catalog?.modes?.length === 2 && catalog?.projects?.length === 1, "panel gets BijouHub's modes and projects");

	// ---------- Tap pauses, tap resumes ----------
	await tap("A");
	await waitFor(() => textOf("A").includes("PAUSED"), 3000, "paused");
	const frozen = textOf("A")[1];
	await sleep(2500);
	snapshot("A paused");
	check(textOf("A").includes("PAUSED"), "tap pauses A");
	check(textOf("A")[1] === frozen, "paused countdown holds still");
	await tap("A");
	await waitFor(() => !textOf("A").includes("PAUSED"), 3000, "resumed");
	await sleep(1500);
	snapshot("A resumed");
	check(!textOf("A").includes("PAUSED") && textOf("A")[0] !== frozen, "tap again resumes");

	// ---------- Another key switches the session ----------
	await tap("B");
	await waitFor(() => textOf("B").includes("ELAPSED"), 5000, "B count-up");
	await sleep(2500);
	snapshot("B running");
	check(textOf("B").includes("ELAPSED"), "B takes over and counts up");
	check(svgOf("A").includes("M66 37") && svgOf("A").includes('opacity="0.42"'), "A is back to idle (dimmed while B runs)");

	// ---------- Misconfigured keys refuse ----------
	await tap("C");
	await tap("D");
	await sleep(500);
	check(alerts("ctx-C") >= 1 && alerts("ctx-D") >= 1, "unconfigured/invalid keys show an alert instead of starting");

	// ---------- Hold finishes ----------
	await hold("B");
	await waitFor(() => textOf("B").includes("LOGGED"), 3000, "logged flash");
	snapshot("B finished");
	check(textOf("B").includes("LOGGED"), "holding B finishes and logs it");
	await sleep(2200);
	snapshot("after flash");
	check(svgOf("B").includes("M66 37"), "B returns to its ready state");

	// ---------- Countdown into overtime ----------
	if (RUN_OVERTIME) {
		await tap("A");
		await waitFor(() => textOf("A").some((t) => /^0:5\d$/.test(t)), 8000, "A restarted");
		const over = await waitFor(() => textOf("A").includes("OVER"), 70000, "overtime");
		await sleep(2500);
		snapshot("A overtime");
		check(over && textOf("A").some((t) => t.startsWith("+0:0")), "A runs past zero into overtime");
		await hold("A");
		await waitFor(() => textOf("A").includes("LOGGED"), 3000, "logged A");
		check(textOf("A").includes("LOGGED"), "holding A logs the overtime session");
	}
	await sleep(1000);
} finally {
	// ---------- Tear down and inspect what BijouHub logged ----------
	let hubPid = null;
	try {
		hubPid = JSON.parse(readFileSync(join(dataDir, "bridge.json"), "utf8")).pid ?? null;
	} catch {}
	child.kill();
	server.close();

	mkdirSync(OUT, { recursive: true });
	writeFileSync(join(OUT, "frames.json"), JSON.stringify(frames, null, 1));
	const seen = new Set();
	const tiles = frames.filter((f) => {
		const key = f.context + f.svg;
		if (seen.has(key)) return false;
		seen.add(key);
		return true;
	});
	writeFileSync(join(OUT, "index.html"), `<!doctype html><body style="background:#222;color:#aaa;font:12px Segoe UI;margin:12px">
${tiles.map((f) => `<div style="display:inline-block;margin:4px;text-align:center;width:144px">${f.svg}<div>${f.context.slice(4)} · ${f.step}</div></div>`).join("\n")}</body>`);

	let sessions = [];
	try {
		sessions = JSON.parse(readFileSync(join(dataDir, "sessions.json"), "utf8"));
	} catch {}
	console.log("logged sessions:", sessions.map((s) => `${s.ModeName} ${s.ActiveSeconds}s active/${s.IdleSeconds}s idle project=${s.ProjectName ?? "-"}`));
	check(sessions.length === (RUN_OVERTIME ? 3 : 2), "every finished or replaced session was logged");
	check(sessions.every((s) => !s.ProjectId), "deck sessions start unassigned");
	check(sessions[0]?.IdleSeconds >= 2, "paused time is logged as idle, not worked");
	if (RUN_OVERTIME) check(sessions[2]?.ActiveSeconds >= 60, "overtime session kept counting past the target");

	if (hubPid) {
		try {
			process.kill(hubPid);
		} catch {}
	}
	await sleep(800);
	if (failures === 0) rmSync(dataDir, { recursive: true, force: true });
	else console.log(`scratch data kept at ${dataDir}`);
	console.log(failures === 0 ? "\nALL CHECKS PASSED" : `\n${failures} CHECK(S) FAILED`);
	process.exit(failures === 0 ? 0 : 1);
}
