const $ = id => document.getElementById(id);

const nums = text => text.split(/[ ,]+/).map(Number).filter(n => n > 0 && n <= 240);

async function load() {
  const { settings, log } = await chrome.storage.local.get(["settings", "log"]);
  const s = { ...DEFAULT_SETTINGS, ...(settings || {}) };
  $("rules").value = s.rules;
  $("wait").value = s.waitSec;
  $("cool").value = s.cooldownMin;
  $("hold").value = s.holdSec;
  $("mins").value = s.minutesChoices.join(", ");
  $("omins").value = s.overrideChoices.join(", ");
  $("perday").value = s.maxUnlocksPerDay;
  $("nohold").checked = s.noHold2x !== false;
  $("goals").value = s.goals;
  $("affirm").value = s.affirmations;
  const start = new Date(); start.setHours(0, 0, 0, 0);
  const today = (log || []).filter(e => e.t >= start.getTime()).reverse();
  $("log").innerHTML = today.length ? "" : "<tr><td class='muted'>None yet.</td></tr>";
  for (const e of today) {
    const tr = document.createElement("tr");
    const kind = e.kind === "override" ? " · held" : "";
    for (const text of [new Date(e.t).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }), `${niceName(e.site)} · ${e.minutes} min${kind}`, e.why]) {
      const td = document.createElement("td"); td.textContent = text; tr.appendChild(td);
    }
    $("log").appendChild(tr);
  }
}

function flash(text) {
  $("saved").textContent = text;
  setTimeout(() => { $("saved").textContent = ""; }, 1800);
}

async function save() {
  const mins = nums($("mins").value), omins = nums($("omins").value);
  const settings = {
    rules: $("rules").value,
    waitSec: Math.max(0, +$("wait").value || 0),
    cooldownMin: Math.max(0, +$("cool").value || 0),
    holdSec: Math.max(1, +$("hold").value || 10),
    minutesChoices: mins.length ? mins : DEFAULT_SETTINGS.minutesChoices,
    overrideChoices: omins.length ? omins : DEFAULT_SETTINGS.overrideChoices,
    maxUnlocksPerDay: Math.max(0, +$("perday").value || 0),
    noHold2x: $("nohold").checked,
    goals: $("goals").value,
    affirmations: $("affirm").value,
  };
  await chrome.storage.local.set({ settings });
  flash("Saved.");
}

$("save").onclick = save;

$("preview").onclick = () => {
  const text = pickLine({ goals: $("goals").value, affirmations: $("affirm").value }, $("sample").textContent);
  $("sample").textContent = text || "Add a line above first.";
};

$("addsite").onclick = async () => {
  const before = $("rules").value;
  const after = addSite(before, $("newsite").value);
  if (after === before) { flash("Not a site, or already there."); return; }
  $("rules").value = after;
  $("newsite").value = "";
  await save();
};
$("newsite").addEventListener("keydown", e => { if (e.key === "Enter") $("addsite").click(); });

load();
