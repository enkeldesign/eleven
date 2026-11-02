// Page-level facts and controls the host can't get from Unity.
mergeInto(LibraryManager.library, {
  // Milliseconds since the page started loading (navigation start), so the host can report how
  // long a cold load took, download and start-up included.
  ElevenPageMilliseconds: function () {
    return performance.now();
  },

  // The player's reduced-motion preference (#48), 1 or 0.
  ElevenPrefersReducedMotion: function () {
    return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 1 : 0;
  },

  // The canvas's width in CSS pixels (#48), so the host can make a UI point a CSS pixel.
  ElevenCanvasCssWidth: function () {
    return Module.canvas ? Module.canvas.getBoundingClientRect().width : 0;
  },

  // Captures (#44, #53) are a browser service with no UI: THIS FELT WRONG, SAVE CAPTURE and the
  // status line are Unity's. Everything stays on the device: no network.

  // Whether a capture kept on an earlier visit is here to save, 1 or 0. The newest becomes the one
  // SAVE CAPTURE offers.
  ElevenHasKeptCapture: function () {
    try {
      var kept = JSON.parse(localStorage.getItem('eleven.captures') || '[]');
      var newest = kept[kept.length - 1];
      if (!newest || !newest.name || !newest.data) return 0;
      var binary = atob(newest.data);
      var bytes = new Uint8Array(binary.length);
      for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
      window.__elevenLastCapture = { bytes: bytes, name: newest.name };
      return 1;
    } catch (e) {
      // Storage unavailable or unreadable: nothing kept to offer.
      return 0;
    }
  },

  // Keeps a capture on the device (the last five, in localStorage) for SAVE CAPTURE: 1 if it was
  // stored, 0 if storage is unavailable (it can still be saved this visit).
  ElevenKeepCapture: function (pointer, length, namePointer, summaryPointer) {
    var bytes = HEAPU8.slice(pointer, pointer + length);
    var name = UTF8ToString(namePointer);
    var summary = UTF8ToString(summaryPointer);
    window.__elevenLastCapture = { bytes: bytes, name: name };

    var binary = '';
    for (var i = 0; i < bytes.length; i += 8192) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 8192));
    }

    try {
      var list = JSON.parse(localStorage.getItem('eleven.captures') || '[]');
      list.push({ name: name, summary: summary, data: btoa(binary) });
      while (list.length > 5) list.shift();
      for (;;) {
        try {
          localStorage.setItem('eleven.captures', JSON.stringify(list));
          break;
        } catch (full) {
          if (list.length <= 1) throw full;
          list.shift();
        }
      }
      return 1;
    } catch (e) {
      return 0;
    }
  },

  // Shares the newest capture as a file where the browser can, otherwise downloads it. A share
  // needs the transient activation of a user gesture; a download needs none, so it is also the
  // fallback when the share can't start.
  $ElevenShareCapture: function () {
    var last = window.__elevenLastCapture;
    if (!last) return;
    window.__elevenSavedAt = performance.now();
    var file = new File([last.bytes], last.name, { type: 'application/octet-stream' });
    var download = function () {
      var url = URL.createObjectURL(file);
      var a = document.createElement('a');
      a.href = url;
      a.download = last.name;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
    };
    var active = !navigator.userActivation || navigator.userActivation.isActive;
    if (active && navigator.canShare && navigator.canShare({ files: [file] })) {
      navigator.share({ files: [file], title: 'ELEVEN capture' }).catch(function (e) {
        // A dismissed share sheet is the player's choice, not a failure.
        if (!e || e.name !== 'AbortError') download();
      });
    } else {
      download();
    }
  },

  // Where SAVE CAPTURE is, in the canvas's backing pixels, and whether a press there saves now
  // (ADR-0003, #53). The page's own handlers watch for a press that starts and ends there, and start
  // the share in the release's handler, where the browser's user-gesture requirement holds, whatever
  // the frame timing. Unity's own activation of SAVE CAPTURE then finds it already saved.
  ElevenPlaceSaveCapture__deps: ['$ElevenShareCapture'],
  ElevenPlaceSaveCapture: function (x, y, w, h, screenWidth, screenHeight, active) {
    window.__elevenSaveRect = active ? { x: x, y: y, w: w, h: h, sw: screenWidth, sh: screenHeight } : null;
    if (window.__elevenSaveWatching) return;
    window.__elevenSaveWatching = true;
    var inside = function (e) {
      var s = window.__elevenSaveRect;
      if (!s || !Module.canvas) return false;
      var r = Module.canvas.getBoundingClientRect();
      var px = (e.clientX - r.left) * s.sw / r.width;
      var py = (e.clientY - r.top) * s.sh / r.height;
      return px >= s.x && px <= s.x + s.w && py >= s.y && py <= s.y + s.h;
    };
    var pressed = false;
    window.addEventListener('pointerdown', function (e) { pressed = inside(e); }, true);
    window.addEventListener('pointerup', function (e) {
      var release = pressed && inside(e);
      pressed = false;
      if (release) ElevenShareCapture();
    }, true);
  },

  // SAVE CAPTURE was activated in Unity (a press or a key). If the page's release handler already
  // saved this press, there is nothing to do; otherwise (a key) it saves now, while the key's
  // transient activation still holds.
  ElevenSaveCapture__deps: ['$ElevenShareCapture'],
  ElevenSaveCapture: function () {
    if (window.__elevenSavedAt && performance.now() - window.__elevenSavedAt < 1000) return;
    ElevenShareCapture();
  },
});
