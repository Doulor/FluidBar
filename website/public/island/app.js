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
  var RAIL_DELAY = 900;  /* default ms before the preview islands fade (settings → auto-collapse) */

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

  /* Preview-island morph spring: T 0.32 s, ζ 0.85. Transform/opacity only. */
  var RAIL_SPRING = { T: 0.32, Z: 0.85 };
  var RAIL_GAP = 8;          /* gap between preview islands */
  var RAIL_SLOT = 26;        /* prev/next circle diameter */
  var RAIL_CUR_BASE = 64;    /* current capsule base width (scaled in X) */
  var RAIL_DOT_GAP = 4;      /* gap between far-page dots */
  var RAIL_DOT_GAP_EDGE = 7; /* gap between an island and its dot cluster */
  var RAIL_DOT_MAX = 3;      /* dots per side */

  var ALL_PLUGINS = [
    {
      id: 'weather', name: 'plugin.weather.name', desc: 'plugin.weather.desc', version: '2.1.0',
      author: 'FluidBar Lab', builtin: true, verified: true, icon: 'cloudSun', color: '#2e7fa8',
      permissions: ['network'], category: 'productivity', rating: 4.8, downloads: '48.2k', installedByDefault: true
    },
    {
      id: 'timer', name: 'plugin.timer.name', desc: 'plugin.timer.desc', version: '2.0.0',
      author: 'FluidBar Lab', builtin: true, verified: true, icon: 'timer', color: '#a86428',
      permissions: [], category: 'productivity', rating: 4.7, downloads: '31.5k', installedByDefault: true
    },
    {
      id: 'github', name: 'plugin.github.name', desc: 'plugin.github.desc', version: '1.6.3',
      author: 'doulor', builtin: false, verified: true, icon: 'github', color: '#3d444d',
      permissions: ['network'], category: 'developer', rating: 4.9, downloads: '26.8k', installedByDefault: true
    },
    {
      id: 'sysmon', name: 'plugin.sysmon.name', desc: 'plugin.sysmon.desc', version: '2.0.4',
      author: 'FluidBar Lab', builtin: true, verified: true, icon: 'cpu', color: '#455466',
      permissions: [], category: 'system', rating: 4.6, downloads: '39.1k', installedByDefault: true
    },
    {
      id: 'lyrics', name: 'plugin.lyrics.name', desc: 'plugin.lyrics.desc', version: '0.9.8',
      author: 'Nova Studio', builtin: false, verified: true, icon: 'spotify', color: '#1e7a46',
      permissions: ['media', 'network'], category: 'media', rating: 4.5, downloads: '18.4k', installedByDefault: true
    },
    {
      id: 'calendar', name: 'plugin.calendar.name', desc: 'plugin.calendar.desc', version: '1.8.0',
      author: 'Mori Labs', builtin: false, verified: true, icon: 'calendar', color: '#8a5a2e',
      permissions: ['notifications'], category: 'productivity', rating: 4.7, downloads: '22.6k', installedByDefault: false
    },
    {
      id: 'stocks', name: 'plugin.stocks.name', desc: 'plugin.stocks.desc', version: '1.1.4',
      author: 'Kite Systems', builtin: false, verified: false, icon: 'chart', color: '#3c6652',
      permissions: ['network'], category: 'productivity', rating: 4.3, downloads: '9.7k', installedByDefault: false
    },
    {
      id: 'agent-bridge', name: 'plugin.agent-bridge.name', desc: 'plugin.agent-bridge.desc', version: '2.0.1',
      author: 'FluidBar Lab', builtin: false, verified: true, icon: 'agent', color: '#a65336',
      permissions: ['network', 'notifications'], category: 'ai', rating: 4.9, downloads: '12.8k', installedByDefault: false
    },
    {
      id: 'clipboard-plus', name: 'plugin.clipboard-plus.name', desc: 'plugin.clipboard-plus.desc', version: '1.2.0',
      author: 'doulor', builtin: false, verified: true, icon: 'clipboard', color: '#3a5b86',
      permissions: ['clipboard', 'notifications'], category: 'system', rating: 4.6, downloads: '15.3k', installedByDefault: true
    },
    {
      id: 'layout', name: 'plugin.layout.name', desc: 'plugin.layout.desc', version: '1.0.2',
      author: 'Rossi Works', builtin: false, verified: false, icon: 'layout', color: '#4e4a68',
      permissions: [], category: 'system', rating: 4.4, downloads: '7.2k', installedByDefault: false
    }
  ];
  var PERM_ICONS = { network: 'globe', clipboard: 'clipboard', media: 'music', notifications: 'bell' };
  var ACCENTS = { aqua: '#4cc9f0', mint: '#34c759', amber: '#ff9500', rose: '#ff6b8a', blue: '#0a84ff' };
  var SETTINGS_TABS = ['general', 'appearance', 'island', 'plugins', 'market', 'advanced', 'about'];
  var SETTINGS_NAV = [
    ['general', 'settings', 'nav.general'],
    ['appearance', 'palette', 'nav.appearance'],
    ['island', 'islandIcon', 'nav.island'],
    ['plugins', 'puzzle', 'nav.plugins'],
    ['market', 'storefront', 'nav.market'],
    ['advanced', 'sliders', 'nav.advanced'],
    ['about', 'info', 'nav.about']
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
      'console.note': '设计原型 · 设置与市场为模拟数据',
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
      'scene.termLine3': '✓ 13 screenshots · 0 console errors',

      /* ---- settings + marketplace -------------------------------------- */
      'settings.title': '设置', 'settings.sub': 'FLUIDBAR 2.0', 'settings.preferences': '偏好设置',
      'settings.prototypeNote': '交互原型 · 非发行版本', 'settings.open': '打开设置',
      'settings.market': '插件市场',
      'nav.general': '通用', 'nav.appearance': '外观', 'nav.island': '灵动岛', 'nav.plugins': '插件',
      'nav.market': '插件市场', 'nav.advanced': '高级', 'nav.about': '关于',
      'common.close': '关闭', 'common.install': '安装', 'common.installing': '安装中…',
      'common.installed': '已安装', 'common.builtin': '内置', 'common.community': '社区',
      'common.verified': '已验证', 'common.live': '实时', 'common.reset': '重置',

      'general.title': '通用', 'general.desc': '启动方式、更新策略与全局快捷键。所有更改仅保存在当前原型会话中。',
      'general.language': '界面语言', 'general.languageDesc': '即时切换所有界面文本，无需重新加载。',
      'general.startup': '开机时启动 FluidBar', 'general.startupDesc': '登录 Windows 后自动进入托盘并显示灵动岛。',
      'general.autoUpdate': '自动检查更新', 'general.autoUpdateDesc': '在后台下载稳定版更新，并在空闲时提示重启。',
      'general.tray': '显示系统托盘图标', 'general.trayDesc': '关闭后仍可通过全局快捷键唤起控制中心。',
      'general.shortcuts': '全局快捷键', 'general.shortcutOpen': '唤起控制中心',
      'general.shortcutPlay': '播放 / 暂停媒体', 'general.shortcutClip': '打开剪贴板历史',
      'general.shortcutHide': '临时隐藏灵动岛',

      'appearance.title': '外观', 'appearance.desc': '桌面主题与灵动岛材质。浅色桌面下灵动岛仍保持 Obsidian 黑，以确保内容对比度。',
      'appearance.theme': '桌面主题', 'appearance.themeDesc': '只影响桌面、控制台与设置窗口。',
      'appearance.material': '灵动岛材质', 'appearance.materialDesc': '亚克力是轻微雾面；液态玻璃额外有透镜边缘、顶部高光与轻微色散。两者都会透出桌面。',
      'material.obsidian': '黑曜石', 'material.acrylic': '亚克力', 'material.liquid': '液态玻璃',
      'appearance.accent': '强调色', 'appearance.accentDesc': '用于开关、进度与选中态；媒体页始终使用封面取色。',
      'appearance.radius': '大面板圆角', 'appearance.radiusDesc': '小胶囊始终保持 stadium 半径。',
      'appearance.opacity': '填充不透明度', 'appearance.opacityDesc': '默认 97%；低于 90% 时亚克力会自动增加模糊。',
      'appearance.liveHint': '更改会立即应用于灵动岛。',
      'appearance.preview': '灵动岛预览', 'appearance.meta': '{r} px · {o}%',
      'accent.aqua': '水蓝', 'accent.mint': '薄荷', 'accent.amber': '琥珀', 'accent.rose': '玫瑰', 'accent.blue': '钴蓝',

      'island.title': '灵动岛', 'island.desc': '形态、锚点与事件堆叠策略。',
      'island.defaultMode': '默认形态', 'island.defaultModeDesc': '浮岛常驻顶部；贴边更像 Windows 通知中心；Dock 替换任务栏。',
      'island.anchor': '锚点位置', 'island.anchorDesc': '浮岛模式下沿屏幕顶部对齐，仍可拖拽微调。',
      'island.anchorCenter': '居中', 'island.anchorLeft': '偏左', 'island.anchorRight': '偏右',
      'island.strategy': '显示策略', 'island.strategyDesc': '多个事件同时到达时的呈现方式。',
      'island.strategyStack': '多岛堆叠', 'island.strategyLatest': '仅显示最新',
      'island.hover': '悬停预览', 'island.hoverDesc': '悬停时轻微放大，并显示快捷控制。',
      'island.autoCollapse': '自动收起延迟', 'island.autoCollapseDesc': '临时预览条停留时间，同时决定预览岛的淡出时机。',
      'island.motion': '动效', 'island.motionDesc': '减弱模式会强制临界阻尼，并跳过弹簧过渡。',
      'island.motionStrength': '动效强度', 'island.motionStrengthDesc': '调节预览岛弹簧节奏与位移幅度。',

      'plugins.title': '插件', 'plugins.desc': '管理已安装的插件、启停状态与权限。插件以你的用户权限运行，请仅安装可信来源。',
      'plugins.search': '搜索已安装插件', 'plugins.openFolder': '打开插件目录',
      'plugins.count': '{n} 个已安装', 'plugins.settings': '插件设置', 'plugins.folderDesc': '插件安装或更新后，重启 FluidBar 生效。',
      'plugins.builtin': '内置', 'plugins.community': '社区', 'plugins.verified': '已验证',
      'plugins.perm.network': '网络', 'plugins.perm.clipboard': '剪贴板', 'plugins.perm.media': '媒体',
      'plugins.perm.notifications': '通知',
      'plugin.weather.name': '天气', 'plugin.weather.desc': '在灵动岛与小组件中显示实时天气、降水提醒与未来 12 小时趋势。',
      'plugin.timer.name': '番茄钟', 'plugin.timer.desc': '25 分钟专注周期、完成提醒与轻量统计。',
      'plugin.github.name': 'GitHub', 'plugin.github.desc': '跟踪 PR、评审与 Actions 状态，在灵动岛中即时提醒。',
      'plugin.sysmon.name': '系统监控', 'plugin.sysmon.desc': 'CPU、内存、磁盘与电池的实时读数与异常提醒。',
      'plugin.lyrics.name': 'Spotify 歌词', 'plugin.lyrics.desc': '从 Spotify 同步当前歌词，在媒体页逐行滚动。',
      'plugin.calendar.name': '日历', 'plugin.calendar.desc': '月视图、日程提醒与会议前 5 分钟灵动岛提示。',
      'plugin.stocks.name': '股票行情', 'plugin.stocks.desc': '自选股实时报价与盘前盘后提醒。',
      'plugin.agent-bridge.name': 'AI Agent Bridge', 'plugin.agent-bridge.desc': '把 Claude Code、Codex 与本地脚本接入灵动岛：实时进度、完成提醒与一键回到终端。',
      'plugin.clipboard-plus.name': '剪贴板增强', 'plugin.clipboard-plus.desc': '为剪贴板历史增加固定、搜索与内容类型识别。',
      'plugin.layout.name': '窗口布局', 'plugin.layout.desc': '保存与恢复窗口布局，支持多显示器与快捷布局片段。',

      'market.title': '插件市场', 'market.desc': '为灵动岛添加新的能力。所有社区插件在安装前都会显示所需权限。',
      'market.featured': '本周精选', 'market.featuredName': 'AI Agent Bridge',
      'market.featuredDesc': '把 Claude Code、Codex 与本地脚本接入灵动岛：实时进度、完成提醒与一键回到终端。',
      'market.featuredMeta': '已验证 · 4.9 · 12.8k 次下载', 'market.search': '搜索插件、作者或能力',
      'market.cat.all': '全部', 'market.cat.productivity': '效率', 'market.cat.media': '媒体',
      'market.cat.developer': '开发', 'market.cat.system': '系统', 'market.cat.ai': 'AI',
      'market.footnote': '安装即表示同意插件权限 · 原型中安装为模拟流程',
      'market.noResults': '没有匹配的插件', 'market.downloads': '{n} 次下载',
      'market.verifiedShort': '已验证',

      'advanced.title': '高级', 'advanced.desc': '配置目录、诊断日志与开发者选项。修改前建议导出配置。',
      'advanced.dataFolder': '数据目录', 'advanced.openFolder': '打开数据目录', 'advanced.logs': '查看诊断日志',
      'advanced.export': '导出配置', 'advanced.import': '导入配置', 'advanced.reset': '恢复默认设置',
      'advanced.confirmReset': '再次点击确认', 'advanced.devMode': '开发者模式',
      'advanced.devModeDesc': '启用插件本地调试、详细日志与未签名插件加载（仅限开发）。',
      'advanced.api': '插件 API 版本', 'advanced.apiDesc': '当前运行时支持 Plugin API v2.4，旧版 v1 插件将以兼容模式运行。',

      'about.title': '关于 FluidBar', 'about.desc': '一个为 Windows 打造的动态岛。由社区驱动，使用 WPF 与 .NET 10 构建。',
      'about.version': '版本 2.0.0 · Build 2026.10.18', 'about.update': '检查更新', 'about.checking': '检查中…',
      'about.upToDate': '已是最新版本', 'about.license': 'MIT License · 源代码开放', 'about.contributors': '贡献者',
      'about.docs': '文档', 'about.github': 'GitHub', 'about.feedback': '反馈',
      'about.updateToast': '已是最新版本 · 2.0.0',

      'toast.installed': '{name} 已安装', 'toast.exported': '配置导出已模拟完成',
      'toast.imported': '配置导入已模拟完成', 'toast.reset': '已恢复默认设置',
      'toast.pluginSettings': '{name} 的插件设置将在正式版中打开',
      'toast.linkDisabled': '原型中已禁用外部链接', 'toast.folderMock': '原型中不访问本地文件系统',
      'toast.updateMock': '原型中更新流程为模拟', 'toast.logs': '诊断日志已模拟导出',

      'dots.left': '更早的页面', 'dots.right': '更晚的页面',
      'dots.jump': '跳转到{page}（第 {n} 页）', 'dots.more': '另有 {n} 个更远的页面',
      'rail.prev': '上一页', 'rail.next': '下一页',
      'console.settings': '设置', 'console.market': '插件市场', 'console.gear': '打开设置',

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
      'console.note': 'Design prototype · settings and marketplace are simulated',
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
      'scene.termLine3': '✓ 13 screenshots · 0 console errors',

      /* ---- settings + marketplace -------------------------------------- */
      'settings.title': 'Settings', 'settings.sub': 'FLUIDBAR 2.0', 'settings.preferences': 'Preferences',
      'settings.prototypeNote': 'Interactive prototype · non-release build', 'settings.open': 'Open settings',
      'settings.market': 'Plugin market',
      'nav.general': 'General', 'nav.appearance': 'Appearance', 'nav.island': 'Dynamic island', 'nav.plugins': 'Plugins',
      'nav.market': 'Plugin market', 'nav.advanced': 'Advanced', 'nav.about': 'About',
      'common.close': 'Close', 'common.install': 'Install', 'common.installing': 'Installing…',
      'common.installed': 'Installed', 'common.builtin': 'Built-in', 'common.community': 'Community',
      'common.verified': 'Verified', 'common.live': 'Live', 'common.reset': 'Reset',

      'general.title': 'General', 'general.desc': 'Launch behavior, updates and global shortcuts. Changes live only in this prototype session.',
      'general.language': 'Interface language', 'general.languageDesc': 'Switch every visible string instantly — no reload.',
      'general.startup': 'Launch FluidBar at startup', 'general.startupDesc': 'Start in the tray and reveal the island after you sign in to Windows.',
      'general.autoUpdate': 'Check for updates automatically', 'general.autoUpdateDesc': 'Download stable updates in the background and ask to restart when idle.',
      'general.tray': 'Show system tray icon', 'general.trayDesc': 'When hidden, you can still open the control center with the global shortcut.',
      'general.shortcuts': 'Global shortcuts', 'general.shortcutOpen': 'Open control center',
      'general.shortcutPlay': 'Play / pause media', 'general.shortcutClip': 'Open clipboard history',
      'general.shortcutHide': 'Temporarily hide the island',

      'appearance.title': 'Appearance', 'appearance.desc': 'Desktop theme and island material. The island stays Obsidian black on light desktops for contrast.',
      'appearance.theme': 'Desktop theme', 'appearance.themeDesc': 'Affects the desktop, console and settings window only.',
      'appearance.material': 'Island material', 'appearance.materialDesc': 'Acrylic is a light frost; Liquid Glass adds a lensing rim, specular edge and a faint chromatic fringe. Both show the wallpaper.',
      'material.obsidian': 'Obsidian', 'material.acrylic': 'Acrylic', 'material.liquid': 'Liquid Glass',
      'appearance.accent': 'Accent color', 'appearance.accentDesc': 'Used for toggles, progress and selection. Media always derives color from artwork.',
      'appearance.radius': 'Large panel radius', 'appearance.radiusDesc': 'The compact pill always keeps its stadium radius.',
      'appearance.opacity': 'Fill opacity', 'appearance.opacityDesc': '97% by default. Acrylic adds more blur below 90%.',
      'appearance.liveHint': 'Changes are applied to the island immediately.',
      'appearance.preview': 'Island preview', 'appearance.meta': '{r} px · {o}%',
      'accent.aqua': 'Aqua', 'accent.mint': 'Mint', 'accent.amber': 'Amber', 'accent.rose': 'Rose', 'accent.blue': 'Cobalt',

      'island.title': 'Dynamic island', 'island.desc': 'Shape, anchor and event-stacking strategy.',
      'island.defaultMode': 'Default mode', 'island.defaultModeDesc': 'Float keeps a pill on top; Flush reads like a Windows notification bar; Dock replaces the taskbar.',
      'island.anchor': 'Anchor position', 'island.anchorDesc': 'Aligns the floating pill along the top; drag to fine-tune at any time.',
      'island.anchorCenter': 'Center', 'island.anchorLeft': 'Left', 'island.anchorRight': 'Right',
      'island.strategy': 'Display strategy', 'island.strategyDesc': 'How simultaneous activities are presented.',
      'island.strategyStack': 'Stacked islands', 'island.strategyLatest': 'Latest only',
      'island.hover': 'Hover preview', 'island.hoverDesc': 'The pill grows slightly on hover and reveals quick controls.',
      'island.autoCollapse': 'Auto-collapse delay', 'island.autoCollapseDesc': 'How long the transient preview strip stays visible after the last gesture.',
      'island.motion': 'Motion', 'island.motionDesc': 'Reduced motion forces critical damping and skips spring travel.',
      'island.motionStrength': 'Motion strength', 'island.motionStrengthDesc': 'Adjusts preview-island spring pacing and travel.',

      'plugins.title': 'Plugins', 'plugins.desc': 'Manage installed plugins, enablement and permissions. Plugins run with your user rights: only install from trusted sources.',
      'plugins.search': 'Search installed plugins', 'plugins.openFolder': 'Open plugin folder',
      'plugins.count': '{n} installed', 'plugins.settings': 'Plugin settings', 'plugins.folderDesc': 'Plugin installs and updates take effect after restarting FluidBar.',
      'plugins.builtin': 'Built-in', 'plugins.community': 'Community', 'plugins.verified': 'Verified',
      'plugins.perm.network': 'Network', 'plugins.perm.clipboard': 'Clipboard', 'plugins.perm.media': 'Media',
      'plugins.perm.notifications': 'Notifications',
      'plugin.weather.name': 'Weather', 'plugin.weather.desc': 'Live conditions, precipitation alerts and a 12-hour outlook in the island and widgets.',
      'plugin.timer.name': 'Focus Timer', 'plugin.timer.desc': '25-minute focus cycles, completion alerts and light statistics.',
      'plugin.github.name': 'GitHub', 'plugin.github.desc': 'Track pull requests, reviews and Actions status with instant island alerts.',
      'plugin.sysmon.name': 'System Monitor', 'plugin.sysmon.desc': 'Live CPU, memory, disk and battery telemetry with anomaly alerts.',
      'plugin.lyrics.name': 'Spotify Lyrics', 'plugin.lyrics.desc': 'Sync the current lyric from Spotify and scroll it line by line in Media.',
      'plugin.calendar.name': 'Calendar', 'plugin.calendar.desc': 'Month view, event reminders and a 5-minute heads-up before meetings.',
      'plugin.stocks.name': 'Stocks', 'plugin.stocks.desc': 'Live quotes for your watchlist, plus pre- and post-market alerts.',
      'plugin.agent-bridge.name': 'AI Agent Bridge', 'plugin.agent-bridge.desc': 'Bring Claude Code, Codex and local scripts into the island: live progress, completion alerts and one-click return to the terminal.',
      'plugin.clipboard-plus.name': 'Clipboard+', 'plugin.clipboard-plus.desc': 'Pinning, search and content-type detection for clipboard history.',
      'plugin.layout.name': 'Window Layout', 'plugin.layout.desc': 'Save and restore window layouts across monitors with quick layout snippets.',

      'market.title': 'Plugin market', 'market.desc': 'Give the island new abilities. Community plugins list required permissions before install.',
      'market.featured': 'Featured this week', 'market.featuredName': 'AI Agent Bridge',
      'market.featuredDesc': 'Bring Claude Code, Codex and local scripts into the island: live progress, completion alerts and one-click return to the terminal.',
      'market.featuredMeta': 'Verified · 4.9 · 12.8k downloads', 'market.search': 'Search plugins, authors or capabilities',
      'market.cat.all': 'All', 'market.cat.productivity': 'Productivity', 'market.cat.media': 'Media',
      'market.cat.developer': 'Developer', 'market.cat.system': 'System', 'market.cat.ai': 'AI',
      'market.footnote': 'Installing accepts plugin permissions · installs are simulated in this prototype',
      'market.noResults': 'No matching plugins', 'market.downloads': '{n} downloads',
      'market.verifiedShort': 'Verified',

      'advanced.title': 'Advanced', 'advanced.desc': 'Config location, diagnostic logs and developer options. Export your config before making changes.',
      'advanced.dataFolder': 'Data folder', 'advanced.openFolder': 'Open data folder', 'advanced.logs': 'View diagnostic logs',
      'advanced.export': 'Export config', 'advanced.import': 'Import config', 'advanced.reset': 'Reset all settings',
      'advanced.confirmReset': 'Click again to confirm', 'advanced.devMode': 'Developer mode',
      'advanced.devModeDesc': 'Enables local plugin debugging, verbose logs and unsigned plugin loading (development only).',
      'advanced.api': 'Plugin API version', 'advanced.apiDesc': 'The runtime supports Plugin API v2.4. v1 plugins run in compatibility mode.',

      'about.title': 'About FluidBar', 'about.desc': 'A dynamic island built for Windows. Community-driven, built with WPF and .NET 10.',
      'about.version': 'Version 2.0.0 · Build 2026.10.18', 'about.update': 'Check for updates', 'about.checking': 'Checking…',
      'about.upToDate': 'Up to date', 'about.license': 'MIT License · Open source', 'about.contributors': 'Contributors',
      'about.docs': 'Docs', 'about.github': 'GitHub', 'about.feedback': 'Feedback',
      'about.updateToast': 'You’re up to date · 2.0.0',

      'toast.installed': '{name} installed', 'toast.exported': 'Config export simulated',
      'toast.imported': 'Config import simulated', 'toast.reset': 'Settings restored to defaults',
      'toast.pluginSettings': '{name} plugin settings will open in the shipped app',
      'toast.linkDisabled': 'External links are disabled in this prototype', 'toast.folderMock': 'The prototype does not access the local file system',
      'toast.updateMock': 'Update flow is simulated in this prototype', 'toast.logs': 'Diagnostic log export simulated',

      'dots.left': 'Earlier pages', 'dots.right': 'Later pages',
      'dots.jump': 'Jump to {page} (page {n})', 'dots.more': '{n} more page(s) beyond this side',
      'rail.prev': 'Previous page', 'rail.next': 'Next page',
      'console.settings': 'Settings', 'console.market': 'Plugin market', 'console.gear': 'Open settings',

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

    /* Settings + marketplace */
    settingsOpen: false, settingsTab: 'general', settingsScroll: 0,
    material: 'obsidian', accentKey: 'aqua', radius: 30, opacity: 97,
    anchor: 'center', strategy: 'stack', hoverPreview: true,
    autoCollapse: 0.9, motionStrength: 1,
    startup: true, autoUpdate: true, trayIcon: true, devMode: false,
    installedPlugins: new Set(ALL_PLUGINS.filter(function (p) { return p.installedByDefault; }).map(function (p) { return p.id; })),
    pluginEnabled: {}, installing: {}, marketCat: 'all', marketQuery: '', pluginQuery: '',
    confirmReset: false, updateState: 'idle', panelRequest: null
  };
  ALL_PLUGINS.forEach(function (p) { S.pluginEnabled[p.id] = true; });


  var P = {
    offset: 0, target: 0, v: 0, raf: 0, animating: false, snapLast: 0,
    dragging: false, moved: false, startX: 0, startY: 0, startOffset: 0, startPage: 0,
    lastX: 0, lastT: 0, vx: 0, suppressClick: 0,
    wheelAccum: 0, wheelLastCommit: 0,
    overDir: 0, overAmount: 0, overSince: 0,
    uiIndex: -1, railIndex: -1, railLock: null
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
     07. TRANSIENT PREVIEW ISLANDS + FAR-PAGE DOTS

     Three preview islands (prev / current / next) live below the panel and are
     only visible for a moment after activity. Every morph here is a rAF spring
     (T 0.32 s, ζ 0.85) writing transforms/opacity only: the capsule width is a
     scaleX on the glass layer (the icon/label layer is never scaled), and the
     neighbours + far-page dot clusters are translated. No CSS transition or
     keyframe touches these properties, and the layout never reflows mid-morph.
     ========================================================================== */
  function RailSpring(value) {
    this.v = value;
    this.target = value;
    this.vel = 0;
  }
  RailSpring.prototype.to = function (target) { this.target = target; };
  RailSpring.prototype.snap = function (value) {
    this.v = value;
    this.target = value;
    this.vel = 0;
  };
  RailSpring.prototype.step = function (dt, T, Z) {
    var steps = Math.max(1, Math.ceil(dt / 0.004));
    var h = dt / steps;
    var w = (2 * Math.PI) / T;
    for (var i = 0; i < steps; i++) {
      var a = -w * w * (this.v - this.target) - 2 * Z * w * this.vel;
      this.vel += a * h;
      this.v += this.vel * h;
    }
    if (Math.abs(this.v - this.target) < 0.02 && Math.abs(this.vel) < 0.05) {
      this.v = this.target;
      this.vel = 0;
      return false;
    }
    return true;
  };

  var RAIL = {
    built: false, index: -1, visible: false, raf: 0, last: 0, el: null,
    curW: new RailSpring(RAIL_CUR_BASE),
    prevX: new RailSpring(0), nextX: new RailSpring(0),
    prevA: new RailSpring(0), nextA: new RailSpring(0),
    leftX: new RailSpring(0), rightX: new RailSpring(0),
    leftA: new RailSpring(0), rightA: new RailSpring(0),
    leftN: -1, rightN: -1, leftTotal: -1, rightTotal: -1, dotsLang: '',
    leftW: 0, rightW: 0,
    labelStart: 0, labelAnim: false,
    pulse: { prev: 0, current: 0, next: 0 },
    labelCache: {}
  };

  function buildRail() {
    E.miniRail.innerHTML =
      '<span class="mini prev" data-slot="prev">' +
        '<button class="mini-bg" type="button" data-rail="prev"></button>' +
        '<span class="mini-content"><span class="mini-ico"></span></span></span>' +
      '<span class="mini current" data-slot="current" aria-hidden="true">' +
        '<span class="mini-bg"></span><span class="mini-content">' +
          '<span class="mini-ico"></span>' +
          '<span class="mini-label"><span class="mini-label-new"></span>' +
          '<span class="mini-label-old"></span></span>' +
        '</span></span>' +
      '<span class="mini next" data-slot="next">' +
        '<button class="mini-bg" type="button" data-rail="next"></button>' +
        '<span class="mini-content"><span class="mini-ico"></span></span></span>' +
      '<span class="dot-cluster left" data-dots="left"></span>' +
      '<span class="dot-cluster right" data-dots="right"></span>' +
      '<span class="mini-measure" aria-hidden="true"></span>';

    var q = function (sel) { return E.miniRail.querySelector(sel); };
    RAIL.el = {
      prev: q('.mini.prev'), prevBg: q('.mini.prev .mini-bg'), prevIco: q('.mini.prev .mini-ico'),
      prevContent: q('.mini.prev .mini-content'), prevBtn: q('.mini.prev .mini-bg'),
      nextBtn: q('.mini.next .mini-bg'),
      current: q('.mini.current'), curBg: q('.mini.current .mini-bg'), curIco: q('.mini.current .mini-ico'),
      curContent: q('.mini.current .mini-content'),
      labelNew: q('.mini-label-new'), labelOld: q('.mini-label-old'),
      next: q('.mini.next'), nextBg: q('.mini.next .mini-bg'), nextIco: q('.mini.next .mini-ico'),
      nextContent: q('.mini.next .mini-content'),
      left: q('.dot-cluster.left'), right: q('.dot-cluster.right'),
      measure: q('.mini-measure')
    };
    RAIL.built = true;
    applyRailFrame(performance.now());
  }

  function measureRailLabel(text) {
    var key = S.lang + '|' + text;
    if (RAIL.labelCache[key] !== undefined) return RAIL.labelCache[key];
    var el = RAIL.el.measure;
    el.textContent = text;
    var w = el.getBoundingClientRect().width || text.length * 6;
    RAIL.labelCache[key] = w;
    return w;
  }

  function railMetrics(index) {
    var page = PAGES[index];
    var label = t('short.' + page.id);
    var labelW = Math.ceil(measureRailLabel(label));
    /* Base 64 px matches the approved capsule; longer labels (zh 3-char,
       English words) grow the capsule so the label never clips. */
    var w = clamp(Math.round(64 + Math.max(0, labelW - 20) * 1.1), RAIL_CUR_BASE, 104);
    return {
      page: page, label: label, w: w,
      hasPrev: index > 0, hasNext: index < MAX_PAGE
    };
  }

  function setMiniIcon(slot, page, immediate) {
    var icoEl = slot === 'prev' ? RAIL.el.prevIco
      : slot === 'next' ? RAIL.el.nextIco : RAIL.el.curIco;
    var key = page ? page.id : '';
    if (icoEl.dataset.page === key) return;
    icoEl.dataset.page = key;
    icoEl.innerHTML = page ? icon(page.icon, 12) : '';
    if (!immediate) RAIL.pulse[slot] = performance.now();
  }

  function setRailLabel(label, immediate) {
    var e = RAIL.el;
    if (immediate || S.motion === 'reduced') {
      e.labelNew.textContent = label;
      e.labelOld.textContent = '';
      e.labelOld.style.opacity = '0';
      e.labelNew.style.opacity = '1';
      e.labelOld.style.transform = 'translate3d(0, 0, 0)';
      e.labelNew.style.transform = 'translate3d(0, 0, 0)';
      RAIL.labelAnim = false;
      return;
    }
    if (e.labelNew.textContent === label) return;
    e.labelOld.textContent = e.labelNew.textContent;
    e.labelOld.style.opacity = '1';
    e.labelOld.style.transform = 'translate3d(0, 0, 0)';
    e.labelNew.textContent = label;
    e.labelNew.style.opacity = '0';
    e.labelNew.style.transform = 'translate3d(0, 3px, 0)';
    RAIL.labelStart = performance.now();
    RAIL.labelAnim = true;
  }

  function farCounts(index) {
    return {
      leftTotal: Math.max(0, index - 1),
      rightTotal: Math.max(0, MAX_PAGE - index - 1)
    };
  }

  function dotClusterWidth(n, total) {
    if (!n) return 0;
    return n * 3 + (n - 1) * RAIL_DOT_GAP + (total > n ? 4 : 0);
  }

  function dotClusterHTML(total, targetIndex) {
    var n = Math.min(RAIL_DOT_MAX, total);
    if (!n) return '';
    var target = PAGES[targetIndex];
    var label = t('dots.jump', { page: t('tab.' + target.id), n: targetIndex + 1 });
    var extra = total - n;
    var out = '';
    for (var i = 0; i < n; i++) {
      var wide = extra > 0 && i === n - 1;
      var dotLabel = wide ? label + ' · ' + t('dots.more', { n: extra }) : label;
      out += '<button class="far-dot' + (wide ? ' wide' : '') + '" type="button" data-far="' +
        targetIndex + '" aria-label="' + esc(dotLabel) + '" title="' + esc(dotLabel) + '"></button>';
    }
    return out;
  }

  function updateDots(index) {
    if (!RAIL.built) return;
    var totals = farCounts(index);
    var leftN = Math.min(RAIL_DOT_MAX, totals.leftTotal);
    var rightN = Math.min(RAIL_DOT_MAX, totals.rightTotal);
    var rebuild = leftN !== RAIL.leftN || rightN !== RAIL.rightN ||
      totals.leftTotal !== RAIL.leftTotal || totals.rightTotal !== RAIL.rightTotal ||
      RAIL.dotsLang !== S.lang;
    if (rebuild) {
      RAIL.leftN = leftN;
      RAIL.rightN = rightN;
      RAIL.leftTotal = totals.leftTotal;
      RAIL.rightTotal = totals.rightTotal;
      RAIL.dotsLang = S.lang;
      RAIL.leftW = dotClusterWidth(leftN, totals.leftTotal);
      RAIL.rightW = dotClusterWidth(rightN, totals.rightTotal);
      RAIL.el.left.innerHTML = dotClusterHTML(totals.leftTotal, 0);
      RAIL.el.right.innerHTML = dotClusterHTML(totals.rightTotal, MAX_PAGE);
      RAIL.el.left.classList.toggle('is-live', leftN > 0);
      RAIL.el.right.classList.toggle('is-live', rightN > 0);
      RAIL.el.left.setAttribute('role', 'group');
      RAIL.el.right.setAttribute('role', 'group');
      RAIL.el.left.setAttribute('aria-label', t('dots.left') + (totals.leftTotal ? ' · ' + totals.leftTotal : ''));
      RAIL.el.right.setAttribute('aria-label', t('dots.right') + (totals.rightTotal ? ' · ' + totals.rightTotal : ''));
    }
    RAIL.leftA.to(leftN > 0 ? 1 : 0);
    RAIL.rightA.to(rightN > 0 ? 1 : 0);
    var w = RAIL.curW.target;
    var prevX = -(w / 2 + RAIL_GAP + RAIL_SLOT / 2);
    var nextX = w / 2 + RAIL_GAP + RAIL_SLOT / 2;
    RAIL.leftX.to(prevX - RAIL_SLOT / 2 - RAIL_DOT_GAP_EDGE - RAIL.leftW / 2);
    RAIL.rightX.to(nextX + RAIL_SLOT / 2 + RAIL_DOT_GAP_EDGE + RAIL.rightW / 2);
  }

  function applyRailFrame(now) {
    if (!RAIL.built) return false;
    var e = RAIL.el;
    e.prev.style.transform = 'translate3d(' + RAIL.prevX.v.toFixed(2) + 'px, 0, 0)';
    e.next.style.transform = 'translate3d(' + RAIL.nextX.v.toFixed(2) + 'px, 0, 0)';
    e.curBg.style.transform = 'scaleX(' + (RAIL.curW.v / RAIL_CUR_BASE).toFixed(4) + ')';
    e.prev.style.opacity = RAIL.prevA.v.toFixed(3);
    e.next.style.opacity = RAIL.nextA.v.toFixed(3);
    e.left.style.transform = 'translate3d(' + RAIL.leftX.v.toFixed(2) + 'px, 0, 0) translate(-50%, -50%)';
    e.right.style.transform = 'translate3d(' + RAIL.rightX.v.toFixed(2) + 'px, 0, 0) translate(-50%, -50%)';
    e.left.style.opacity = RAIL.leftA.v.toFixed(3);
    e.right.style.opacity = RAIL.rightA.v.toFixed(3);

    var pulsing = false;
    var slots = [['prev', e.prevContent], ['current', e.curContent], ['next', e.nextContent]];
    for (var i = 0; i < slots.length; i++) {
      var start = RAIL.pulse[slots[i][0]];
      if (!start) continue;
      var p = clamp((now - start) / 150, 0, 1);
      var eased = p * p * (3 - 2 * p);
      slots[i][1].style.opacity = (0.35 + 0.65 * eased).toFixed(3);
      if (p >= 1) RAIL.pulse[slots[i][0]] = 0;
      else pulsing = true;
    }
    return pulsing;
  }

  function kickRail() {
    if (RAIL.raf) return;
    RAIL.last = performance.now();
    RAIL.raf = requestAnimationFrame(railTick);
  }

  function railTick(now) {
    RAIL.raf = 0;
    var dt = clamp((now - RAIL.last) / 1000, 0.001, 0.05);
    RAIL.last = now;
    var T = RAIL_SPRING.T / clamp(S.motionStrength, 0.5, 1.5);
    var springs = [RAIL.curW, RAIL.prevX, RAIL.nextX, RAIL.prevA, RAIL.nextA,
      RAIL.leftX, RAIL.rightX, RAIL.leftA, RAIL.rightA];
    var moving = false;
    for (var i = 0; i < springs.length; i++) {
      if (springs[i].step(dt, T, RAIL_SPRING.Z)) moving = true;
    }

    if (RAIL.labelAnim) {
      var p = clamp((now - RAIL.labelStart) / 150, 0, 1);
      var eased = p * p * (3 - 2 * p);
      RAIL.el.labelOld.style.opacity = (1 - eased).toFixed(3);
      RAIL.el.labelNew.style.opacity = eased.toFixed(3);
      RAIL.el.labelOld.style.transform = 'translate3d(0, ' + (-3 * eased).toFixed(2) + 'px, 0)';
      RAIL.el.labelNew.style.transform = 'translate3d(0, ' + (3 * (1 - eased)).toFixed(2) + 'px, 0)';
      if (p >= 1) {
        RAIL.el.labelOld.textContent = '';
        RAIL.el.labelOld.style.opacity = '0';
        RAIL.el.labelOld.style.transform = 'translate3d(0, 0, 0)';
        RAIL.el.labelNew.style.transform = 'translate3d(0, 0, 0)';
        RAIL.labelAnim = false;
      } else {
        moving = true;
      }
    }

    if (applyRailFrame(now)) moving = true;
    if (moving) RAIL.raf = requestAnimationFrame(railTick);
  }

  function snapRailSprings() {
    var springs = [RAIL.curW, RAIL.prevX, RAIL.nextX, RAIL.prevA, RAIL.nextA,
      RAIL.leftX, RAIL.rightX, RAIL.leftA, RAIL.rightA];
    for (var i = 0; i < springs.length; i++) springs[i].snap(springs[i].target);
    RAIL.labelAnim = false;
    RAIL.el.labelOld.textContent = '';
    RAIL.el.labelOld.style.opacity = '0';
    RAIL.el.labelOld.style.transform = 'translate3d(0, 0, 0)';
    RAIL.el.labelNew.style.opacity = '1';
    RAIL.el.labelNew.style.transform = 'translate3d(0, 0, 0)';
  }

  function railSetPage(index, opts) {
    opts = opts || {};
    if (!RAIL.built) return;
    index = clamp(index, 0, MAX_PAGE);
    var m = railMetrics(index);
    var changed = RAIL.index !== index;
    RAIL.index = index;
    P.railIndex = index;

    var prevX = -(m.w / 2 + RAIL_GAP + RAIL_SLOT / 2);
    var nextX = m.w / 2 + RAIL_GAP + RAIL_SLOT / 2;
    RAIL.curW.to(m.w);
    RAIL.prevX.to(prevX);
    RAIL.nextX.to(nextX);
    RAIL.prevA.to(m.hasPrev ? 1 : 0);
    RAIL.nextA.to(m.hasNext ? 1 : 0);

    /* The two neighbour pills are real buttons: a click turns one page. At the
       first/last page the slot is empty, so the button is disabled (no focus,
       no pointer target) while the glass springs to opacity 0. */
    RAIL.el.prevBtn.disabled = !m.hasPrev;
    RAIL.el.nextBtn.disabled = !m.hasNext;
    RAIL.el.prevBtn.setAttribute('aria-label', m.hasPrev
      ? t('rail.prev') + ' · ' + t('short.' + PAGES[index - 1].id) : t('rail.prev'));
    RAIL.el.nextBtn.setAttribute('aria-label', m.hasNext
      ? t('rail.next') + ' · ' + t('short.' + PAGES[index + 1].id) : t('rail.next'));

    setMiniIcon('prev', m.hasPrev ? PAGES[index - 1] : null, opts.immediate);
    setMiniIcon('current', PAGES[index], opts.immediate);
    setMiniIcon('next', m.hasNext ? PAGES[index + 1] : null, opts.immediate);
    if (changed) setRailLabel(m.label, opts.immediate);
    updateDots(index);

    if (opts.immediate || S.motion === 'reduced') snapRailSprings();
    kickRail();
    applyRailFrame(performance.now());
  }

  function railDelay() {
    return Math.round(clamp(S.autoCollapse || RAIL_DELAY / 1000, 0.4, 4.8) * 1000);
  }

  function showRail(index) {
    if (!S.expanded) return;
    var wasVisible = E.miniRail.classList.contains('is-visible');
    var target = index === undefined ? clamp(Math.round(P.offset), 0, MAX_PAGE) : index;
    railSetPage(target, { immediate: !wasVisible });
    if (S.forceArch) {
      E.miniRail.classList.add('is-visible');
      RAIL.visible = true;
      return;
    }
    if (!wasVisible) {
      E.miniRail.classList.remove('is-visible');
      void E.miniRail.offsetWidth;
      E.miniRail.classList.add('is-visible');
    }
    RAIL.visible = true;
    clearTimeout(railTimer);
    railTimer = setTimeout(function () { hideRail(); }, railDelay());
  }

  function hideRail(force) {
    if (S.forceArch && !force) return;
    clearTimeout(railTimer);
    railTimer = 0;
    E.miniRail.classList.remove('is-visible');
    RAIL.visible = false;
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
      if (P.railLock !== null) {
        /* A far-page dot jump owns the strip until the spring settles, so the
           preview islands do not flicker through every intermediate page. */
        if (Math.abs(P.offset - P.railLock) < 0.002) {
          P.railLock = null;
          railSetPage(idx);
        }
      } else {
        railSetPage(idx);
      }
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
    P.railLock = null;
    springTo(next, 0);
    showRail(next);
    P.wheelAccum = 0;
    P.wheelLastCommit = performance.now();
  }

  /* Far-page dots jump straight to the farthest page in that direction. The
     strip is locked to the destination while the pager springs, then resumes
     normal per-page updates. */
  function jumpToPage(target) {
    target = clamp(target, 0, MAX_PAGE);
    if (S.motion === 'reduced') {
      setPageInstant(target);
      showRail(target);
      return;
    }
    P.railLock = target;
    springTo(target, 0);
    showRail(target);
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
    document.body.dataset.material = S.material;
    document.body.dataset.anchor = S.anchor;
    document.body.dataset.hover = S.hoverPreview ? 'on' : 'off';
    document.body.dataset.state = S.expanded ? 'expanded' : S.view;
    document.documentElement.lang = S.lang === 'zh' ? 'zh-CN' : 'en';
    E.island.setAttribute('aria-label', t(S.expanded ? 'island.collapse' : 'island.expand'));
  }

  /* Live appearance: accent / fill / panel radius are CSS variables, so every
     surface (island, console, settings, preview) updates in one paint. */
  function applyAppearance() {
    var root = document.documentElement;
    root.style.setProperty('--accent', ACCENTS[S.accentKey] || ACCENTS.aqua);
    root.style.setProperty('--island-fill', (S.opacity / 100).toFixed(3));
    root.style.setProperty('--panel-radius', S.radius + 'px');
    syncChrome();
    updateAppearancePreview();
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
    S.expanded = true;
    renderPages();
    setPageInstant(pageIndex(S.tab));
    railSetPage(pageIndex(S.tab), { immediate: true });
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
    if (S.settingsOpen) renderSettings();
  }

  function setTheme(theme) {
    S.theme = theme;
    applyAppearance();
    renderScene();
    renderTaskbar();
    renderPages();
    updateConsole();
    if (S.settingsOpen) renderSettings();
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
    if (RAIL.built) {
      RAIL.labelCache = {};
      railSetPage(P.railIndex >= 0 ? P.railIndex : Math.round(P.offset), { immediate: true });
    }
    if (S.forceArch) showRail(P.railIndex >= 0 ? P.railIndex : Math.round(P.offset));
    if (S.settingsOpen) renderSettings();
  }

  function setMotion(motion) {
    S.motion = motion;
    syncChrome();
    if (motion === 'reduced') {
      cancelSnap();
      P.offset = P.target;
      P.v = 0;
      applyPager();
      if (RAIL.built) railSetPage(P.uiIndex >= 0 ? P.uiIndex : 0, { immediate: true });
    }
    renderConsole();
    updateConsole();
    if (S.settingsOpen) renderSettings();
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
        '<button class="console-gear" type="button" data-open-panel="settings" aria-label="' + esc(t('console.gear')) + '" title="' + esc(t('console.gear')) + '">' + icon('settings', 13) + '</button>' +
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
        '<div class="console-actions">' +
          '<button class="console-action" type="button" data-open-panel="settings">' + icon('settings', 9) + '<span>' + esc(t('console.settings')) + '</span></button>' +
          '<button class="console-action" type="button" data-open-panel="market">' + icon('storefront', 9) + '<span>' + esc(t('console.market')) + '</span></button>' +
        '</div>' +
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
    var material = q.get('material');
    var accent = q.get('accent');
    var radius = Number(q.get('radius'));
    var opacity = Number(q.get('opacity'));
    var anchor = q.get('anchor');
    var panel = q.get('panel');
    var panelTab = q.get('panelTab');

    if (theme === 'light' || theme === 'dark') S.theme = theme;
    if (lang === 'en' || lang === 'zh') S.lang = lang;
    if (mode === 'float' || mode === 'flush' || mode === 'dock') S.mode = mode;
    if (motion === 'reduced') S.motion = 'reduced';
    else if (motion === 'normal') S.motion = 'normal';
    else if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) S.motion = 'reduced';

    if (material === 'acrylic' || material === 'obsidian' || material === 'liquid' || material === 'liquid-glass') {
      S.material = material === 'liquid-glass' ? 'liquid' : material;
    }
    if (accent && ACCENTS[accent]) S.accentKey = accent;
    if (radius >= 16 && radius <= 36) S.radius = radius;
    if (opacity >= 85 && opacity <= 100) S.opacity = opacity;
    if (anchor === 'center' || anchor === 'left' || anchor === 'right') S.anchor = anchor;

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

    if (panel === 'settings' || panel === 'market') {
      S.panelRequest = {
        tab: SETTINGS_TABS.indexOf(panelTab) >= 0 ? panelTab : (panel === 'market' ? 'market' : 'general')
      };
    }
  }

  /* ==========================================================================
     09b. SETTINGS + PLUGIN MARKETPLACE

     A 900×620 modal with a seven-section left nav: general / appearance /
     island / plugins / marketplace / advanced / about. Appearance and island
     controls write the same CSS variables the island already uses, so every
     change is live. All data is local demo data; installs are simulated.
     ========================================================================== */
  var toastTimer = 0;

  function showToast(message, opts) {
    opts = opts || {};
    if (!E.toast) return;
    var ico = opts.icon || (opts.kind === 'error' ? 'alert' : 'check');
    E.toast.className = 'toast ' + (opts.kind || '');
    E.toast.innerHTML = '<span class="toast-ico">' + icon(ico, 12) + '</span><span>' +
      esc(message) + '</span>' + (opts.sub ? '<small>' + esc(opts.sub) + '</small>' : '');
    requestAnimationFrame(function () { E.toast.classList.add('show'); });
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { E.toast.classList.remove('show'); }, opts.duration || 2600);
  }

  function pluginById(id) {
    for (var i = 0; i < ALL_PLUGINS.length; i++) if (ALL_PLUGINS[i].id === id) return ALL_PLUGINS[i];
    return null;
  }

  function pluginSearchString(p) {
    return (t(p.name) + ' ' + t(p.desc) + ' ' + p.author + ' ' + p.category).toLowerCase();
  }

  function switchControl(key, on, labelKey) {
    return '<button class="switch' + (on ? ' on' : '') + '" type="button" role="switch" aria-checked="' + on +
      '" data-toggle="' + key + '" aria-label="' + esc(t(labelKey)) + '"></button>';
  }

  function settingRow(titleKey, descKey, controlHTML) {
    return '<div class="setting-row">' +
      '<div class="setting-text"><div class="setting-title">' + esc(t(titleKey)) + '</div>' +
      (descKey ? '<div class="setting-desc">' + esc(t(descKey)) + '</div>' : '') + '</div>' +
      '<div class="setting-control">' + controlHTML + '</div>' +
      '</div>';
  }

  function segControl(setting, options, current) {
    return '<div class="segmented">' + options.map(function (o) {
      return '<button class="seg' + (current === o[0] ? ' active' : '') + '" type="button" data-set="' +
        setting + '" data-value="' + o[0] + '">' + esc(o[1]) + '</button>';
    }).join('') + '</div>';
  }

  function rangeMeta(key) {
    return {
      radius: { min: 16, max: 36, step: 1 },
      opacity: { min: 85, max: 100, step: 1 },
      autoCollapse: { min: 0.4, max: 3.6, step: 0.1 },
      motionStrength: { min: 0.5, max: 1.5, step: 0.05 }
    }[key];
  }

  function rangeVal(value, min, max) {
    return clamp(((value - min) / (max - min)) * 100, 0, 100).toFixed(1) + '%';
  }

  function fmtSettingValue(key, v) {
    if (key === 'radius') return Math.round(v) + ' px';
    if (key === 'opacity') return Math.round(v) + '%';
    if (key === 'autoCollapse') return v.toFixed(1) + ' s';
    if (key === 'motionStrength') return '×' + v.toFixed(2);
    return String(v);
  }

  function settingRange(key, value) {
    var m = rangeMeta(key);
    return '<div class="setting-range">' +
      '<input class="ui-range" type="range" min="' + m.min + '" max="' + m.max + '" step="' + m.step +
        '" value="' + value + '" data-range="' + key + '" aria-label="' + esc(key) +
        '" style="--val:' + rangeVal(value, m.min, m.max) + '">' +
      '<span class="value-tag" data-range-value="' + key + '">' + esc(fmtSettingValue(key, value)) + '</span>' +
      '</div>';
  }

  function paneHeader(key, descKey) {
    return '<div class="pane-head-lg">' + esc(t(key)) + '</div><div class="pane-desc">' + esc(t(descKey)) + '</div>';
  }

  function shortcutRow(labelKey, keys) {
    return '<div class="shortcut-row"><strong>' + esc(t(labelKey)) + '</strong><span class="keycaps">' +
      keys.map(function (k) { return '<span class="keycap">' + esc(k) + '</span>'; }).join('') + '</span></div>';
  }

  function renderGeneral() {
    return paneHeader('general.title', 'general.desc') +
      '<div class="setting-group">' +
        settingRow('general.language', 'general.languageDesc',
          segControl('lang', [['zh', t('lang.zh')], ['en', t('lang.en')]], S.lang)) +
        settingRow('general.startup', 'general.startupDesc', switchControl('startup', S.startup, 'general.startup')) +
        settingRow('general.autoUpdate', 'general.autoUpdateDesc', switchControl('autoUpdate', S.autoUpdate, 'general.autoUpdate')) +
        settingRow('general.tray', 'general.trayDesc', switchControl('trayIcon', S.trayIcon, 'general.tray')) +
      '</div>' +
      '<div class="section-caption">' + esc(t('general.shortcuts')) + '</div>' +
      '<div class="setting-group">' +
        shortcutRow('general.shortcutOpen', ['Ctrl', 'Alt', 'Space']) +
        shortcutRow('general.shortcutPlay', ['Ctrl', 'Alt', 'M']) +
        shortcutRow('general.shortcutClip', ['Ctrl', 'Alt', 'C']) +
        shortcutRow('general.shortcutHide', ['Alt', 'H']) +
      '</div>' +
      '<div class="settings-footer-note">' + esc(t('settings.prototypeNote')) + '</div>';
  }

  function renderAppearance() {
    var swatches = Object.keys(ACCENTS).map(function (k) {
      return '<button class="accent-swatch' + (S.accentKey === k ? ' active' : '') + '" type="button" data-accent="' +
        k + '" style="--sw:' + ACCENTS[k] + '" title="' + esc(t('accent.' + k)) + '" aria-label="' +
        esc(t('accent.' + k)) + '"></button>';
    }).join('');
    return paneHeader('appearance.title', 'appearance.desc') +
      '<div class="setting-group">' +
        settingRow('appearance.theme', 'appearance.themeDesc',
          segControl('theme', [['dark', t('theme.dark')], ['light', t('theme.light')]], S.theme)) +
        settingRow('appearance.material', 'appearance.materialDesc',
          segControl('material', [['obsidian', t('material.obsidian')], ['acrylic', t('material.acrylic')], ['liquid', t('material.liquid')]], S.material)) +
        settingRow('appearance.accent', 'appearance.accentDesc', '<div class="accent-row">' + swatches + '</div>') +
        settingRow('appearance.radius', 'appearance.radiusDesc', settingRange('radius', S.radius)) +
        settingRow('appearance.opacity', 'appearance.opacityDesc', settingRange('opacity', S.opacity)) +
      '</div>' +
      '<div class="appearance-preview" aria-hidden="true">' +
        '<div class="ap-head"><span>' + esc(t('appearance.preview')) + '</span><span class="ap-meta" data-ap-meta></span></div>' +
        '<div class="ap-canvas">' +
          '<div class="ap-shell" id="apShell" style="--ap-fill:' + (S.opacity / 100).toFixed(2) + ';border-radius:' + S.radius + 'px">' +
            '<span class="art ap-art" data-art="0"></span>' +
            '<div class="ap-copy"><b>14:32</b><span>Afterglow · Nova Lane</span></div>' +
            '<div class="ap-wave"><i></i><i></i><i></i><i></i></div>' +
            '<span class="ap-play">' + icon('pause', 11) + '</span>' +
            '<span class="ap-progress"><i></i></span>' +
          '</div>' +
        '</div>' +
      '</div>' +
      '<div class="settings-footer-note">' + esc(t('appearance.liveHint')) + '</div>';
  }

  function updateAppearancePreview() {
    var shell = $('apShell');
    if (!shell) return;
    shell.style.setProperty('--ap-fill', (S.opacity / 100).toFixed(2));
    shell.style.borderRadius = S.radius + 'px';
    var meta = document.querySelector('[data-ap-meta]');
    if (meta) meta.textContent = t('appearance.meta', { r: Math.round(S.radius), o: Math.round(S.opacity) });
  }

  function renderIslandSettings() {
    return paneHeader('island.title', 'island.desc') +
      '<div class="setting-group">' +
        settingRow('island.defaultMode', 'island.defaultModeDesc',
          segControl('mode', [['float', t('mode.float')], ['flush', t('mode.flush')], ['dock', t('mode.dock')]], S.mode)) +
        settingRow('island.anchor', 'island.anchorDesc',
          segControl('anchor', [['center', t('island.anchorCenter')], ['left', t('island.anchorLeft')], ['right', t('island.anchorRight')]], S.anchor)) +
        settingRow('island.strategy', 'island.strategyDesc',
          segControl('strategy', [['stack', t('island.strategyStack')], ['latest', t('island.strategyLatest')]], S.strategy)) +
        settingRow('island.hover', 'island.hoverDesc', switchControl('hoverPreview', S.hoverPreview, 'island.hover')) +
        settingRow('island.motion', 'island.motionDesc',
          segControl('motion', [['normal', t('console.normal')], ['reduced', t('console.reduced')]], S.motion)) +
        settingRow('island.autoCollapse', 'island.autoCollapseDesc', settingRange('autoCollapse', S.autoCollapse)) +
        settingRow('island.motionStrength', 'island.motionStrengthDesc', settingRange('motionStrength', S.motionStrength)) +
      '</div>' +
      '<div class="settings-footer-note">' + esc(t('settings.prototypeNote')) + '</div>';
  }

  function renderPlugins() {
    var installed = ALL_PLUGINS.filter(function (p) { return S.installedPlugins.has(p.id); });
    var rows = installed.map(function (p) {
      var perms = p.permissions.length ? '<div class="perms">' + p.permissions.map(function (perm) {
        return '<span class="perm">' + icon(PERM_ICONS[perm] || 'shield', 7) + esc(t('plugins.perm.' + perm)) + '</span>';
      }).join('') + '</div>' : '';
      return '<div class="plugin-row" data-plugin-row data-search="' + esc(pluginSearchString(p)) + '">' +
        '<span class="plugin-icon" style="--p:' + p.color + '">' + icon(p.icon, 17) + '</span>' +
        '<div class="plugin-main">' +
          '<div class="plugin-name-row"><span class="plugin-name">' + esc(t(p.name)) + '</span>' +
            '<span class="plugin-version">v' + esc(p.version) + '</span>' +
            '<span class="badge ' + (p.builtin ? 'builtin' : 'community') + '">' + esc(t(p.builtin ? 'common.builtin' : 'common.community')) + '</span>' +
            (p.verified ? '<span class="badge verified">' + icon('check', 7) + esc(t('common.verified')) + '</span>' : '') +
          '</div>' +
          '<div class="plugin-meta">' + esc(p.author) + '</div>' +
          perms +
        '</div>' +
        '<div class="plugin-actions">' +
          '<button class="icon-btn" type="button" data-plugin-settings="' + p.id + '" aria-label="' + esc(t('plugins.settings')) + '">' + icon('settings', 12) + '</button>' +
          '<button class="switch' + (S.pluginEnabled[p.id] ? ' on' : '') + '" type="button" role="switch" aria-checked="' + !!S.pluginEnabled[p.id] +
            '" data-toggle-plugin="' + p.id + '" aria-label="' + esc(t(p.name)) + '"></button>' +
        '</div>' +
      '</div>';
    }).join('');
    return paneHeader('plugins.title', 'plugins.desc') +
      '<div class="search-field">' + icon('search', 13) +
        '<input type="text" data-search-input="pluginQuery" placeholder="' + esc(t('plugins.search')) +
          '" aria-label="' + esc(t('plugins.search')) + '" value="' + esc(S.pluginQuery) + '">' +
      '</div>' +
      '<div class="plugin-list">' + rows + '</div>' +
      '<div class="plugin-toolbar"><span class="setting-desc">' + esc(t('plugins.folderDesc')) + '</span>' +
        '<button class="open-folder" type="button" data-action="open-plugin-folder">' + icon('folder', 11) + esc(t('plugins.openFolder')) + '</button>' +
      '</div>' +
      '<div class="settings-footer-note">' + esc(t('plugins.count', { n: installed.length })) + '</div>';
  }

  function installButton(id, featured) {
    var installed = S.installedPlugins.has(id);
    var installing = !!S.installing[id];
    if (featured) {
      if (installed) return '<button class="featured-install" type="button" disabled>' + icon('check', 10) + '<span>' + esc(t('common.installed')) + '</span></button>';
      if (installing) return '<button class="featured-install" type="button" disabled><span>' + esc(t('common.installing')) + '</span></button>';
      return '<button class="featured-install" type="button" data-install="' + id + '"><span>' + esc(t('common.install')) + '</span></button>';
    }
    if (installed) return '<button class="mc-install installed" type="button" disabled>' + icon('check', 9) + '<span>' + esc(t('common.installed')) + '</span></button>';
    if (installing) return '<button class="mc-install installing" type="button" disabled data-install="' + id + '"><span class="install-fill"></span><span>' + esc(t('common.installing')) + '</span></button>';
    return '<button class="mc-install" type="button" data-install="' + id + '"><span>' + esc(t('common.install')) + '</span></button>';
  }

  function renderMarket() {
    var cats = [
      ['all', 'market.cat.all'], ['productivity', 'market.cat.productivity'], ['media', 'market.cat.media'],
      ['developer', 'market.cat.developer'], ['system', 'market.cat.system'], ['ai', 'market.cat.ai']
    ];
    var cards = ALL_PLUGINS.filter(function (p) {
      return S.marketCat === 'all' || p.category === S.marketCat;
    }).map(function (p) {
      return '<article class="market-card" data-market-card data-search="' + esc(pluginSearchString(p)) + '">' +
        '<div class="mc-head">' +
          '<span class="mc-icon" style="--p:' + p.color + '">' + icon(p.icon, 16) + '</span>' +
          '<div class="mc-title"><div class="mc-name">' + esc(t(p.name)) + (p.verified ? icon('shield', 9) : '') + '</div>' +
            '<div class="mc-author">' + esc(p.author) + ' · v' + esc(p.version) + '</div>' +
          '</div>' +
        '</div>' +
        '<div class="mc-desc">' + esc(t(p.desc)) + '</div>' +
        '<div class="mc-foot">' +
          '<span class="mc-rating">' + icon('star', 8) + p.rating.toFixed(1) + '</span>' +
          '<span class="mc-downloads">' + esc(t('market.downloads', { n: p.downloads })) + '</span>' +
          installButton(p.id, false) +
        '</div>' +
      '</article>';
    }).join('');
    var featured = pluginById('agent-bridge');
    return paneHeader('market.title', 'market.desc') +
      '<div class="market-featured">' +
        '<div class="featured-copy">' +
          '<div class="featured-kicker">' + esc(t('market.featured')) + '</div>' +
          '<div class="featured-title">' + esc(t(featured.name)) + '</div>' +
          '<div class="featured-desc">' + esc(t('market.featuredDesc')) + '</div>' +
          '<div class="featured-actions">' + installButton('agent-bridge', true) +
            '<span class="featured-meta">' + esc(t('market.featuredMeta')) + '</span>' +
          '</div>' +
        '</div>' +
        '<div class="featured-art">' + icon('agent', 32) + '</div>' +
      '</div>' +
      '<div class="market-toolbar"><div class="search-field">' + icon('search', 13) +
        '<input type="text" data-search-input="marketQuery" placeholder="' + esc(t('market.search')) +
          '" aria-label="' + esc(t('market.search')) + '" value="' + esc(S.marketQuery) + '">' +
      '</div></div>' +
      '<div class="market-cats">' + cats.map(function (c) {
        return '<button class="cat-chip' + (S.marketCat === c[0] ? ' active' : '') + '" type="button" data-cat="' +
          c[0] + '">' + esc(t(c[1])) + '</button>';
      }).join('') + '</div>' +
      '<div class="market-grid">' + cards + '<div class="market-empty" hidden>' + esc(t('market.noResults')) + '</div></div>' +
      '<div class="market-footnote">' + icon('shield', 10) + esc(t('market.footnote')) + '</div>';
  }

  function renderAdvanced() {
    return paneHeader('advanced.title', 'advanced.desc') +
      '<div class="section-caption">' + esc(t('advanced.dataFolder')) + '</div>' +
      '<div class="path-field">' + icon('folder', 12) + '<span>C:\\Users\\Doulor\\AppData\\Roaming\\FluidBar</span></div>' +
      '<div class="adv-actions">' +
        '<button class="small-action" type="button" data-action="open-data-folder">' + icon('folder', 11) + esc(t('advanced.openFolder')) + '</button>' +
        '<button class="small-action" type="button" data-action="view-logs">' + icon('file', 11) + esc(t('advanced.logs')) + '</button>' +
        '<button class="small-action" type="button" data-action="export-config">' + icon('download', 11) + esc(t('advanced.export')) + '</button>' +
        '<button class="small-action" type="button" data-action="import-config">' + icon('refresh', 11) + esc(t('advanced.import')) + '</button>' +
      '</div>' +
      '<div class="setting-group" style="margin-top:12px">' +
        settingRow('advanced.devMode', 'advanced.devModeDesc', switchControl('devMode', S.devMode, 'advanced.devMode')) +
        settingRow('advanced.api', 'advanced.apiDesc', '<span class="value-tag" style="min-width:unset">v2.4.0</span>') +
      '</div>' +
      '<div class="adv-actions"><button class="small-action danger" type="button" data-action="reset-settings">' + icon('trash', 11) +
        esc(S.confirmReset ? t('advanced.confirmReset') : t('advanced.reset')) + '</button></div>' +
      '<div class="settings-footer-note">' + esc(t('settings.prototypeNote')) + '</div>';
  }

  function renderAbout() {
    var updating = S.updateState === 'checking';
    return paneHeader('about.title', 'about.desc') +
      '<div class="about-pane">' +
        '<span class="about-mark">' + icon('islandIcon', 30) + '</span>' +
        '<div class="about-name">FluidBar</div>' +
        '<div class="about-version">' + esc(t('about.version')) + '</div>' +
        '<div class="about-actions">' +
          '<button class="about-btn" type="button" data-action="update-check">' + icon('refresh', 11) +
            esc(updating ? t('about.checking') : (S.updateState === 'done' ? t('about.upToDate') : t('about.update'))) + '</button>' +
          '<button class="about-btn" type="button" data-action="proto-link">' + icon('external', 11) + esc(t('about.github')) + '</button>' +
        '</div>' +
        '<div class="license-line">' + esc(t('about.license')) + '</div>' +
        '<div class="contributors">' +
          '<div class="contributors-title">' + esc(t('about.contributors')) + '</div>' +
          '<div class="contrib-row">' +
            [['doulor', '#4cc9f0'], ['Vera Chen', '#f2b35e'], ['Kai', '#7f9bff'], ['Mori', '#8fd9a8']].map(function (c) {
              return '<span class="contrib" style="--c:' + c[1] + '">' + esc(c[0]) + '</span>';
            }).join('') +
          '</div>' +
        '</div>' +
        '<div class="about-links">' +
          '<button class="about-link" type="button" data-action="proto-link">' + icon('file', 10) + esc(t('about.docs')) + '</button>' +
          '<button class="about-link" type="button" data-action="proto-link">' + icon('github', 10) + esc(t('about.github')) + '</button>' +
          '<button class="about-link" type="button" data-action="proto-link">' + icon('chat', 10) + esc(t('about.feedback')) + '</button>' +
        '</div>' +
      '</div>';
  }

  function renderSettingsPane() {
    if (S.settingsTab === 'appearance') return renderAppearance();
    if (S.settingsTab === 'island') return renderIslandSettings();
    if (S.settingsTab === 'plugins') return renderPlugins();
    if (S.settingsTab === 'market') return renderMarket();
    if (S.settingsTab === 'advanced') return renderAdvanced();
    if (S.settingsTab === 'about') return renderAbout();
    return renderGeneral();
  }

  function navItem(tab, iconName, labelKey, count) {
    return '<button class="nav-item' + (S.settingsTab === tab ? ' active' : '') + '" type="button" data-nav="' +
      tab + '">' + icon(iconName, 15) + '<span>' + esc(t(labelKey)) + '</span>' +
      (count ? '<span class="nav-count">' + count + '</span>' : '') + '</button>';
  }

  function renderSettings(opts) {
    opts = opts || {};
    if (!E.settingsWindow) return;
    var oldPane = $('settingsPane');
    var scroll = opts.resetScroll ? 0 : (oldPane ? oldPane.scrollTop : S.settingsScroll);
    var nav = '<div class="nav-kicker">' + esc(t('settings.preferences')) + '</div>' +
      SETTINGS_NAV.map(function (n) {
        var count = 0;
        if (n[0] === 'plugins') count = S.installedPlugins.size;
        else if (n[0] === 'market') count = ALL_PLUGINS.length;
        return navItem(n[0], n[1], n[2], count);
      }).join('');

    E.settingsWindow.innerHTML =
      '<div class="settings-titlebar">' +
        '<span class="sw-mark">' + icon('islandIcon', 12) + '</span>' +
        '<span class="sw-title">' + esc(t('settings.title')) + '</span>' +
        '<span class="sw-sub">' + esc(t('settings.sub')) + '</span>' +
        '<div class="spacer" style="flex:1"></div>' +
        '<button class="sw-close" type="button" data-action="close-modal" aria-label="' + esc(t('common.close')) + '">' + icon('close', 14) + '</button>' +
      '</div>' +
      '<div class="settings-body">' +
        '<nav class="settings-nav" aria-label="' + esc(t('settings.preferences')) + '">' + nav +
          '<div class="nav-version"><b>FluidBar 2.0.0</b>' + esc(t('settings.prototypeNote')) + '</div>' +
        '</nav>' +
        '<div class="settings-pane" id="settingsPane" tabindex="0">' + renderSettingsPane() + '</div>' +
      '</div>';

    var pane = $('settingsPane');
    if (pane) {
      pane.scrollTop = scroll;
      pane.addEventListener('scroll', function () { S.settingsScroll = pane.scrollTop; }, { passive: true });
    }
    applySearchFilters();
    if (S.settingsTab === 'appearance') updateAppearancePreview();
    requestAnimationFrame(function () {
      if (!E.settingsWindow) return;
      var fills = E.settingsWindow.querySelectorAll('.mc-install.installing');
      for (var i = 0; i < fills.length; i++) fills[i].classList.add('started');
    });
  }

  function applySearchFilters() {
    if (!E.settingsWindow) return;
    var q1 = S.pluginQuery.trim().toLowerCase();
    var rows = E.settingsWindow.querySelectorAll('[data-plugin-row]');
    for (var i = 0; i < rows.length; i++) {
      rows[i].hidden = !!q1 && (rows[i].dataset.search || '').indexOf(q1) === -1;
    }
    var q2 = S.marketQuery.trim().toLowerCase();
    var visible = 0;
    var cards = E.settingsWindow.querySelectorAll('[data-market-card]');
    for (var j = 0; j < cards.length; j++) {
      var show = !q2 || (cards[j].dataset.search || '').indexOf(q2) !== -1;
      cards[j].hidden = !show;
      if (show) visible++;
    }
    var empty = E.settingsWindow.querySelector('.market-empty');
    if (empty) empty.hidden = visible !== 0;
  }

  function openSettings(tab, opts) {
    opts = opts || {};
    S.settingsOpen = true;
    S.settingsTab = SETTINGS_TABS.indexOf(tab) >= 0 ? tab : 'general';
    if (opts.resetScroll !== false) S.settingsScroll = 0;
    renderSettings({ resetScroll: true });
    E.modalLayer.hidden = false;
    document.body.classList.add('modal-open');
    if (opts.instant) E.modalLayer.classList.add('open');
    else requestAnimationFrame(function () { E.modalLayer.classList.add('open'); });
    clearTimeout(openSettings._focusTimer);
    openSettings._focusTimer = setTimeout(function () {
      if (E.settingsWindow && S.settingsOpen) E.settingsWindow.focus({ preventScroll: true });
    }, 60);
  }

  function closeSettings() {
    if (!S.settingsOpen) return;
    S.settingsOpen = false;
    E.modalLayer.classList.remove('open');
    document.body.classList.remove('modal-open');
    clearTimeout(closeSettings._timer);
    closeSettings._timer = setTimeout(function () {
      if (!S.settingsOpen) E.modalLayer.hidden = true;
    }, 260);
  }

  function installPlugin(id) {
    if (!id || S.installedPlugins.has(id) || S.installing[id]) return;
    S.installing[id] = true;
    renderSettings();
    setTimeout(function () {
      S.installing[id] = false;
      S.installedPlugins.add(id);
      var p = pluginById(id);
      if (S.settingsOpen) renderSettings();
      showToast(t('toast.installed', { name: p ? t(p.name) : id }), { kind: 'success' });
    }, 1150);
  }

  function applySetting(prop, value) {
    if (prop === 'lang') { setLang(value); return; }
    if (prop === 'mode') { setMode(value); return; }
    if (prop === 'theme') { setTheme(value); return; }
    if (prop === 'motion') { setMotion(value); return; }
    if (prop === 'material') {
      S.material = value;
      applyAppearance();
    } else if (prop === 'anchor') {
      S.anchor = value;
      syncChrome();
    } else if (prop === 'strategy') {
      S.strategy = value;
    }
    if (S.settingsOpen) renderSettings();
  }

  function applyRange(key, value) {
    if (key === 'radius') S.radius = value;
    else if (key === 'opacity') S.opacity = value;
    else if (key === 'autoCollapse') S.autoCollapse = value;
    else if (key === 'motionStrength') S.motionStrength = value;
    if (key === 'radius' || key === 'opacity') applyAppearance();
    var out = document.querySelector('[data-range-value="' + key + '"]');
    if (out) out.textContent = fmtSettingValue(key, value);
    if (key === 'radius' || key === 'opacity') updateAppearancePreview();
  }

  function resetAllSettings() {
    S.theme = 'dark';
    S.material = 'obsidian';
    S.motion = 'normal';
    S.accentKey = 'aqua';
    S.radius = 30;
    S.opacity = 97;
    S.anchor = 'center';
    S.strategy = 'stack';
    S.hoverPreview = true;
    S.autoCollapse = 0.9;
    S.motionStrength = 1;
    S.startup = true;
    S.autoUpdate = true;
    S.trayIcon = true;
    S.devMode = false;
    S.confirmReset = false;
    S.mode = 'float';
    applyAppearance();
    renderScene();
    renderTaskbar();
    renderDock();
    renderCompact();
    renderPages();
    updateConsole();
    updateDockActive();
    renderSettings();
    showToast(t('toast.reset'), { kind: 'success' });
  }


  /* ==========================================================================
     10. EVENTS & BOOT
     ========================================================================== */
  function onClick(e) {
    var panelBtn = e.target.closest('[data-open-panel]');
    if (panelBtn) {
      openSettings(panelBtn.dataset.openPanel === 'market' ? 'market' : 'general');
      return;
    }

    var farDot = e.target.closest('[data-far]');
    if (farDot) {
      jumpToPage(Number(farDot.dataset.far));
      return;
    }

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
      applySetting(setter.dataset.set, setter.dataset.value);
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
    if (e.key === 'Escape') {
      if (S.settingsOpen) {
        e.preventDefault();
        closeSettings();
        return;
      }
      if (S.expanded) {
        e.preventDefault();
        collapse();
        return;
      }
      return;
    }
    if (S.settingsOpen) return;
    if (!S.expanded) {
      if ((e.key === 'Enter' || e.key === ' ') && document.activeElement === E.island) {
        e.preventDefault();
        expand(tabForView(S.view));
      }
      return;
    }
    if (e.target.closest && e.target.closest('input, textarea, select')) return;
    var idx = clamp(Math.round(P.offset), 0, MAX_PAGE);
    var target = null;
    if (/^[1-6]$/.test(e.key)) {
      target = Number(e.key) - 1;
    } else if (e.key === 'ArrowLeft') {
      target = idx - 1;
    } else if (e.key === 'ArrowRight') {
      target = idx + 1;
    } else if (e.key === 'Home') {
      target = 0;
    } else if (e.key === 'End') {
      target = MAX_PAGE;
    }
    if (target === null) return;
    e.preventDefault();
    target = clamp(target, 0, MAX_PAGE);
    P.railLock = null;
    springTo(target, 0);
    showRail(target);
  }

  function bindModal() {
    E.modalLayer.addEventListener('click', function (e) {
      var act = e.target.closest('[data-action]');
      if (act) {
        var a = act.dataset.action;
        if (a === 'close-modal') { closeSettings(); return; }
        if (a === 'update-check') {
          if (S.updateState === 'checking') return;
          S.updateState = 'checking';
          renderSettings();
          setTimeout(function () {
            S.updateState = 'done';
            if (S.settingsOpen) renderSettings();
            showToast(t('about.updateToast'), { kind: 'success' });
          }, 1000);
          return;
        }
        if (a === 'reset-settings') {
          if (!S.confirmReset) {
            S.confirmReset = true;
            renderSettings();
            clearTimeout(bindModal._resetTimer);
            bindModal._resetTimer = setTimeout(function () {
              S.confirmReset = false;
              if (S.settingsOpen) renderSettings();
            }, 3200);
          } else {
            resetAllSettings();
          }
          return;
        }
        if (a === 'open-plugin-folder' || a === 'open-data-folder') {
          showToast(t('toast.folderMock'), { icon: 'folder' });
          return;
        }
        if (a === 'view-logs') {
          showToast(t('toast.logs'), { icon: 'file', sub: 'fluidbar-2026-10-18.log' });
          return;
        }
        if (a === 'export-config') { showToast(t('toast.exported'), { icon: 'download' }); return; }
        if (a === 'import-config') { showToast(t('toast.imported'), { icon: 'refresh' }); return; }
        if (a === 'proto-link') { showToast(t('toast.linkDisabled'), { icon: 'external' }); return; }
        return;
      }

      var nav = e.target.closest('[data-nav]');
      if (nav) {
        S.settingsTab = nav.dataset.nav;
        S.confirmReset = false;
        S.settingsScroll = 0;
        renderSettings({ resetScroll: true });
        return;
      }

      var toggle = e.target.closest('[data-toggle]');
      if (toggle) {
        var prop = toggle.dataset.toggle;
        S[prop] = !S[prop];
        toggle.classList.toggle('on', S[prop]);
        toggle.setAttribute('aria-checked', String(S[prop]));
        if (prop === 'hoverPreview') syncChrome();
        return;
      }

      var tglPlugin = e.target.closest('[data-toggle-plugin]');
      if (tglPlugin) {
        var pid = tglPlugin.dataset.togglePlugin;
        S.pluginEnabled[pid] = !S.pluginEnabled[pid];
        tglPlugin.classList.toggle('on', !!S.pluginEnabled[pid]);
        tglPlugin.setAttribute('aria-checked', String(!!S.pluginEnabled[pid]));
        return;
      }

      var pluginSettings = e.target.closest('[data-plugin-settings]');
      if (pluginSettings) {
        var plugin = pluginById(pluginSettings.dataset.pluginSettings);
        if (plugin) showToast(t('toast.pluginSettings', { name: t(plugin.name) }), { icon: 'puzzle' });
        return;
      }

      var accent = e.target.closest('[data-accent]');
      if (accent) {
        S.accentKey = accent.dataset.accent;
        applyAppearance();
        renderSettings();
        return;
      }

      var install = e.target.closest('[data-install]');
      if (install) {
        installPlugin(install.dataset.install);
        return;
      }

      var cat = e.target.closest('[data-cat]');
      if (cat) {
        S.marketCat = cat.dataset.cat;
        renderSettings();
        return;
      }
    });

    E.modalLayer.addEventListener('input', function (e) {
      var el = e.target;
      if (el.matches('[data-range]')) {
        applyRange(el.dataset.range, parseFloat(el.value));
        el.style.setProperty('--val', rangeVal(el.value, parseFloat(el.min), parseFloat(el.max)));
        return;
      }
      if (el.matches('[data-search-input]')) {
        S[el.dataset.searchInput] = el.value;
        applySearchFilters();
      }
    });
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
    E.miniRail.addEventListener('focusin', function () {
      if (!S.expanded) return;
      clearTimeout(railTimer);
      E.miniRail.classList.add('is-visible');
      RAIL.visible = true;
      if (!S.forceArch) railTimer = setTimeout(function () { hideRail(); }, 4000);
    });
    E.miniRail.addEventListener('click', function (e) {
      var btn = e.target.closest('.mini-bg[data-rail]');
      if (!btn || btn.disabled) return;
      if (!S.expanded) return;
      e.preventDefault();
      e.stopPropagation();
      /* Pill body = adjacent page (far-page jumps stay on the dots). */
      turnPage(btn.dataset.rail === 'next' ? 1 : -1);
    });
    E.miniRail.addEventListener('pointerover', function (e) {
      /* Hovering the interactive rail keeps it on screen instead of letting the
         900 ms inactivity timer pull it away under the cursor. */
      if (!S.expanded || !RAIL.visible) return;
      if (!e.target.closest('.mini-bg[data-rail], .far-dot')) return;
      showRail();
    });
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
    bindModal();
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
    E.toast = $('toast');
    E.modalLayer = $('modalLayer');
    E.settingsWindow = $('settingsWindow');

    E.pagerCollapseIcon.innerHTML = icon('chevronUp', 14);

    parseParams();
    applyAppearance();
    renderScene();
    renderTaskbar();
    renderDock();
    renderConsole();
    buildRail();
    renderCompact();
    renderPages();

    var startIndex = S.expanded ? pageIndex(S.tab) : 0;
    setPageInstant(startIndex);
    railSetPage(startIndex, { immediate: true });
    applyExpandedState();
    bindEvents();

    if (S.forceArch) showRail(startIndex);
    if (S.panelRequest) openSettings(S.panelRequest.tab, { instant: true });

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
