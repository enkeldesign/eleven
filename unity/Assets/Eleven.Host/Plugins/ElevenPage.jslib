// Page-level facts and controls the host can't get from Unity.
mergeInto(LibraryManager.library, {
  // Milliseconds since the page started loading (navigation start), so the host can report how
  // long a cold load took, download and start-up included.
  ElevenPageMilliseconds: function () {
    return performance.now();
  },

  // The canvas's width in CSS pixels (#48), so the host can make a UI point a CSS pixel.
  ElevenCanvasCssWidth: function () {
    return Module.canvas ? Module.canvas.getBoundingClientRect().width : 0;
  },

  // THIS FELT WRONG (#44): a real page button over the canvas, so assistive technology, the keyboard
  // and touch all reach it, with a status line announced politely and a SAVE CAPTURE button that
  // shares or downloads the last capture. A press asks for an optional note with the browser's own
  // prompt (while it is open no frames run, so the simulation does not advance), then leaves the
  // note for the host to collect on its next frame. Everything stays on the device: no network.
  // Colours are the host's semantic tokens (HostPalette), passed in as CSS colours.
  ElevenFeltWrongInstall: function (textPointer, panelPointer, borderPointer, focusPointer) {
    if (document.getElementById('eleven-felt-wrong')) return;
    var text = UTF8ToString(textPointer), panel = UTF8ToString(panelPointer);
    var border = UTF8ToString(borderPointer), focus = UTF8ToString(focusPointer);

    // A visible keyboard focus ring of at least 3:1 (WCAG 1.4.11), whatever the browser's default.
    var sheet = document.createElement('style');
    sheet.textContent = '#eleven-felt-wrong:focus-visible,#eleven-save-capture:focus-visible{outline:3px solid ' + focus + ';outline-offset:2px;}';
    document.head.appendChild(sheet);

    var style =
      'position:fixed;z-index:10;box-sizing:border-box;min-width:44px;min-height:44px;margin:0;' +
      'padding:4px 6px;border:2px solid ' + border + ';border-radius:8px;background:' + panel + ';' +
      'color:' + text + ';font:700 13px/1.15 system-ui,-apple-system,sans-serif;letter-spacing:0.04em;' +
      'text-align:center;touch-action:manipulation;-webkit-user-select:none;user-select:none;';

    var felt = document.createElement('button');
    felt.id = 'eleven-felt-wrong';
    felt.type = 'button';
    felt.innerHTML = 'FELT<br>WRONG';
    felt.setAttribute('aria-label', 'This felt wrong: capture the last 10 seconds');
    felt.style.cssText = style + 'display:none;';

    var save = document.createElement('button');
    save.id = 'eleven-save-capture';
    save.type = 'button';
    save.textContent = 'SAVE CAPTURE';
    save.setAttribute('aria-label', 'Save capture: share or download the last capture');
    save.style.cssText = style + 'display:none;';

    var status = document.createElement('div');
    status.id = 'eleven-capture-status';
    status.setAttribute('role', 'status');
    status.setAttribute('aria-live', 'polite');
    status.style.cssText =
      'position:fixed;z-index:10;left:50%;top:8px;transform:translateX(-50%);max-width:90vw;padding:6px 10px;' +
      'border:2px solid ' + border + ';border-radius:8px;background:' + panel + ';color:' + text + ';' +
      'font:600 14px/1.3 system-ui,-apple-system,sans-serif;opacity:0;pointer-events:none;';
    // Always rendered, so screen readers announce its changes; idle it is empty and transparent.

    felt.addEventListener('click', function () {
      var note = window.prompt('What felt wrong? (optional)', '');
      // Cancel keeps the capture, without a note: the press is the capture.
      window.__elevenFeltWrong = { note: note === null ? '' : note };
    });

    save.addEventListener('click', function () {
      var last = window.__elevenLastCapture;
      if (!last) return;
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
      if (navigator.canShare && navigator.canShare({ files: [file] })) {
        navigator.share({ files: [file], title: 'ELEVEN capture' }).catch(download);
      } else {
        download();
      }
    });

    document.body.appendChild(felt);
    document.body.appendChild(save);
    document.body.appendChild(status);

    // A capture kept on an earlier visit stays exportable: SAVE CAPTURE offers the newest one.
    try {
      var kept = JSON.parse(localStorage.getItem('eleven.captures') || '[]');
      var newest = kept[kept.length - 1];
      if (newest && newest.name && newest.data) {
        var binary = atob(newest.data);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        window.__elevenLastCapture = { bytes: bytes, name: newest.name };
        save.style.display = 'block';
      }
    } catch (e) {
      // Storage unavailable or unreadable: nothing kept to offer.
    }
  },

  // Lays the button over the host's rect, given in the canvas's backing pixels (top-left origin),
  // with SAVE CAPTURE just above it.
  ElevenFeltWrongPlace: function (x, y, w, h, screenWidth, screenHeight) {
    var felt = document.getElementById('eleven-felt-wrong');
    var save = document.getElementById('eleven-save-capture');
    if (!felt || !Module.canvas) return;
    var r = Module.canvas.getBoundingClientRect();
    var sx = r.width / screenWidth, sy = r.height / screenHeight;
    var place = function (el, top) {
      el.style.left = (r.left + x * sx) + 'px';
      el.style.top = top + 'px';
      el.style.width = Math.max(44, w * sx) + 'px';
      el.style.height = Math.max(44, h * sy) + 'px';
    };
    place(felt, r.top + y * sy);
    felt.style.display = 'block';
    place(save, r.top + y * sy - Math.max(44, h * sy) - 6);
  },

  // The note of a press since the last call, or null: the host polls this once a frame.
  ElevenTakeFeltWrong: function () {
    var pressed = window.__elevenFeltWrong;
    if (!pressed) return null;
    window.__elevenFeltWrong = null;
    var size = lengthBytesUTF8(pressed.note) + 1;
    var buffer = _malloc(size);
    stringToUTF8(pressed.note, buffer, size);
    return buffer;
  },

  // Keeps a capture on the device (the last five, in localStorage), offers SAVE CAPTURE for it,
  // and announces it.
  ElevenKeepCapture: function (pointer, length, namePointer, summaryPointer) {
    var bytes = HEAPU8.slice(pointer, pointer + length);
    var name = UTF8ToString(namePointer);
    var summary = UTF8ToString(summaryPointer);
    window.__elevenLastCapture = { bytes: bytes, name: name };

    var binary = '';
    for (var i = 0; i < bytes.length; i += 8192) {
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 8192));
    }

    var kept = 'kept on this device';
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
    } catch (e) {
      kept = 'not kept on this device (storage unavailable): save it now';
    }

    var save = document.getElementById('eleven-save-capture');
    if (save) save.style.display = 'block';
    var status = document.getElementById('eleven-capture-status');
    if (status) {
      status.textContent = 'Captured ' + summary + ', ' + kept + '.';
      status.style.opacity = '1';
      clearTimeout(window.__elevenStatusTimer);
      window.__elevenStatusTimer = setTimeout(function () { status.style.opacity = '0'; }, 6000);
    }
  },
});
