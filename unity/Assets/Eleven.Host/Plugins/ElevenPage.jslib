// Page-level facts and controls the host can't get from Unity.
mergeInto(LibraryManager.library, {
  // Milliseconds since the page started loading (navigation start), so the host can report how
  // long a cold load took, download and start-up included.
  ElevenPageMilliseconds: function () {
    return performance.now();
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

    // The page's one status line, also used by the feel test bench (#45).
    window.__elevenAnnounce = function (message) {
      status.textContent = message;
      status.style.opacity = '1';
      clearTimeout(window.__elevenStatusTimer);
      window.__elevenStatusTimer = setTimeout(function () { status.style.opacity = '0'; }, 6000);
    };

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
    if (window.__elevenAnnounce) window.__elevenAnnounce('Captured ' + summary + ', ' + kept + '.');
  },

  // FEEL / TEST BENCH (#45): a page panel listing every knob the host describes (one per
  // SimConfig.Tunables and PlayerCapabilities.Parameters entry), so touch, VoiceOver and the
  // keyboard work natively. Edits make a pending candidate; APPLY / RETRY hands the whole candidate
  // to the host, which refuses it whole or starts a fresh run with it. The panel never touches the
  // simulation, and opening or closing it is presentation only. Values cross as IEEE-754 bits, so
  // the host gets exactly the doubles shown; decimal is for display only.
  ElevenBenchInstall: function (textP, panelP, sheetP, fieldP, focusP, changedP, knobsP, presetP) {
    if (document.getElementById('eleven-bench-open')) return;
    var c = {
      text: UTF8ToString(textP), panel: UTF8ToString(panelP), sheet: UTF8ToString(sheetP),
      field: UTF8ToString(fieldP), focus: UTF8ToString(focusP), changed: UTF8ToString(changedP),
    };

    var fromBits = function (hex) {
      var v = new DataView(new ArrayBuffer(8));
      v.setUint32(0, parseInt(hex.slice(0, 8), 16));
      v.setUint32(4, parseInt(hex.slice(8, 16), 16));
      return v.getFloat64(0);
    };
    var toBits = function (x) {
      var v = new DataView(new ArrayBuffer(8));
      v.setFloat64(0, x);
      var h = function (n) { return ('00000000' + n.toString(16)).slice(-8); };
      return h(v.getUint32(0)) + h(v.getUint32(4));
    };
    // Six significant figures for reading; the fields hold the exact value.
    var show = function (x) { return isFinite(x) ? String(Number(x.toPrecision(6))) : String(x); };
    var parseKnobs = function (text) {
      return text.split('\n').filter(Boolean).map(function (line) {
        var f = line.split('\t');
        return {
          group: f[0], name: f[1], label: f[2], unit: f[3], min: fromBits(f[4]), max: fromBits(f[5]),
          startMin: f[6] === '-' ? null : fromBits(f[6]), startMax: f[7] === '-' ? null : fromBits(f[7]),
          step: fromBits(f[8]), decimals: parseInt(f[9], 10), value: fromBits(f[10]),
        };
      });
    };
    var describePreset = function (preset) {
      return preset === 'technical-reference' ? 'Technical reference values' : 'Unsaved values, not approved feel';
    };

    var bench = window.__elevenBench = { knobs: parseKnobs(UTF8ToString(knobsP)), pending: {}, rows: {}, preset: UTF8ToString(presetP) };
    bench.knobs.forEach(function (k) { bench.pending[k.name] = k.value; });

    var sheet = document.createElement('style');
    var focusRing = 'outline:3px solid ' + c.focus + ';outline-offset:2px;';
    sheet.textContent =
      '#eleven-bench-open:focus-visible,#eleven-bench :focus-visible{' + focusRing + '}' +
      '#eleven-bench{position:fixed;z-index:20;box-sizing:border-box;display:flex;flex-direction:column;background:' + c.sheet + ';color:' + c.text + ';' +
        'border:0 solid ' + c.text + ';font:16px/1.35 system-ui,-apple-system,sans-serif;}' +
      '#eleven-bench[hidden]{display:none;}' +
      '@media (orientation:landscape){#eleven-bench{top:0;right:0;bottom:0;width:min(30rem,100vw);border-left-width:2px;' +
        'padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) 0;}}' +
      '@media (orientation:portrait){#eleven-bench{left:0;right:0;bottom:0;height:78vh;border-top-width:2px;' +
        'padding:0 env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left);}}' +
      '#eleven-bench .head{padding:10px 16px 12px;border-bottom:2px solid ' + c.text + ';}' +
      '#eleven-bench h2{margin:0 0 4px;font-size:18px;letter-spacing:0.04em;}' +
      '#eleven-bench h2:focus{outline:none;}#eleven-bench h2:focus-visible{' + focusRing + '}' +
      '#eleven-bench .status{margin:0 0 8px;}' +
      '#eleven-bench .actions{display:flex;flex-wrap:wrap;gap:8px;}' +
      '#eleven-bench button{box-sizing:border-box;min-width:44px;min-height:44px;margin:0;padding:6px 12px;border:2px solid ' + c.text + ';border-radius:8px;' +
        'background:' + c.field + ';color:' + c.text + ';font:700 16px/1.1 system-ui,-apple-system,sans-serif;touch-action:manipulation;}' +
      '#eleven-bench button:disabled{border-style:dashed;cursor:not-allowed;}' +
      '#eleven-bench .list{flex:1;overflow-y:auto;-webkit-overflow-scrolling:touch;overscroll-behavior:contain;padding:0 16px 24px;}' +
      '#eleven-bench h3{margin:18px 0 2px;font-size:15px;letter-spacing:0.08em;text-transform:uppercase;}' +
      '#eleven-bench .knob{margin:10px 0 0;padding:2px 0 2px 10px;border-left:4px solid transparent;}' +
      '#eleven-bench .knob.changed{border-left-color:' + c.changed + ';}' +
      '#eleven-bench .knob.invalid{border-left-style:dashed;border-left-color:' + c.changed + ';}' +
      '#eleven-bench .name{font-weight:700;}' +
      '#eleven-bench .meta,#eleven-bench .state{font-size:14px;}' +
      '#eleven-bench .state{font-weight:700;}' +
      '#eleven-bench .controls{display:flex;align-items:center;gap:8px;margin-top:4px;}' +
      '#eleven-bench .controls button{width:44px;padding:0;font-size:22px;}' +
      '#eleven-bench input.value{box-sizing:border-box;width:7.5em;min-height:44px;margin:0;padding:4px 8px;border:2px solid ' + c.text + ';border-radius:6px;' +
        'background:' + c.field + ';color:' + c.text + ';font:16px system-ui,-apple-system,sans-serif;}' +
      '#eleven-bench input[type=range]{flex:1;min-width:5em;height:44px;margin:0;accent-color:' + c.focus + ';}';
    document.head.appendChild(sheet);

    var open = document.createElement('button');
    open.id = 'eleven-bench-open';
    open.type = 'button';
    open.innerHTML = 'TEST<br>BENCH';
    open.setAttribute('aria-label', 'Test bench: tune the feel');
    open.setAttribute('aria-controls', 'eleven-bench');
    open.setAttribute('aria-expanded', 'false');
    open.style.cssText =
      'position:fixed;z-index:10;box-sizing:border-box;min-width:44px;min-height:44px;margin:0;display:none;' +
      'padding:4px 6px;border:2px solid ' + c.text + ';border-radius:8px;background:' + c.panel + ';' +
      'color:' + c.text + ';font:700 13px/1.15 system-ui,-apple-system,sans-serif;letter-spacing:0.04em;' +
      'text-align:center;touch-action:manipulation;-webkit-user-select:none;user-select:none;';

    var panel = document.createElement('section');
    panel.id = 'eleven-bench';
    panel.hidden = true;
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('aria-labelledby', 'eleven-bench-title');
    panel.innerHTML =
      '<div class="head"><h2 id="eleven-bench-title" tabindex="-1">FEEL / TEST BENCH</h2>' +
      '<p class="status" id="eleven-bench-status" role="status" aria-live="polite"></p>' +
      '<div class="actions"><button type="button" id="eleven-bench-apply">APPLY / RETRY</button>' +
      '<button type="button" id="eleven-bench-discard">DISCARD</button>' +
      '<button type="button" id="eleven-bench-close">CLOSE</button></div></div>' +
      '<div class="list"></div>';
    var list = panel.querySelector('.list');
    var status = panel.querySelector('#eleven-bench-status');
    var apply = panel.querySelector('#eleven-bench-apply');

    // Keep page input to the page, whatever the player listens to.
    ['pointerdown', 'mousedown', 'touchstart', 'wheel'].forEach(function (type) {
      panel.addEventListener(type, function (e) { e.stopPropagation(); }, { passive: true });
    });

    var valid = function (k, v) { return isFinite(v) && v >= k.min && v <= k.max; };
    var same = function (a, b) { return toBits(a) === toBits(b); };
    var range = function (k) { return show(k.min) + '–' + show(k.max) + ' ' + k.unit; };

    var update = function (k, fromField) {
      var row = bench.rows[k.name], v = bench.pending[k.name];
      var ok = valid(k, v), changed = ok && !same(v, k.value);
      if (!fromField) row.field.value = isFinite(v) ? String(v) : row.field.value;
      if (ok) row.slider.value = String(v);
      row.slider.setAttribute('aria-valuetext', ok ? show(v) + ' ' + k.unit : 'invalid');
      row.field.setAttribute('aria-invalid', ok ? 'false' : 'true');
      row.box.classList.toggle('changed', changed);
      row.box.classList.toggle('invalid', !ok);
      row.state.textContent = !ok
        ? (isFinite(v) ? 'Out of range: valid ' + range(k) + '.' : 'Not a number: valid ' + range(k) + '.')
        : changed ? 'Changed, was ' + show(k.value) + '.' : '';
    };

    var refresh = function () {
      var changes = 0, invalid = [];
      bench.knobs.forEach(function (k) {
        var v = bench.pending[k.name];
        if (!valid(k, v)) invalid.push(k.label);
        else if (!same(v, k.value)) changes++;
      });
      apply.disabled = invalid.length > 0;
      status.textContent = invalid.length > 0
        ? (invalid.length === 1 ? invalid[0] + ' is out of range: fix it to apply.' : invalid.length + ' values are out of range (' + invalid.join(', ') + '): fix them to apply.')
        : changes > 0
          ? changes + ' pending change' + (changes === 1 ? '' : 's') + '. APPLY / RETRY starts a fresh run with ' + (changes === 1 ? 'it' : 'them') + '.'
          : 'No pending changes. ' + describePreset(bench.preset) + '. APPLY / RETRY restarts the practice.';
      bench.changes = changes;
      bench.invalid = invalid.length;
    };

    var nudge = function (k, direction) {
      var v = bench.pending[k.name];
      if (!isFinite(v)) v = k.value;
      var places = Math.max(k.decimals, 0);
      var n = direction > 0 ? Math.floor(v / k.step + 1e-9) + 1 : Math.ceil(v / k.step - 1e-9) - 1;
      var next = Number((n * k.step).toFixed(places));
      bench.pending[k.name] = Math.min(k.max, Math.max(k.min, next));
      update(k, false);
      refresh();
    };

    var group = '';
    bench.knobs.forEach(function (k) {
      if (k.group !== group) {
        group = k.group;
        var heading = document.createElement('h3');
        heading.textContent = group;
        list.appendChild(heading);
      }

      var id = 'eleven-knob-' + k.name;
      var box = document.createElement('div');
      box.className = 'knob';
      box.setAttribute('role', 'group');
      box.setAttribute('aria-labelledby', id + '-name');
      var start = k.startMin === null ? 'technical reference' : 'research start ' + show(k.startMin) + '–' + show(k.startMax);
      box.innerHTML =
        '<div><span class="name" id="' + id + '-name"></span> <span class="unit"></span></div>' +
        '<div class="meta" id="' + id + '-meta"></div>' +
        '<div class="controls"><button type="button" class="less"></button>' +
        '<input class="value" type="text" inputmode="decimal" autocomplete="off" spellcheck="false" enterkeyhint="done">' +
        '<button type="button" class="more"></button>' +
        '<input type="range"></div>' +
        '<div class="state" id="' + id + '-state"></div>';
      box.querySelector('.name').textContent = k.label;
      box.querySelector('.unit').textContent = '(' + k.unit + ')';
      box.querySelector('.meta').textContent = 'Current ' + show(k.value) + ' · ' + start + ' · valid ' + show(k.min) + '–' + show(k.max);

      var row = bench.rows[k.name] = {
        box: box, meta: box.querySelector('.meta'), state: box.querySelector('.state'),
        field: box.querySelector('input.value'), slider: box.querySelector('input[type=range]'),
        less: box.querySelector('.less'), more: box.querySelector('.more'), start: start,
      };
      row.less.textContent = '−';
      row.more.textContent = '+';
      row.less.setAttribute('aria-label', 'Decrease ' + k.label + ' by ' + show(k.step));
      row.more.setAttribute('aria-label', 'Increase ' + k.label + ' by ' + show(k.step));
      row.field.setAttribute('aria-label', k.label + ', ' + k.unit);
      row.field.setAttribute('aria-describedby', id + '-meta ' + id + '-state');
      row.slider.setAttribute('aria-label', k.label + ' slider');
      row.slider.setAttribute('aria-describedby', id + '-meta');
      row.slider.min = String(k.min);
      row.slider.max = String(k.max);
      row.slider.step = String(k.step);

      row.less.addEventListener('click', function () { nudge(k, -1); });
      row.more.addEventListener('click', function () { nudge(k, 1); });
      row.field.addEventListener('input', function () {
        var text = row.field.value.trim().replace(',', '.');
        bench.pending[k.name] = text === '' ? NaN : Number(text);
        update(k, true);
        refresh();
      });
      row.slider.addEventListener('input', function () {
        bench.pending[k.name] = Number(row.slider.value);
        update(k, false);
        refresh();
      });
      list.appendChild(box);
      update(k, false);
    });

    var setOpen = function (visible) {
      panel.hidden = !visible;
      open.setAttribute('aria-expanded', visible ? 'true' : 'false');
      if (visible) {
        refresh();
        panel.querySelector('#eleven-bench-title').focus();
      } else {
        open.focus();
      }
      console.log('ELEVEN bench ' + (visible ? 'opened' : 'closed'));
    };

    open.addEventListener('click', function () { setOpen(panel.hidden); });
    panel.querySelector('#eleven-bench-close').addEventListener('click', function () { setOpen(false); });
    panel.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') { e.preventDefault(); setOpen(false); }
    });
    panel.querySelector('#eleven-bench-discard').addEventListener('click', function () {
      bench.knobs.forEach(function (k) { bench.pending[k.name] = k.value; update(k, false); });
      refresh();
    });
    apply.addEventListener('click', function () {
      if (bench.invalid > 0) return;
      var values = bench.knobs.map(function (k) { return k.name + '=' + toBits(bench.pending[k.name]); }).join('\n');
      window.__elevenBenchApply = { values: values, pressed: performance.now() };
      status.textContent = 'Applying…';
    });

    // The host's answers.
    bench.applied = function (knobs, preset, config, changes, hostMs) {
      var pressed = window.__elevenBenchPressed;
      var pageMs = pressed ? performance.now() - pressed : NaN;
      bench.preset = preset;
      parseKnobs(knobs).forEach(function (fresh) {
        var k = bench.knobs.filter(function (x) { return x.name === fresh.name; })[0];
        if (!k) return;
        k.value = fresh.value;
        bench.pending[k.name] = k.value;
        bench.rows[k.name].meta.textContent = 'Current ' + show(k.value) + ' · ' + bench.rows[k.name].start + ' · valid ' + show(k.min) + '–' + show(k.max);
        update(k, false);
      });
      refresh();
      setOpen(false);
      var ms = isFinite(pageMs) ? Math.round(pageMs) : Math.round(hostMs);
      var what = changes === 0 ? 'Restarted' : 'Applied ' + changes + ' change' + (changes === 1 ? '' : 's') + ',';
      if (window.__elevenAnnounce) window.__elevenAnnounce(what + ' fresh run in ' + ms + ' ms. ' + describePreset(preset) + '.');
      console.log('ELEVEN bench page applied pageMs=' + (isFinite(pageMs) ? pageMs.toFixed(1) : 'none') + ' hostMs=' + hostMs.toFixed(1) + ' config=' + config);
    };
    bench.refused = function (problem) {
      status.textContent = 'Not applied: ' + problem;
    };

    document.body.appendChild(open);
    document.body.appendChild(panel);
  },

  // Lays the TEST BENCH button over the host's rect, given in the canvas's backing pixels.
  ElevenBenchPlace: function (x, y, w, h, screenWidth, screenHeight) {
    var open = document.getElementById('eleven-bench-open');
    if (!open || !Module.canvas) return;
    var r = Module.canvas.getBoundingClientRect();
    var sx = r.width / screenWidth, sy = r.height / screenHeight;
    open.style.left = (r.left + x * sx) + 'px';
    open.style.top = (r.top + y * sy) + 'px';
    open.style.width = Math.max(44, w * sx) + 'px';
    open.style.height = Math.max(44, h * sy) + 'px';
    open.style.display = 'block';
  },

  // The candidate of an APPLY / RETRY press since the last call (every knob as name=bits), or null.
  ElevenTakeBenchApply: function () {
    var pressed = window.__elevenBenchApply;
    if (!pressed) return null;
    window.__elevenBenchApply = null;
    window.__elevenBenchPressed = pressed.pressed;
    var size = lengthBytesUTF8(pressed.values) + 1;
    var buffer = _malloc(size);
    stringToUTF8(pressed.values, buffer, size);
    return buffer;
  },

  ElevenBenchApplied: function (knobsP, presetP, configP, changes, hostMs) {
    if (window.__elevenBench) window.__elevenBench.applied(UTF8ToString(knobsP), UTF8ToString(presetP), UTF8ToString(configP), changes, hostMs);
  },

  ElevenBenchRefused: function (problemP) {
    if (window.__elevenBench) window.__elevenBench.refused(UTF8ToString(problemP));
  },
});
