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

  // The part of the canvas the page shows, in CSS pixels from the canvas's top (#55). On iOS the
  // on-screen keyboard covers the canvas's lower part without resizing it; only the visual viewport
  // says how much is still in view.
  ElevenVisibleTop: function () {
    var c = Module.canvas;
    var vv = window.visualViewport;
    if (!c || !vv) return 0;
    var r = c.getBoundingClientRect();
    return Math.max(0, Math.min(r.height, vv.offsetTop - r.top));
  },

  ElevenVisibleBottom: function () {
    var c = Module.canvas;
    if (!c) return 0;
    var r = c.getBoundingClientRect();
    var vv = window.visualViewport;
    if (!vv) return r.height;
    return Math.max(0, Math.min(r.height, vv.offsetTop + vv.height - r.top));
  },

  // Captures (#44, #53) are a browser service with no UI: THIS FELT WRONG, SAVE CAPTURE and the
  // status line are Unity's. Everything stays on the device: no network.

  // A text kept on the device under a key (#46: the named presets), or the empty string. In
  // localStorage, so it is the same for every build served from this origin.
  ElevenReadStored: function (keyPointer) {
    var value = '';
    try {
      value = localStorage.getItem(UTF8ToString(keyPointer)) || '';
    } catch (e) {
      // Storage unavailable: nothing kept.
    }
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  // Keeps a text on the device under a key: 1 if it was kept, 0 if storage refused it.
  ElevenWriteStored: function (keyPointer, valuePointer) {
    try {
      localStorage.setItem(UTF8ToString(keyPointer), UTF8ToString(valuePointer));
      return 1;
    } catch (e) {
      return 0;
    }
  },

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
    var share = active && navigator.canShare && navigator.canShare({ files: [file] });
    console.log('ELEVEN page save ' + (share ? 'share' : 'download') + ' name=' + last.name + ' activation=' + (active ? 'yes' : 'no'));
    if (share) {
      // No title (#137): a target that keeps the title and drops the file showed "ELEVEN capture"
      // as if it were the capture. COPY CAPTURE is the way for such a target.
      navigator.share({ files: [file] }).catch(function (e) {
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
    console.log('ELEVEN page save target ' + (active ? Math.round(x) + ',' + Math.round(y) + ',' + Math.round(w) + ',' + Math.round(h) + ' of ' + screenWidth + 'x' + screenHeight : 'none'));
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
      if (pressed) console.log('ELEVEN page save release ' + (release ? 'on' : 'off') + ' the target');
      pressed = false;
      if (release) ElevenShareCapture();
    }, true);
  },

  // SAVE CAPTURE was activated in Unity (a press or a key). If the page's release handler already
  // saved this press, there is nothing to do; otherwise (a key) it saves now, while the key's
  // transient activation still holds.
  ElevenSaveCapture__deps: ['$ElevenShareCapture'],
  ElevenSaveCapture: function () {
    var handled = window.__elevenSavedAt && performance.now() - window.__elevenSavedAt < 1000;
    console.log('ELEVEN page save activation ' + (handled ? 'already saved by the release' : 'saves now'));
    if (handled) return;
    ElevenShareCapture();
  },

  // The newest capture as text (#137): CaptureText's form, a header with its name, length and
  // FNV-1a checksum, its bytes in base64 in lines of 76, then END. eleven-sim replay reads it.
  $ElevenCaptureText: function (last) {
    var bytes = last.bytes;
    var hash = 2166136261;
    var binary = '';
    for (var i = 0; i < bytes.length; i++) {
      hash = Math.imul(hash ^ bytes[i], 16777619) >>> 0;
      binary += String.fromCharCode(bytes[i]);
    }
    var body = btoa(binary);
    var text = 'ELEVEN CAPTURE v1 name=' + last.name.replace(/ /g, '_') + ' bytes=' + bytes.length + ' fnv=' + ('00000000' + hash.toString(16)).slice(-8) + '\n';
    for (var j = 0; j < body.length; j += 76) text += body.substr(j, 76) + '\n';
    return text + 'END\n';
  },

  // Copies the newest capture as text, in the press's own handler where a browser lets a page
  // write the clipboard; a hidden text area and the copy command where the clipboard API refuses.
  $ElevenCopyCaptureText__deps: ['$ElevenCaptureText'],
  $ElevenCopyCaptureText: function () {
    var last = window.__elevenLastCapture;
    if (!last) return;
    window.__elevenCopiedAt = performance.now();
    var text = ElevenCaptureText(last);
    window.__elevenCopiedText = text;
    var fallback = function () {
      var area = document.createElement('textarea');
      area.value = text;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      var ok = false;
      try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
      area.remove();
      console.log('ELEVEN page copy ' + (ok ? 'ok' : 'failed') + ' via=command name=' + last.name + ' chars=' + text.length);
    };
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(function () {
        console.log('ELEVEN page copy ok via=clipboard name=' + last.name + ' chars=' + text.length);
      }, fallback);
    } else {
      fallback();
    }
  },

  // Where COPY CAPTURE is (#137), as for SAVE CAPTURE: the copy starts in the release's handler.
  ElevenPlaceCopyCapture__deps: ['$ElevenCopyCaptureText'],
  ElevenPlaceCopyCapture: function (x, y, w, h, screenWidth, screenHeight, active) {
    window.__elevenCopyRect = active ? { x: x, y: y, w: w, h: h, sw: screenWidth, sh: screenHeight } : null;
    if (window.__elevenCopyWatching) return;
    window.__elevenCopyWatching = true;
    var inside = function (e) {
      var c = window.__elevenCopyRect;
      if (!c || !Module.canvas) return false;
      var r = Module.canvas.getBoundingClientRect();
      var px = (e.clientX - r.left) * c.sw / r.width;
      var py = (e.clientY - r.top) * c.sh / r.height;
      return px >= c.x && px <= c.x + c.w && py >= c.y && py <= c.y + c.h;
    };
    var pressed = false;
    window.addEventListener('pointerdown', function (e) { pressed = inside(e); }, true);
    window.addEventListener('pointerup', function (e) {
      var release = pressed && inside(e);
      pressed = false;
      if (release) ElevenCopyCaptureText();
    }, true);
  },

  // COPY CAPTURE was activated in Unity: copied already by the release, or now (a key).
  ElevenCopyCapture__deps: ['$ElevenCopyCaptureText'],
  ElevenCopyCapture: function () {
    var handled = window.__elevenCopiedAt && performance.now() - window.__elevenCopiedAt < 1000;
    if (!handled) ElevenCopyCaptureText();
  },
});
