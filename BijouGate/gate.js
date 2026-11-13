const params = new URLSearchParams(location.search);
const site = params.get("site") || "this site";
const target = params.get("u") || "";
const $ = id => document.getElementById(id);

let fromReady = false;
const show = id => {
  for (const s of document.querySelectorAll("section")) s.hidden = s.id !== id;
  // The page you came from is only worth showing on the first screen and the cooldown screen.
  $("from").hidden = !(fromReady && (id === "s1" || id === "cool"));
};

// What you were on before the gate stepped in: a YouTube video gets its title, thumbnail and channel.
const fromUrl = params.get("from");
async function showFrom() {
  let u;
  try { u = new URL(fromUrl); } catch { return; }
  if (u.protocol !== "https:" && u.protocol !== "http:") return;
  const host = u.hostname.replace(/^www\.|^m\./, "");
  const isVideo = (host === "youtube.com" && u.pathname === "/watch" && u.searchParams.get("v")) || host === "youtu.be";
  let title = "", by = host;
  if (isVideo) {
    try {
      const r = await fetch(`https://www.youtube.com/oembed?format=json&url=${encodeURIComponent(fromUrl)}`);
      const j = await r.json();
      title = j.title; by = j.author_name || host;
      const img = document.createElement("img");
      img.alt = "";
      img.src = j.thumbnail_url;
      $("from").prepend(img);
    } catch { title = "The video you were watching"; }
  } else if (host === "youtube.com" && u.pathname === "/results") {
    title = `Search: ${u.searchParams.get("search_query") || ""}`;
  } else if (host === "youtube.com" && u.pathname === "/playlist") {
    title = "The playlist you had open";
  } else {
    title = "The page you were on";
  }
  $("fromtitle").textContent = title;
  $("fromby").textContent = by;
  $("from").href = fromUrl;
  fromReady = true;
  const visible = ["s1", "cool"].find(id => !$(id).hidden);
  $("from").hidden = !visible;
}
if (fromUrl) showFrom();
const leave = () => { if (history.length > 1) history.back(); else window.close(); };
for (const id of ["leave", "leave2", "leave3"]) $(id).onclick = leave;

const name = niceName(site);
$("title").innerHTML = `You're about to open <em></em>`;
$("title").querySelector("em").textContent = name;
$("sub").textContent = "Is this part of the work, or is it a detour?";
document.title = `${name} · hold on`;

let info = null;
let lastLine = "";
const line = () => (lastLine = pickLine(info || {}, lastLine));
const say = ids => { const l = line(); for (const id of ids) $(id).textContent = l; };

chrome.runtime.sendMessage({ type: "info", site }, r => {
  info = r;
  if (r.phase === "grace") { location.replace(target || `https://${site}/`); return; }
  say(["a1", "a3", "a4", "a5"]);
  if (r.phase === "cooldown") { startCooldown(); return; }
  if (r.shut) { $("sub").textContent = `You've opened ${name} ${r.count} times today.`; show("shut"); }
});

$("go").onclick = () => { if (info && info.phase === "blocked" && !info.shut) { show("s2"); $("why").focus(); } };

// Step 2: two short answers. Enter moves along.
const answered = () => $("why").value.trim().length >= 8 && $("after").value.trim().length >= 8;
for (const id of ["why", "after"]) $(id).addEventListener("input", () => { $("next2").disabled = !answered(); });
$("why").addEventListener("keydown", e => { if (e.key === "Enter") $("after").focus(); });
$("after").addEventListener("keydown", e => { if (e.key === "Enter" && answered()) $("next2").click(); });
$("next2").onclick = () => { show("s3"); startWait(); };

// Step 3: the clock only moves while this tab is visible and focused; leaving resets it.
// The wait is also when the lines change, since it's the moment they're most useful.
function startWait() {
  const total = info.waitSec;
  const bar = $("bar");
  const circ = 2 * Math.PI * 88;
  bar.style.strokeDasharray = circ;
  let left = total;
  const draw = () => {
    $("count").textContent = Math.max(left, 0);
    bar.style.strokeDashoffset = circ * (1 - Math.max(left, 0) / Math.max(total, 1));
  };
  const tick = () => {
    if (document.hidden || !document.hasFocus()) left = total; else left -= 1;
    draw();
    if (left <= 0) { clearInterval(iv); clearInterval(rotate); showChoices(false); }
  };
  draw();
  const iv = setInterval(tick, 1000);
  const rotate = setInterval(() => {
    const el = $("a3");
    el.classList.add("fade");
    setTimeout(() => { el.textContent = line(); el.classList.remove("fade"); }, 350);
  }, 6000);
  if (total <= 0) { clearInterval(iv); clearInterval(rotate); showChoices(false); }
}

// Step 4: pick a time.
function showChoices(override) {
  show("s5");
  $("choosemsg").textContent = override ? "Fine. How long?" : "How long do you actually need?";
  const box = $("choices");
  box.textContent = "";
  for (const m of override ? info.overrideChoices : info.minutesChoices) {
    const b = document.createElement("button");
    b.innerHTML = `${m} <small>min</small>`;
    b.onclick = () => {
      for (const x of box.children) x.disabled = true;
      chrome.runtime.sendMessage({
        type: "unlock", site, minutes: m, url: target, kind: override ? "override" : "normal",
        why: $("why").value.trim() || "(held the button)", after: $("after").value.trim(),
      }, r => {
        if (r && r.ok) location.replace(target || `https://${site}/`);
        else for (const x of box.children) x.disabled = false;
      });
    };
    box.appendChild(b);
  }
  box.firstElementChild && box.firstElementChild.focus();
}

// A session just ended, so the site is shut for a while. Only holding the button gets past it.
function startCooldown() {
  $("sub").textContent = "You just had your time. Give it a break.";
  show("cool");
  const tick = () => {
    const left = info.until - Date.now();
    if (left <= 0) { location.reload(); return; }
    $("coolclock").textContent = fmtClock(left);
  };
  tick();
  setInterval(tick, 1000);

  const hold = $("hold"), fill = $("holdfill");
  let startedAt = 0, raf = 0;
  const reset = () => {
    cancelAnimationFrame(raf);
    startedAt = 0;
    fill.style.width = "0%";
    $("holdtext").textContent = "Hold to open anyway";
  };
  const frame = () => {
    if (!startedAt) return;
    const need = info.holdSec * 1000;
    const done = Math.min((performance.now() - startedAt) / need, 1);
    fill.style.width = `${done * 100}%`;
    $("holdtext").textContent = `Keep holding… ${Math.ceil(info.holdSec * (1 - done))}`;
    if (done >= 1) { reset(); showChoices(true); return; }
    raf = requestAnimationFrame(frame);
  };
  hold.addEventListener("pointerdown", e => {
    e.preventDefault();
    hold.setPointerCapture(e.pointerId);
    startedAt = performance.now();
    raf = requestAnimationFrame(frame);
  });
  for (const ev of ["pointerup", "pointercancel", "lostpointercapture"]) hold.addEventListener(ev, reset);
  hold.addEventListener("contextmenu", e => e.preventDefault());
  addEventListener("blur", reset);
}
