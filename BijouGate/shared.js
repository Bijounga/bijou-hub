// Rules, matching and site state, shared by the background worker, the gate, the popup and the options page.
//
// One rule per line:
//   x.com                 the whole site (and its subdomains)
//   youtube.com/shorts    any page whose path starts with /shorts
//   =youtube.com/         only that exact path (here: the YouTube home page)
// Blank lines and lines starting with # are ignored.

const DEFAULT_RULES = [
  "# The whole site",
  "x.com",
  "twitter.com",
  "",
  "# YouTube: the feed and Shorts are blocked, but search, videos and playlists stay open for music and assets",
  "=youtube.com/",
  "youtube.com/shorts",
  "youtube.com/feed",
].join("\n");

// What the gate says to you while you wait. {goal} is swapped for one of the goals below.
const DEFAULT_GOALS = [
  "Omniwield",
  "today's editing hours",
  "the next upload",
  "this week's classes",
].join("\n");

const DEFAULT_AFFIRMATIONS = [
  "Ten more minutes on {goal}, then look at whatever you want.",
  "A quick look turns into forty minutes. You know how this goes.",
  "What were you in the middle of? Go back to that.",
  "Be honest: will this help with {goal}?",
  "Whatever's on there will still be there after your session.",
  "{goal} first. Everything else after.",
  "You've already started. Don't make yourself start over later.",
  "If you still want it when the timer ends, fine. At least you chose it.",
  "It's probably not urgent. Check when the next render finishes.",
  "Bored is okay. It usually passes in a minute.",
  "Foggy day? Do one small thing, like one clip or one page, and see how you feel.",
  "Studying counts. One problem, then you can check.",
].join("\n");

const DEFAULT_SETTINGS = {
  rules: DEFAULT_RULES,
  goals: DEFAULT_GOALS,
  affirmations: DEFAULT_AFFIRMATIONS,
  waitSec: 15,                      // the wait before you can pick a time
  cooldownMin: 20,                  // after a session ends, the site stays shut this long
  holdSec: 10,                      // how long the "I really need it" button has to be held
  minutesChoices: [5, 10, 15, 30],  // normal unlock lengths
  overrideChoices: [2, 5, 10, 30],  // unlock lengths after holding the button during a cooldown
  maxUnlocksPerDay: 0,              // per site; 0 = no limit
  noHold2x: true,                   // YouTube: holding Space or the mouse on the video no longer jumps to 2x
};

// Sites that are really the same place share one unlock.
const ALIASES = { "twitter.com": "x.com", "m.youtube.com": "youtube.com", "music.youtube.com": "youtube.com" };
const NICE = { "x.com": "X", "youtube.com": "YouTube" };

function normHost(h) {
  h = h.toLowerCase().replace(/^www\./, "").replace(/^mobile\./, "");
  return ALIASES[h] || h;
}

function niceName(site) { return NICE[site] || site; }

function parseRules(text) {
  const rules = [];
  for (let line of (text || "").split(/\r?\n/)) {
    line = line.trim();
    if (!line || line.startsWith("#")) continue;
    let exact = false;
    if (line.startsWith("=")) { exact = true; line = line.slice(1); }
    line = line.replace(/^https?:\/\//, "");
    const slash = line.indexOf("/");
    const host = (slash < 0 ? line : line.slice(0, slash)).toLowerCase().replace(/^www\./, "");
    const path = slash < 0 ? "" : line.slice(slash);
    if (!host) continue;
    rules.push({ host, path, exact, site: normHost(host) });
  }
  return rules;
}

// The rule a URL falls under, or null if it's free to open.
function matchRule(url, rules) {
  let u;
  try { u = new URL(url); } catch { return null; }
  if (u.protocol !== "http:" && u.protocol !== "https:") return null;
  const host = u.hostname.toLowerCase().replace(/^www\./, "");
  for (const r of rules) {
    // Whole-site rules cover subdomains. Path rules only cover the site itself (and its mobile
    // site), so studio.youtube.com, where you edit your own channel, stays open.
    const hostOk = r.path
      ? host === r.host || host === "m." + r.host
      : host === r.host || host.endsWith("." + r.host);
    if (!hostOk) continue;
    if (!r.path) return r;
    const p = u.pathname;
    if (r.exact ? p === r.path : p.startsWith(r.path)) return r;
  }
  return null;
}

// The distinct sites the rules cover, in order.
function siteList(rules) {
  return [...new Set(rules.map(r => r.site))];
}

// Adds a whole-site rule for a host (or a pasted address) unless that site is already covered.
// Returns the new rules text.
function addSite(text, input) {
  let host = (input || "").trim().toLowerCase().replace(/^https?:\/\//, "").split(/[/?#]/)[0].replace(/^www\./, "");
  if (!/^[a-z0-9-]+(\.[a-z0-9-]+)+$/.test(host)) return text;
  if (parseRules(text).some(r => r.host === host && !r.path)) return text;
  return (text.endsWith("\n") || !text ? text : text + "\n") + host + "\n";
}

// Where a site stands right now:
//   grace    - opened up on purpose; { until, total }
//   cooldown - a session just ended, so it's shut and can't be asked for the normal way; { until, total }
//   blocked  - shut; you can ask to open it
// Unlocks are stored as { until, total } per site. The cooldown comes from when the last one ended.
function phaseOf(site, unlocks, settings, now = Date.now()) {
  const u = unlocks[site];
  if (u && u.until > now) return { name: "grace", until: u.until, total: u.total };
  const total = settings.cooldownMin * 60000;
  if (u && total > 0 && u.until + total > now) return { name: "cooldown", until: u.until + total, total };
  return { name: "blocked", until: 0, total: 0 };
}

// One line to show you: a random affirmation, with {goal} filled in from your goals.
function pickLine(settings, avoid) {
  const lines = (settings.affirmations || "").split(/\r?\n/).map(l => l.trim()).filter(Boolean);
  const goals = (settings.goals || "").split(/\r?\n/).map(l => l.trim()).filter(Boolean);
  if (!lines.length) return "";
  let line;
  for (let i = 0; i < 6; i++) {
    line = lines[Math.floor(Math.random() * lines.length)];
    if (lines.length < 2 || line !== avoid) break;
  }
  const goal = goals.length ? goals[Math.floor(Math.random() * goals.length)] : "the work";
  return line.replace(/\{goal\}/g, goal);
}

function fmtClock(ms) {
  const s = Math.max(0, Math.ceil(ms / 1000));
  return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
}

function todayKey() {
  const d = new Date();
  return `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`;
}

if (typeof module !== "undefined") module.exports = { pickLine, DEFAULT_GOALS, DEFAULT_AFFIRMATIONS, DEFAULT_RULES, DEFAULT_SETTINGS, parseRules, matchRule, normHost, siteList, addSite, phaseOf, fmtClock, todayKey, niceName };
