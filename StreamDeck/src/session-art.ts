import { formatClock } from "./duration.js";
import type { HubState } from "./hub.js";
import { AWAY_COLOR, type KeyArt, OVER_COLOR, PAUSED_COLOR } from "./key-art.js";

/**
 * The running session as a key: countdown ring (or elapsed time when counting up), with paused,
 * away and overtime states. Shared by the Timer key that started it and the Current Session key.
 */
export const BREAK_COLOR = "#4ADE80";

export function liveSessionArt(state: HubState, color: string, caption: string): KeyArt {
	const status = state.paused ? { color: PAUSED_COLOR, label: "PAUSED" } : state.idle ? { color: AWAY_COLOR, label: "AWAY" } : null;

	// Pomodoro: the current focus or break counting down, the ring draining with it.
	const pomodoro = state.pomodoro;
	if (pomodoro) {
		const onBreak = pomodoro.phase === "break";
		const phaseColor = onBreak ? BREAK_COLOR : color;
		return {
			color: state.paused ? PAUSED_COLOR : onBreak ? BREAK_COLOR : status?.color ?? color,
			fraction: Math.min(1, pomodoro.remaining / pomodoro.phaseSeconds),
			label: state.paused ? "PAUSED" : onBreak ? "BREAK" : status?.label ?? `FOCUS · ${pomodoro.round}`,
			labelColor: state.paused || (!onBreak && status) ? undefined : phaseColor,
			big: formatClock(pomodoro.remaining),
			caption
		};
	}

	if (state.targetSeconds === null) {
		return {
			color: status?.color ?? color,
			fraction: (state.activeSeconds % 3600) / 3600,
			label: status?.label ?? "ELAPSED",
			labelColor: status ? undefined : color,
			big: formatClock(state.activeSeconds),
			caption
		};
	}

	const remaining = state.targetSeconds - state.activeSeconds;
	if (remaining <= 0) {
		return {
			color: status?.color ?? OVER_COLOR,
			fraction: 1,
			label: status?.label ?? "OVER",
			big: "+" + formatClock(-remaining),
			caption
		};
	}

	return {
		color: status?.color ?? color,
		fraction: remaining / state.targetSeconds,
		label: status?.label,
		big: formatClock(remaining),
		caption
	};
}

/** 8100 → "2h 15m", 2700 → "45m", 0 → "0m". */
export function formatSpan(totalSeconds: number): string {
	const minutes = Math.floor(Math.max(0, totalSeconds) / 60);
	const h = Math.floor(minutes / 60);
	const m = minutes % 60;
	if (h === 0) return `${m}m`;
	return m === 0 ? `${h}h` : `${h}h ${m}m`;
}
