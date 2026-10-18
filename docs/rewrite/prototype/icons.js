/* ==========================================================================
   FluidBar 2.0 — interactive prototype
   icons.js — inline SVG icon library (no icon font, no external assets)
   All icons on a 24×24 grid, stroke icons use currentColor.
   ========================================================================== */
(function (global) {
  'use strict';

  var I = {
    /* ---- navigation / chrome ------------------------------------------- */
    chevronDown: '<path d="m6 9 6 6 6-6"/>',
    chevronUp: '<path d="m6 15 6-6 6 6"/>',
    chevronLeft: '<path d="m15 18-6-6 6-6"/>',
    chevronRight: '<path d="m9 18 6-6-6-6"/>',
    close: '<path d="M6 6l12 12M18 6 6 18"/>',
    check: '<path d="m4.5 12.5 5 5L19.5 7"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    minus: '<path d="M5 12h14"/>',
    more: '<circle cx="5" cy="12" r="1.2" fill="currentColor" stroke="none"/><circle cx="12" cy="12" r="1.2" fill="currentColor" stroke="none"/><circle cx="19" cy="12" r="1.2" fill="currentColor" stroke="none"/>',
    dot: '<circle cx="12" cy="12" r="3.2" fill="currentColor" stroke="none"/>',
    grip: '<path d="M8 6h8M8 12h8M8 18h8"/>',
    external: '<path d="M14 4h6v6"/><path d="M20 4 11 13"/><path d="M18 14v4.5A1.5 1.5 0 0 1 16.5 20h-11A1.5 1.5 0 0 1 4 18.5v-11A1.5 1.5 0 0 1 5.5 6H10"/>',
    search: '<circle cx="10.8" cy="10.8" r="6.3"/><path d="m15.5 15.5 4.5 4.5"/>',
    refresh: '<path d="M20 11a8 8 0 0 0-14.8-4"/><path d="M4 4v5h5"/><path d="M4 13a8 8 0 0 0 14.8 4"/><path d="M20 20v-5h-5"/>',
    arrowRight: '<path d="M4 12h15"/><path d="m14 7 5 5-5 5"/>',
    arrowUpRight: '<path d="M7 17 17 7"/><path d="M8 7h9v9"/>',
    keyboard: '<rect x="2.5" y="6" width="19" height="12" rx="2.5"/><path d="M6 10h.01M9.5 10h.01M13 10h.01M16.5 10h.01M6 13.5h.01M9.5 13.5h.01M13 13.5h.01M16.5 13.5h.01M8 16.5h8"/>',
    accessibility: '<circle cx="12" cy="4.4" r="1.9"/><path d="M4.5 8.2c2.5.5 5 .8 7.5.8s5-.3 7.5-.8"/><path d="M12 9v4.5"/><path d="m8 21 4-7.5 4 7.5"/>',

    /* ---- island tabs / activities -------------------------------------- */
    home: '<path d="M3.2 10.7 12 3.2l8.8 7.5"/><path d="M5.5 9.4V20a1 1 0 0 0 1 1h11a1 1 0 0 0 1-1V9.4"/><path d="M9.7 21v-6.4h4.6V21"/>',
    music: '<path d="M9.2 18V5.2l11-2v12.6"/><ellipse cx="6.4" cy="18.2" rx="2.8" ry="2.4"/><ellipse cx="17.4" cy="16.2" rx="2.8" ry="2.4"/>',
    bell: '<path d="M18 8.4a6 6 0 0 0-12 0c0 6.4-2.6 6.8-2.6 8.6h17.2C20.6 15.2 18 14.8 18 8.4"/><path d="M9.8 20.6a2.4 2.4 0 0 0 4.4 0"/>',
    clipboard: '<rect x="5" y="4.6" width="14" height="16.2" rx="2.6"/><path d="M9 4.6V3.2h6v1.4"/><path d="M9 10h6M9 13.7h6M9 17.4h3.5"/>',
    agent: '<path d="m12 2.8 1.9 5.2 5.3 2-5.3 1.9L12 17.2l-1.9-5.3-5.3-1.9 5.3-2z"/><path d="m18.7 15.2.8 2.1 2.1.8-2.1.8-.8 2.1-.8-2.1-2.1-.8 2.1-.8z"/>',
    widgets: '<rect x="3.2" y="3.2" width="7.4" height="7.4" rx="2"/><rect x="13.4" y="3.2" width="7.4" height="7.4" rx="2"/><rect x="3.2" y="13.4" width="7.4" height="7.4" rx="2"/><rect x="13.4" y="13.4" width="7.4" height="7.4" rx="2"/>',
    play: '<path d="M8 5.2v13.6L19.4 12z" fill="currentColor" stroke="none"/>',
    pause: '<rect x="6.6" y="4.8" width="3.6" height="14.4" rx="1.2" fill="currentColor" stroke="none"/><rect x="13.8" y="4.8" width="3.6" height="14.4" rx="1.2" fill="currentColor" stroke="none"/>',
    prev: '<rect x="4.4" y="5.4" width="2.3" height="13.2" rx="1.1" fill="currentColor" stroke="none"/><path d="M19.5 5.6v12.8L8.3 12z" fill="currentColor" stroke="none"/>',
    next: '<rect x="17.3" y="5.4" width="2.3" height="13.2" rx="1.1" fill="currentColor" stroke="none"/><path d="M4.5 5.6v12.8L15.7 12z" fill="currentColor" stroke="none"/>',
    waveform: '<path d="M4 10v4M8 7.5v9M12 4.8v14.4M16 8v8M20 10.5v3"/>',
    queue: '<path d="M4 6.4h10M4 11.4h10M4 16.4h6.8"/><path d="M18.6 8.2v9.3"/><circle cx="16.8" cy="18.2" r="1.8" fill="currentColor" stroke="none"/>',
    headphones: '<path d="M4 14v-2a8 8 0 0 1 16 0v2"/><path d="M4 14h2.6a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H5.5A1.5 1.5 0 0 1 4 18.5z"/><path d="M20 14h-2.6a1 1 0 0 0-1 1v4a1 1 0 0 0 1 1h1.1a1.5 1.5 0 0 0 1.5-1.5z"/>',
    speaker: '<path d="M4.2 9.3h3.1L12.6 5v14l-5.3-4.3H4.2z"/><path d="M16.2 8.6a4.8 4.8 0 0 1 0 6.8"/><path d="M18.8 6a8.5 8.5 0 0 1 0 12"/>',
    volume: '<path d="M4.2 9.3h3.1L12.6 5v14l-5.3-4.3H4.2z"/><path d="M16.2 8.6a4.8 4.8 0 0 1 0 6.8"/>',
    volumeLow: '<path d="M4.2 9.3h3.1L12.6 5v14l-5.3-4.3H4.2z"/>',
    mute: '<path d="M4.2 9.3h3.1L12.6 5v14l-5.3-4.3H4.2z"/><path d="m16.5 9.5 5 5M21.5 9.5l-5 5"/>',
    brightness: '<circle cx="12" cy="12" r="4.1"/><path d="M12 2.4v2.3M12 19.3v2.3M2.4 12h2.3M19.3 12h2.3M5.2 5.2 6.8 6.8M17.2 17.2l1.6 1.6M18.8 5.2l-1.6 1.6M6.8 17.2l-1.6 1.6"/>',
    timer: '<circle cx="12" cy="13.2" r="7.8"/><path d="M12 9.5v3.7l2.4 1.6"/><path d="M9.2 2.8h5.6"/>',
    battery: '<rect x="2.6" y="7.2" width="16.8" height="9.6" rx="2.6"/><path d="M21.4 10.3v3.4"/>',
    batteryCharging: '<rect x="2.6" y="7.2" width="16.8" height="9.6" rx="2.6"/><path d="M21.4 10.3v3.4"/><path d="m12.5 8.6-3 4.4h2.4l-.6 3.4 3.2-4.6h-2.4z" fill="currentColor" stroke="none"/>',
    bolt: '<path d="M13.4 2.6 5.2 13.4h5.6l-.9 8 8.9-11.2h-5.7z"/>',
    plug: '<path d="M8.5 3v5M15.5 3v5"/><path d="M6 8h12v3.4a6 6 0 0 1-12 0z"/><path d="M12 17.4V21"/>',
    cpu: '<rect x="6.2" y="6.2" width="11.6" height="11.6" rx="2.2"/><rect x="9.6" y="9.6" width="4.8" height="4.8" rx="1"/><path d="M9.2 2.8v3.4M14.8 2.8v3.4M9.2 17.8v3.4M14.8 17.8v3.4M2.8 9.2h3.4M2.8 14.8h3.4M17.8 9.2h3.4M17.8 14.8h3.4"/>',
    memory: '<rect x="2.8" y="6.4" width="18.4" height="11.2" rx="2.2"/><rect x="6.5" y="9.6" width="3.2" height="4.8" rx=".8"/><rect x="10.4" y="9.6" width="3.2" height="4.8" rx=".8"/><rect x="14.3" y="9.6" width="3.2" height="4.8" rx=".8"/><path d="M6.2 17.6v2.6M17.8 17.6v2.6"/>',
    clock: '<circle cx="12" cy="12" r="8.6"/><path d="M12 7.2V12l3.2 2"/>',
    timerRing: '<circle cx="12" cy="12" r="8.4"/><path d="M12 7.2V12l3.1 1.9"/>',

    /* ---- system / toggles ---------------------------------------------- */
    wifi: '<path d="M2.8 9.2a14 14 0 0 1 18.4 0"/><path d="M6.2 12.7a9.2 9.2 0 0 1 11.6 0"/><path d="M9.5 16.1a4.4 4.4 0 0 1 5 0"/><circle cx="12" cy="19.4" r="1.15" fill="currentColor" stroke="none"/>',
    bluetooth: '<path d="m7 7.5 10 9-5 4V3.5l5 4-10 9"/>',
    moon: '<path d="M20.4 14.4A8.6 8.6 0 0 1 9.6 3.6a8.6 8.6 0 1 0 10.8 10.8z"/>',
    sun: '<circle cx="12" cy="12" r="4.2"/><path d="M12 2.6v2.2M12 19.2v2.2M2.6 12h2.2M19.2 12h2.2M5.3 5.3l1.6 1.6M17.1 17.1l1.6 1.6M18.7 5.3l-1.6 1.6M6.9 17.1l-1.6 1.6"/>',
    cloud: '<path d="M7.2 18.6a4.5 4.5 0 0 1-.5-9 6 6 0 0 1 11.5-1.4 4 4 0 0 1 3.1 3.9 4 4 0 0 1-4 4z"/>',
    cloudSun: '<circle cx="8.2" cy="8" r="3.1"/><path d="M8.2 2.3v1.5M2.5 8h1.5M4.2 3.9l1 1M12.2 3.9l-1 1M2.5 12.1h1.5"/><path d="M9.1 19.3a3.8 3.8 0 0 1-.5-7.6 5 5 0 0 1 9.6-1.2 3.4 3.4 0 0 1 .5 6.7 3.8 3.8 0 0 1-.8 2.1z"/>',
    pin: '<path d="M9.1 3.2h5.8l-.8 5.2 3.1 3.2H6.8l3.1-3.2z"/><path d="M12 11.6V21"/>',
    copy: '<rect x="8.4" y="8.4" width="12" height="12" rx="2.4"/><path d="M15.6 8.4V5.6A2.2 2.2 0 0 0 13.4 3.4h-8A2.2 2.2 0 0 0 3.2 5.6v8a2.2 2.2 0 0 0 2.2 2.2h2.8"/>',
    link: '<path d="M10.2 13.8a4.3 4.3 0 0 0 6.1 0l2.9-2.9a4.3 4.3 0 0 0-6.1-6.1l-1.4 1.4"/><path d="M13.8 10.2a4.3 4.3 0 0 0-6.1 0l-2.9 2.9a4.3 4.3 0 0 0 6.1 6.1l1.4-1.4"/>',
    code: '<path d="m8.2 7.4-5 4.6 5 4.6M15.8 7.4l5 4.6-5 4.6"/>',
    terminal: '<rect x="2.8" y="4.1" width="18.4" height="15.8" rx="2.6"/><path d="m6.6 9.4 3.2 2.9-3.2 2.9M12.8 15.4h4.6"/>',
    image: '<rect x="3.2" y="4.2" width="17.6" height="15.6" rx="2.6"/><circle cx="8.6" cy="9.6" r="1.7"/><path d="m4.6 17.2 4.6-4.6 3.2 3.1 3.6-3.6 3.6 3.6"/>',
    text: '<path d="M5 6.2h14M5 10.2h10M5 14.2h14M5 18.2h7"/>',
    file: '<path d="M13.4 2.8H6.8A1.8 1.8 0 0 0 5 4.6v14.8a1.8 1.8 0 0 0 1.8 1.8h10.4a1.8 1.8 0 0 0 1.8-1.8V8z"/><path d="M13.2 3v5.2H19"/>',
    folder: '<path d="M3 6.4A2.4 2.4 0 0 1 5.4 4h3.7l2 2.4h7.5A2.4 2.4 0 0 1 21 8.8v8.8a2.4 2.4 0 0 1-2.4 2.4H5.4A2.4 2.4 0 0 1 3 17.6z"/>',
    trash: '<path d="M4.4 7h15.2M9.4 3.4h5.2l.8 3.6H8.6z"/><path d="m6.4 7 .9 12.2a1.8 1.8 0 0 0 1.8 1.6h5.8a1.8 1.8 0 0 0 1.8-1.6L17.6 7"/><path d="M10.2 11v5.6M13.8 11v5.6"/>',
    download: '<path d="M12 3.4v11.2"/><path d="m7.4 10.2 4.6 4.6 4.6-4.6"/><path d="M4.4 19.6h15.2"/>',
    star: '<path d="m12 3.2 2.7 5.5 6.1.9-4.4 4.3 1 6.1-5.4-2.9-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z"/>',
    shield: '<path d="M12 2.8 4.6 5.8v5.7c0 4.7 3.1 7.9 7.4 9.7 4.3-1.8 7.4-5 7.4-9.7V5.8z"/><path d="m8.6 11.8 2.3 2.3 4.5-4.6"/>',
    layout: '<rect x="3" y="4" width="18" height="16" rx="2.4"/><path d="M3 9.2h18M9.4 9.2V20"/>',
    chart: '<path d="M4 20V4"/><path d="M4 20h16"/><path d="M8 16v-4.2M12.6 16V8M17.2 16v-6.4"/>',
    calendar: '<rect x="3.4" y="5" width="17.2" height="16" rx="2.6"/><path d="M3.4 9.6h17.2M8.2 3v4M15.8 3v4"/><path d="M7.6 13.2h.01M12 13.2h.01M16.4 13.2h.01M7.6 17.2h.01M12 17.2h.01"/>',
    lock: '<rect x="5" y="10.4" width="14" height="10.4" rx="2.4"/><path d="M8.2 10.4V7.8a3.8 3.8 0 0 1 7.6 0v2.6"/>',
    globe: '<circle cx="12" cy="12" r="8.8"/><path d="M3.4 12h17.2"/><path d="M12 3.2c2.5 2.4 3.8 5.4 3.8 8.8s-1.3 6.4-3.8 8.8c-2.5-2.4-3.8-5.4-3.8-8.8S9.5 5.6 12 3.2z"/>',
    info: '<circle cx="12" cy="12" r="8.8"/><path d="M12 10.8v5.4"/><circle cx="12" cy="7.9" r="1" fill="currentColor" stroke="none"/>',
    alert: '<path d="M12 3.6 2.8 20h18.4z"/><path d="M12 9.2v4.6"/><circle cx="12" cy="16.8" r=".9" fill="currentColor" stroke="none"/>',
    help: '<circle cx="12" cy="12" r="8.8"/><path d="M9.6 9.2a2.5 2.5 0 0 1 4.9.6c0 1.6-2.4 2-2.4 3.7"/><circle cx="12" cy="16.9" r=".9" fill="currentColor" stroke="none"/>',

    /* ---- settings icons ------------------------------------------------ */
    settings: '<circle cx="12" cy="12" r="3.1"/><path d="M19.1 13.8a1.6 1.6 0 0 0 .3 1.8l.1.1-2.6 2.6-.1-.1a1.6 1.6 0 0 0-1.8-.3 1.6 1.6 0 0 0-1 1.5v.2h-3.7v-.2a1.6 1.6 0 0 0-1-1.5 1.6 1.6 0 0 0-1.8.3l-.1.1-2.6-2.6.1-.1a1.6 1.6 0 0 0 .3-1.8 1.6 1.6 0 0 0-1.5-1H4v-3.7h.2a1.6 1.6 0 0 0 1.5-1 1.6 1.6 0 0 0-.3-1.8l-.1-.1 2.6-2.6.1.1a1.6 1.6 0 0 0 1.8.3 1.6 1.6 0 0 0 1-1.5v-.2h3.7v.2a1.6 1.6 0 0 0 1 1.5 1.6 1.6 0 0 0 1.8-.3l.1-.1 2.6 2.6-.1.1a1.6 1.6 0 0 0-.3 1.8 1.6 1.6 0 0 0 1.5 1h.2v3.7h-.2a1.6 1.6 0 0 0-1.5 1z"/>',
    palette: '<path d="M12 3a9 9 0 1 0 0 18h1.4a2.1 2.1 0 0 0 1.2-3.8 2.1 2.1 0 0 1 1.2-3.8H18a3 3 0 0 0 3-3A9 9 0 0 0 12 3z"/><circle cx="7.8" cy="11.6" r="1.1" fill="currentColor" stroke="none"/><circle cx="9.8" cy="7.4" r="1.1" fill="currentColor" stroke="none"/><circle cx="14.2" cy="7.2" r="1.1" fill="currentColor" stroke="none"/>',
    islandIcon: '<rect x="5.2" y="3.2" width="13.6" height="17.6" rx="6.8"/><path d="M9.8 9.2h4.4M9.8 13.2h2.8"/>',
    puzzle: '<path d="M9.2 4.2A2.2 2.2 0 0 1 13.6 4v1.4h2.8a1.6 1.6 0 0 1 1.6 1.6v3h1.4a2.2 2.2 0 1 1 0 4.4H18v3.4a1.6 1.6 0 0 1-1.6 1.6h-3.2v-1.4a2.2 2.2 0 0 0-4.4 0v1.4H5.4a1.6 1.6 0 0 1-1.6-1.6v-3.2H5a2.2 2.2 0 1 0 0-4.4H3.8V7a1.6 1.6 0 0 1 1.6-1.6h3.8z"/>',
    storefront: '<path d="M4 9.2V19a1.6 1.6 0 0 0 1.6 1.6h12.8A1.6 1.6 0 0 0 20 19V9.2"/><path d="M3.2 9.2 5 3.8h14l1.8 5.4a3.2 3.2 0 0 1-5.9 1.7 3.2 3.2 0 0 1-5.8 0 3.2 3.2 0 0 1-5.9-1.7z"/><path d="M9.2 20.6v-5.4h5.6v5.4"/>',
    plugIcon: '<path d="M9 3.4v5M15 3.4v5"/><path d="M6.4 8.4h11.2v2.8a5.6 5.6 0 0 1-11.2 0z"/><path d="M12 16.8v3.8"/>',
    sliders: '<path d="M4 7.2h8.4M17.2 7.2h2.8M4 16.8h2.8M11.6 16.8H20"/><circle cx="15" cy="7.2" r="2.2"/><circle cx="9.4" cy="16.8" r="2.2"/>',
    bellRing: '<path d="M18 8.4a6 6 0 0 0-12 0c0 6.4-2.6 6.8-2.6 8.6h17.2C20.6 15.2 18 14.8 18 8.4"/><path d="M9.8 20.6a2.4 2.4 0 0 0 4.4 0"/><path d="M4.8 2.8 3 4.6M19.2 2.8 21 4.6"/>',

    /* ---- brands (drawn, not imported) --------------------------------- */
    windows: '<rect x="3.2" y="3.2" width="8" height="8" rx=".8" fill="currentColor" stroke="none"/><rect x="12.8" y="3.2" width="8" height="8" rx=".8" fill="currentColor" stroke="none"/><rect x="3.2" y="12.8" width="8" height="8" rx=".8" fill="currentColor" stroke="none"/><rect x="12.8" y="12.8" width="8" height="8" rx=".8" fill="currentColor" stroke="none"/>',
    github: '<path d="M9.2 20.4c-4.2 1.3-4.2-2.1-5.9-2.6m11.8 5.3v-3.3c0-1 .1-1.4-.5-2 2.4-.3 4.9-1.2 4.9-5.3a4.1 4.1 0 0 0-1.1-2.9 3.8 3.8 0 0 0-.1-2.8s-.9-.3-3 1.1a10.3 10.3 0 0 0-5.4 0C7.8 5.5 6.9 5.8 6.9 5.8a3.8 3.8 0 0 0-.1 2.8 4.1 4.1 0 0 0-1.1 2.9c0 4.1 2.5 5 4.9 5.3-.4.4-.6.9-.6 1.5v3.6" transform="translate(1.4 -1.4) scale(.9)"/>',
    chat: '<path d="M20.6 11.6a8.2 8.2 0 0 1-8.2 8.2 8.6 8.6 0 0 1-3.6-.8L4 20.4l1.4-4.4a8.2 8.2 0 1 1 15.2-4.4z"/><path d="M8.4 10.8h.01M12 10.8h.01M15.6 10.8h.01" stroke-width="2.8"/>',
    mail: '<rect x="2.8" y="5" width="18.4" height="14" rx="2.4"/><path d="m3.8 7.2 8.2 6 8.2-6"/>',
    claude: '<path d="M12 2.8v18.4M3.6 7.2l16.8 9.6M20.4 7.2 3.6 16.8"/><path d="m12 7.4 1.5 3.1L12 13.6l-1.5-3.1z" fill="currentColor" stroke="none"/>',
    codex: '<path d="m7.6 6.6-5 5.4 5 5.4M16.4 6.6l5 5.4-5 5.4"/><path d="m13.6 4.8-3.2 14.4"/>',
    spotify: '<circle cx="12" cy="12" r="9"/><path d="M7.4 9.2c3.4-1 6.8-.6 9.6 1M8 12.4c2.7-.8 5.5-.4 7.7.8M8.8 15.6c2-.6 4-.3 5.8.6"/>',
    figma: '<path d="M9.2 3.4h2.8v5.6H9.2a2.8 2.8 0 1 1 0-5.6zM12 3.4h2.8a2.8 2.8 0 1 1 0 5.6H12zM12 9h2.8a2.8 2.8 0 1 1-2.8 2.8zM9.2 9.2H12v5.6H9.2a2.8 2.8 0 1 1 0-5.6zM9.2 15H12v2.8a2.8 2.8 0 1 1-2.8-2.8z"/>'
  };

  /**
   * icon(name, size, className)
   * @returns {string} inline SVG markup
   */
  function icon(name, size, cls) {
    var body = I[name] || I.dot;
    var s = size || 16;
    var klass = cls ? ' ' + cls : '';
    return '<svg class="ico' + klass + '" width="' + s + '" height="' + s +
      '" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" ' +
      'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
      body + '</svg>';
  }

  global.FB_ICONS = { I: I, icon: icon };
})(window);
