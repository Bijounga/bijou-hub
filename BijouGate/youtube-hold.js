// Takes away YouTube's "hold to speed up": holding Space, or pressing and holding the mouse on the
// video, jumps playback to 2x. A quick tap still pauses and plays as normal. While a hold is in
// progress any speed change is undone, so the speed you chose stays put.
(() => {
  let enabled = true;
  let holding = false;
  let releaseTimer = 0;
  let chosenRate = 1;
  let styled = false;

  const apply = () => {
    if (styled) return;
    styled = true;
    const s = document.createElement("style");
    s.textContent = ".ytp-speedmaster-overlay, .ytp-speedmaster-user-edu { display: none !important; }";
    (document.head || document.documentElement).appendChild(s);
  };

  chrome.storage.local.get("settings", ({ settings }) => {
    enabled = !(settings && settings.noHold2x === false);
  });
  chrome.storage.onChanged.addListener(ch => {
    if (ch.settings) enabled = !(ch.settings.newValue && ch.settings.newValue.noHold2x === false);
  });

  const startHold = () => {
    clearTimeout(releaseTimer);
    if (!holding) {
      const v = document.querySelector("video");
      if (v) chosenRate = v.playbackRate; // the speed to keep, taken before anything can change it
    }
    holding = true;
  };
  // Stay "holding" a moment after release: YouTube sets the speed back then, and that must not be
  // mistaken for a speed you picked.
  const endHold = () => { clearTimeout(releaseTimer); releaseTimer = setTimeout(() => { holding = false; }, 300); };

  const typing = e => {
    const t = e.target;
    return t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName));
  };

  addEventListener("keydown", e => {
    if (!enabled || e.code !== "Space" || typing(e)) return;
    apply();
    startHold();
    // Key-repeat events are what YouTube treats as "still holding".
    if (e.repeat) { e.stopImmediatePropagation(); e.preventDefault(); }
  }, true);
  addEventListener("keyup", e => { if (e.code === "Space") endHold(); }, true);

  addEventListener("mousedown", e => {
    if (!enabled || e.button !== 0) return;
    if (e.target.closest && e.target.closest(".html5-video-player") && !e.target.closest(".ytp-chrome-bottom, .ytp-chrome-top, .ytp-popup")) {
      apply();
      startHold();
    }
  }, true);
  addEventListener("mouseup", endHold, true);
  addEventListener("blur", endHold);

  // Catch the speed jump itself, on whichever video element the page has.
  document.addEventListener("ratechange", e => {
    const v = e.target;
    if (!(v instanceof HTMLVideoElement)) return;
    if (!enabled) { chosenRate = v.playbackRate; return; }
    if (holding && v.playbackRate !== chosenRate) v.playbackRate = chosenRate;
    else if (!holding) chosenRate = v.playbackRate;
  }, true);
})();
