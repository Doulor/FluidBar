/* ==========================================================================
   FluidBar website — same-origin postMessage bridge for the prototype embed.
   --------------------------------------------------------------------------
   Generated embed.html loads: icons.js, app.js, then this file. It translates
   parent commands into real interactions inside docs/rewrite/prototype2 (the
   actual hidden demo-console buttons and the real pager keyboard handler) and
   mirrors the real DOM state back to the parent.

   Protocol (same-origin only; the parent must be the embedding window):
     parent -> iframe:
       { type: 'fluidbar:set', state?, page?, mode?, material?, accent?, motion? }
     iframe -> parent:
       { type: 'fluidbar:state', state: {
           ready, expanded, view, page, mode, material, theme, accent,
           width, height } }

   - `state`: 'idle' | 'media' | 'notifications' | 'clipboard' | 'agent' |
              'timer' | 'battery' | 'hud' | 'expanded'
   - `page`: integer 0..5 (ensures the island is expanded, then presses 1..6)
   - `mode`: 'float' | 'flush' | 'dock'
   - `material`: 'obsidian' | 'acrylic' | 'liquid'
   - `accent`: any CSS color string
   - `motion`: 'normal' | 'reduced'

   Unknown/invalid fields are ignored, never forwarded into the prototype.
   State is re-posted ~350ms after the last DOM change (plus transitionend),
   so the parent observes settled values.
   ========================================================================== */
