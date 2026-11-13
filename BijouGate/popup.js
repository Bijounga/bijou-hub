const $ = id => document.getElementById(id);
let settings = DEFAULT_SETTINGS, unlocks = {}, tabHost = null;

async function load() {
  const data = await chrome.storage.local.get(["settings", "unlocks"]);
  settings = { ...DEFAULT_SETTINGS, ...(data.settings || {}) };
  unlocks = data.unlocks || {};
}

function render() {
  const now = Date.now();
  const sites = siteList(parseRules(settings.rules));
  const list = $("list");
  list.textContent = "";
  if (!sites.length) list.innerHTML = '<div class="empty">No sites yet. Add one below.</div>';
  for (const site of sites) {
    const p = phaseOf(site, unlocks, settings, now);
    const row = document.createElement("div");
    row.className = `row ${p.name}`;
    const label = p.name === "grace" ? `Grace · ${fmtClock(p.until - now)} left`
      : p.name === "cooldown" ? `Cooldown · ${fmtClock(p.until - now)}`
      : "Blocked";
    const frac = p.total ? Math.max(0, Math.min(1, (p.until - now) / p.total)) : 0;
    row.innerHTML = `<div class="top"><span class="name"></span><span class="pill ${p.name}"></span></div>` +
      `<div class="track"><div class="fill" style="width:${frac * 100}%"></div></div>`;
    row.querySelector(".name").textContent = niceName(site);
    row.querySelector(".pill").textContent = label;
    if (p.name === "grace") {
      const end = document.createElement("button");
      end.className = "end";
      end.textContent = "Shut it now";
      end.onclick = async () => { await chrome.runtime.sendMessage({ type: "relock", site }); await load(); render(); };
      row.appendChild(end);
    }
    list.appendChild(row);
  }

  // Offer to add the site in the current tab.
  const covered = tabHost && sites.includes(normHost(tabHost));
  $("add").hidden = !tabHost || covered;
  if (tabHost) $("addbtn").textContent = `Block ${tabHost}`;
}

$("edit").onclick = () => chrome.runtime.openOptionsPage();
$("addbtn").onclick = async () => {
  const { settings: saved } = await chrome.storage.local.get("settings");
  const next = { ...DEFAULT_SETTINGS, ...(saved || {}) };
  next.rules = addSite(next.rules, tabHost);
  await chrome.storage.local.set({ settings: next });
  await load();
  render();
};

(async () => {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  try {
    const u = new URL(tab.url);
    if (u.protocol === "http:" || u.protocol === "https:") tabHost = u.hostname.replace(/^www\./, "");
  } catch {}
  await load();
  render();
  setInterval(async () => { await load(); render(); }, 1000);
})();
