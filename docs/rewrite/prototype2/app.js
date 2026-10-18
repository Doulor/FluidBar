/* ==========================================================================
   FluidBar 2.0 — focused interactive prototype
   app.js

   Sections
   01. Constants & demo data
   02. Strings (zh / en)
   03. State & helpers
   04. Scene, taskbar, dock
   05. Compact states (pixel-tight)
   06. Expanded pages
   07. Transient preview islands
   08. Pager (drag / flick / wheel / scroll chaining / keyboard)
   09. Shell, console, URL params
   10. Boot
   ========================================================================== */
(function () {
  'use strict';

  var icon = window.FB_ICONS.icon;

  /* ==========================================================================
     01. CONSTANTS & DEMO DATA
     ========================================================================== */
  var PAGES = [
    { id: 'home', icon: 'home' },
    { id: 'media', icon: 'music' },
    { id: 'notifications', icon: 'bell' },
    { id: 'clipboard', icon: 'clipboard' },
    { id: 'agent', icon: 'agent' },
    { id: 'widgets', icon: 'widgets' }
  ];
  var PAGE_W = 400;
  var MAX_PAGE = PAGES.length - 1;

  /* Pager contract. */
  var PAGER = {
    gain: 0.85,          /* drag follow ratio */
    rubber: 0.35,        /* overscroll beyond first/last page */
    flick: 0.55,         /* px/ms — a flick commits at most one page */
    snapT: 0.38,         /* snap spring period (s) */
    snapZ: 0.94,         /* snap spring damping ratio */
    wheelStep: 140,      /* accumulated px per wheel page turn */
    wheelWindow: 180,    /* ms between wheel commits */
    overscroll: 40,      /* px past a content edge before chaining */
    dwell: 120           /* ms of sustained overscroll */
  };
  var RAIL_DELAY = 900;  /* fade the preview islands this long after activity */

  var COMPACT_VIEWS = ['idle', 'media', 'notifications', 'clipboard', 'hud', 'timer', 'battery', 'agent'];
  var EXPANDED_TABS = ['home', 'media', 'notifications', 'clipboard', 'agent', 'widgets'];

  var TRACKS = [
    {
      title: 'Midnight Drive', artist: 'Lumen Fields', album: 'Nocturne', art: 0,
      accent: '#66e0d4', duration: 214, initials: 'MD',
      lyrics: {
        zh: ['霓虹在后视镜里融化', '城市把呼吸放慢'],
        en: ['Neon melts in the rear-view', 'The city lets its breath out']
      }
    },
    {
      title: 'Glass Harbor', artist: 'Aster Vale', album: 'Tidal', art: 1,
      accent: '#f4b15e', duration: 188, initials: 'GH',
      lyrics: {
        zh: ['潮汐把灯光推回岸边', '玻璃港没有回声'],
        en: ['The tide pushes light ashore', 'Glass harbor keeps no echo']
      }
    },
    {
      title: 'Amber Hours', artist: 'Sable & Co.', album: 'Late Sun', art: 2,
      accent: '#8fb8ff', duration: 236, initials: 'AH',
      lyrics: {
        zh: ['琥珀色的钟停在六点', '影子比路更长'],
        en: ['Amber clocks stop at six', 'Shadows run past the road']
      }
    }
  ];

  var NOTIFICATIONS = [
    {
      id: 'n1', app: 'Teams', color: '#2f6fed', icon: 'chat',
      title: { zh: '设计评审改到 15:00', en: 'Design review moved to 15:00' },
      body: { zh: '会议室 A · 请带上最新的动效走查。', en: 'Room A · bring the latest motion walkthrough.' },
      time: { zh: '刚刚', en: 'now' }
    },
    {
      id: 'n2', app: 'Outlook', color: '#0a84ff', icon: 'mail',
      title: { zh: '构建完成 · prototype2', en: 'Build finished · prototype2' },
      body: { zh: '全部截图已导出，控制台零错误。', en: 'All screenshots exported, console is clean.' },
      time: { zh: '3 分钟', en: '3 min' }
    },
    {
      id: 'n3', app: 'GitHub', color: '#3a3f4a', icon: 'github',
      title: { zh: 'PR #128 等待评审', en: 'PR #128 awaits review' },
      body: { zh: 'pager: 稳定滚轮与滚动链边界。', en: 'pager: stabilise wheel and scroll chaining.' },
      time: { zh: '12 分钟', en: '12 min' }
    },
    {
      id: 'n4', app: 'Calendar', color: '#34c759', icon: 'calendar',
      title: { zh: '15:00 设计走查', en: '15:00 design walkthrough' },
      body: { zh: '还剩 24 分钟，会议室 A。', en: 'Starts in 24 minutes, Room A.' },
      time: { zh: '25 分钟', en: '25 min' }
    }
  ];

  var CLIPS = [
    {
      id: 'c1', type: 'code', icon: 'code', mono: true, pinned: true, time: { zh: '刚刚', en: 'now' },
      content: 'npm run build -- --mode production'
    },
    {
      id: 'c2', type: 'text', icon: 'text', mono: false, pinned: false, time: { zh: '2 分钟', en: '2 min' },
      content: { zh: '紧凑态不要重叠：图标槽 20px，文本单行省略。', en: 'No overlap in compact: 20px icon slot, single-line ellipsis.' }
    },
    {
      id: 'c3', type: 'link', icon: 'link', mono: false, pinned: false, time: { zh: '8 分钟', en: '8 min' },
      content: 'https://fluidbar.dev/spec/pager'
    },
    {
      id: 'c4', type: 'image', icon: 'image', mono: false, pinned: false, time: { zh: '14 分钟', en: '14 min' },
      content: { zh: '微光渐变封面.png', en: 'soft-gradient-cover.png' }
    },
    {
      id: 'c5', type: 'text', icon: 'text', mono: true, pinned: false, time: { zh: '32 分钟', en: '32 min' },
      content: 'damping 0.85 · flick ≤ 1 page · snap T 0.38 ζ 0.94'
    },
    {
      id: 'c6', type: 'text', icon: 'text', mono: false, pinned: false, time: { zh: '1 小时', en: '1 hr' },
      content: 'Segoe UI Variable Text · obsidian island'
    }
  ];

  var AGENTS = [
    {
      id: 'a1', tool: 'Claude Code', project: 'fluidbar', status: 'running', duration: '02:14', progress: 62,
      task: { zh: '重构设置模块的动效层', en: 'Rebuild the settings motion layer' }
    },
    {
      id: 'a2', tool: 'Codex', project: 'prototype2', status: 'done', duration: '48s', progress: 100,
      task: { zh: '补齐紧凑态尺寸与省略规则', en: 'Fix compact metrics and ellipsis rules' }
    },
    {
      id: 'a3', tool: 'Claude Code', project: 'docs', status: 'running', duration: '01:03', progress: 34,
      task: { zh: '生成设计走查截图', en: 'Generate the design walkthrough shots' }
    },
    {
      id: 'a4', tool: 'Codex', project: 'pager', status: 'error', duration: '12s', progress: 0,
      task: { zh: '滚动链边界测试', en: 'Scroll-chaining boundary test' }
    }
  ];

  var DOCK_APPS = [
    { tab: 'home', icon: 'home', label: 'tab.home' },
    { tab: 'media', icon: 'music', label: 'tab.media' },
    { tab: 'notifications', icon: 'bell', label: 'tab.notifications' },
    { tab: 'clipboard', icon: 'clipboard', label: 'tab.clipboard' },
    { tab: 'agent', icon: 'agent', label: 'tab.agent' },
    { tab: 'widgets', icon: 'widgets', label: 'tab.widgets' }
  ];

  /* ==========================================================================
     02. STRINGS
     ========================================================================== */
  var STR = {
    zh: {
      'brand.title': 'FluidBar 2.0',
      'brand.kicker': '演示控制台',
      'brand.live': '实时',
      'console.state': '状态',
      'console.stateHint': '点击即时切换',
      'console.mode': '模式',
      'console.theme': '主题',
      'console.lang': '语言',
      'console.motion': '动效',
      'console.expand': '展开面板',
      'console.collapsePanel': '收起面板',
      'console.normal': '标准',
      'console.reduced': '减弱',
      'console.note': '设计原型 · 不含设置与市场',
      'console.archive': '查看归档原型',
      'trigger.idle': '空闲',
      'trigger.media': '媒体',
      'trigger.notifications': '通知',
      'trigger.clipboard': '剪贴板',
      'trigger.hud': '音量',
      'trigger.timer': '计时器',
      'trigger.battery': '电池',
      'trigger.agent': 'Agent',
      'mode.float': '悬浮',
      'mode.flush': '贴顶',
      'mode.dock': '停靠',
      'theme.dark': '深色',
      'theme.light': '浅色',
      'lang.zh': '中文',
      'lang.en': 'English',
      'tab.home': '主页',
      'tab.media': '媒体',
      'tab.notifications': '通知',
      'tab.clipboard': '剪贴板',
      'tab.agent': 'Agent',
      'tab.widgets': '小组件',
      'short.home': '主页',
      'short.media': '媒体',
      'short.notifications': '通知',
      'short.clipboard': '剪贴板',
      'short.agent': 'Agent',
      'short.widgets': '小组件',
      'island.expand': '展开 FluidBar',
      'island.collapse': '收起 FluidBar',
      'island.collapseBtn': '收起面板',
      'home.date': '10月18日 周日',
      'home.shortDate': '10月18日',
      'home.weather': '多云转晴',
      'home.quick.wifi': 'Wi‑Fi',
      'home.quick.bluetooth': '蓝牙',
      'home.quick.focus': '专注',
      'home.quick.dark': '深色',
      'home.charging': '已接通电源',
      'home.remaining': '剩余 4 小时',
      'media.nowPlaying': '正在播放',
      'media.nextLine': '下一句',
      'media.play': '播放',
      'media.pause': '暂停',
      'media.prev': '上一首',
      'media.next': '下一首',
      'media.speaker': '扬声器',
      'media.queue': '播放队列',
      'notifications.emptyTitle': '没有新通知',
      'notifications.emptySub': '新消息会出现在这里',
      'notifications.dismiss': '忽略通知',
      'clipboard.copied': '已复制',
      'clipboard.copy': '复制',
      'clipboard.pin': '固定',
      'clipboard.unpin': '取消固定',
      'clipboard.pinned': '已固定',
      'clipboard.type.text': '文本',
      'clipboard.type.code': '代码',
      'clipboard.type.link': '链接',
      'clipboard.type.image': '图片',
      'agent.running': '进行中',
      'agent.done': '已完成',
      'agent.error': '失败',
      'widgets.weather': '多云转晴',
      'widgets.highLow': '18° / 11°',
      'widgets.wind': '西北风 3 级',
      'widgets.cpu': 'CPU',
      'widgets.ram': '内存',
      'widgets.battery': '电池',
      'widgets.notes': '今日清单',
      'widgets.progress': '{n}/{m} 完成',
      'widgets.calEvent': '设计评审 · 15:00',
      'widgets.calToday': '今天',
      'widgets.todo1': '整理动效走查',
      'widgets.todo2': '同步紧凑态尺寸',
      'widgets.todo3': '导出走查截图',
      'timer.focus': '专注',
      'hud.volume': '音量',
      'hud.brightness': '亮度',
      'battery.plugged': '已接通电源',
      'battery.remaining': '剩余 4 小时',
      'scene.hint': '拖动或滚轮翻页 · 1–6 切换 · Esc 收起',
      'scene.projects': '项目',
      'scene.spec': '设计规范',
      'scene.readme': '说明',
      'scene.terminal': '终端',
      'scene.explorer': '项目 — 文件资源管理器',
      'scene.quickAccess': '快速访问',
      'scene.thisPc': '此电脑',
      'scene.desktop': '桌面',
      'scene.downloads': '下载',
      'scene.documents': '文档',
      'scene.status': '4 个项目 · 1 个已选中',
      'scene.termLine1': 'PS D:\\build\\GitLocal\\FluidBar> npm run verify',
      'scene.termLine2': '✓ node --check app.js icons.js',
      'scene.termLine3': '✓ 9 screenshots · 0 console errors',
      'cal.weekdays': ['一', '二', '三', '四', '五', '六', '日']
    },
    en: {
      'brand.title': 'FluidBar 2.0',
      'brand.kicker': 'Demo console',
      'brand.live': 'Live',
      'console.state': 'State',
      'console.stateHint': 'Tap to preview',
      'console.mode': 'Mode',
      'console.theme': 'Theme',
      'console.lang': 'Language',
      'console.motion': 'Motion',
      'console.expand': 'Open panel',
      'console.collapsePanel': 'Close panel',
      'console.normal': 'Normal',
      'console.reduced': 'Reduced',
      'console.note': 'Design prototype · no settings or marketplace',
      'console.archive': 'View archived prototype',
      'trigger.idle': 'Idle',
      'trigger.media': 'Media',
      'trigger.notifications': 'Notifications',
      'trigger.clipboard': 'Clipboard',
      'trigger.hud': 'Volume',
      'trigger.timer': 'Timer',
      'trigger.battery': 'Battery',
      'trigger.agent': 'Agent',
      'mode.float': 'Float',
      'mode.flush': 'Flush',
      'mode.dock': 'Dock',
      'theme.dark': 'Dark',
      'theme.light': 'Light',
      'lang.zh': '中文',
      'lang.en': 'English',
      'tab.home': 'Home',
      'tab.media': 'Media',
      'tab.notifications': 'Notifications',
      'tab.clipboard': 'Clipboard',
      'tab.agent': 'Agent',
      'tab.widgets': 'Widgets',
      'short.home': 'Home',
      'short.media': 'Media',
      'short.notifications': 'Alerts',
      'short.clipboard': 'Clip',
      'short.agent': 'Agent',
      'short.widgets': 'Widgets',
      'island.expand': 'Open FluidBar',
      'island.collapse': 'Close FluidBar',
      'island.collapseBtn': 'Collapse panel',
      'home.date': 'Sun, Oct 18',
      'home.shortDate': 'Oct 18',
      'home.weather': 'Partly sunny',
      'home.quick.wifi': 'Wi‑Fi',
      'home.quick.bluetooth': 'Bluetooth',
      'home.quick.focus': 'Focus',
      'home.quick.dark': 'Dark',
      'home.charging': 'Plugged in',
      'home.remaining': '4h left',
      'media.nowPlaying': 'Now playing',
      'media.nextLine': 'Next',
      'media.play': 'Play',
      'media.pause': 'Pause',
      'media.prev': 'Previous',
      'media.next': 'Next',
      'media.speaker': 'Speakers',
      'media.queue': 'Queue',
      'notifications.emptyTitle': 'You’re all caught up',
      'notifications.emptySub': 'New messages will show up here',
      'notifications.dismiss': 'Dismiss notification',
      'clipboard.copied': 'Copied',
      'clipboard.copy': 'Copy',
      'clipboard.pin': 'Pin',
      'clipboard.unpin': 'Unpin',
      'clipboard.pinned': 'Pinned',
      'clipboard.type.text': 'Text',
      'clipboard.type.code': 'Code',
      'clipboard.type.link': 'Link',
      'clipboard.type.image': 'Image',
      'agent.running': 'Running',
      'agent.done': 'Done',
      'agent.error': 'Failed',
      'widgets.weather': 'Partly sunny',
      'widgets.highLow': '18° / 11°',
      'widgets.wind': 'NW 3',
      'widgets.cpu': 'CPU',
      'widgets.ram': 'RAM',
      'widgets.battery': 'Battery',
      'widgets.notes': 'Today',
      'widgets.progress': '{n}/{m} done',
      'widgets.calEvent': 'Design review · 15:00',
      'widgets.calToday': 'Today',
      'widgets.todo1': 'Motion walkthrough',
      'widgets.todo2': 'Sync compact metrics',
      'widgets.todo3': 'Export walkthrough shots',
      'timer.focus': 'Focus',
      'hud.volume': 'Volume',
      'hud.brightness': 'Brightness',
      'battery.plugged': 'Plugged in',
      'battery.remaining': '4h left',
      'scene.hint': 'Drag or scroll to page · 1–6 · Esc to close',
      'scene.projects': 'Projects',
      'scene.spec': 'Spec',
      'scene.readme': 'README',
      'scene.terminal': 'Terminal',
      'scene.explorer': 'Projects — File Explorer',
      'scene.quickAccess': 'Quick access',
      'scene.thisPc': 'This PC',
      'scene.desktop': 'Desktop',
      'scene.downloads': 'Downloads',
      'scene.documents': 'Documents',
      'scene.status': '4 items · 1 selected',
      'scene.termLine1': 'PS D:\\build\\GitLocal\\FluidBar> npm run verify',
      'scene.termLine2': '✓ node --check app.js icons.js',
      'scene.termLine3': '✓ 9 screenshots · 0 console errors',
      'cal.weekdays': ['M', 'T', 'W', 'T', 'F', 'S', 'S']
    }
  };

  /* ==========================================================================
     03. STATE & HELPERS
     ========================================================================== */
  var S = {
    lang: 'zh', theme: 'dark', mode: 'float', motion: 'normal',
    expanded: false, view: 'idle', tab: 'home',
    playing: true, track: 0, progress: 74,
    volume: 62, brightness: 78, hudType: 'volume',
    toggles: { wifi: true, bluetooth: false, focus: false },
    unread: 3, dismissed: new Set(), copiedId: 'c1', pinned: new Set(['c1']),
    agentStatus: 'running',
    calYear: 2026, calMonth: 9,
    todos: [
      { key: 'widgets.todo1', done: true },
      { key: 'widgets.todo2', done: false },
      { key: 'widgets.todo3', done: false }
    ],
    forceArch: false,
    returnView: 'idle'
  };

  var P = {
    offset: 0, target: 0, v: 0, raf: 0, animating: false, snapLast: 0,
    dragging: false, moved: false, startX: 0, startY: 0, startOffset: 0, startPage: 0,
    lastX: 0, lastT: 0, vx: 0, suppressClick: 0,
    wheelAccum: 0, wheelLastCommit: 0,
    overDir: 0, overAmount: 0, overSince: 0,
    uiIndex: -1, railIndex: -1
  };

  var E = {};
  var railTimer = 0;

  function $(id) { return document.getElementById(id); }
  function clamp(n, min, max) { return n < min ? min : (n > max ? max : n); }
  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function t(key, vars) {
    var dict = STR[S.lang] || STR.zh;
    var value = dict[key];
    if (value === undefined) value = STR.zh[key];
    if (value === undefined) return key;
    if (vars && typeof value === 'string') {
      value = value.replace(/\{(\w+)\}/g, function (m, k) {
        return vars[k] === undefined ? m : vars[k];
      });
    }
    return value;
  }
  function loc(value) {
    if (value === null || value === undefined) return '';
    return typeof value === 'string' ? value : (value[S.lang] || value.zh || '');
  }
  function fmtTime(seconds) {
    var s = Math.max(0, Math.round(seconds));
    return Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
  }
  function currentTrack() { return TRACKS[S.track]; }
  function progressPct() { return clamp((S.progress / currentTrack().duration) * 100, 0, 100); }
  function visibleNotifs() { return NOTIFICATIONS.filter(function (n) { return !S.dismissed.has(n.id); }); }
  function activeNotif() { return visibleNotifs()[0] || NOTIFICATIONS[0]; }
  function activeClip() {
    for (var i = 0; i < CLIPS.length; i++) if (CLIPS[i].id === S.copiedId) return CLIPS[i];
    return CLIPS[0];
  }
  function activeAgent() {
    if (S.agentStatus === 'done') return AGENTS[1];
    if (S.agentStatus === 'error') return AGENTS[3];
    return AGENTS[0];
  }
  function pageIndex(id) {
    for (var i = 0; i < PAGES.length; i++) if (PAGES[i].id === id) return i;
    return 0;
  }
  function tabForView(view) {
    if (view === 'media') return 'media';
    if (view === 'notifications') return 'notifications';
    if (view === 'clipboard') return 'clipboard';
    if (view === 'agent') return 'agent';
    return 'home';
  }
  function viewForTab(tab) {
    if (tab === 'media') return 'media';
    if (tab === 'notifications') return 'notifications';
    if (tab === 'clipboard') return 'clipboard';
    if (tab === 'agent') return 'agent';
    return 'idle';
  }
  function artHTML(track, cls) {
    return '<span class="art ' + (cls || '') + '" data-art="' + track.art + '" aria-hidden="true"></span>';
  }

  /* ==========================================================================
     04. SCENE, TASKBAR, DOCK
     ========================================================================== */
  function renderScene() {
    E.desktopIcons.innerHTML = [
      { icon: 'folder', label: t('scene.projects') },
      { icon: 'image', label: t('scene.spec') },
      { icon: 'file', label: t('scene.readme') },
      { icon: 'terminal', label: t('scene.terminal') }
    ].map(function (x) {
      return '<div class="desk-icon"><span class="tile">' + icon(x.icon, 19) + '</span><span class="label">' + esc(x.label) + '</span></div>';
    }).join('');

    var sideRow = function (ic, label, active) {
      return '<div class="win-side-row' + (active ? ' active' : '') + '">' + icon(ic, 11) + '<span>' + esc(label) + '</span></div>';
    };
    var file = function (ic, label) {
      return '<div class="win-file">' + icon(ic, 17) + '<span>' + esc(label) + '</span></div>';
    };

    E.sceneWindows.innerHTML =
      '<article class="window window-explorer">' +
        '<div class="win-bar">' + icon('folder', 12) +
          '<span class="win-title">' + esc(t('scene.explorer')) + '</span>' +
          '<div class="win-controls"><i></i><i class="box"></i><i class="close"></i></div>' +
        '</div>' +
        '<div class="win-body">' +
          '<div class="win-side">' +
            '<div class="win-side-title">' + esc(t('scene.quickAccess')) + '</div>' +
            sideRow('folder', t('scene.desktop'), false) +
            sideRow('download', t('scene.downloads'), false) +
            sideRow('file', t('scene.documents'), false) +
            '<div class="win-side-title" style="margin-top:6px">' + esc(t('scene.thisPc')) + '</div>' +
            sideRow('windows', 'Windows (C:)', false) +
            sideRow('folder', 'Projects', true) +
          '</div>' +
          '<div class="win-main">' +
            '<div class="win-crumbs">' + icon('folder', 9) + '<span>Projects</span>' + icon('chevronRight', 9) + '<span>rewrite</span></div>' +
            '<div class="win-files">' +
              file('file', 'island-spec.svg') +
              file('folder', 'prototype') +
              file('folder', 'prototype2') +
              file('file', 'README.md') +
              file('file', 'tokens.css') +
              file('file', 'pager.js') +
              file('file', 'shot-home.png') +
              file('file', 'notes.md') +
            '</div>' +
            '<div class="win-status"><b>' + esc(t('scene.status')) + '</b></div>' +
          '</div>' +
        '</div>' +
      '</article>' +
      '<article class="window window-terminal">' +
        '<div class="win-bar">' + icon('terminal', 12) +
          '<span class="win-title">' + esc(t('scene.terminal')) + '</span>' +
          '<div class="win-controls"><i></i><i class="box"></i><i class="close"></i></div>' +
        '</div>' +
        '<div class="terminal-body">' +
          '<div><span class="cmd">' + esc(t('scene.termLine1')) + '</span></div>' +
          '<div class="dim">' + esc(t('scene.termLine2')) + '</div>' +
          '<div class="dim">' + esc(t('scene.termLine3')) + '</div>' +
          '<div><span class="cursor"></span></div>' +
        '</div>' +
      '</article>';

    E.sceneHint.textContent = t('scene.hint');
  }

  function renderTaskbar() {
    var apps = [
      { icon: 'windows', active: true },
      { icon: 'search', active: false },
      { icon: 'folder', active: false },
      { icon: 'terminal', active: false },
      { icon: 'music', active: false }
    ];
    E.taskbar.innerHTML = apps.map(function (a) {
      return '<span class="task-btn' + (a.active ? ' active' : '') + '">' + icon(a.icon, 17) + '</span>';
    }).join('') +
      '<div class="task-tray">' + icon('wifi', 13) + icon('volume', 13) + icon('battery', 13) +
        '<div class="task-clock"><b>14:32</b><span>' + esc(S.lang === 'zh' ? '2026/10/18' : '10/18/2026') + '</span></div>' +
      '</div>';
  }

  function renderDock() {
    var activeTab = S.mode === 'dock' ? (S.expanded ? S.tab : tabForView(S.view)) : null;
    E.dock.innerHTML = DOCK_APPS.map(function (a) {
      return '<button class="dock-app' + (a.tab === activeTab ? ' active' : '') + '" type="button" ' +
        'data-dock-tab="' + a.tab + '" aria-label="' + esc(t(a.label)) + '">' + icon(a.icon, 16) + '</button>';
    }).join('');
  }

  /* ==========================================================================
     05. COMPACT STATES
     ========================================================================== */
  function compactHTML() {
    var view = S.view;

    if (view === 'media') {
      var tr = currentTrack();
      return '<div class="pill pill-media">' +
        artHTML(tr, 'media-art') +
        '<div class="media-copy"><span class="media-title">' + esc(tr.title) + '</span><span class="media-sub">' + esc(tr.artist) + '</span></div>' +
        '<span class="wave' + (S.playing ? '' : ' paused') + '" aria-hidden="true"><i></i><i></i><i></i><i></i><i></i></span>' +
        '<button class="mini-play" type="button" data-act="toggle-play" aria-label="' + esc(t(S.playing ? 'media.pause' : 'media.play')) + '">' +
          icon(S.playing ? 'pause' : 'play', 12) +
        '</button>' +
        '<span class="media-progress" aria-hidden="true"><span style="width:' + progressPct().toFixed(2) + '%"></span></span>' +
      '</div>';
    }

    if (view === 'notifications') {
      var n = activeNotif();
      return '<div class="pill pill-notif">' +
        '<span class="app-glyph" style="--app:' + n.color + '">' + icon(n.icon, 12) + '</span>' +
        '<span class="notif-text"><b>' + esc(n.app) + '</b> · ' + esc(loc(n.title)) + '</span>' +
        (S.unread > 0 ? '<span class="unread-badge">' + S.unread + '</span>' : '') +
      '</div>';
    }

    if (view === 'clipboard') {
      var clip = activeClip();
      return '<div class="pill pill-clip">' +
        '<span class="slot clip-slot">' + icon('clipboard', 12) + '</span>' +
        '<span class="clip-text"><b class="clip-label">' + esc(t('clipboard.copied')) + '</b>' +
          '<span class="clip-dot"></span><span class="clip-preview">' + esc(loc(clip.content)) + '</span></span>' +
      '</div>';
    }

    if (view === 'agent') {
      var ag = activeAgent();
      return '<div class="pill pill-agent">' +
        '<span class="agent-ring ' + ag.status + '">' + (ag.status === 'running' ? '' : icon(ag.status === 'done' ? 'check' : 'alert', 10)) + '</span>' +
        '<span class="agent-task">' + esc(loc(ag.task)) + '</span>' +
        '<span class="agent-meta">' + esc(t('agent.' + ag.status)) + ' · ' + ag.duration + '</span>' +
      '</div>';
    }

    if (view === 'timer') {
      var remain = 24 * 60 + 36;
      var total = 25 * 60;
      var circ = 2 * Math.PI * 8.4;
      return '<div class="pill pill-timer">' +
        '<span class="timer-glyph">' + icon('timer', 17) + '</span>' +
        '<span class="timer-time">' + fmtTime(remain) + '</span>' +
        '<span class="timer-label">' + esc(t('timer.focus')) + '</span>' +
        '<span class="timer-ring" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none">' +
          '<circle class="tr-track" cx="12" cy="12" r="8.4" stroke-width="2"/>' +
          '<circle class="tr-fill" cx="12" cy="12" r="8.4" stroke-width="2" stroke-dasharray="' + circ.toFixed(2) +
            '" stroke-dashoffset="' + (circ * (1 - remain / total)).toFixed(2) + '"/></svg></span>' +
      '</div>';
    }

    if (view === 'battery') {
      return '<div class="pill pill-battery">' +
        '<span class="battery-ico">' + icon('bolt', 14) + '</span>' +
        '<span class="battery-meta">' + esc(t('battery.plugged')) + '</span>' +
        '<span class="battery-pct">86%</span>' +
      '</div>';
    }

    if (view === 'hud') {
      var isVol = S.hudType === 'volume';
      var val = isVol ? S.volume : S.brightness;
      return '<div class="pill pill-hud">' +
        '<span class="hud-ico">' + icon(isVol ? 'volume' : 'brightness', 17) + '</span>' +
        '<span class="hud-label">' + esc(t(isVol ? 'hud.volume' : 'hud.brightness')) + '</span>' +
        '<span class="hud-track"><span class="hud-fill" style="width:' + val + '%"></span></span>' +
        '<span class="hud-val">' + val + '%</span>' +
      '</div>';
    }

    return '<div class="pill pill-idle">' +
      '<span class="idle-time">14:32</span>' +
      '<span class="idle-dot" aria-hidden="true"></span>' +
      '<span class="idle-date">' + esc(t('home.shortDate')) + '</span>' +
    '</div>';
  }

  function renderCompact() {
    E.island.dataset.view = S.view;
    E.island.style.setProperty('--track-accent', currentTrack().accent);
    E.compactLayer.innerHTML = compactHTML();
  }

  /* ==========================================================================
     06. EXPANDED PAGES
     ========================================================================== */
  function pageHTML(page) {
    if (page.id === 'media') return mediaHTML();
    if (page.id === 'notifications') return notificationsHTML();
    if (page.id === 'clipboard') return clipboardHTML();
    if (page.id === 'agent') return agentHTML();
    if (page.id === 'widgets') return widgetsHTML();
    return homeHTML();
  }

  function homeHTML() {
    var tr = currentTrack();
    var quick = [
      { id: 'wifi', icon: 'wifi', label: t('home.quick.wifi'), on: S.toggles.wifi },
      { id: 'bluetooth', icon: 'bluetooth', label: t('home.quick.bluetooth'), on: S.toggles.bluetooth },
      { id: 'focus', icon: 'moon', label: t('home.quick.focus'), on: S.toggles.focus },
      { id: 'dark', icon: S.theme === 'dark' ? 'moon' : 'sun', label: t('home.quick.dark'), on: S.theme === 'dark' }
    ];
    return '<div class="home">' +
      '<div class="home-top">' +
        '<div><div class="clock-time">14:32</div><div class="clock-date">' + esc(t('home.date')) + '</div></div>' +
        '<div class="weather">' + icon('cloudSun', 16) +
          '<div class="weather-copy"><span class="weather-temp">18°</span><span class="weather-sub">' + esc(t('home.weather')) + '</span></div>' +
        '</div>' +
      '</div>' +
      '<div class="quick-grid">' + quick.map(function (q) {
        return '<button class="q-toggle' + (q.on ? ' on' : '') + '" type="button" data-act="quick" data-quick="' + q.id + '" aria-pressed="' + q.on + '">' +
          icon(q.icon, 14) + '<span>' + esc(q.label) + '</span></button>';
      }).join('') + '</div>' +
      '<div class="media-card" data-act="go-media">' + artHTML(tr, 'media-art') +
        '<div class="media-copy"><span class="media-title">' + esc(tr.title) + '</span><span class="media-sub">' + esc(tr.artist) + ' · ' + esc(tr.album) + '</span></div>' +
        '<button class="mini-play" type="button" data-act="toggle-play" aria-label="' + esc(t(S.playing ? 'media.pause' : 'media.play')) + '">' +
          icon(S.playing ? 'pause' : 'play', 12) + '</button>' +
        '<span class="media-progress" aria-hidden="true"><span style="width:' + progressPct().toFixed(2) + '%"></span></span>' +
      '</div>' +
      '<div class="slider-card">' +
        '<div class="slider-cell">' + icon('volume', 13) +
          '<input class="fb-range" type="range" min="0" max="100" value="' + S.volume + '" data-slider="volume" aria-label="' + esc(t('hud.volume')) + '" style="--val:' + S.volume + '%">' +
          '<span class="slider-pct js-volume">' + S.volume + '</span>' +
        '</div>' +
        '<span class="slider-divider"></span>' +
        '<div class="slider-cell">' + icon('brightness', 13) +
          '<input class="fb-range" type="range" min="0" max="100" value="' + S.brightness + '" data-slider="brightness" aria-label="' + esc(t('hud.brightness')) + '" style="--val:' + S.brightness + '%">' +
          '<span class="slider-pct js-brightness">' + S.brightness + '</span>' +
        '</div>' +
      '</div>' +
      '<div class="battery-line">' +
        '<span class="batt-visual"><span></span></span><strong>86%</strong>' +
        '<span>· ' + esc(t('home.charging')) + '</span>' +
        '<span class="right">' + esc(t('home.remaining')) + '</span>' +
      '</div>' +
    '</div>';
  }

  function mediaHTML() {
    var tr = currentTrack();
    var lyric = tr.lyrics[S.lang] || tr.lyrics.zh;
    var pct = progressPct();
    return '<div class="media-page">' +
      '<div class="media-top">' + artHTML(tr, 'media-art-lg') +
        '<div class="media-info">' +
          '<div class="media-eyebrow">' + esc(t('media.nowPlaying')) + '</div>' +
          '<div class="media-title">' + esc(tr.title) + '</div>' +
          '<div class="media-artist">' + esc(tr.artist) + '</div>' +
          '<div class="media-album">' + esc(tr.album) + ' · 2026</div>' +
        '</div>' +
      '</div>' +
      '<div class="lyric-card">' +
        '<div class="lyric-now">' + esc(lyric[0]) + '</div>' +
        '<div class="lyric-next"><span class="tag">' + esc(t('media.nextLine')) + '</span><span class="text">' + esc(lyric[1]) + '</span></div>' +
      '</div>' +
      '<div class="seek-row">' +
        '<span class="seek-time js-time-current">' + fmtTime(S.progress) + '</span>' +
        '<div class="seek" data-act="seek" role="slider" tabindex="0" aria-label="Seek" aria-valuemin="0" aria-valuemax="' + tr.duration + '" aria-valuenow="' + Math.round(S.progress) + '">' +
          '<div class="seek-track"><div class="seek-fill" style="width:' + pct.toFixed(2) + '%"></div><div class="seek-thumb" style="left:' + pct.toFixed(2) + '%"></div></div>' +
        '</div>' +
        '<span class="seek-time end js-time-remaining">' + fmtTime(tr.duration - S.progress) + '</span>' +
      '</div>' +
      '<div class="media-foot">' +
        '<div class="transport">' +
          '<button class="transport-btn" type="button" data-act="prev" aria-label="' + esc(t('media.prev')) + '">' + icon('prev', 14) + '</button>' +
          '<button class="play-btn" type="button" data-act="toggle-play" aria-label="' + esc(t(S.playing ? 'media.pause' : 'media.play')) + '">' + icon(S.playing ? 'pause' : 'play', 13) + '</button>' +
          '<button class="transport-btn" type="button" data-act="next" aria-label="' + esc(t('media.next')) + '">' + icon('next', 14) + '</button>' +
        '</div>' +
        '<div class="media-output">' +
          '<button class="output-btn" type="button" data-act="output">' + icon('speaker', 12) + '<span>' + esc(t('media.speaker')) + '</span></button>' +
          '<button class="queue-btn" type="button" data-act="queue">' + icon('queue', 12) + '<span>' + esc(t('media.queue')) + '</span><span class="queue-count">· 3</span></button>' +
        '</div>' +
      '</div>' +
    '</div>';
  }

  function notificationsHTML() {
    var vis = visibleNotifs();
    if (!vis.length) {
      return '<div class="pane"><div class="empty-state">' + icon('bell', 26) +
        '<div class="empty-title">' + esc(t('notifications.emptyTitle')) + '</div>' +
        '<div class="empty-sub">' + esc(t('notifications.emptySub')) + '</div></div></div>';
    }
    return '<div class="pane"><div class="scroll-area notif-list">' + vis.map(function (n) {
      return '<div class="notif-row" data-act="notif-open" data-id="' + n.id + '" role="button" tabindex="0">' +
        '<span class="app-glyph lg" style="--app:' + n.color + '">' + icon(n.icon, 14) + '</span>' +
        '<div class="notif-copy">' +
          '<div class="notif-line"><span class="notif-app">' + esc(n.app) + '</span><span class="notif-title">' + esc(loc(n.title)) + '</span><span class="notif-time">' + esc(loc(n.time)) + '</span></div>' +
          '<div class="notif-body">' + esc(loc(n.body)) + '</div>' +
        '</div>' +
        '<button class="notif-dismiss" type="button" data-act="notif-dismiss" data-id="' + n.id + '" aria-label="' + esc(t('notifications.dismiss')) + '">' + icon('close', 10) + '</button>' +
      '</div>';
    }).join('') + '</div></div>';
  }

  function clipboardHTML() {
    var items = CLIPS.slice().sort(function (a, b) {
      return (S.pinned.has(b.id) ? 1 : 0) - (S.pinned.has(a.id) ? 1 : 0);
    });
    return '<div class="pane"><div class="scroll-area clip-list">' + items.map(function (it) {
      var pinned = S.pinned.has(it.id);
      var copied = S.copiedId === it.id;
      return '<div class="clip-row' + (pinned ? ' pinned' : '') + (copied ? ' copied' : '') + '" data-act="clip-copy" data-id="' + it.id + '" role="button" tabindex="0">' +
        '<span class="clip-badge ' + it.type + '">' + icon(it.icon, 12) + '</span>' +
        '<div class="clip-main">' +
          '<div class="clip-line' + (it.mono ? ' mono' : '') + '">' + esc(loc(it.content)) + '</div>' +
          '<div class="clip-sub">' +
            (pinned ? '<span class="pin-mark">' + icon('pin', 8) + esc(t('clipboard.pinned')) + '</span><span>·</span>' : '') +
            '<span>' + esc(t('clipboard.type.' + it.type)) + '</span><span>·</span><span>' + esc(loc(it.time)) + '</span>' +
          '</div>' +
        '</div>' +
        '<div class="clip-actions">' +
          '<button class="icon-btn pin-btn" type="button" data-act="clip-pin" data-id="' + it.id + '" aria-label="' + esc(t(pinned ? 'clipboard.unpin' : 'clipboard.pin')) + '">' + icon('pin', 11) + '</button>' +
          '<button class="icon-btn copy-btn" type="button" data-act="clip-copy" data-id="' + it.id + '" aria-label="' + esc(t('clipboard.copy')) + '">' + icon(copied ? 'check' : 'copy', 11) + '</button>' +
        '</div>' +
      '</div>';
    }).join('') + '</div></div>';
  }

  function agentHTML() {
    return '<div class="pane"><div class="agent-list">' + AGENTS.map(function (a) {
      return '<div class="agent-row">' +
        '<span class="agent-state ' + a.status + '">' +
          (a.status === 'running' ? '<span class="spinner-ring"></span>' : icon(a.status === 'done' ? 'check' : 'alert', 11)) +
        '</span>' +
        '<div class="agent-main">' +
          '<div class="agent-head"><span class="agent-tool">' + esc(a.tool) + '</span><span class="agent-project">' + esc(a.project) + '</span></div>' +
          '<div class="agent-task">' + esc(loc(a.task)) + '</div>' +
        '</div>' +
        '<div class="agent-right"><span class="agent-status ' + a.status + '">' + esc(t('agent.' + a.status)) + '</span><span class="agent-duration">' + a.duration + '</span></div>' +
        (a.status === 'running' ? '<span class="agent-progress"><span></span></span>' : '') +
      '</div>';
    }).join('') + '</div></div>';
  }

  function monthLabel() {
    var zhMonths = ['1月', '2月', '3月', '4月', '5月', '6月', '7月', '8月', '9月', '10月', '11月', '12月'];
    var enMonths = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
    return S.lang === 'zh'
      ? S.calYear + '年' + zhMonths[S.calMonth]
      : enMonths[S.calMonth] + ' ' + S.calYear;
  }

  function calendarHTML() {
    var first = new Date(S.calYear, S.calMonth, 1);
    var start = (first.getDay() + 6) % 7;
    var days = new Date(S.calYear, S.calMonth + 1, 0).getDate();
    var prevDays = new Date(S.calYear, S.calMonth, 0).getDate();
    var cells = Math.ceil((start + days) / 7) * 7;
    var out = '';
    for (var i = 0; i < cells; i++) {
      var dayNum = i - start + 1;
      var n, mute = false;
      if (dayNum < 1) { n = prevDays + dayNum; mute = true; }
      else if (dayNum > days) { n = dayNum - days; mute = true; }
      else n = dayNum;
      var isToday = !mute && S.calYear === 2026 && S.calMonth === 9 && n === 18;
      var isEvent = !mute && S.calYear === 2026 && S.calMonth === 9 && (n === 18 || n === 22 || n === 27);
      out += '<span class="cal-day' + (mute ? ' mute' : '') + (isToday ? ' today' : '') + (isEvent && !isToday ? ' event' : '') + '">' + n + '</span>';
    }
    var weekdays = t('cal.weekdays');
    var doneCount = S.todos.filter(function (x) { return x.done; }).length;
    return '<div class="widgets">' +
      '<div class="cal-card">' +
        '<div class="cal-head"><span class="cal-month">' + esc(monthLabel()) + '</span>' +
          '<span class="cal-nav">' +
            '<button type="button" data-act="cal-prev" aria-label="Previous month">' + icon('chevronLeft', 10) + '</button>' +
            '<button type="button" data-act="cal-next" aria-label="Next month">' + icon('chevronRight', 10) + '</button>' +
          '</span>' +
        '</div>' +
        '<div class="cal-week">' + weekdays.map(function (d) { return '<span>' + esc(d) + '</span>'; }).join('') + '</div>' +
        '<div class="cal-grid">' + out + '</div>' +
        '<div class="cal-foot"><span class="dot-accent"></span><span>' + esc(t('widgets.calEvent')) + '</span><span class="today-label">' + esc(t('widgets.calToday')) + '</span></div>' +
      '</div>' +
      '<div class="widget-side">' +
        '<div class="weather-card">' +
          '<span class="w-icon">' + icon('cloudSun', 18) + '</span>' +
          '<div class="w-copy"><span class="w-temp">18°</span><span class="w-desc">' + esc(t('widgets.weather')) + '</span></div>' +
          '<div class="w-meta"><strong>' + esc(t('widgets.highLow')) + '</strong><span>' + esc(t('widgets.wind')) + '</span></div>' +
        '</div>' +
        '<div class="stats-card">' +
          '<div class="stat-row"><span class="stat-label">' + esc(t('widgets.cpu')) + '</span><span class="stat-track"><span class="stat-fill cpu"></span></span><span class="stat-value">38%</span></div>' +
          '<div class="stat-row"><span class="stat-label">' + esc(t('widgets.ram')) + '</span><span class="stat-track"><span class="stat-fill ram"></span></span><span class="stat-value">64%</span></div>' +
          '<div class="stat-row"><span class="stat-label">' + esc(t('widgets.battery')) + '</span><span class="stat-track"><span class="stat-fill batt"></span></span><span class="stat-value">86%</span></div>' +
        '</div>' +
        '<div class="notes-card">' +
          '<div class="notes-head">' + esc(t('widgets.notes')) + '<span>' + esc(t('widgets.progress', { n: doneCount, m: S.todos.length })) + '</span></div>' +
          S.todos.map(function (td, i) {
            return '<button class="todo-row' + (td.done ? ' done' : '') + '" type="button" data-act="todo" data-index="' + i + '">' +
              '<span class="check">' + icon('check', 7) + '</span><span class="label">' + esc(t(td.key)) + '</span></button>';
          }).join('') +
        '</div>' +
      '</div>' +
    '</div>';
  }

  function widgetsHTML() {
    return '<div class="pane">' + calendarHTML() + '</div>';
  }

  function renderPages() {
    E.pagerTrack.innerHTML = PAGES.map(function (page, i) {
      return '<section class="page" data-page="' + page.id + '" role="group" aria-roledescription="slide" ' +
        'aria-label="' + (i + 1) + ' / ' + PAGES.length + ' · ' + esc(t('tab.' + page.id)) + '">' + pageHTML(page) + '</section>';
    }).join('');
    applyPagerTransform();
    var idx = clamp(P.uiIndex >= 0 ? P.uiIndex : Math.round(P.offset), 0, MAX_PAGE);
    updatePageA11y(idx);
    syncTitle(idx);
  }

  /* ==========================================================================
     07. TRANSIENT PREVIEW ISLANDS
     ========================================================================== */
  function buildRail() {
    E.miniRail.innerHTML =
      '<span class="mini prev" aria-hidden="true"></span>' +
      '<span class="mini current" aria-hidden="true"></span>' +
      '<span class="mini next" aria-hidden="true"></span>';
  }

  function fillRailSlot(el, page, kind) {
    if (!page) {
      el.classList.add('empty');
      el.innerHTML = '';
      return;
    }
    el.classList.remove('empty');
    if (kind === 'current') {
      el.innerHTML = icon(page.icon, 11) + '<span class="mini-label">' + esc(t('short.' + page.id)) + '</span>';
    } else {
      el.innerHTML = icon(page.icon, 12);
    }
  }

  function updateRail(index) {
    index = clamp(index, 0, MAX_PAGE);
    P.railIndex = index;
    var prev = index > 0 ? PAGES[index - 1] : null;
    var next = index < MAX_PAGE ? PAGES[index + 1] : null;
    fillRailSlot(E.miniRail.querySelector('.mini.prev'), prev, 'prev');
    fillRailSlot(E.miniRail.querySelector('.mini.current'), PAGES[index], 'current');
    fillRailSlot(E.miniRail.querySelector('.mini.next'), next, 'next');
  }

  function showRail(index) {
    if (!S.expanded) return;
    updateRail(index === undefined ? clamp(Math.round(P.offset), 0, MAX_PAGE) : index);
    if (S.forceArch) {
      E.miniRail.classList.add('is-visible');
      return;
    }
    if (!E.miniRail.classList.contains('is-visible')) {
      E.miniRail.classList.remove('is-visible');
      void E.miniRail.offsetWidth;
      E.miniRail.classList.add('is-visible');
    }
    clearTimeout(railTimer);
    railTimer = setTimeout(function () { hideRail(); }, RAIL_DELAY);
  }

  function hideRail(force) {
    if (S.forceArch && !force) return;
    clearTimeout(railTimer);
    railTimer = 0;
    E.miniRail.classList.remove('is-visible');
  }

  /* ==========================================================================
     08. PAGER
     ========================================================================== */
  function applyPagerTransform() {
    E.pagerTrack.style.transform = 'translate3d(' + (-P.offset * PAGE_W).toFixed(3) + 'px, 0, 0)';
  }

  function syncTitle(index) {
    index = clamp(index, 0, MAX_PAGE);
    var page = PAGES[index];
    E.pagerTitle.innerHTML = '<span class="page-no">' + String(index + 1).padStart(2, '0') + '</span>' +
      '<span>' + esc(t('tab.' + page.id)) + '</span>';
  }

  function updatePageA11y(index) {
    var pages = E.pagerTrack.children;
    for (var i = 0; i < pages.length; i++) {
      var active = i === index;
      pages[i].setAttribute('aria-hidden', active ? 'false' : 'true');
      if (active) pages[i].removeAttribute('inert');
      else pages[i].setAttribute('inert', '');
    }
  }

  function applyPagerState() {
    var idx = clamp(Math.round(P.offset), 0, MAX_PAGE);
    if (idx !== P.uiIndex) {
      P.uiIndex = idx;
      S.tab = PAGES[idx].id;
      syncTitle(idx);
      updatePageA11y(idx);
      updateDockActive();
      if (S.forceArch) updateRail(idx);
    }
  }

  function applyPager() {
    applyPagerTransform();
    applyPagerState();
  }

  function cancelSnap() {
    if (P.raf) cancelAnimationFrame(P.raf);
    P.raf = 0;
    P.animating = false;
  }

  function snapTick(now) {
    P.raf = 0;
    var dt = clamp((now - P.snapLast) / 1000, 0.001, 0.05);
    P.snapLast = now;
    var steps = Math.max(1, Math.ceil(dt / 0.004));
    var h = dt / steps;
    var w = (2 * Math.PI) / PAGER.snapT;
    var z = PAGER.snapZ;
    for (var i = 0; i < steps; i++) {
      var a = -w * w * (P.offset - P.target) - 2 * z * w * P.v;
      P.v += a * h;
      P.offset += P.v * h;
    }
    if (Math.abs(P.offset - P.target) < 0.0006 && Math.abs(P.v) < 0.006) {
      P.offset = P.target;
      P.v = 0;
      P.animating = false;
      applyPager();
      return;
    }
    applyPager();
    P.raf = requestAnimationFrame(snapTick);
  }

  function springTo(target, initialV) {
    P.target = clamp(target, 0, MAX_PAGE);
    if (S.motion === 'reduced') {
      cancelSnap();
      P.offset = P.target;
      P.v = 0;
      applyPager();
      return;
    }
    P.v = initialV || 0;
    if (!P.raf) {
      P.animating = true;
      P.snapLast = performance.now();
      P.raf = requestAnimationFrame(snapTick);
    }
  }

  function setPageInstant(index) {
    cancelSnap();
    P.offset = clamp(index, 0, MAX_PAGE);
    P.target = P.offset;
    P.v = 0;
    P.uiIndex = -1;
    applyPager();
  }

  function turnPage(dir) {
    var base = P.animating ? P.target : clamp(Math.round(P.offset), 0, MAX_PAGE);
    var next = clamp(base + dir, 0, MAX_PAGE);
    if (next === base) {
      showRail(base);
      return;
    }
    springTo(next, 0);
    showRail(next);
    P.wheelAccum = 0;
    P.wheelLastCommit = performance.now();
  }

  function rubber(raw) {
    if (raw < 0) return raw * PAGER.rubber;
    if (raw > MAX_PAGE) return MAX_PAGE + (raw - MAX_PAGE) * PAGER.rubber;
    return raw;
  }

  function normalizedWheel(e, delta) {
    if (e.deltaMode === 1) return delta * 16;
    if (e.deltaMode === 2) return delta * (E.pager.clientHeight || 260);
    return delta;
  }

  function scrollableFrom(node) {
    var el = node && node.closest ? node.closest('.scroll-area') : null;
    if (!el) return null;
    if (!E.pagerTrack.contains(el)) return null;
    return el;
  }

  function onPointerDown(e) {
    if (!S.expanded) return;
    if (e.button !== undefined && e.button !== 0) return;
    if (e.target.closest('button, a, input, textarea, select, [data-no-drag], [data-act="seek"]')) return;
    cancelSnap();
    P.dragging = true;
    P.moved = false;
    P.startX = e.clientX;
    P.startY = e.clientY;
    P.startOffset = P.offset;
    P.startPage = clamp(Math.round(P.offset), 0, MAX_PAGE);
    P.lastX = e.clientX;
    P.lastT = performance.now();
    P.vx = 0;
    P.suppressClick = 0;
    E.pager.classList.add('dragging');
    try { E.pagerViewport.setPointerCapture(e.pointerId); } catch (err) { /* noop */ }
  }

  function onPointerMove(e) {
    if (!P.dragging) return;
    var dx = e.clientX - P.startX;
    var dy = e.clientY - P.startY;
    if (!P.moved) {
      if (Math.abs(dx) < 4 && Math.abs(dy) < 4) return;
      if (Math.abs(dy) > Math.abs(dx) * 1.15) {
        /* Vertical intent: leave native scrolling alone. */
        P.dragging = false;
        E.pager.classList.remove('dragging');
        try { E.pagerViewport.releasePointerCapture(e.pointerId); } catch (err) { /* noop */ }
        return;
      }
      P.moved = true;
    }
    if (e.cancelable) e.preventDefault();
    var raw = P.startOffset - (dx * PAGER.gain) / PAGE_W;
    P.offset = rubber(raw);
    var now = performance.now();
    var dt = now - P.lastT;
    if (dt > 0) {
      var v = (e.clientX - P.lastX) / dt;
      P.vx = P.vx * 0.72 + v * 0.28;
      P.lastX = e.clientX;
      P.lastT = now;
    }
    applyPager();
    var idx = clamp(Math.round(P.offset), 0, MAX_PAGE);
    showRail(idx);
  }

  function onPointerUp(e) {
    if (!P.dragging) return;
    P.dragging = false;
    E.pager.classList.remove('dragging');
    try { E.pagerViewport.releasePointerCapture(e.pointerId); } catch (err) { /* noop */ }
    if (!P.moved) return;

    P.suppressClick = performance.now() + 360;
    var dx = e.clientX - P.startX;
    var raw = P.startOffset - (dx * PAGER.gain) / PAGE_W;
    var distance = raw - P.startOffset;
    var target;

    if (Math.abs(distance) >= 0.75) {
      /* Long drag: distance decides, so several pages are reachable. */
      target = clamp(Math.round(raw), 0, MAX_PAGE);
    } else if (Math.abs(P.vx) > PAGER.flick && Math.abs(dx) > 10) {
      /* Flick: exactly one page, never more. */
      target = clamp(P.startPage + (P.vx > 0 ? -1 : 1), 0, MAX_PAGE);
    } else {
      target = clamp(Math.round(P.offset), 0, MAX_PAGE);
    }

    var v0 = clamp((-P.vx * PAGER.gain / PAGE_W) * 1000, -14, 14);
    springTo(target, v0);
    showRail(target);
  }

  function onWheel(e) {
    if (!S.expanded) return;
    var dx = e.deltaX || 0;
    var dy = e.deltaY || 0;
    var delta = Math.abs(dx) > Math.abs(dy) ? dx : dy;
    if (!delta) return;
    var amount = normalizedWheel(e, delta);
    var dir = amount > 0 ? 1 : -1;
    var now = performance.now();

    var scroller = scrollableFrom(e.target);
    if (scroller && scroller.scrollHeight <= scroller.clientHeight + 1) scroller = null;
    if (scroller) {
      var canScroll = dir > 0
        ? scroller.scrollTop + scroller.clientHeight < scroller.scrollHeight - 1
        : scroller.scrollTop > 0;
      if (canScroll) {
        if (e.cancelable) e.preventDefault();
        scroller.scrollTop += amount;
        P.overDir = 0;
        P.overAmount = 0;
        return;
      }
      /* At the boundary: hold 40px of overscroll for 120ms to chain. */
      if (e.cancelable) e.preventDefault();
      if (P.overDir !== dir) {
        P.overDir = dir;
        P.overAmount = 0;
        P.overSince = now;
      }
      P.overAmount += Math.abs(amount);
      if (P.overAmount >= PAGER.overscroll && now - P.overSince >= PAGER.dwell) {
        P.overAmount = 0;
        P.overSince = now;
        P.overDir = 0;
        turnPage(dir);
      }
      return;
    }

    if (e.cancelable) e.preventDefault();
    if (P.wheelAccum !== 0 && Math.sign(amount) !== Math.sign(P.wheelAccum)) P.wheelAccum = 0;
    P.wheelAccum += amount;

    var base = P.animating ? P.target : clamp(Math.round(P.offset), 0, MAX_PAGE);
    showRail(base);

    if (Math.abs(P.wheelAccum) >= PAGER.wheelStep && now - P.wheelLastCommit >= PAGER.wheelWindow) {
      var sign = P.wheelAccum > 0 ? 1 : -1;
      P.wheelAccum -= sign * PAGER.wheelStep;
      P.wheelLastCommit = now;
      var next = clamp(base + sign, 0, MAX_PAGE);
      if (next !== base) {
        springTo(next, 0);
        showRail(next);
      }
    }
  }

  function bindPager() {
    E.pagerViewport.addEventListener('pointerdown', onPointerDown);
    window.addEventListener('pointermove', onPointerMove, { passive: false });
    window.addEventListener('pointerup', onPointerUp);
    window.addEventListener('pointercancel', onPointerUp);
    E.island.addEventListener('wheel', onWheel, { passive: false });
    E.pager.addEventListener('click', function (e) {
      if (performance.now() < P.suppressClick) {
        e.stopPropagation();
        e.preventDefault();
      }
    }, true);
    E.pager.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' || e.key === ' ') {
        var seek = e.target.closest ? e.target.closest('[data-act="seek"]') : null;
        if (seek) {
          e.preventDefault();
          var rect = seek.getBoundingClientRect();
          setSeek(seek, 0.5);
        }
      }
    });
  }

  function setSeek(el, ratio) {
    var tr = currentTrack();
    ratio = clamp(ratio, 0, 1);
    S.progress = Math.round(ratio * tr.duration);
    var fill = el.querySelector('.seek-fill');
    var thumb = el.querySelector('.seek-thumb');
    if (fill) fill.style.width = (ratio * 100).toFixed(2) + '%';
    if (thumb) thumb.style.left = (ratio * 100).toFixed(2) + '%';
    el.setAttribute('aria-valuenow', String(S.progress));
    var row = el.closest('.seek-row');
    if (row) {
      var cur = row.querySelector('.js-time-current');
      var remain = row.querySelector('.js-time-remaining');
      if (cur) cur.textContent = fmtTime(S.progress);
      if (remain) remain.textContent = fmtTime(tr.duration - S.progress);
    }
  }

  /* ==========================================================================
     09. SHELL, CONSOLE, URL PARAMS
     ========================================================================== */
  function syncChrome() {
    document.body.dataset.theme = S.theme;
    document.body.dataset.mode = S.mode;
    document.body.dataset.lang = S.lang;
    document.body.dataset.motion = S.motion;
    document.body.dataset.state = S.expanded ? 'expanded' : S.view;
    document.documentElement.lang = S.lang === 'zh' ? 'zh-CN' : 'en';
    E.island.setAttribute('aria-label', t(S.expanded ? 'island.collapse' : 'island.expand'));
  }

  function applyExpandedState() {
    E.island.dataset.expanded = S.expanded ? 'true' : 'false';
    E.island.setAttribute('aria-expanded', S.expanded ? 'true' : 'false');
    E.expandedLayer.inert = !S.expanded;
    E.expandedLayer.setAttribute('aria-hidden', S.expanded ? 'false' : 'true');
    E.pagerCollapse.tabIndex = S.expanded ? 0 : -1;
    E.anchor.dataset.expanded = S.expanded ? 'true' : 'false';
    if (!S.expanded) hideRail(true);
    syncChrome();
  }

  function expand(tab) {
    if (tab) S.tab = tab;
    if (!S.expanded) S.returnView = S.view;
    S.expanded = true;
    renderPages();
    setPageInstant(pageIndex(S.tab));
    applyExpandedState();
    renderCompact();
    updateConsole();
    updateDockActive();
  }

  function collapse() {
    if (!S.expanded) return;
    S.expanded = false;
    S.view = viewForTab(S.tab);
    renderCompact();
    applyExpandedState();
    updateConsole();
    updateDockActive();
  }

  function showCompact(view) {
    S.expanded = false;
    S.view = view;
    renderCompact();
    applyExpandedState();
    updateConsole();
    updateDockActive();
  }

  function setMode(mode) {
    S.mode = mode;
    syncChrome();
    renderDock();
    updateConsole();
  }

  function setTheme(theme) {
    S.theme = theme;
    syncChrome();
    renderScene();
    renderTaskbar();
    renderPages();
    updateConsole();
  }

  function setLang(lang) {
    S.lang = lang;
    syncChrome();
    renderScene();
    renderTaskbar();
    renderDock();
    renderCompact();
    renderPages();
    renderConsole();
    syncTitle(P.uiIndex >= 0 ? P.uiIndex : Math.round(P.offset));
    if (S.forceArch) showRail(P.railIndex >= 0 ? P.railIndex : Math.round(P.offset));
  }

  function setMotion(motion) {
    S.motion = motion;
    syncChrome();
    if (motion === 'reduced') {
      cancelSnap();
      P.offset = P.target;
      P.v = 0;
      applyPager();
    }
    renderConsole();
    updateConsole();
  }

  function updateDockActive() {
    if (!E.dock) return;
    var activeTab = S.mode === 'dock' ? (S.expanded ? S.tab : tabForView(S.view)) : null;
    var buttons = E.dock.querySelectorAll('.dock-app');
    for (var i = 0; i < buttons.length; i++) {
      buttons[i].classList.toggle('active', buttons[i].dataset.dockTab === activeTab);
    }
  }

  function renderConsole() {
    var states = [
      { id: 'idle', icon: 'islandIcon', key: 'trigger.idle' },
      { id: 'media', icon: 'music', key: 'trigger.media' },
      { id: 'notifications', icon: 'bell', key: 'trigger.notifications' },
      { id: 'clipboard', icon: 'clipboard', key: 'trigger.clipboard' },
      { id: 'hud', icon: 'volume', key: 'trigger.hud' },
      { id: 'timer', icon: 'timer', key: 'trigger.timer' },
      { id: 'battery', icon: 'batteryCharging', key: 'trigger.battery' },
      { id: 'agent', icon: 'agent', key: 'trigger.agent' }
    ];
    var activeState = S.expanded ? '' : S.view;
    var seg = function (setting, options, current) {
      return '<div class="segmented">' + options.map(function (o) {
        return '<button class="seg' + (o[0] === current ? ' active' : '') + '" type="button" data-set="' + setting + '" data-value="' + o[0] + '">' + esc(o[1]) + '</button>';
      }).join('') + '</div>';
    };

    E.console.innerHTML =
      '<div class="console-head">' +
        '<span class="console-mark">' + icon('islandIcon', 14) + '</span>' +
        '<div class="console-titles"><div class="console-title">' + esc(t('brand.title')) + '</div>' +
          '<div class="console-kicker">' + esc(t('brand.kicker')) + '</div></div>' +
        '<span class="console-live">' + esc(t('brand.live')) + '</span>' +
        '<button class="console-collapse" type="button" data-console-toggle aria-label="' + esc(t('console.collapsePanel')) + '">' + icon('chevronUp', 13) + '</button>' +
      '</div>' +
      '<div class="console-body">' +
        '<section class="console-sec">' +
          '<div class="sec-label"><span>' + esc(t('console.state')) + '</span><span class="hint">' + esc(t('console.stateHint')) + '</span></div>' +
          '<div class="trigger-grid">' + states.map(function (s) {
            return '<button class="trigger' + (activeState === s.id ? ' active' : '') + '" type="button" data-trigger="' + s.id + '">' +
              icon(s.icon, 12) + '<span>' + esc(t(s.key)) + '</span></button>';
          }).join('') + '</div>' +
          '<button class="trigger wide' + (S.expanded ? ' active' : '') + '" type="button" data-trigger="expanded">' +
            icon(S.expanded ? 'chevronUp' : 'chevronDown', 12) + '<span>' + esc(t(S.expanded ? 'console.collapsePanel' : 'console.expand')) + '</span></button>' +
        '</section>' +
        '<section class="console-sec">' +
          '<div class="sec-label"><span>' + esc(t('console.mode')) + '</span></div>' +
          seg('mode', [['float', t('mode.float')], ['flush', t('mode.flush')], ['dock', t('mode.dock')]], S.mode) +
        '</section>' +
        '<section class="console-sec">' +
          '<div class="console-row2">' +
            '<div><div class="field-label">' + esc(t('console.theme')) + '</div>' +
              seg('theme', [['dark', t('theme.dark')], ['light', t('theme.light')]], S.theme) + '</div>' +
            '<div><div class="field-label">' + esc(t('console.lang')) + '</div>' +
              seg('lang', [['zh', t('lang.zh')], ['en', t('lang.en')]], S.lang) + '</div>' +
          '</div>' +
        '</section>' +
        '<section class="console-sec">' +
          '<div class="sec-label"><span>' + esc(t('console.motion')) + '</span></div>' +
          seg('motion', [['normal', t('console.normal')], ['reduced', t('console.reduced')]], S.motion) +
        '</section>' +
      '</div>' +
      '<div class="console-foot">' +
        '<div class="console-note">' + esc(t('console.note')) + '</div>' +
        '<a class="console-archive" href="../prototype/index.html" target="_blank" rel="noopener">' +
          icon('external', 9) + '<span>' + esc(t('console.archive')) + '</span></a>' +
      '</div>';
  }

  function updateConsole() {
    renderConsole();
  }

  function parseParams() {
    var q = new URLSearchParams(window.location.search);
    var state = q.get('state');
    var tab = q.get('tab');
    var mode = q.get('mode');
    var theme = q.get('theme');
    var lang = q.get('lang');
    var motion = q.get('motion');

    if (theme === 'light' || theme === 'dark') S.theme = theme;
    if (lang === 'en' || lang === 'zh') S.lang = lang;
    if (mode === 'float' || mode === 'flush' || mode === 'dock') S.mode = mode;
    if (motion === 'reduced') S.motion = 'reduced';
    else if (motion === 'normal') S.motion = 'normal';
    else if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) S.motion = 'reduced';

    if (tab && EXPANDED_TABS.indexOf(tab) >= 0) S.tab = tab;

    if (state === 'expanded') {
      S.expanded = true;
      if (!tab || EXPANDED_TABS.indexOf(tab) < 0) S.tab = 'home';
    } else if (COMPACT_VIEWS.indexOf(state) >= 0) {
      S.view = state;
      if (state === 'hud') S.hudType = 'volume';
    }

    if (q.get('arch') === '1') {
      S.forceArch = true;
      S.expanded = true;
      if (state && state !== 'expanded' && COMPACT_VIEWS.indexOf(state) >= 0) {
        S.tab = tabForView(state);
      }
    }
  }

  /* ==========================================================================
     10. EVENTS & BOOT
     ========================================================================== */
  function onClick(e) {
    var trigger = e.target.closest('[data-trigger]');
    if (trigger) {
      var id = trigger.dataset.trigger;
      if (id === 'expanded') {
        if (S.expanded) collapse();
        else expand(tabForView(S.view));
      } else if (id === 'media') {
        showCompact('media');
      } else if (id === 'idle') {
        showCompact('idle');
      } else if (id === 'hud') {
        S.hudType = 'volume';
        showCompact('hud');
      } else if (COMPACT_VIEWS.indexOf(id) >= 0) {
        showCompact(id);
      }
      return;
    }

    var setter = e.target.closest('[data-set]');
    if (setter) {
      var key = setter.dataset.set;
      var value = setter.dataset.value;
      if (key === 'mode') setMode(value);
      else if (key === 'theme') setTheme(value);
      else if (key === 'lang') setLang(value);
      else if (key === 'motion') setMotion(value);
      return;
    }

    var dockBtn = e.target.closest('[data-dock-tab]');
    if (dockBtn) {
      expand(dockBtn.dataset.dockTab);
      return;
    }

    var consoleToggle = e.target.closest('[data-console-toggle]');
    if (consoleToggle) {
      E.console.classList.toggle('is-collapsed');
      return;
    }

    var act = e.target.closest('[data-act]');
    if (!act) return;
    var action = act.dataset.act;

    if (action === 'toggle-play') {
      S.playing = !S.playing;
      renderCompact();
      renderPages();
    } else if (action === 'prev' || action === 'next') {
      S.track = (S.track + (action === 'next' ? 1 : TRACKS.length - 1)) % TRACKS.length;
      S.progress = 0;
      renderCompact();
      renderPages();
    } else if (action === 'go-media') {
      expand('media');
    } else if (action === 'quick') {
      var quick = act.dataset.quick;
      if (quick === 'dark') {
        setTheme(S.theme === 'dark' ? 'light' : 'dark');
      } else if (S.toggles[quick] !== undefined) {
        S.toggles[quick] = !S.toggles[quick];
        renderPages();
      }
    } else if (action === 'notif-dismiss') {
      var nid = act.dataset.id;
      S.dismissed.add(nid);
      S.unread = Math.max(0, S.unread - 1);
      renderPages();
      if (!S.expanded) renderCompact();
    } else if (action === 'notif-open') {
      if (act.dataset.id) {
        S.dismissed.add(act.dataset.id);
        S.unread = Math.max(0, S.unread - 1);
        renderPages();
      }
    } else if (action === 'clip-copy') {
      S.copiedId = act.dataset.id;
      renderPages();
      if (!S.expanded) renderCompact();
    } else if (action === 'clip-pin') {
      var pid = act.dataset.id;
      if (S.pinned.has(pid)) S.pinned.delete(pid);
      else S.pinned.add(pid);
      renderPages();
    } else if (action === 'todo') {
      var ti = Number(act.dataset.index);
      if (S.todos[ti]) S.todos[ti].done = !S.todos[ti].done;
      renderPages();
    } else if (action === 'cal-prev' || action === 'cal-next') {
      var delta = action === 'cal-next' ? 1 : -1;
      S.calMonth += delta;
      if (S.calMonth < 0) { S.calMonth = 11; S.calYear -= 1; }
      if (S.calMonth > 11) { S.calMonth = 0; S.calYear += 1; }
      renderPages();
    } else if (action === 'seek') {
      var rect = act.getBoundingClientRect();
      setSeek(act, (e.clientX - rect.left) / rect.width);
    } else if (action === 'output' || action === 'queue') {
      /* Prototype: output pickers are intentionally out of scope. */
    }
  }

  function onKeyDown(e) {
    if (e.key === 'Escape' && S.expanded) {
      e.preventDefault();
      collapse();
      return;
    }
    if (!S.expanded) {
      if ((e.key === 'Enter' || e.key === ' ') && document.activeElement === E.island) {
        e.preventDefault();
        expand(tabForView(S.view));
      }
      return;
    }
    if (e.target.closest && e.target.closest('input, textarea, select')) return;
    var idx = clamp(Math.round(P.offset), 0, MAX_PAGE);
    if (/^[1-6]$/.test(e.key)) {
      e.preventDefault();
      springTo(Number(e.key) - 1, 0);
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault();
      springTo(idx - 1, 0);
    } else if (e.key === 'ArrowRight') {
      e.preventDefault();
      springTo(idx + 1, 0);
    } else if (e.key === 'Home') {
      e.preventDefault();
      springTo(0, 0);
    } else if (e.key === 'End') {
      e.preventDefault();
      springTo(MAX_PAGE, 0);
    }
  }

  function bindEvents() {
    document.addEventListener('click', onClick);
    document.addEventListener('keydown', onKeyDown);
    E.island.addEventListener('click', function (e) {
      if (e.target.closest('button, a, input, [data-act]')) return;
      if (!S.expanded) expand(tabForView(S.view));
    });
    E.island.addEventListener('keydown', function (e) {
      if ((e.key === 'Enter' || e.key === ' ') && !S.expanded && document.activeElement === E.island) {
        e.preventDefault();
        expand(tabForView(S.view));
      }
    });
    E.pagerCollapse.addEventListener('click', collapse);
    document.addEventListener('input', function (e) {
      var slider = e.target.closest('[data-slider]');
      if (!slider) return;
      var key = slider.dataset.slider;
      var value = Number(slider.value);
      S[key] = value;
      slider.style.setProperty('--val', value + '%');
      var label = slider.closest('.slider-cell').querySelector('.slider-pct');
      if (label) label.textContent = String(value);
      if (S.view === 'hud') renderCompact();
    });
    bindPager();
  }

  function boot() {
    document.body.classList.add('booting');
    E.island = $('island');
    E.anchor = $('anchor');
    E.compactLayer = $('compactLayer');
    E.expandedLayer = $('expandedLayer');
    E.pager = $('pager');
    E.pagerViewport = $('pagerViewport');
    E.pagerTrack = $('pagerTrack');
    E.pagerTitle = $('pagerTitle');
    E.pagerCollapse = $('pagerCollapse');
    E.pagerCollapseIcon = $('pagerCollapseIcon');
    E.miniRail = $('miniRail');
    E.console = $('console');
    E.sceneWindows = $('sceneWindows');
    E.desktopIcons = $('desktopIcons');
    E.sceneHint = $('sceneHint');
    E.taskbar = $('taskbar');
    E.dock = $('dock');

    E.pagerCollapseIcon.innerHTML = icon('chevronUp', 14);

    parseParams();
    S.view = S.expanded ? S.view : S.view;
    syncChrome();
    buildRail();
    renderScene();
    renderTaskbar();
    renderDock();
    renderConsole();
    renderCompact();
    renderPages();

    var startIndex = S.expanded ? pageIndex(S.tab) : 0;
    setPageInstant(startIndex);
    applyExpandedState();
    bindEvents();

    if (S.forceArch) {
      updateRail(startIndex);
      E.miniRail.classList.add('is-visible');
    }

    requestAnimationFrame(function () {
      document.body.classList.remove('booting');
      requestAnimationFrame(function () {
        document.body.classList.add('ready');
        document.documentElement.dataset.ready = '1';
      });
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