(function () {
  'use strict';

  var COMPACT_VIEWS = ['idle', 'media', 'notifications', 'clipboard', 'agent', 'timer', 'battery', 'hud'];
  var MODES = ['float', 'flush', 'dock'];
  var MATERIALS = ['obsidian', 'acrylic', 'liquid'];
  var MOTIONS = ['normal', 'reduced'];
  var PAGE_COUNT = 6;
  var SETTLE_MS = 350;
  var MAX_ACCENT_LENGTH = 64;

  var MAX_PENDING = 16;
  var postTimer = 0;
  var lastSerialized = '';
  var lastReady = false;
  var pendingCommands = [];
  var desiredMaterial = null;
  var desiredAccent = null;
  var pinning = false;

  var TARGET_ORIGIN = window.location.origin && window.location.origin !== 'null'
    ? window.location.origin
    : '*';

  function byId(id) {
    return document.getElementById(id);
  }

  function island() {
    return byId('island');
  }

  function isExpanded() {
    var el = island();
    return !!el && el.getAttribute('data-expanded') === 'true';
  }

  function currentPage() {
    var no = document.querySelector('.pager-title .page-no');
    var value = no ? parseInt(no.textContent, 10) : NaN;
    if (!isFinite(value)) return 0;
    return Math.max(0, Math.min(PAGE_COUNT - 1, value - 1));
  }

  function click(selector) {
    var el = document.querySelector(selector);
    if (el) el.click();
    return !!el;
  }

  function dispatchPageKey(index) {
    document.dispatchEvent(new KeyboardEvent('keydown', {
      key: String(index + 1),
      bubbles: true,
      cancelable: true
    }));
  }

  /* ------------------------------------------------------------------ */
  /* Message validation                                                  */
  /* ------------------------------------------------------------------ */

  function isPlainObject(value) {
    return !!value && typeof value === 'object' && !Array.isArray(value);
  }

  function isOneOf(list, value) {
    return typeof value === 'string' && list.indexOf(value) !== -1;
  }

  function isValidPage(value) {
    return typeof value === 'number' && isFinite(value) && Math.floor(value) === value;
  }

  function isValidAccent(value) {
    if (typeof value !== 'string') return false;
    var color = value.trim();
    if (!color || color.length > MAX_ACCENT_LENGTH) return false;
    if (window.CSS && typeof window.CSS.supports === 'function') {
      return window.CSS.supports('color', color);
    }
    return /^(#[0-9a-f]{3,8}|[a-z]{3,24}|(rgb|rgba|hsl|hsla)\([^()]{1,48}\))$/i.test(color);
  }

  /* ------------------------------------------------------------------ */
  /* State snapshot + posting                                            */
  /* ------------------------------------------------------------------ */

  function snapshot() {
    var el = island();
    return {
      ready: document.body.classList.contains('ready'),
      expanded: isExpanded(),
      view: el ? el.getAttribute('data-view') : null,
      page: currentPage(),
      mode: document.body.dataset.mode || null,
      material: document.body.dataset.material || null,
      theme: document.body.dataset.theme || null,
      accent: getComputedStyle(document.documentElement).getPropertyValue('--accent').trim(),
      /* Layout size, so the parent never sees the temporary hover scale (1.022). */
      width: el ? Math.round(el.offsetWidth) : 0,
      height: el ? Math.round(el.offsetHeight) : 0
    };
  }

  function postNow() {
    if (!window.parent || window.parent === window) return;
    var payload = { type: 'fluidbar:state', state: snapshot() };
    var serialized = JSON.stringify(payload);
    if (serialized === lastSerialized) return;
    lastSerialized = serialized;
    try {
      window.parent.postMessage(payload, TARGET_ORIGIN);
    } catch (error) {
      /* The parent went away or is not same-origin; nothing to mirror. */
    }
  }

  function schedulePost(delay) {
    clearTimeout(postTimer);
    postTimer = setTimeout(postNow, typeof delay === 'number' ? delay : SETTLE_MS);
  }

  /* The prototype owns its own state and re-applies body datasets on every
     chrome sync (expand/collapse, mode, theme). Material and accent are set
     directly through the DOM contract, so pin them across those syncs. */
  function pinDesired() {
    if (pinning) return;
    pinning = true;
    try {
      if (desiredMaterial && document.body.dataset.material !== desiredMaterial) {
        document.body.dataset.material = desiredMaterial;
      }
      if (desiredAccent) {
        var current = document.documentElement.style.getPropertyValue('--accent');
        if (current.trim() !== desiredAccent) {
          document.documentElement.style.setProperty('--accent', desiredAccent);
        }
      }
    } finally {
      pinning = false;
    }
  }

  /* ------------------------------------------------------------------ */
  /* Parent -> iframe commands                                           */
  /* ------------------------------------------------------------------ */

  function applyCommand(message) {
    var applied = false;

    /* Mode / motion are real console settings, clicked through the hidden
       [#data-set] controls. They must be applied before the island state so
       the prototype's own syncChrome() cannot fight them on the same turn. */
    if (isOneOf(MODES, message.mode)) {
      applied = click('[data-set="mode"][data-value="' + message.mode + '"]') || applied;
    }
    if (isOneOf(MOTIONS, message.motion)) {
      applied = click('[data-set="motion"][data-value="' + message.motion + '"]') || applied;
    }
    if (isValidAccent(message.accent)) {
      desiredAccent = message.accent.trim();
      document.documentElement.style.setProperty('--accent', desiredAccent);
      applied = true;
    }
    if (isOneOf(MATERIALS, message.material)) {
      desiredMaterial = message.material;
      document.body.dataset.material = message.material;
      applied = true;
    }

    if (typeof message.state === 'string') {
      if (message.state === 'expanded') {
        if (!isExpanded()) {
          applied = click('[data-trigger="expanded"]') || applied;
        }
      } else if (isOneOf(COMPACT_VIEWS, message.state)) {
        var el = island();
        if (isExpanded() || !el || el.getAttribute('data-view') !== message.state) {
          applied = click('[data-trigger="' + message.state + '"]') || applied;
        }
      }
    }

    if (isValidPage(message.page)) {
      var page = Math.max(0, Math.min(PAGE_COUNT - 1, message.page));
      if (!isExpanded()) click('[data-trigger="expanded"]');
      /* The prototype listens for page keys on document; dispatch after the
         expanded pager exists. */
      window.setTimeout(function () {
        dispatchPageKey(page);
        schedulePost();
      }, 0);
      applied = true;
    }

    return applied;
  }

  function flushPending() {
    if (!pendingCommands.length) return;
    var queued = pendingCommands;
    pendingCommands = [];
    for (var i = 0; i < queued.length; i += 1) applyCommand(queued[i]);
    schedulePost();
  }

  window.addEventListener('message', function (event) {
    if (!event || !isPlainObject(event.data)) return;
    if (event.data.type !== 'fluidbar:set') return;
    /* Same-origin contract: only the embedding window may steer the island. */
    if (event.source !== window.parent) return;
    if (event.origin !== window.location.origin) return;
    /* app.js builds the hidden triggers during boot; a parent that sends its
       initial state early must not lose it. Queue until ready, then replay. */
    if (document.body && !document.body.classList.contains('ready')) {
      if (pendingCommands.length < MAX_PENDING) pendingCommands.push(event.data);
      return;
    }
    if (applyCommand(event.data)) schedulePost();
  });

  /* ------------------------------------------------------------------ */
  /* Observe the real prototype DOM and mirror settled state             */
  /* ------------------------------------------------------------------ */

  function onMutation() {
    pinDesired();
    schedulePost();
    var ready = document.body.classList.contains('ready');
    if (ready && !lastReady) {
      /* Surface ready as soon as boot finishes; the parent can fade the poster out. */
      lastReady = true;
      schedulePost(0);
    }
    if (ready && pendingCommands.length) flushPending();
  }

  function observe() {
    var el = island();
    if (el) {
      new MutationObserver(onMutation).observe(el, {
        attributes: true,
        attributeFilter: ['data-expanded', 'data-view', 'aria-expanded']
      });
      el.addEventListener('transitionend', function (event) {
        if (event.target !== el) return;
        if (event.propertyName === 'width' || event.propertyName === 'height') {
          schedulePost(30);
        }
      });
    }
    if (document.body) {
      new MutationObserver(onMutation).observe(document.body, {
        attributes: true,
        attributeFilter: ['class', 'data-mode', 'data-material', 'data-theme', 'data-motion', 'data-ready']
      });
    }
    /* syncChrome() also writes --accent on <html>. Re-pin a parent-requested
       accent whenever the prototype resets that inline style. */
    new MutationObserver(onMutation).observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['style']
    });
    var title = byId('pagerTitle');
    if (title) {
      new MutationObserver(onMutation).observe(title, { childList: true, subtree: true, characterData: true });
    }
    /* Boot happens after DOMContentLoaded; keep a couple of catch-up posts so
       the parent gets ready=true even if it attached late. */
    schedulePost(0);
    schedulePost(500);
    schedulePost(1200);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', observe);
  } else {
    observe();
  }
})();
