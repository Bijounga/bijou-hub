/**
 * Parses what people actually type for a timer length into minutes:
 * "45", "45m", "1:30", "1h", "1h30", "1h 30m", "1.5h", "90 min".
 * Empty means "no limit, count up" (null); anything unreadable is undefined.
 */
export function parseDuration(text: string | undefined): number | null | undefined {
	const value = (text ?? "").trim().toLowerCase();
	if (value === "") return null;

	// h:mm
	const clock = /^(\d{1,2}):(\d{1,2})$/.exec(value);
	if (clock) return positive(Number(clock[1]) * 60 + Number(clock[2]));

	// Plain number = minutes.
	if (/^\d+(\.\d+)?$/.test(value)) return positive(Math.round(Number(value)));

	// 1h, 1.5h, 1h30, 1h 30m, 90m, 90 min, 2 hours
	const parts = /^(?:(\d+(?:\.\d+)?)\s*h(?:ours?|rs?)?)?\s*(?:(\d+)\s*(?:m|mins?|minutes?)?)?$/.exec(value);
	if (parts && (parts[1] || parts[2])) {
		const hours = parts[1] ? Number(parts[1]) : 0;
		const minutes = parts[2] ? Number(parts[2]) : 0;
		return positive(Math.round(hours * 60 + minutes));
	}

	return undefined;
}

function positive(minutes: number): number | undefined {
	return minutes > 0 && minutes <= 24 * 60 ? minutes : undefined;
}

/** 45 → "45m", 60 → "1h", 90 → "1h 30m". */
export function formatPreset(minutes: number): string {
	const h = Math.floor(minutes / 60);
	const m = minutes % 60;
	if (h === 0) return `${m}m`;
	return m === 0 ? `${h}h` : `${h}h ${m}m`;
}

/** Seconds as a clock: 2712 → "45:12", 5399 → "1:29:59". */
export function formatClock(totalSeconds: number): string {
	const s = Math.max(0, Math.floor(totalSeconds));
	const h = Math.floor(s / 3600);
	const m = Math.floor((s % 3600) / 60);
	const sec = s % 60;
	const pad = (n: number) => n.toString().padStart(2, "0");
	return h > 0 ? `${h}:${pad(m)}:${pad(sec)}` : `${m}:${pad(sec)}`;
}
