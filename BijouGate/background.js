importScripts("shared.js");

const GATE = chrome.runtime.getURL("gate.html");

async function getSettings() {
  const { settings } = await chrome.storage.local.get("settings");
  return { ...DEFAULT_SETTINGS, ...(settings || {}) };
}

async function getUnlocks() {
  const { unlocks } = await chrome.storage.local.get("unlocks");
  return unlocks || {};
}

async function todayCount(site) {
  const { days } = await chrome.storage.local.get("days");
  const today = (days && days.date === todayKey()) ? days : { date: todayKey(), counts: {} };
  return { today, count: today.counts[site] || 0 };
}

async function check(tabId, url, frameId) {
  if (frameId !== 0 || !url || url.startsWith(GATE)) return;
  const settings = await getSettings();
  const rule = matchRule(url, parseRules(settings.rules));
  if (!rule) { syncOverlay(tabId, null, 0); rememberPage(tabId, url); return; }
  const phase = phaseOf(rule.site, await getUnlocks(), settings);
  if (phase.name === "grace") { syncOverlay(tabId, rule.site, phase.until); rememberPage(tabId, url); return; }
  // Hand the gate the page this tab was on a moment ago, so it can say what you were watching.
  const from = await lastPage(tabId);
  let gate = `${GATE}?site=${encodeURIComponent(rule.site)}&u=${encodeURIComponent(url)}`;
  if (from && from !== url) gate += `&from=${encodeURIComponent(from)}`;
  chrome.tabs.update(tabId, { url: gate }).catch(() => {});
}

// The last page each tab was allowed to show. Kept in session storage because the worker sleeps.
async function rememberPage(tabId, url) {
  if (!/^https?:/.test(url)) return;
  const { last } = await chrome.storage.session.get("last");
  const map = last || {};
  map[tabId] = url;
  await chrome.storage.session.set({ last: map });
}

async function lastPage(tabId) {
  const { last } = await chrome.storage.session.get("last");
  return (last || {})[tabId] || null;
}

chrome.webNavigation.onBeforeNavigate.addListener(d => check(d.tabId, d.url, d.frameId));
// Single-page sites (YouTube, X) change the address without a real navigation.
chrome.webNavigation.onHistoryStateUpdated.addListener(d => check(d.tabId, d.url, d.frameId));
chrome.webNavigation.onCompleted.addListener(d => check(d.tabId, d.url, d.frameId));

// ---------- the little timer on the page while a site is open ----------

function overlayPage(until, label) {
  const ID = "bijougate-timer";
  document.getElementById(ID)?.remove();
  if (!until) return;
  const el = document.createElement("div");
  el.id = ID;
  el.style.cssText = "position:fixed;right:14px;bottom:14px;z-index:2147483647;padding:6px 10px;border-radius:8px;" +
    "background:#1b1530;color:#00d8ff;border:1px solid #316cc8;font:12px/1 Consolas,monospace;pointer-events:none;opacity:.92;box-shadow:0 2px 10px rgba(0,0,0,.35)";
  document.documentElement.appendChild(el);
  const fmt = ms => { const s = Math.max(0, Math.ceil(ms / 1000)); return String(Math.floor(s / 60)).padStart(2, "0") + ":" + String(s % 60).padStart(2, "0"); };
  const tick = () => {
    const left = until - Date.now();
    if (left <= 0) { clearInterval(iv); el.remove(); try { chrome.runtime.sendMessage({ type: "expired" }); } catch {} return; }
    el.textContent = label + " · " + fmt(left) + " left";
  };
  const iv = setInterval(tick, 1000);
  tick();
}

const overlayTabs = new Set();

async function syncOverlay(tabId, site, until) {
  if (!until && !overlayTabs.has(tabId)) return;
  try {
    await chrome.scripting.executeScript({ target: { tabId }, func: overlayPage, args: [until, "Grace"] });
    if (until) overlayTabs.add(tabId); else overlayTabs.delete(tabId);
  } catch { /* a page the browser won't let us touch */ }
}

// ---------- ending sessions ----------

// Pulls every tab that's on a shut site back to the gate.
async function sweep() {
  const settings = await getSettings();
  const rules = parseRules(settings.rules);
  const unlocks = await getUnlocks();
  const tabs = await chrome.tabs.query({});
  for (const t of tabs) {
    if (!t.url || t.url.startsWith(GATE)) continue;
    const rule = matchRule(t.url, rules);
    if (rule && phaseOf(rule.site, unlocks, settings).name !== "grace") check(t.id, t.url, 0);
  }
}

chrome.alarms.onAlarm.addListener(a => { if (a.name === "sweep" || a.name.startsWith("expire:")) sweep(); });
chrome.alarms.create("sweep", { periodInMinutes: 0.5 });
chrome.runtime.onStartup.addListener(sweep);

// The first set of reminder lines was replaced with plainer ones. If settings were saved with the
// old set untouched, swap in the new one; anything you wrote yourself stays.
chrome.runtime.onInstalled.addListener(async () => {
  const { settings } = await chrome.storage.local.get("settings");
  if (settings && typeof settings.affirmations === "string" && settings.affirmations.includes("Momentum beats mood")) {
    settings.affirmations = DEFAULT_AFFIRMATIONS;
    settings.goals = DEFAULT_GOALS;
    await chrome.storage.local.set({ settings });
  }
});
chrome.tabs.onRemoved.addListener(async id => {
  overlayTabs.delete(id);
  const { last } = await chrome.storage.session.get("last");
  if (last && id in last) { delete last[id]; await chrome.storage.session.set({ last }); }
});

chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  (async () => {
    const settings = await getSettings();
    const unlocks = await getUnlocks();
    if (msg.type === "info") {
      const phase = phaseOf(msg.site, unlocks, settings);
      const { count } = await todayCount(msg.site);
      reply({
        phase: phase.name, until: phase.until, count,
        shut: settings.maxUnlocksPerDay > 0 && count >= settings.maxUnlocksPerDay,
        waitSec: settings.waitSec, holdSec: settings.holdSec,
        minutesChoices: settings.minutesChoices, overrideChoices: settings.overrideChoices,
        affirmations: settings.affirmations, goals: settings.goals,
      });
    } else if (msg.type === "unlock") {
      const phase = phaseOf(msg.site, unlocks, settings);
      const { today, count } = await todayCount(msg.site);
      const override = msg.kind === "override";
      const choices = override ? settings.overrideChoices : settings.minutesChoices;
      const allowed = override ? phase.name === "cooldown" : phase.name === "blocked";
      const underLimit = !(settings.maxUnlocksPerDay > 0 && count >= settings.maxUnlocksPerDay);
      if (!allowed || !underLimit || !choices.includes(msg.minutes)) return reply({ ok: false });
      const until = Date.now() + msg.minutes * 60000;
      unlocks[msg.site] = { until, total: msg.minutes * 60000 };
      today.counts[msg.site] = count + 1;
      const { log } = await chrome.storage.local.get("log");
      const entries = (log || []).slice(-299);
      entries.push({ t: Date.now(), site: msg.site, minutes: msg.minutes, kind: override ? "override" : "normal", why: msg.why, after: msg.after, url: msg.url });
      await chrome.storage.local.set({ unlocks, days: today, log: entries });
      chrome.alarms.create(`expire:${msg.site}`, { when: until + 500 });
      reply({ ok: true });
    } else if (msg.type === "relock") {
      // Closing a session early still starts the cooldown, or reopening would be a loophole.
      const u = unlocks[msg.site];
      if (u && u.until > Date.now()) {
        unlocks[msg.site] = { until: Date.now(), total: u.total };
        await chrome.storage.local.set({ unlocks });
      }
      await sweep();
      reply({ ok: true });
    } else if (msg.type === "expired") {
      await sweep();
      reply({ ok: true });
    }
  })();
  return true;
});
