/* ==========================================================================
   FluidBar 2.0 — interactive prototype
   app.js

   01. Utilities & i18n
   02. Demo data
   03. Application state
   04. Spring engine + shell geometry
   05. Scene / taskbar / dock rendering
   06. Island compact views
   07. Expanded panel (home, media, notifications, clipboard, agent, widgets)
   08. Demo console
   09. Settings + plugin market
   10. Annotations
   11. Interactions
   12. Playback, timer, auto demo
   13. URL params + boot
   ========================================================================== */
(function () {
  'use strict';

  var FB = window.FB_ICONS || {};
  var icon = FB.icon || function () { return ''; };

  /* ==========================================================================
     01. UTILITIES & I18N
     ========================================================================== */
  var $ = function (sel, root) { return (root || document).querySelector(sel); };
  var $$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };
  var clamp = function (v, lo, hi) { return Math.min(hi, Math.max(lo, v)); };
  var pad2 = function (n) { return String(n).padStart(2, '0'); };
  var esc = function (s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
    });
  };
  var fmtTime = function (seconds) {
    seconds = Math.max(0, Math.round(seconds));
    return Math.floor(seconds / 60) + ':' + pad2(seconds % 60);
  };

  var I18N = {
    zh: {
      'common.close': '关闭', 'common.open': '打开', 'common.copy': '复制',
      'common.install': '安装', 'common.installed': '已安装', 'common.installing': '安装中',
      'common.search': '搜索', 'common.all': '全部', 'common.today': '今天',
      'common.update': '检查更新', 'common.reset': '重置', 'common.verified': '已验证',
      'common.builtin': '内置', 'common.community': '社区', 'common.live': '实时',
      'common.prevMonth': '上个月', 'common.nextMonth': '下个月',

      'scene.computer': '此电脑', 'scene.recycle': '回收站', 'scene.fluidbar': 'FluidBar 2.0',
      'scene.explorerTitle': '项目 — 文件资源管理器', 'scene.explorerTab1': '项目', 'scene.explorerTab2': '此电脑',
      'scene.navHome': '主页', 'scene.navProjects': '项目', 'scene.navRecent': '最近使用',
      'scene.navFavorites': '收藏夹', 'scene.navDownloads': '下载', 'scene.navDesign': '设计资料',
      'scene.folderFluidbar': 'fluidbar-v2', 'scene.folderDesign': 'design', 'scene.folderSrc': 'src',
      'scene.fileReadme': 'README.md', 'scene.fileSolution': 'FluidBar.sln', 'scene.fileNotes': 'notes.md',
      'scene.fileIcons': 'island-spec.svg', 'scene.terminalTitle': 'Windows 终端', 'scene.terminalTab': 'PowerShell',
      'scene.termRestore': '正在确定要还原的项目…', 'scene.termBuilt': '生成成功 · 0 个警告 · 0 个错误',
      'scene.hint': '拖拽灵动岛改变位置 · 双击桌面或按 Esc 收起',
      'scene.hintDock': '点击图标或托盘展开面板 · 按 Esc 收起',
      'taskbar.weather': '多云', 'taskbar.city': '上海',

      'app.start': '开始', 'app.search': '搜索', 'app.widgets': '小组件', 'app.files': '文件资源管理器',
      'app.terminal': '终端', 'app.music': '音乐', 'app.settings': '设置', 'app.notifications': '通知',
      'app.clipboard': '剪贴板', 'app.agent': 'Agent', 'app.dock': 'FluidBar Dock',
      'tray.wifi': '网络', 'tray.volume': '音量', 'tray.battery': '电池 86%', 'tray.clock': '时间和日期',

      'island.expand': '点击展开', 'island.collapse': '收起灵动岛',
      'rail.label': '面板导航',
      'tab.home': '主页', 'tab.media': '媒体', 'tab.notifications': '通知', 'tab.clipboard': '剪贴板',
      'tab.agent': 'Agent', 'tab.widgets': '小组件',
      'media.prev': '上一首', 'media.next': '下一首', 'media.play': '播放', 'media.pause': '暂停',
      'media.output': '音频输出', 'media.speaker': '扬声器',
      'media.nowPlaying': '正在播放', 'media.seek': '播放进度', 'media.queue': '队列', 'media.nextLine': '下一句',
      'hud.volume': '音量', 'hud.brightness': '亮度',
      'clipboard.copied': '已复制', 'agent.running': '进行中', 'agent.done': '已完成', 'agent.error': '失败',
      'timer.focus': '专注', 'battery.plugged': '已接通电源',

      'home.date': '2026 年 10 月 18 日 · 星期日', 'home.weather': '多云 · 上海',
      'home.quickWifi': 'Wi-Fi', 'home.quickBluetooth': '蓝牙', 'home.quickFocus': '专注', 'home.quickDark': '深色',
      'home.charging': '已接通电源', 'home.remaining': '剩余约 3 小时 12 分',
      'home.weatherHigh': '最高 22°',

      'media.speaker': '扬声器',
      'media.lyric0': '霓虹沉入海面，我们仍亮着',
      'media.lyric1': '潮汐退去，信号仍在闪烁',
      'media.lyric2': '纸飞机落在周日的光里',
      'media.lyricNext0': '潮汐退去，信号仍在闪烁',
      'media.lyricNext1': '纸飞机落在周日的光里',
      'media.lyricNext2': '等城市熄灯，再把名字念一遍',

      'notifications.title': '通知', 'notifications.unread': '{n} 条未读', 'notifications.allRead': '全部已读',
      'notifications.clear': '全部清除', 'notifications.open': '打开', 'notifications.dismiss': '忽略',
      'notifications.emptyTitle': '没有新通知', 'notifications.emptySub': '新的提醒会显示在这里',

      'clipboard.title': '剪贴板', 'clipboard.count': '{n} 条记录', 'clipboard.copy': '复制',
      'clipboard.pin': '固定', 'clipboard.unpin': '取消固定', 'clipboard.pinned': '已固定', 'clipboard.unpinned': '已取消固定',
      'clipboard.type.link': '链接', 'clipboard.type.image': '图片', 'clipboard.type.code': '代码',
      'clipboard.type.command': '命令', 'clipboard.type.text': '文本',

      'agent.title': 'Agent 任务', 'agent.count': '{n} 个进行中', 'agent.tabCount': '{n} 个任务',
      'agent.status.running': '进行中', 'agent.status.done': '已完成', 'agent.status.error': '失败',

      'widgets.weekdays': '一,二,三,四,五,六,日',
      'widgets.month': '{year} 年 {month} 月', 'widgets.today': '今天',
      'widgets.weather': '多云 · 上海', 'widgets.highLow': '最高 22° · 最低 15°', 'widgets.wind': '东南风 3 级',
      'widgets.cpu': 'CPU', 'widgets.ram': '内存', 'widgets.battery': '电量',
      'widgets.ramValue': '6.1 / 16 GB', 'widgets.cpuValue': '23%', 'widgets.battValue': '86%',
      'widgets.notesTitle': '今日待办', 'widgets.notesProgress': '{n}/{m} 已完成',
      'widgets.todo0': '确认 440 宽的展开面板节奏', 'widgets.todo1': '把六个分区统一到 284 高度', 'widgets.todo2': '录一版 120Hz 交互演示',
      'widgets.calEvent': '设计评审 15:00', 'widgets.calToday': '今天 18 日',

      'console.title': 'FluidBar 2.0 · 交互原型', 'console.version': 'v2.0',
      'console.controls': '演示控制', 'console.mode': '模式', 'console.material': '材质', 'console.theme': '主题',
      'console.lang': '语言', 'console.motion': '动效', 'console.modeFloat': '浮岛', 'console.modeFlush': '贴边',
      'console.modeDock': 'Dock', 'console.obsidian': '纯黑', 'console.acrylic': '亚克力',
      'console.dark': '深色', 'console.light': '浅色', 'console.zh': '中文', 'console.en': 'English',
      'console.normal': '标准', 'console.reduced': '减弱', 'console.live': '实时', 'console.manual': '手动',
      'console.triggers': '触发场景', 'console.motionLab': 'Motion Lab',
      'console.trig.media': '音乐播放', 'console.trig.pause': '暂停', 'console.trig.notification': '通知',
      'console.trig.clipboard': '剪贴板', 'console.trig.volume': '音量 HUD', 'console.trig.brightness': '亮度 HUD',
      'console.trig.agentDone': 'Agent 完成', 'console.trig.agentFail': 'Agent 失败', 'console.trig.timer': '番茄钟',
      'console.trig.battery': '电池充电', 'console.trig.demo': '自动演示', 'console.trig.stop': '停止演示',
      'console.expandT': '展开 T', 'console.expandZ': '展开 ζ', 'console.collapseT': '收起 T',
      'console.collapseZ': '收起 ζ', 'console.heightLag': '高度滞后',
      'console.replay': '重播展开', 'console.resetMotion': '重置', 'console.readoutK': 'k',
      'console.readoutC': 'c', 'console.readoutSettle': '收敛', 'console.settings': '设置',
      'console.market': '插件市场', 'console.annotations': '设计标注', 'console.annotationsOn': '关闭标注',
      'console.note': '原型演示控制台 · 不随正式版发布', 'console.collapse': '收起控制台', 'console.open': '打开控制台',

      'settings.title': '设置', 'settings.sub': 'FLUIDBAR 2.0', 'settings.preferences': '偏好设置',
      'settings.prototype': '交互原型', 'settings.prototypeNote': '交互原型 · 非发行版本',
      'nav.general': '通用', 'nav.appearance': '外观', 'nav.island': '灵动岛', 'nav.plugins': '插件',
      'nav.market': '插件市场', 'nav.advanced': '高级', 'nav.about': '关于',

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
      'appearance.material': '灵动岛材质', 'appearance.materialDesc': '亚克力会实时采样桌面背景，可能略微增加 GPU 占用。',
      'appearance.accent': '强调色', 'appearance.accentDesc': '用于开关、进度与选中态；媒体页始终使用封面取色。',
      'appearance.radius': '大面板圆角', 'appearance.radiusDesc': '小胶囊始终保持 stadium 半径。',
      'appearance.opacity': '填充不透明度', 'appearance.opacityDesc': '默认 96%；低于 88% 时亚克力会自动增加模糊。',
      'appearance.font': '字体缩放', 'appearance.fontDesc': '缩放灵动岛内的时间与标题字号。',
      'appearance.liveHint': '更改会立即应用于灵动岛。',
      'appearance.preview': '灵动岛预览',
      'accent.aqua': '水蓝', 'accent.mint': '薄荷', 'accent.amber': '琥珀', 'accent.rose': '玫瑰', 'accent.blue': '钴蓝',

      'island.title': '灵动岛', 'island.desc': '形态、锚点与事件堆叠策略。',
      'island.defaultMode': '默认形态', 'island.defaultModeDesc': '浮岛常驻顶部；贴边更像 Windows 通知中心；Dock 替换任务栏。',
      'island.anchor': '锚点位置', 'island.anchorDesc': '浮岛模式下沿屏幕顶部对齐，仍可拖拽微调。',
      'island.anchorCenter': '居中', 'island.anchorLeft': '靠左', 'island.anchorRight': '靠右',
      'island.strategy': '显示策略', 'island.strategyDesc': '多个事件同时到达时的呈现方式。',
      'island.strategyStack': '多岛堆叠', 'island.strategyLatest': '仅显示最新',
      'island.hover': '悬停预览', 'island.hoverDesc': '悬停 90ms 后轻微放大，并显示快捷控制。',
      'island.autoCollapse': '自动收起延迟', 'island.autoCollapseDesc': '音量、亮度与剪贴板等临时卡片停留时间。',
      'island.motionStrength': '动效强度', 'island.motionStrengthDesc': '调节弹簧节奏与位移幅度；减弱模式会强制临界阻尼。',

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

      'toast.copied': '已复制到剪贴板', 'toast.installed': '{name} 已安装',
      'toast.exported': '配置导出已模拟完成', 'toast.imported': '配置导入已模拟完成',
      'toast.reset': '已恢复默认设置', 'toast.pluginSettings': '{name} 的插件设置将在正式版中打开',
      'toast.linkDisabled': '原型中已禁用外部链接', 'toast.openedNotification': '正在打开 {app}…',
      'toast.openedAgent': '已切换到 {tool} · {project}', 'toast.output': '音频输出：扬声器',
      'toast.queue': '播放队列 · {n} 首',
      'toast.demoStarted': '自动演示已开始', 'toast.demoStopped': '自动演示已停止',
      'toast.folderMock': '原型中不访问本地文件系统', 'toast.updateMock': '原型中更新流程为模拟',

      'anno.title': 'FLUIDBAR · 实时标注', 'anno.shell': 'SHELL', 'anno.width': 'W', 'anno.height': 'H',
      'anno.radius': 'R', 'anno.size': '尺寸', 'anno.radiusLabel': '圆角', 'anno.alpha': '内容 α',
      'anno.spring': '展开弹簧', 'anno.kc': 'k / c', 'anno.settle': '收敛', 'anno.tokens': 'TOKENS'
    },

    en: {
      'common.close': 'Close', 'common.open': 'Open', 'common.copy': 'Copy',
      'common.install': 'Install', 'common.installed': 'Installed', 'common.installing': 'Installing',
      'common.search': 'Search', 'common.all': 'All', 'common.today': 'Today',
      'common.update': 'Check for updates', 'common.reset': 'Reset', 'common.verified': 'Verified',
      'common.builtin': 'Built-in', 'common.community': 'Community', 'common.live': 'Live',
      'common.prevMonth': 'Previous month', 'common.nextMonth': 'Next month',

      'scene.computer': 'This PC', 'scene.recycle': 'Recycle Bin', 'scene.fluidbar': 'FluidBar 2.0',
      'scene.explorerTitle': 'Projects — File Explorer', 'scene.explorerTab1': 'Projects', 'scene.explorerTab2': 'This PC',
      'scene.navHome': 'Home', 'scene.navProjects': 'Projects', 'scene.navRecent': 'Recent',
      'scene.navFavorites': 'Favorites', 'scene.navDownloads': 'Downloads', 'scene.navDesign': 'Design assets',
      'scene.folderFluidbar': 'fluidbar-v2', 'scene.folderDesign': 'design', 'scene.folderSrc': 'src',
      'scene.fileReadme': 'README.md', 'scene.fileSolution': 'FluidBar.sln', 'scene.fileNotes': 'notes.md',
      'scene.fileIcons': 'island-spec.svg', 'scene.terminalTitle': 'Windows Terminal', 'scene.terminalTab': 'PowerShell',
      'scene.termRestore': 'Determining projects to restore…', 'scene.termBuilt': 'Build succeeded · 0 warnings · 0 errors',
      'scene.hint': 'Drag the island to reposition · double-click the desktop or press Esc to collapse',
      'scene.hintDock': 'Click an icon or the tray to open the panel · press Esc to dismiss',
      'taskbar.weather': 'Cloudy', 'taskbar.city': 'Shanghai',

      'app.start': 'Start', 'app.search': 'Search', 'app.widgets': 'Widgets', 'app.files': 'File Explorer',
      'app.terminal': 'Terminal', 'app.music': 'Music', 'app.settings': 'Settings', 'app.notifications': 'Notifications',
      'app.clipboard': 'Clipboard', 'app.agent': 'Agent', 'app.dock': 'FluidBar Dock',
      'tray.wifi': 'Network', 'tray.volume': 'Volume', 'tray.battery': 'Battery 86%', 'tray.clock': 'Time and date',

      'island.expand': 'Click to expand', 'island.collapse': 'Collapse island',
      'rail.label': 'Panel navigation',
      'tab.home': 'Home', 'tab.media': 'Media', 'tab.notifications': 'Notifications', 'tab.clipboard': 'Clipboard',
      'tab.agent': 'Agent', 'tab.widgets': 'Widgets',
      'media.prev': 'Previous', 'media.next': 'Next', 'media.play': 'Play', 'media.pause': 'Pause',
      'media.output': 'Audio output', 'media.speaker': 'Speakers',
      'media.nowPlaying': 'Now playing', 'media.seek': 'Playback position', 'media.queue': 'Queue', 'media.nextLine': 'Next',
      'hud.volume': 'Volume', 'hud.brightness': 'Brightness',
      'clipboard.copied': 'Copied', 'agent.running': 'Running', 'agent.done': 'Done', 'agent.error': 'Failed',
      'timer.focus': 'Focus', 'battery.plugged': 'Plugged in',

      'home.date': 'Sunday, October 18, 2026', 'home.weather': 'Cloudy · Shanghai',
      'home.quickWifi': 'Wi-Fi', 'home.quickBluetooth': 'Bluetooth', 'home.quickFocus': 'Focus', 'home.quickDark': 'Dark',
      'home.charging': 'Plugged in', 'home.remaining': 'About 3 h 12 min left',
      'home.weatherHigh': 'High 22°',

      'media.speaker': 'Speakers',
      'media.lyric0': 'Neon sinks into the sea — we’re still burning',
      'media.lyric1': 'The tide recedes, the signal still glows',
      'media.lyric2': 'Paper planes land in Sunday light',
      'media.lyricNext0': 'The tide recedes, the signal still glows',
      'media.lyricNext1': 'Paper planes land in Sunday light',
      'media.lyricNext2': 'When the city sleeps, we say our names again',

      'notifications.title': 'Notifications', 'notifications.unread': '{n} unread', 'notifications.allRead': 'All read',
      'notifications.clear': 'Clear all', 'notifications.open': 'Open', 'notifications.dismiss': 'Dismiss',
      'notifications.emptyTitle': 'You’re all caught up', 'notifications.emptySub': 'New alerts will appear here',

      'clipboard.title': 'Clipboard', 'clipboard.count': '{n} items', 'clipboard.copy': 'Copy',
      'clipboard.pin': 'Pin', 'clipboard.unpin': 'Unpin', 'clipboard.pinned': 'Pinned', 'clipboard.unpinned': 'Unpinned',
      'clipboard.type.link': 'Link', 'clipboard.type.image': 'Image', 'clipboard.type.code': 'Code',
      'clipboard.type.command': 'Command', 'clipboard.type.text': 'Text',

      'agent.title': 'Agent tasks', 'agent.count': '{n} running', 'agent.tabCount': '{n} tasks',
      'agent.status.running': 'Running', 'agent.status.done': 'Done', 'agent.status.error': 'Failed',

      'widgets.weekdays': 'M,T,W,T,F,S,S',
      'widgets.month': '{month} {year}', 'widgets.today': 'Today',
      'widgets.weather': 'Cloudy · Shanghai', 'widgets.highLow': 'H 22° · L 15°', 'widgets.wind': 'SE wind · 3',
      'widgets.cpu': 'CPU', 'widgets.ram': 'RAM', 'widgets.battery': 'Battery',
      'widgets.ramValue': '6.1 / 16 GB', 'widgets.cpuValue': '23%', 'widgets.battValue': '86%',
      'widgets.notesTitle': 'Today', 'widgets.notesProgress': '{n}/{m} done',
      'widgets.todo0': 'Lock the 440px expanded panel rhythm', 'widgets.todo1': 'Unify all six sections at 284px', 'widgets.todo2': 'Record a 120Hz interaction demo',
      'widgets.calEvent': 'Design review 15:00', 'widgets.calToday': 'Today · Oct 18',

      'console.title': 'FluidBar 2.0 · Prototype', 'console.version': 'v2.0',
      'console.controls': 'Demo controls', 'console.mode': 'Mode', 'console.material': 'Material', 'console.theme': 'Theme',
      'console.lang': 'Language', 'console.motion': 'Motion', 'console.modeFloat': 'Float', 'console.modeFlush': 'Flush',
      'console.modeDock': 'Dock', 'console.obsidian': 'Obsidian', 'console.acrylic': 'Acrylic',
      'console.dark': 'Dark', 'console.light': 'Light', 'console.zh': '中文', 'console.en': 'English',
      'console.normal': 'Standard', 'console.reduced': 'Reduced', 'console.live': 'LIVE', 'console.manual': 'MANUAL',
      'console.triggers': 'Trigger events', 'console.motionLab': 'Motion Lab',
      'console.trig.media': 'Play music', 'console.trig.pause': 'Pause', 'console.trig.notification': 'Notification',
      'console.trig.clipboard': 'Clipboard', 'console.trig.volume': 'Volume HUD', 'console.trig.brightness': 'Brightness HUD',
      'console.trig.agentDone': 'Agent done', 'console.trig.agentFail': 'Agent failed', 'console.trig.timer': 'Focus timer',
      'console.trig.battery': 'Battery charging', 'console.trig.demo': 'Auto demo', 'console.trig.stop': 'Stop demo',
      'console.expandT': 'Expand T', 'console.expandZ': 'Expand ζ', 'console.collapseT': 'Collapse T',
      'console.collapseZ': 'Collapse ζ', 'console.heightLag': 'Height lag',
      'console.replay': 'Replay expand', 'console.resetMotion': 'Reset', 'console.readoutK': 'k',
      'console.readoutC': 'c', 'console.readoutSettle': 'settle', 'console.settings': 'Settings',
      'console.market': 'Plugin market', 'console.annotations': 'Design specs', 'console.annotationsOn': 'Hide specs',
      'console.note': 'Prototype console · not part of the shipped app', 'console.collapse': 'Collapse console', 'console.open': 'Open console',

      'settings.title': 'Settings', 'settings.sub': 'FLUIDBAR 2.0', 'settings.preferences': 'Preferences',
      'settings.prototype': 'Interactive prototype', 'settings.prototypeNote': 'Interactive prototype · non-release build',
      'nav.general': 'General', 'nav.appearance': 'Appearance', 'nav.island': 'Dynamic island', 'nav.plugins': 'Plugins',
      'nav.market': 'Plugin market', 'nav.advanced': 'Advanced', 'nav.about': 'About',

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
      'appearance.material': 'Island material', 'appearance.materialDesc': 'Acrylic samples the wallpaper live and may slightly increase GPU usage.',
      'appearance.accent': 'Accent color', 'appearance.accentDesc': 'Used for toggles, progress and selection. Media always derives color from artwork.',
      'appearance.radius': 'Large panel radius', 'appearance.radiusDesc': 'The compact pill always keeps its stadium radius.',
      'appearance.opacity': 'Fill opacity', 'appearance.opacityDesc': '96% by default. Acrylic adds more blur below 88%.',
      'appearance.font': 'Font scale', 'appearance.fontDesc': 'Scales the clock and titles inside the island.',
      'appearance.liveHint': 'Changes are applied to the island immediately.',
      'appearance.preview': 'Island preview',
      'accent.aqua': 'Aqua', 'accent.mint': 'Mint', 'accent.amber': 'Amber', 'accent.rose': 'Rose', 'accent.blue': 'Cobalt',

      'island.title': 'Dynamic island', 'island.desc': 'Shape, anchor and event-stacking strategy.',
      'island.defaultMode': 'Default mode', 'island.defaultModeDesc': 'Float keeps a pill on top; Flush reads like a Windows notification bar; Dock replaces the taskbar.',
      'island.anchor': 'Anchor position', 'island.anchorDesc': 'Aligns the floating pill along the top; drag to fine-tune at any time.',
      'island.anchorCenter': 'Center', 'island.anchorLeft': 'Left', 'island.anchorRight': 'Right',
      'island.strategy': 'Display strategy', 'island.strategyDesc': 'How simultaneous activities are presented.',
      'island.strategyStack': 'Stacked islands', 'island.strategyLatest': 'Latest only',
      'island.hover': 'Hover preview', 'island.hoverDesc': 'After 90ms of hover, the pill grows slightly and reveals quick controls.',
      'island.autoCollapse': 'Auto-collapse delay', 'island.autoCollapseDesc': 'How long temporary cards such as volume, brightness and clipboard stay visible.',
      'island.motionStrength': 'Motion strength', 'island.motionStrengthDesc': 'Adjusts spring pacing and travel; Reduced motion forces critical damping.',

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

      'toast.copied': 'Copied to clipboard', 'toast.installed': '{name} installed',
      'toast.exported': 'Config export simulated', 'toast.imported': 'Config import simulated',
      'toast.reset': 'Settings restored to defaults', 'toast.pluginSettings': '{name} plugin settings will open in the shipped app',
      'toast.linkDisabled': 'External links are disabled in this prototype', 'toast.openedNotification': 'Opening {app}…',
      'toast.openedAgent': 'Switched to {tool} · {project}', 'toast.output': 'Audio output: Speakers',
      'toast.queue': 'Play queue · {n} tracks',
      'toast.demoStarted': 'Auto demo started', 'toast.demoStopped': 'Auto demo stopped',
      'toast.folderMock': 'The prototype does not access the local file system', 'toast.updateMock': 'Update flow is simulated in this prototype',

      'anno.title': 'FLUIDBAR · LIVE SPEC', 'anno.shell': 'SHELL', 'anno.width': 'W', 'anno.height': 'H',
      'anno.radius': 'R', 'anno.size': 'SIZE', 'anno.radiusLabel': 'RADIUS', 'anno.alpha': 'CONTENT α',
      'anno.spring': 'EXPAND SPRING', 'anno.kc': 'k / c', 'anno.settle': 'SETTLE', 'anno.tokens': 'TOKENS'
    }
  };

  var t = function (key, vars) {
    var s = (I18N[S.lang] && I18N[S.lang][key]) || I18N.zh[key] || key;
    if (vars) {
      Object.keys(vars).forEach(function (k) {
        s = s.replace(new RegExp('\\{' + k + '\\}', 'g'), vars[k]);
      });
    }
    return s;
  };
  var loc = function (v) {
    if (v && typeof v === 'object' && !Array.isArray(v)) return v[S.lang] != null ? v[S.lang] : (v.zh || '');
    return v;
  };

  var MONTHS_EN = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
  var WEEK_FULL_ZH = ['星期日', '星期一', '星期二', '星期三', '星期四', '星期五', '星期六'];
  var WEEK_SHORT_ZH = ['日', '一', '二', '三', '四', '五', '六'];
  var WEEK_FULL_EN = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

  function idleDate() {
    return S.lang === 'zh' ? '10/18 周日' : 'OCT 18';
  }
  function homeDate() {
    return S.lang === 'zh'
      ? '2026 年 10 月 18 日 · 星期日'
      : 'Sunday, October 18, 2026';
  }
  function sceneDate() {
    return S.lang === 'zh' ? '2026/10/18' : '10/18/2026';
  }
  function weekdayLabels() {
    return t('widgets.weekdays').split(',');
  }
  function monthLabel(year, month) {
    return S.lang === 'zh' ? (year + ' 年 ' + (month + 1) + ' 月') : (MONTHS_EN[month] + ' ' + year);
  }

  /* ==========================================================================
     02. DEMO DATA
     ========================================================================== */
  var TRACKS = [
    { title: 'Afterglow', artist: 'Nova Lane', album: 'Midnight Bloom', art: 0, accent: '#F06AB0', duration: 222, initials: 'NL' },
    { title: 'Neon Tide', artist: 'Kairo', album: 'Soft Static', art: 1, accent: '#38D6D6', duration: 198, initials: 'KA' },
    { title: 'Paper Planes', artist: 'Mira & The Weeks', album: 'Sunday Light', art: 2, accent: '#F2B35E', duration: 245, initials: 'MW' }
  ];

  var NOTIFICATIONS = [
    {
      id: 'wechat', appIcon: 'chat', color: '#07C160',
      app: { zh: '微信', en: 'WeChat' }, title: { zh: '李维', en: 'Wei Li' },
      body: { zh: '明天下午的评审会改到 3 点，可以吗？', en: 'Can we move tomorrow afternoon’s review to 3 PM?' },
      time: { zh: '刚刚', en: 'Just now' }
    },
    {
      id: 'outlook', appIcon: 'mail', color: '#2B7CD3',
      app: { zh: 'Outlook', en: 'Outlook' }, title: { zh: '产品周会', en: 'Product weekly' },
      body: { zh: '会议纪要已发送至你的邮箱', en: 'Meeting notes have been sent to your inbox' },
      time: { zh: '2 分钟前', en: '2 min ago' }
    },
    {
      id: 'github', appIcon: 'github', color: '#3D444D',
      app: { zh: 'GitHub', en: 'GitHub' }, title: { zh: 'doulor/fluidbar', en: 'doulor/fluidbar' },
      body: { zh: 'PR #128 已通过全部检查', en: 'PR #128 passed all checks' },
      time: { zh: '12 分钟前', en: '12 min ago' }
    },
    {
      id: 'system', appIcon: 'battery', color: '#5B6270',
      app: { zh: '系统', en: 'System' }, title: { zh: '电池', en: 'Battery' },
      body: { zh: '剩余 18%，建议接通电源', en: '18% remaining — plug in your charger' },
      time: { zh: '1 小时前', en: '1 hr ago' }
    },
    {
      id: 'claude', appIcon: 'claude', color: '#C96442',
      app: { zh: 'Claude Code', en: 'Claude Code' }, title: { zh: 'Agent 已完成', en: 'Agent finished' },
      body: { zh: '重构 settings 模块', en: 'Refactor the settings module' },
      time: { zh: '5 分钟前', en: '5 min ago' }
    }
  ];

  var CLIP_ITEMS = [
    {
      id: 'gh', type: 'link', icon: 'link', colorType: 't-link', pinned: true,
      content: { zh: 'github.com/doulor/fluidbar/pull/128', en: 'github.com/doulor/fluidbar/pull/128' },
      raw: 'https://github.com/doulor/fluidbar/pull/128', mono: false,
      time: { zh: '2 分钟前', en: '2 min ago' }
    },
    {
      id: 'shot', type: 'image', icon: 'image', colorType: 't-image', pinned: false,
      content: { zh: 'Screenshot_2026-10-18_143022.png', en: 'Screenshot_2026-10-18_143022.png' },
      raw: 'Screenshot_2026-10-18_143022.png', mono: false,
      time: { zh: '14 分钟前', en: '14 min ago' }
    },
    {
      id: 'build', type: 'command', icon: 'terminal', colorType: 't-command', pinned: false,
      content: { zh: 'dotnet build -c Release', en: 'dotnet build -c Release' },
      raw: 'dotnet build -c Release', mono: true,
      time: { zh: '32 分钟前', en: '32 min ago' }
    },
    {
      id: 'chat', type: 'text', icon: 'chat', colorType: 't-text', pinned: false,
      content: { zh: '今天先到这，明天继续对一下动效参数。', en: 'Let’s stop here today — we can tune the motion curve tomorrow.' },
      raw: '今天先到这，明天继续对一下动效参数。', mono: false,
      time: { zh: '1 小时前', en: '1 hr ago' }
    },
    {
      id: 'code', type: 'code', icon: 'code', colorType: 't-code', pinned: false,
      content: { zh: 'const spring = new Spring({ T: 0.36, zeta: 0.82 })', en: 'const spring = new Spring({ T: 0.36, zeta: 0.82 })' },
      raw: 'const spring = new Spring({ T: 0.36, zeta: 0.82 })', mono: true,
      time: { zh: '2 小时前', en: '2 hr ago' }
    }
  ];

  var AGENTS = [
    {
      id: 'a1', tool: 'Claude Code', icon: 'claude', color: '#C96442', project: 'fluidbar-v2',
      task: { zh: '重构 island renderer', en: 'Refactor island renderer' }, status: 'running', duration: '12:34'
    },
    {
      id: 'a2', tool: 'Codex', icon: 'codex', color: '#7A828F', project: 'i18n-kit',
      task: { zh: '生成 i18n 资源', en: 'Generate i18n resources' }, status: 'done', duration: '2:10'
    },
    {
      id: 'a3', tool: 'Claude Code', icon: 'claude', color: '#C96442', project: 'market-api',
      task: { zh: '插件市场 API 设计', en: 'Design marketplace API' }, status: 'done', duration: '5:44'
    },
    {
      id: 'a4', tool: 'Codex', icon: 'codex', color: '#7A828F', project: 'fluidbar-v2',
      task: { zh: '修复 DPI 布局', en: 'Fix DPI layout' }, status: 'error', duration: '0:38'
    }
  ];

  var ALL_PLUGINS = [
    {
      id: 'weather', name: 'plugin.weather.name', desc: 'plugin.weather.desc', version: '2.1.0',
      author: 'FluidBar Lab', builtin: true, verified: true, icon: 'cloudSun', color: '#2E7FA8',
      permissions: ['network'], category: 'productivity', rating: 4.8, downloads: '48.2k', installedByDefault: true
    },
    {
      id: 'timer', name: 'plugin.timer.name', desc: 'plugin.timer.desc', version: '2.0.0',
      author: 'FluidBar Lab', builtin: true, verified: true, icon: 'timer', color: '#A86428',
      permissions: [], category: 'productivity', rating: 4.7, downloads: '31.5k', installedByDefault: true
    },
    {
      id: 'github', name: 'plugin.github.name', desc: 'plugin.github.desc', version: '1.6.3',
      author: 'doulor', builtin: false, verified: true, icon: 'github', color: '#3D444D',
      permissions: ['network'], category: 'developer', rating: 4.9, downloads: '26.8k', installedByDefault: true
    },
    {
      id: 'sysmon', name: 'plugin.sysmon.name', desc: 'plugin.sysmon.desc', version: '2.0.4',
      author: 'FluidBar Lab', builtin: true, verified: true, icon: 'cpu', color: '#455466',
      permissions: [], category: 'system', rating: 4.6, downloads: '39.1k', installedByDefault: true
    },
    {
      id: 'lyrics', name: 'plugin.lyrics.name', desc: 'plugin.lyrics.desc', version: '0.9.8',
      author: 'Nova Studio', builtin: false, verified: true, icon: 'spotify', color: '#1E7A46',
      permissions: ['media', 'network'], category: 'media', rating: 4.5, downloads: '18.4k', installedByDefault: true
    },
    {
      id: 'calendar', name: 'plugin.calendar.name', desc: 'plugin.calendar.desc', version: '1.8.0',
      author: 'Mori Labs', builtin: false, verified: true, icon: 'calendar', color: '#8A5A2E',
      permissions: ['notifications'], category: 'productivity', rating: 4.7, downloads: '22.6k', installedByDefault: false
    },
    {
      id: 'stocks', name: 'plugin.stocks.name', desc: 'plugin.stocks.desc', version: '1.1.4',
      author: 'Kite Systems', builtin: false, verified: false, icon: 'chart', color: '#3C6652',
      permissions: ['network'], category: 'productivity', rating: 4.3, downloads: '9.7k', installedByDefault: false
    },
    {
      id: 'agent-bridge', name: 'plugin.agent-bridge.name', desc: 'plugin.agent-bridge.desc', version: '2.0.1',
      author: 'FluidBar Lab', builtin: false, verified: true, icon: 'agent', color: '#A65336',
      permissions: ['network', 'notifications'], category: 'ai', rating: 4.9, downloads: '12.8k', installedByDefault: false
    },
    {
      id: 'clipboard-plus', name: 'plugin.clipboard-plus.name', desc: 'plugin.clipboard-plus.desc', version: '1.2.0',
      author: 'doulor', builtin: false, verified: true, icon: 'clipboard', color: '#3A5B86',
      permissions: ['clipboard', 'notifications'], category: 'system', rating: 4.6, downloads: '15.3k', installedByDefault: true
    },
    {
      id: 'layout', name: 'plugin.layout.name', desc: 'plugin.layout.desc', version: '1.0.2',
      author: 'Rossi Works', builtin: false, verified: false, icon: 'layout', color: '#4E4A68',
      permissions: [], category: 'system', rating: 4.4, downloads: '7.2k', installedByDefault: false
    }
  ];

  var PERM_ICONS = { network: 'globe', clipboard: 'clipboard', media: 'music', notifications: 'bell' };
  var ACCENTS = {
    aqua: '#4CC9F0', mint: '#34C759', amber: '#FF9500', rose: '#FF6B8A', blue: '#0A84FF'
  };

  var DOCK_APPS = [
    { id: 'music', icon: 'music', tile: 'tile-music', label: 'app.music', tab: 'media', running: true },
    { id: 'notifications', icon: 'bell', tile: 'tile-notifications', label: 'app.notifications', tab: 'notifications', running: true },
    { id: 'clipboard', icon: 'clipboard', tile: 'tile-clipboard', label: 'app.clipboard', tab: 'clipboard', running: false },
    { id: 'agent', icon: 'agent', tile: 'tile-agent', label: 'app.agent', tab: 'agent', running: true },
    { id: 'widgets', icon: 'widgets', tile: 'tile-widgets', label: 'app.widgets', tab: 'widgets', running: false },
    { id: 'files', icon: 'folder', tile: 'tile-files', label: 'app.files', tab: 'home', running: false },
    { id: 'terminal', icon: 'terminal', tile: 'tile-terminal', label: 'app.terminal', tab: 'home', running: false },
    { id: 'settings', icon: 'settings', tile: 'tile-settings', label: 'app.settings', tab: 'home', running: false }
  ];

  var TASKBAR_APPS = [
    { id: 'start', icon: 'windows', tile: 'tile-start', label: 'app.start' },
    { id: 'search', icon: 'search', tile: 'tile-search', label: 'app.search' },
    { id: 'widgets', icon: 'widgets', tile: 'tile-widgets', label: 'app.widgets' },
    { id: 'explorer', icon: 'folder', tile: 'tile-explorer', label: 'app.files', running: true },
    { id: 'terminal', icon: 'terminal', tile: 'tile-terminal', label: 'app.terminal', running: true },
    { id: 'music', icon: 'music', tile: 'tile-music', label: 'app.music', running: true },
    { id: 'settings', icon: 'settings', tile: 'tile-settings', label: 'app.settings' }
  ];

  var TAB_META = [
    { id: 'home', label: 'tab.home', icon: 'home' },
    { id: 'media', label: 'tab.media', icon: 'music' },
    { id: 'notifications', label: 'tab.notifications', icon: 'bell' },
    { id: 'clipboard', label: 'tab.clipboard', icon: 'clipboard' },
    { id: 'agent', label: 'tab.agent', icon: 'agent' },
    { id: 'widgets', label: 'tab.widgets', icon: 'widgets' }
  ];

  var DEFAULTS = {
    expandT: 0.36, expandZ: 0.82,
    collapseT: 0.30, collapseZ: 1.00,
    morphT: 0.28, morphZ: 0.85,
    hoverT: 0.24, hoverZ: 0.70,
    pressT: 0.18, pressZ: 0.65,
    nudgeT: 0.26, nudgeZ: 0.55
  };

  var COMPACT = {
    idle: { w: 132, h: 36 },
    media: { w: 226, h: 40 },
    notifications: { w: 248, h: 36 },
    hud: { w: 208, h: 40 },
    clipboard: { w: 264, h: 36 },
    agent: { w: 236, h: 36 },
    timer: { w: 190, h: 36 },
    battery: { w: 208, h: 36 }
  };

  var EXPANDED_W = 440;
  /* One shell size for every expanded section — sections never resize the island. */
  var EXPANDED_H = 284;

  /* ==========================================================================
     03. APPLICATION STATE
     ========================================================================== */
  var S = {
    lang: 'zh', theme: 'dark', material: 'obsidian', mode: 'float', motion: 'normal',
    view: 'idle', expanded: false, tab: 'home', peek: false,
    unread: 3, stack: 1, latestNotif: 0, dismissed: new Set(), cleared: false,
    playing: true, track: 0, progress: 74, playStart: 0,
    volume: 62, brightness: 78, hudType: 'volume',
    timerRemaining: 1500, timerTotal: 1500, timerRunning: false,
    agentStatus: null, dragX: 0, anchor: 'center',
    accentKey: 'aqua', radius: 28, opacity: 96, fontScale: 100,
    hudPrevView: 'idle',
    settingsOpen: false, settingsTab: 'general', settingsScroll: 0,
    installedPlugins: new Set(ALL_PLUGINS.filter(function (p) { return p.installedByDefault; }).map(function (p) { return p.id; })),
    pluginEnabled: {}, installing: {}, marketCat: 'all', marketQuery: '', pluginQuery: '',
    startup: true, autoUpdate: true, trayIcon: true, devMode: false, hoverPreview: true,
    strategy: 'stack', autoCollapse: 2.6, motionStrength: 1, heightLag: 0.08,
    motionParams: Object.assign({}, DEFAULTS),
    annotations: false, consoleCollapsed: false, autoDemo: false,
    activeTrigger: null, navFocus: null, confirmReset: false, updateState: 'idle',
    calYear: 2026, calMonth: 9, toggles: { wifi: true, bluetooth: true, focus: false },
    dockOpen: false, dockActiveApp: null, shellPhase: 'compact',
    lastLargeH: EXPANDED_H, boot: true
  };
  ALL_PLUGINS.forEach(function (p) { S.pluginEnabled[p.id] = true; });

  var ISLAND = null, ANCHOR = null, COMPACT_LAYER = null, PANEL = null, RAIL = null;
  var ghosts = [], stackBadge = null, expandedLayer = null;
  var consoleEl = null, consoleHandle = null, settingsWindow = null, modalLayer = null;
  var toastEl = null, annotationsEl = null, measureSvg = null, specCard = null, measureTags = null;
  var dockEl = null, taskbarEl = null, sceneEl = null;
  var fpsEl = null;

  /* ==========================================================================
     04. SPRING ENGINE + SHELL GEOMETRY
     ========================================================================== */

  /**
   * Spring authored as period T and damping ratio ζ.
   * Integrates with a fixed 1/300s substep. Returns true while awake.
   */
  function Spring(value, T, z) {
    this.x = value;
    this.target = value;
    this.v = 0;
    this.lastDt = 1 / 60;
    this.setParams(T, z);
  }
  Spring.prototype.setParams = function (T, z) {
    this.T = T;
    this.z = z;
    this.k = Math.pow((2 * Math.PI) / T, 2);
    this.c = (4 * Math.PI * z) / T;
  };
  Spring.prototype.retarget = function (target, T, z) {
    if (T && z) this.setParams(T, z);
    var delta = target - this.x;
    if (Math.abs(delta) < 0.01 && Math.abs(this.v) < 0.01) {
      this.x = target;
      this.v = 0;
      this.target = target;
      return;
    }
    var sameDirection = Math.abs(this.v) < 0.001 || Math.sign(this.v) === Math.sign(delta);
    if (!sameDirection) this.v *= 0.35;
    var dt = Math.max(this.lastDt, 1 / 240);
    var maxV = (0.2 * Math.abs(delta)) / dt;
    if (Math.abs(this.v) > maxV) this.v = Math.sign(this.v) * maxV;
    this.target = target;
  };
  Spring.prototype.snap = function (value) {
    this.x = value;
    this.target = value;
    this.v = 0;
  };
  Spring.prototype.step = function (dt) {
    this.lastDt = dt;
    var remaining = dt;
    var sub = 1 / 300;
    while (remaining > 1e-7) {
      var h = Math.min(sub, remaining);
      var a = -this.k * (this.x - this.target) - this.c * this.v;
      this.v += a * h;
      this.x += this.v * h;
      remaining -= h;
    }
    if (Math.abs(this.target - this.x) < 0.04 && Math.abs(this.v) < 0.35) {
      this.x = this.target;
      this.v = 0;
      return false;
    }
    return true;
  };

  var widthSpring = new Spring(132, DEFAULTS.expandT, DEFAULTS.expandZ);
  var heightSpring = new Spring(36, DEFAULTS.expandT, DEFAULTS.expandZ);
  var radiusSpring = new Spring(18, 0.3, 0.9);
  var pressSpring = new Spring(1, DEFAULTS.pressT, DEFAULTS.pressZ);
  var nudgeSpring = new Spring(0, DEFAULTS.nudgeT, DEFAULTS.nudgeZ);

  var rafId = 0, lastTs = 0, fpsLast = 60, fpsAccum = 0, fpsCount = 0;
  var fpsText = null;

  function motionAdjust(T, z) {
    if (S.motion === 'reduced') return { T: T * 0.5, z: 1 };
    var strength = S.motionStrength || 1;
    return { T: Math.max(0.1, T * (1.15 - 0.15 * strength)), z: z };
  }
  function toks(kind) {
    var P = S.motionParams;
    if (kind === 'expand') return motionAdjust(P.expandT, P.expandZ);
    if (kind === 'expandH') return motionAdjust(P.expandT * (1 + S.heightLag), clamp(P.expandZ + 0.04, 0.2, 1.2));
    if (kind === 'collapse') return motionAdjust(P.collapseT, P.collapseZ);
    if (kind === 'morph') return motionAdjust(P.morphT, P.morphZ);
    if (kind === 'hover') return motionAdjust(P.hoverT, P.hoverZ);
    if (kind === 'press') return motionAdjust(P.pressT, P.pressZ);
    if (kind === 'nudge') return motionAdjust(P.nudgeT, P.nudgeZ);
    return motionAdjust(P.expandT, P.expandZ);
  }

  function targetSize() {
    if (S.expanded) {
      return { w: EXPANDED_W, h: EXPANDED_H, expanded: true };
    }
    if (S.mode === 'dock') {
      return { w: EXPANDED_W, h: 46, expanded: false };
    }
    var m = COMPACT[S.view] || COMPACT.idle;
    var peek = S.peek ? 1 : 0;
    return { w: m.w + (peek ? 10 : 0), h: m.h + (peek ? 6 : 0), expanded: false };
  }

  function maxDrag(width) {
    return Math.max(0, window.innerWidth / 2 - width / 2 - 24);
  }
  function clampDragForWidth(width) {
    var max = maxDrag(width);
    if (Math.abs(S.dragX) > max) {
      S.dragX = Math.sign(S.dragX) * max;
      applyDragX(true);
    }
  }
  function applyDragX(transit) {
    if (!ANCHOR) return;
    ANCHOR.style.setProperty('--drag-x', (S.dragX || 0) + 'px');
    ANCHOR.classList.toggle('settling', !!transit);
    if (transit) {
      window.setTimeout(function () { ANCHOR.classList.remove('settling'); }, 380);
    }
  }

  function largeAlphaFor(h, hLarge) {
    return clamp((h - 0.62 * hLarge) / (0.35 * hLarge), 0, 1);
  }

  function computeAlpha(t) {
    var h = heightSpring.x;
    if (S.shellPhase === 'expanded') return 1;
    if (S.shellPhase === 'expanding') return largeAlphaFor(h, t.h);
    if (S.shellPhase === 'collapsing') return largeAlphaFor(h, S.lastLargeH);
    return 0;
  }

  function applyShellFrame() {
    if (!ISLAND) return;
    var tgt = targetSize();
    var w = widthSpring.x;
    var h = heightSpring.x;
    var alpha = computeAlpha(tgt);
    var compactAlpha = clamp(1 - alpha * 1.9, 0, 1);

    ISLAND.style.width = w.toFixed(2) + 'px';
    ISLAND.style.height = h.toFixed(2) + 'px';

    if (S.mode !== 'flush') {
      var r = radiusSpring.x;
      ISLAND.style.borderRadius = r.toFixed(2) + 'px';
      if (ANCHOR) ANCHOR.style.setProperty('--shell-radius', r.toFixed(2) + 'px');
    } else {
      ISLAND.style.borderRadius = '';
      if (ANCHOR) ANCHOR.style.setProperty('--shell-radius', '0 0 22px 22px');
    }

    var press = S.expanded ? 1 : pressSpring.x;
    var nudge = S.expanded ? 0 : nudgeSpring.x;
    ISLAND.style.transform = 'translateY(' + nudge.toFixed(2) + 'px) scale(' + press.toFixed(4) + ')';
    ISLAND.style.setProperty('--content-alpha', alpha.toFixed(3));
    ISLAND.style.setProperty('--compact-alpha', compactAlpha.toFixed(3));

    updateStackVisibility(w, h);
    if (S.annotations) updateAnnotations();
  }

  function wake() {
    if (!rafId) {
      lastTs = 0;
      rafId = requestAnimationFrame(loop);
    }
  }

  function noteFrame(dt) {
    /* Ignore stalls (screenshots, tab switches) so the readout reflects render cadence. */
    if (dt > 0.028) return;
    fpsAccum += dt;
    fpsCount++;
    if (fpsCount >= 10) {
      fpsLast = clamp(Math.round(fpsCount / fpsAccum), 24, 120);
      fpsAccum = 0;
      fpsCount = 0;
      updateFPS();
    }
  }

  function loop(ts) {
    rafId = 0;
    if (!lastTs) lastTs = ts;
    var dt = clamp((ts - lastTs) / 1000, 0.001, 0.05);
    lastTs = ts;

    var awake = false;
    if (widthSpring.step(dt)) awake = true;
    if (heightSpring.step(dt)) awake = true;
    if (radiusSpring.step(dt)) awake = true;
    if (pressSpring.step(dt)) awake = true;
    if (nudgeSpring.step(dt)) awake = true;

    applyShellFrame();
    noteFrame(dt);

    if (awake) {
      rafId = requestAnimationFrame(loop);
    } else {
      lastTs = 0;
      S.shellPhase = S.expanded ? 'expanded' : 'compact';
      applyShellFrame();
    }
  }

  function updateFPS() {
    if (!fpsText) fpsText = $('[data-fps]');
    if (fpsText) fpsText.textContent = String(fpsLast);
  }

  function syncTargets(kind, opts) {
    opts = opts || {};
    var tgt = targetSize();
    if (tgt.expanded) S.lastLargeH = tgt.h;
    var wT, hT;
    if (kind === 'expand') {
      wT = toks('expand');
      hT = toks('expandH');
      if (!opts.instant) S.shellPhase = 'expanding';
    } else if (kind === 'collapse') {
      wT = toks('collapse');
      hT = toks('collapse');
      if (!opts.instant) S.shellPhase = 'collapsing';
    } else if (kind === 'hover') {
      wT = toks('hover');
      hT = toks('hover');
    } else {
      wT = toks('morph');
      hT = toks('morph');
    }

    var rTarget = S.expanded ? S.radius : Math.min(targetSize().h / 2, 44);
    var instant = !!opts.instant || kind === 'snap';

    if (instant) {
      widthSpring.snap(tgt.w);
      heightSpring.snap(tgt.h);
      radiusSpring.snap(rTarget);
      pressSpring.snap(1);
      nudgeSpring.snap(0);
      S.shellPhase = S.expanded ? 'expanded' : 'compact';
      applyShellFrame();
      return;
    }

    widthSpring.retarget(tgt.w, wT.T, wT.z);
    heightSpring.retarget(tgt.h, hT.T, hT.z);
    radiusSpring.retarget(rTarget, toks('morph').T, 0.9);
    if (tgt.expanded || kind === 'expand') clampDragForWidth(tgt.w);
    wake();
  }

  /* ==========================================================================
     05. SCENE / TASKBAR / DOCK
     ========================================================================== */
  function appTile(app) {
    return '<span class="app-tile ' + app.tile + '">' + icon(app.icon, 15) + '</span>';
  }

  function renderScene() {
    var desktop = $('#desktopIcons');
    if (desktop) {
      desktop.innerHTML =
        '<div class="desktop-icon"><span class="di-glyph">' + icon('windows', 30) + '</span><span class="di-label">' + t('scene.computer') + '</span></div>' +
        '<div class="desktop-icon"><span class="di-glyph">' + icon('trash', 30) + '</span><span class="di-label">' + t('scene.recycle') + '</span></div>' +
        '<div class="desktop-icon"><span class="di-glyph">' + icon('islandIcon', 30) + '</span><span class="di-label">' + t('scene.fluidbar') + '</span></div>';
    }

    var exFiles = [
      { icon: 'folder', cls: 'folder', name: t('scene.folderFluidbar') },
      { icon: 'folder', cls: 'folder', name: t('scene.folderDesign') },
      { icon: 'folder', cls: 'folder', name: t('scene.folderSrc') },
      { icon: 'file', cls: 'code', name: t('scene.fileReadme') },
      { icon: 'file', cls: 'code', name: t('scene.fileSolution') },
      { icon: 'image', cls: 'image', name: t('scene.fileIcons') }
    ];
    var nav = [
      { icon: 'home', label: t('scene.navHome'), active: true },
      { icon: 'folder', label: t('scene.navProjects') },
      { icon: 'clock', label: t('scene.navRecent') },
      { icon: 'star', label: t('scene.navFavorites') },
      { icon: 'download', label: t('scene.navDownloads') },
      { icon: 'palette', label: t('scene.navDesign') }
    ];

    var winCtrls = '<div class="win-controls">' +
      '<span class="win-btn"><svg width="9" height="9" viewBox="0 0 10 10"><path d="M2 5h6" stroke="currentColor" stroke-width="1" fill="none"/></svg></span>' +
      '<span class="win-btn"><svg width="9" height="9" viewBox="0 0 10 10"><rect x="2" y="2" width="6" height="6" rx=".5" stroke="currentColor" stroke-width="1" fill="none"/></svg></span>' +
      '<span class="win-btn close"><svg width="9" height="9" viewBox="0 0 10 10"><path d="M2 2l6 6M8 2l-6 6" stroke="currentColor" stroke-width="1.2" fill="none"/></svg></span>' +
      '</div>';

    var windows = $('#sceneWindows');
    if (windows) {
      windows.innerHTML =
        '<article class="mock-window explorer-window" aria-hidden="true">' +
          '<div class="win-titlebar">' + icon('folder', 14, 'win-icon') +
            '<span class="win-title">' + t('scene.explorerTitle') + '</span>' + winCtrls +
          '</div>' +
          '<div class="win-tabs">' +
            '<div class="win-tab active">' + icon('folder', 10) + t('scene.explorerTab1') + '</div>' +
            '<div class="win-tab">' + icon('windows', 10) + t('scene.explorerTab2') + '</div>' +
          '</div>' +
          '<div class="explorer-body">' +
            '<aside class="ex-sidebar">' +
              nav.map(function (n) {
                return '<div class="ex-nav' + (n.active ? ' active' : '') + '">' + icon(n.icon, 12) + '<span>' + n.label + '</span></div>';
              }).join('') +
              '<div class="ex-side-label">' + t('scene.navFavorites') + '</div>' +
              '<div class="ex-nav">' + icon('folder', 12) + '<span>' + t('scene.folderFluidbar') + '</span></div>' +
            '</aside>' +
            '<div class="ex-main">' +
              '<div class="ex-crumbbar">' + t('scene.navHome') + '<span class="ex-crumb-sep">›</span>' + t('scene.navProjects') + '<span class="ex-crumb-sep">›</span>' + t('scene.folderFluidbar') + '</div>' +
              '<div class="ex-toolbar">' +
                '<span class="ex-tool">' + icon('chevronLeft', 12) + '</span>' +
                '<span class="ex-tool">' + icon('chevronRight', 12) + '</span>' +
                '<span class="ex-tool">' + icon('search', 12) + '</span>' +
                '<span class="ex-tool">' + icon('refresh', 12) + '</span>' +
              '</div>' +
              '<div class="ex-files">' +
                exFiles.map(function (f) {
                  return '<div class="ex-file ' + f.cls + '">' + icon(f.icon, 26) + '<span>' + f.name + '</span></div>';
                }).join('') +
              '</div>' +
            '</div>' +
          '</div>' +
        '</article>' +
        '<article class="mock-window terminal-window" aria-hidden="true">' +
          '<div class="win-titlebar">' + icon('terminal', 14, 'win-icon') +
            '<span class="win-title">' + t('scene.terminalTitle') + '</span>' + winCtrls +
          '</div>' +
          '<div class="win-tabs">' +
            '<div class="win-tab active">' + icon('terminal', 10) + t('scene.terminalTab') + '</div>' +
            '<div class="win-tab">' + icon('plus', 10) + '</div>' +
          '</div>' +
          '<div class="terminal-body">' +
            '<div class="term-line">PS D:\\build\\GitLocal\\FluidBar&gt; <span class="term-accent">dotnet build -c Release</span></div>' +
            '<div class="term-line dim">' + t('scene.termRestore') + '</div>' +
            '<div class="term-line">FluidBar -&gt; bin\\Release\\net10.0-windows\\FluidBar.dll</div>' +
            '<div class="term-line term-ok">' + t('scene.termBuilt') + '</div>' +
            '<div class="term-line dim">PS D:\\build\\GitLocal\\FluidBar&gt; <span class="term-cursor"></span></div>' +
          '</div>' +
        '</article>';
    }

    var hint = $('#sceneHint');
    if (hint) {
      hint.innerHTML = icon('keyboard', 13) + '<span>' + t(S.mode === 'dock' ? 'scene.hintDock' : 'scene.hint') + '</span>';
    }
  }

  function renderTaskbar() {
    if (!taskbarEl) return;
    taskbarEl.innerHTML =
      '<div class="taskbar-left">' +
        '<div class="tb-weather" aria-hidden="true">' + icon('cloudSun', 15) +
          '<span class="tb-weather-temp">18°</span><span class="tb-weather-desc">' + t('taskbar.weather') + '</span>' +
        '</div>' +
      '</div>' +
      '<div class="taskbar-center">' +
        TASKBAR_APPS.map(function (a) {
          return '<div class="tb-app" data-label="' + esc(t(a.label)) + '" aria-hidden="true">' + appTile(a) +
            (a.running ? '<span class="running-dot"></span>' : '') + '</div>';
        }).join('') +
      '</div>' +
      '<div class="taskbar-right">' +
        '<div class="tray" aria-hidden="true">' +
          '<div class="tray-icons">' + icon('wifi', 14) + icon('volume', 14) + icon('batteryCharging', 14) + '</div>' +
          '<div class="taskbar-clock"><span>14:32</span><span>' + sceneDate() + '</span></div>' +
        '</div>' +
      '</div>';
  }

  function renderDock() {
    if (!dockEl) return;
    dockEl.setAttribute('aria-hidden', S.mode === 'dock' ? 'false' : 'true');
    dockEl.innerHTML =
      DOCK_APPS.map(function (a) {
        return '<button class="dock-app" type="button" data-dock-app="' + a.id + '" data-dock-tab="' + a.tab + '" aria-label="' + esc(t(a.label)) + '">' +
          appTile(a) +
          (a.running ? '<span class="dock-running"></span>' : '') +
          '<span class="dock-label">' + esc(t(a.label)) + '</span>' +
          '</button>';
      }).join('') +
      '<span class="dock-sep"></span>' +
      '<div class="dock-tray">' +
        '<button class="dock-tray-btn" type="button" data-dock-tab="home" data-dock-tray="1" aria-label="' + esc(t('tray.wifi')) + '">' + icon('wifi', 14) + '</button>' +
        '<button class="dock-tray-btn" type="button" data-dock-tab="home" data-dock-tray="1" aria-label="' + esc(t('tray.volume')) + '">' + icon('volume', 14) + '</button>' +
        '<button class="dock-tray-btn" type="button" data-dock-tab="home" data-dock-tray="1" aria-label="' + esc(t('tray.battery')) + '">' + icon('batteryCharging', 14) + '</button>' +
        '<button class="dock-time" type="button" data-dock-tab="home" data-dock-tray="1" aria-label="' + esc(t('tray.clock')) + '"><span>14:32</span><span class="dock-date">' + sceneDate() + '</span></button>' +
      '</div>';
    updateDockActive();
  }

  function updateDockActive() {
    if (!dockEl) return;
    $$('.dock-app', dockEl).forEach(function (b) {
      b.classList.toggle('active', S.dockOpen && S.dockActiveApp === b.dataset.dockApp);
    });
    dockEl.setAttribute('aria-hidden', S.mode === 'dock' ? 'false' : 'true');
  }

  /* ==========================================================================
     06. ISLAND — COMPACT VIEWS
     ========================================================================== */
  function art(track, cls, withLabel) {
    return '<span class="art ' + (cls || '') + '" data-art="' + track.art + '">' +
      '<span class="art-orb"></span><span class="art-orb2"></span>' +
      (withLabel ? '<span class="art-label">' + esc(track.album) + '</span><span class="art-initial">' + esc(track.initials) + '</span>' : '') +
      '</span>';
  }

  function currentTrack() { return TRACKS[S.track]; }

  function progressPct() {
    var tr = currentTrack();
    return clamp((S.progress / tr.duration) * 100, 0, 100);
  }

  function notifById(id) {
    return NOTIFICATIONS.filter(function (n) { return n.id === id; })[0];
  }
  function visibleNotifications() {
    return NOTIFICATIONS.filter(function (n) { return !S.dismissed.has(n.id); });
  }
  function activeNotification() {
    if (S.cleared) return NOTIFICATIONS[0];
    var vis = visibleNotifications();
    if (!vis.length) return NOTIFICATIONS[0];
    return vis[S.latestNotif % vis.length];
  }

  function compactHUD() {
    var isVol = S.hudType === 'volume';
    var val = isVol ? S.volume : S.brightness;
    return '<div class="pill-view pv-hud">' +
      '<span class="hud-icon">' + icon(isVol ? 'volume' : 'brightness', 17) + '</span>' +
      '<span class="hud-label">' + t(isVol ? 'hud.volume' : 'hud.brightness') + '</span>' +
      '<span class="hud-track"><span class="hud-fill" style="width:' + val + '%"></span></span>' +
      '<span class="hud-value">' + val + '%</span>' +
      '</div>';
  }

  function compactViewHTML() {
    var view = S.view;
    if (view === 'media') {
      var tr = currentTrack();
      return '<div class="pill-view pv-media">' +
        art(tr, 'media-art') +
        '<div class="pv-media-copy"><div class="pill-title">' + esc(tr.title) + '</div><div class="pill-sub">' + esc(tr.artist) + '</div></div>' +
        '<div class="wave' + (S.playing ? '' : ' paused') + '">' + '<i></i><i></i><i></i><i></i><i></i>' + '</div>' +
        '<div class="peek-controls">' +
          '<button class="ctrl-btn" type="button" data-act="prev" aria-label="' + t('media.prev') + '">' + icon('prev', 14) + '</button>' +
          '<button class="ctrl-btn" type="button" data-act="next" aria-label="' + t('media.next') + '">' + icon('next', 14) + '</button>' +
        '</div>' +
        '<div class="pv-controls">' +
          '<button class="ctrl-btn play" type="button" data-act="toggle-play" aria-label="' + t(S.playing ? 'media.pause' : 'media.play') + '">' + icon(S.playing ? 'pause' : 'play', 13) + '</button>' +
        '</div>' +
        '<div class="pill-progress"><span style="width:' + progressPct().toFixed(2) + '%"></span></div>' +
        '</div>';
    }
    if (view === 'notifications') {
      var n = activeNotification();
      return '<div class="pill-view pv-notif">' +
        '<span class="app-glyph" style="--app:' + n.color + '">' + icon(n.appIcon, 13) + '</span>' +
        '<span class="pv-notif-copy"><span class="pv-appname">' + esc(loc(n.app)) + '</span><span class="pv-msg">' + esc(loc(n.title)) + ' · ' + esc(loc(n.body)) + '</span></span>' +
        (S.unread > 0 ? '<span class="unread-badge">' + S.unread + '</span>' : '') +
        '</div>';
    }
    if (view === 'clipboard') {
      var item = CLIP_ITEMS[0];
      return '<div class="pill-view pv-clip">' +
        '<span class="clip-pill-icon">' + icon('clipboard', 13) + '</span>' +
        '<span class="clip-copy-label">' + t('clipboard.copied') + '</span>' +
        '<span class="clip-type-dot"></span>' +
        '<span class="clip-preview">' + esc(loc(item.content)) + '</span>' +
        '</div>';
    }
    if (view === 'agent') {
      var ag = S.agentStatus === 'done' ? AGENTS[1] : (S.agentStatus === 'error' ? AGENTS[3] : AGENTS[0]);
      var statusLabel = t('agent.status.' + ag.status);
      return '<div class="pill-view pv-agent">' +
        '<span class="agent-ring ' + ag.status + '">' +
          (ag.status === 'running' ? '' : icon(ag.status === 'done' ? 'check' : 'alert', 10)) +
        '</span>' +
        '<div class="agent-pill-copy"><span class="agent-tool">' + esc(ag.tool) + '</span><span class="agent-task">' + esc(loc(ag.task)) + '</span></div>' +
        '<span class="agent-dur">' + statusLabel + ' · ' + ag.duration + '</span>' +
        '</div>';
    }
    if (view === 'timer') {
      var remain = S.timerRemaining;
      var pctLeft = clamp(remain / S.timerTotal, 0, 1);
      var circ = 2 * Math.PI * 8.4;
      return '<div class="pill-view pv-timer">' +
        '<span class="timer-glyph">' + icon('timer', 17) + '</span>' +
        '<span class="timer-time">' + fmtTime(remain) + '</span>' +
        '<span class="timer-label">' + t('timer.focus') + '</span>' +
        '<span class="timer-ring">' +
          '<svg viewBox="0 0 24 24" fill="none"><circle class="tr-track" cx="12" cy="12" r="8.4" stroke-width="2"/>' +
          '<circle class="tr-fill" cx="12" cy="12" r="8.4" stroke-width="2" stroke-dasharray="' + circ.toFixed(2) + '" stroke-dashoffset="' + (circ * (1 - pctLeft)).toFixed(2) + '"/></svg>' +
        '</span>' +
        '</div>';
    }
    if (view === 'battery') {
      return '<div class="pill-view pv-battery">' +
        '<span class="battery-dot">' + icon('bolt', 14) + '</span>' +
        '<div class="battery-copy"><span class="meta">' + t('battery.plugged') + '</span><span class="clip-type-dot"></span><span class="pct">86%</span></div>' +
        '</div>';
    }
    if (view === 'hud') return compactHUD();
    return '<div class="pill-view pv-idle">' +
      '<span class="idle-clock">14:32</span>' +
      '<span class="idle-dot"></span>' +
      '<span class="idle-date">' + idleDate() + '</span>' +
      '<span class="idle-hint">' + t('island.expand') + icon('chevronRight', 10) + '</span>' +
      '</div>';
  }

  function renderCompact() {
    if (!COMPACT_LAYER) return;
    COMPACT_LAYER.innerHTML = compactViewHTML();
    ISLAND.dataset.view = S.view;
    ISLAND.style.setProperty('--track-accent', currentTrack().accent);
    ISLAND.style.setProperty('--font-scale', (S.fontScale / 100).toFixed(3));
    updateStackVisibility(widthSpring.x, heightSpring.x);
  }

  function renderShellAttrs() {
    if (!ISLAND) return;
    ISLAND.dataset.expanded = S.expanded ? 'true' : 'false';
    ISLAND.dataset.peek = (S.peek && !S.expanded) ? 'true' : 'false';
    ISLAND.setAttribute('aria-expanded', S.expanded ? 'true' : 'false');
    ISLAND.setAttribute('aria-label', S.expanded ? t('island.collapse') : t('island.expand'));
    if (expandedLayer) {
      if (!S.expanded && expandedLayer.contains(document.activeElement)) {
        if (document.activeElement && document.activeElement.blur) document.activeElement.blur();
      }
      expandedLayer.toggleAttribute('inert', !S.expanded);
      expandedLayer.setAttribute('aria-hidden', S.expanded ? 'false' : 'true');
    }
    if (ANCHOR) ANCHOR.dataset.expanded = S.expanded ? 'true' : 'false';
    updateStackVisibility(widthSpring.x, heightSpring.x);
  }

  function updateStackVisibility(w, h) {
    if (!ANCHOR) return;
    var show = S.strategy === 'stack' && S.stack > 1 && !S.expanded && S.mode !== 'dock' && !S.cleared;
    var count = Math.min(S.stack, 3);
    ANCHOR.dataset.stack = show ? String(count) : '0';
    if (stackBadge) {
      stackBadge.hidden = !(show && count > 1);
      if (show) stackBadge.textContent = '+' + (count - 1);
    }
    if (ghosts[0]) {
      var gw = Math.max(80, w - 18);
      ghosts[0].style.width = gw.toFixed(2) + 'px';
      ghosts[0].style.height = Math.max(20, h).toFixed(2) + 'px';
      ghosts[0].style.top = '8px';
    }
    if (ghosts[1]) {
      var gw2 = Math.max(72, w - 36);
      ghosts[1].style.width = gw2.toFixed(2) + 'px';
      ghosts[1].style.height = Math.max(18, h).toFixed(2) + 'px';
      ghosts[1].style.top = '17px';
    }
  }

  /* ==========================================================================
     07. EXPANDED PANEL
     ========================================================================== */
  var RAIL_BTN = 34;
  var RAIL_GAP = 8;

  function syncRailMetrics() {
    /* Fixed metrics on every section: 6 × 34px buttons + 5 × 8px gaps = 244px,
       exactly the content height of the 440×284 shell (284 − 2 × 20 padding). */
    return { contentH: EXPANDED_H - 40, btn: RAIL_BTN, gap: RAIL_GAP };
  }

  function sectionTitle(tab) {
    if (tab === 'notifications') return t('notifications.title');
    if (tab === 'clipboard') return t('clipboard.title');
    if (tab === 'agent') return t('agent.title');
    return t('tab.' + tab);
  }

  function renderRail() {
    if (!RAIL) return;
    RAIL.setAttribute('aria-label', t('rail.label'));
    RAIL.innerHTML =
      '<span class="rail-indicator" id="railIndicator" aria-hidden="true"></span>' +
      TAB_META.map(function (tab) {
        return '<button class="rail-btn" type="button" data-tab="' + tab.id + '" aria-label="' + t(tab.label) + '">' +
          icon(tab.icon, 17) +
          '<span class="rail-tip" aria-hidden="true">' + t(tab.label) + '</span>' +
          '</button>';
      }).join('');
    updateRail(true);
  }

  function updateRail(instant) {
    if (!RAIL) return;
    var metrics = syncRailMetrics();
    var idx = TAB_META.findIndex(function (m) { return m.id === S.tab; });
    if (idx < 0) idx = 0;
    var btns = $$('.rail-btn', RAIL);
    btns.forEach(function (b) {
      var active = b.dataset.tab === S.tab;
      b.classList.toggle('active', active);
      if (active) b.setAttribute('aria-current', 'page');
      else b.removeAttribute('aria-current');
    });
    var indicator = $('#railIndicator', RAIL);
    if (!indicator) return;
    if (instant) indicator.classList.remove('ready');
    var stack = TAB_META.length * metrics.btn + (TAB_META.length - 1) * metrics.gap;
    var top = (metrics.contentH - stack) / 2 + idx * (metrics.btn + metrics.gap) + (metrics.btn - 18) / 2;
    indicator.style.top = Math.max(0, top).toFixed(2) + 'px';
    indicator.style.height = '18px';
    if (instant) {
      requestAnimationFrame(function () { indicator.classList.add('ready'); });
    } else {
      indicator.classList.add('ready');
    }
  }

  function panelHeadHTML() {
    var title = sectionTitle(S.tab);
    var count = '';
    var action = '';
    if (S.tab === 'notifications') {
      count = S.unread > 0 ? t('notifications.unread', { n: S.unread }) : t('notifications.allRead');
      if (visibleNotifications().length) {
        action = '<button class="text-btn" type="button" data-act="clear-notifs">' + t('notifications.clear') + '</button>';
      }
    } else if (S.tab === 'clipboard') {
      count = t('clipboard.count', { n: sortedClipItems().length });
    } else if (S.tab === 'agent') {
      var running = AGENTS.filter(function (a) { return a.status === 'running'; }).length;
      count = t('agent.count', { n: running });
    }
    return '<div class="panel-head">' +
      '<span class="panel-title">' + title + '</span>' +
      (count ? '<span class="panel-count">' + count + '</span>' : '') +
      '<span class="panel-head-actions">' + action +
        '<button class="panel-close" type="button" data-act="collapse" aria-label="' + t('island.collapse') + '" title="' + t('island.collapse') + '">' +
          icon(S.mode === 'dock' ? 'chevronDown' : 'chevronUp', 15) +
        '</button>' +
      '</span>' +
    '</div>';
  }

  function homeHTML() {
    var tr = currentTrack();
    var q = S.toggles;
    var dark = S.theme === 'dark';
    var quick = [
      { id: 'wifi', icon: 'wifi', label: t('home.quickWifi'), on: q.wifi },
      { id: 'bluetooth', icon: 'bluetooth', label: t('home.quickBluetooth'), on: q.bluetooth },
      { id: 'focus', icon: 'moon', label: t('home.quickFocus'), on: q.focus },
      { id: 'dark', icon: dark ? 'moon' : 'sun', label: t('home.quickDark'), on: dark }
    ];
    return '<div class="pane home-pane">' +
      '<div class="home-head">' +
        '<div class="home-clock-wrap"><div class="home-time">14:32</div><div class="home-date">' + homeDate() + '</div></div>' +
        '<div class="weather-chip">' + icon('cloudSun', 18) +
          '<div class="weather-chip-main"><span class="weather-temp">18°</span><span class="weather-sub">' + t('home.weather') + '</span></div>' +
        '</div>' +
      '</div>' +
      '<div class="mini-media" data-act="go-media">' +
        art(tr, 'mini-art') +
        '<div class="mini-copy"><div class="mini-title">' + esc(tr.title) + '</div><div class="mini-sub">' + esc(tr.artist) + ' · ' + esc(tr.album) + '</div></div>' +
        '<button class="mini-play" type="button" data-act="toggle-play" aria-label="' + t(S.playing ? 'media.pause' : 'media.play') + '">' + icon(S.playing ? 'pause' : 'play', 12) + '</button>' +
        '<div class="mini-progress"><span style="width:' + progressPct().toFixed(2) + '%"></span></div>' +
      '</div>' +
      '<div class="quick-grid">' +
        quick.map(function (x) {
          return '<button class="q-toggle' + (x.on ? ' on' : '') + '" type="button" data-act="quick" data-quick="' + x.id + '" aria-pressed="' + x.on + '">' +
            icon(x.icon, 14) + '<span class="q-label">' + x.label + '</span></button>';
        }).join('') +
      '</div>' +
      '<div class="slider-card">' +
        '<div class="slider-cell">' + icon('volume', 13) +
          '<input class="fb-range" type="range" min="0" max="100" value="' + S.volume + '" data-act="volume-range" aria-label="' + t('hud.volume') + '" style="--val:' + S.volume + '%">' +
          '<span class="slider-pct">' + S.volume + '</span>' +
        '</div>' +
        '<span class="slider-divider"></span>' +
        '<div class="slider-cell">' + icon('brightness', 13) +
          '<input class="fb-range" type="range" min="0" max="100" value="' + S.brightness + '" data-act="brightness-range" aria-label="' + t('hud.brightness') + '" style="--val:' + S.brightness + '%">' +
          '<span class="slider-pct">' + S.brightness + '</span>' +
        '</div>' +
      '</div>' +
      '<div class="battery-chip">' +
        '<span class="batt-visual"><span class="batt-fill"></span></span>' +
        '<strong>86%</strong><span class="batt-meta">· ' + t('home.charging') + '</span>' +
        '<span class="batt-right">' + t('home.remaining') + '</span>' +
      '</div>' +
      '</div>';
  }

  function mediaHTML() {
    var tr = currentTrack();
    var cur = fmtTime(S.progress);
    var remaining = fmtTime(Math.max(0, tr.duration - S.progress));
    var lyric = t('media.lyric' + tr.art);
    var nextLyric = t('media.lyricNext' + tr.art);
    return '<div class="pane media-pane">' +
      '<div class="media-top">' +
        art(tr, 'media-art-lg', true) +
        '<div class="media-info">' +
          '<div class="media-eyebrow">' + t('media.nowPlaying') + '</div>' +
          '<div class="media-title">' + esc(tr.title) + '</div>' +
          '<div class="media-artist">' + esc(tr.artist) + '</div>' +
          '<div class="media-album">' + esc(tr.album) + '</div>' +
          '<div class="media-lyric-card">' +
            '<div class="media-lyric-now"><span>' + esc(lyric) + '</span></div>' +
            '<div class="media-lyric-next"><span class="lyric-tag">' + t('media.nextLine') + '</span>' +
              '<span class="lyric-text">' + esc(nextLyric) + '</span></div>' +
          '</div>' +
        '</div>' +
      '</div>' +
      '<div class="media-seek">' +
        '<span class="media-time js-time-current">' + cur + '</span>' +
        '<div class="seek js-seek" role="slider" tabindex="0" aria-label="' + t('media.seek') + '" aria-valuemin="0" aria-valuemax="' + tr.duration + '" aria-valuenow="' + Math.round(S.progress) + '">' +
          '<div class="seek-track"><div class="seek-buffer"></div><div class="seek-fill js-seek-fill" style="width:' + progressPct().toFixed(2) + '%"></div><div class="seek-thumb js-seek-thumb" style="left:' + progressPct().toFixed(2) + '%"></div></div>' +
        '</div>' +
        '<span class="media-time end js-time-remaining">' + remaining + '</span>' +
      '</div>' +
      '<div class="media-footer">' +
        '<div class="media-transport">' +
          '<button class="transport-btn" type="button" data-act="prev" aria-label="' + t('media.prev') + '">' + icon('prev', 15) + '</button>' +
          '<button class="play-btn" type="button" data-act="toggle-play" aria-label="' + t(S.playing ? 'media.pause' : 'media.play') + '">' + icon(S.playing ? 'pause' : 'play', 16) + '</button>' +
          '<button class="transport-btn" type="button" data-act="next" aria-label="' + t('media.next') + '">' + icon('next', 15) + '</button>' +
        '</div>' +
        '<div class="media-output-group">' +
          '<button class="output-btn" type="button" data-act="output">' + icon('speaker', 13) + '<span>' + t('media.speaker') + '</span></button>' +
          '<span class="media-foot-divider"></span>' +
          '<button class="queue-btn" type="button" data-act="queue" aria-label="' + t('media.queue') + '">' + icon('queue', 13) +
            '<span>' + t('media.queue') + '</span><span class="queue-count">· ' + TRACKS.length + '</span></button>' +
        '</div>' +
      '</div>' +
      '</div>';
  }

  function notificationsHTML() {
    var vis = visibleNotifications();
    var list = vis.map(function (n) {
      return '<div class="notif-row" data-act="notif-open" data-id="' + n.id + '" role="button" tabindex="0">' +
        '<span class="app-glyph lg" style="--app:' + n.color + '">' + icon(n.appIcon, 16) + '</span>' +
        '<div class="notif-copy">' +
          '<div class="notif-line">' +
            '<span class="notif-app">' + esc(loc(n.app)) + '</span>' +
            '<span class="notif-title">' + esc(loc(n.title)) + '</span>' +
            '<span class="notif-time">' + esc(loc(n.time)) + '</span>' +
          '</div>' +
          '<div class="notif-text">' + esc(loc(n.body)) + '</div>' +
        '</div>' +
        '<button class="notif-dismiss" type="button" data-act="notif-dismiss" data-id="' + n.id + '" aria-label="' + t('notifications.dismiss') + '">' + icon('close', 10) + '</button>' +
        '<span class="notif-open">' + t('notifications.open') + icon('arrowUpRight', 9) + '</span>' +
        '</div>';
    }).join('');
    return '<div class="pane notif-pane">' +
      (vis.length
        ? '<div class="notif-list scroll-area">' + list + '</div>'
        : '<div class="empty-state">' + icon('bell', 26) + '<div class="empty-title">' + t('notifications.emptyTitle') + '</div><div class="empty-sub">' + t('notifications.emptySub') + '</div></div>') +
      '</div>';
  }

  function sortedClipItems() {
    return CLIP_ITEMS.slice().sort(function (a, b) {
      var pa = S.pinned.has(a.id) ? 1 : 0;
      var pb = S.pinned.has(b.id) ? 1 : 0;
      return pb - pa;
    });
  }

  function clipboardHTML() {
    var items = sortedClipItems();
    var list = items.map(function (it) {
      var pinned = S.pinned.has(it.id);
      return '<div class="clip-row' + (pinned ? ' pinned' : '') + '" data-act="copy" data-id="' + it.id + '" role="button" tabindex="0">' +
        '<span class="clip-badge ' + it.colorType + '">' + icon(it.icon, 12) + '</span>' +
        '<div class="clip-main">' +
          '<div class="clip-text' + (it.mono ? ' mono' : '') + '">' + esc(loc(it.content)) + '</div>' +
          '<div class="clip-subline">' + (pinned ? '<span class="pin-mark">' + icon('pin', 8) + t('clipboard.pinned') + '</span><span>·</span>' : '') +
            '<span>' + t('clipboard.type.' + it.type) + '</span></div>' +
        '</div>' +
        '<span class="clip-time">' + esc(loc(it.time)) + '</span>' +
        '<div class="clip-actions">' +
          '<button class="icon-btn pin-btn" type="button" data-act="pin" data-id="' + it.id + '" aria-label="' + t(pinned ? 'clipboard.unpin' : 'clipboard.pin') + '">' + icon('pin', 12) + '</button>' +
          '<button class="icon-btn" type="button" data-act="copy" data-id="' + it.id + '" aria-label="' + t('clipboard.copy') + '">' + icon('copy', 12) + '</button>' +
        '</div>' +
        '</div>';
    }).join('');
    return '<div class="pane clip-pane">' +
      '<div class="clip-list scroll-area">' + list + '</div>' +
      '</div>';
  }

  function agentHTML() {
    var list = AGENTS.map(function (a) {
      return '<div class="agent-row" data-act="agent-open" data-id="' + a.id + '" role="button" tabindex="0">' +
        '<span class="agent-state ' + a.status + '">' +
          (a.status === 'running' ? '<span class="spinner-ring"></span>' : icon(a.status === 'done' ? 'check' : 'alert', 12)) +
        '</span>' +
        '<div class="agent-main">' +
          '<div class="agent-head"><span class="agent-toolname">' + esc(a.tool) + '</span><span class="agent-project">' + esc(a.project) + '</span></div>' +
          '<div class="agent-task">' + esc(loc(a.task)) + '</div>' +
        '</div>' +
        '<div class="agent-right"><span class="agent-status ' + a.status + '">' + t('agent.status.' + a.status) + '</span><span class="agent-duration">' + a.duration + '</span></div>' +
        (a.status === 'running' ? '<div class="agent-progress"></div>' : '') +
        '</div>';
    }).join('');
    return '<div class="pane agent-pane">' +
      '<div class="agent-list scroll-area">' + list + '</div></div>';
  }

  function calendarHTML() {
    var year = S.calYear, month = S.calMonth;
    var first = new Date(year, month, 1);
    var firstIdx = (first.getDay() + 6) % 7;
    var days = new Date(year, month + 1, 0).getDate();
    var weeks = Math.ceil((firstIdx + days) / 7);
    var cells = weeks * 7;
    var labels = weekdayLabels();
    var today = (year === 2026 && month === 9) ? 18 : -1;
    var out = '';
    for (var i = 0; i < cells; i++) {
      var dayNum = i - firstIdx + 1;
      var muted = dayNum < 1 || dayNum > days;
      var show = muted ? (dayNum < 1 ? (new Date(year, month, 0).getDate() + dayNum) : (dayNum - days)) : dayNum;
      var cls = 'cal-day' + (muted ? ' mute' : '');
      if (!muted && dayNum === today) cls += ' today';
      out += '<span class="' + cls + '">' + show + '</span>';
    }
    return '<div class="cal-card">' +
      '<div class="cal-head"><span class="cal-month">' + monthLabel(year, month) + '</span>' +
        '<span class="cal-nav">' +
          '<button type="button" data-act="cal-prev" aria-label="' + t('common.prevMonth') + '">' + icon('chevronLeft', 11) + '</button>' +
          '<button type="button" data-act="cal-next" aria-label="' + t('common.nextMonth') + '">' + icon('chevronRight', 11) + '</button>' +
        '</span>' +
      '</div>' +
      '<div class="cal-week">' + labels.map(function (d) { return '<span>' + d + '</span>'; }).join('') + '</div>' +
      '<div class="cal-grid">' + out + '</div>' +
      '<div class="cal-foot"><span class="dot-accent"></span><span>' + t('widgets.calEvent') + '</span><span class="cal-today-label">' + t('widgets.calToday') + '</span></div>' +
      '</div>';
  }

  function widgetsHTML() {
    var doneCount = S.todos.filter(function (x) { return x.done; }).length;
    var todos = S.todos.map(function (td, i) {
      return '<button class="todo-row' + (td.done ? ' done' : '') + '" type="button" data-act="todo" data-index="' + i + '">' +
        '<span class="todo-check">' + icon('check', 6) + '</span><span class="todo-label">' + t(td.key) + '</span></button>';
    }).join('');
    return '<div class="pane"><div class="widgets-pane">' +
      calendarHTML() +
      '<div class="widget-side">' +
        '<div class="weather-card">' +
          '<span class="w-icon">' + icon('cloudSun', 17) + '</span>' +
          '<div class="w-copy"><span class="w-temp">18°</span><span class="w-desc">' + t('widgets.weather') + '</span></div>' +
          '<div class="w-meta"><strong>' + t('widgets.highLow') + '</strong><span>' + t('widgets.wind') + '</span></div>' +
        '</div>' +
        '<div class="stats-card">' +
          '<div class="stat-row"><span class="stat-label">' + t('widgets.cpu') + '</span><span class="stat-track"><span class="stat-fill cpu"></span></span><span class="stat-value">' + t('widgets.cpuValue') + '</span></div>' +
          '<div class="stat-row"><span class="stat-label">' + t('widgets.ram') + '</span><span class="stat-track"><span class="stat-fill ram"></span></span><span class="stat-value">' + t('widgets.ramValue') + '</span></div>' +
          '<div class="stat-row"><span class="stat-label">' + t('widgets.battery') + '</span><span class="stat-track"><span class="stat-fill batt"></span></span><span class="stat-value">' + t('widgets.battValue') + '</span></div>' +
        '</div>' +
        '<div class="notes-card">' +
          '<div class="notes-head">' + t('widgets.notesTitle') + '<span>' + t('widgets.notesProgress', { n: doneCount, m: S.todos.length }) + '</span></div>' +
          todos +
        '</div>' +
      '</div>' +
      '</div></div>';
  }

  function renderPane() {
    if (!PANEL) return;
    var html;
    if (S.tab === 'media') html = mediaHTML();
    else if (S.tab === 'notifications') html = notificationsHTML();
    else if (S.tab === 'clipboard') html = clipboardHTML();
    else if (S.tab === 'agent') html = agentHTML();
    else if (S.tab === 'widgets') html = widgetsHTML();
    else html = homeHTML();
    PANEL.innerHTML = panelHeadHTML() + html;
    requestAnimationFrame(updateScrollFades);
  }

  function updateScrollFades() {
    $$('.scroll-area').forEach(function (el) {
      el.classList.toggle('is-overflowing', el.scrollHeight > el.clientHeight + 1);
    });
  }

  /* ==========================================================================
     08. DEMO CONSOLE
     ========================================================================== */
  function segControl(setting, options, current) {
    return '<div class="segmented">' + options.map(function (o) {
      return '<button class="seg' + (current === o[0] ? ' active' : '') + '" type="button" data-set="' + setting + '" data-value="' + o[0] + '">' + o[1] + '</button>';
    }).join('') + '</div>';
  }

  function triggerButton(kind, iconName, labelKey) {
    return '<button class="trigger' + (S.activeTrigger === kind ? ' active' : '') + '" type="button" data-trigger="' + kind + '">' +
      icon(iconName, 13) + '<span>' + t(labelKey) + '</span></button>';
  }

  var TRIGGERS = [
    ['media', 'music', 'console.trig.media'],
    ['pause', 'pause', 'console.trig.pause'],
    ['notification', 'bell', 'console.trig.notification'],
    ['clipboard', 'clipboard', 'console.trig.clipboard'],
    ['volume', 'volume', 'console.trig.volume'],
    ['brightness', 'brightness', 'console.trig.brightness'],
    ['agentDone', 'check', 'console.trig.agentDone'],
    ['agentFail', 'alert', 'console.trig.agentFail'],
    ['timer', 'timer', 'console.trig.timer'],
    ['battery', 'batteryCharging', 'console.trig.battery']
  ];

  function rangeVal(value, min, max) {
    return clamp(((value - min) / (max - min)) * 100, 0, 100).toFixed(1) + '%';
  }

  function motionRow(key, value, min, max, step, labelKey, formatter) {
    return '<div class="motion-row">' +
      '<div class="motion-head"><span>' + t(labelKey) + '</span><span class="motion-val" data-mv="' + key + '">' + formatter(value) + '</span></div>' +
      '<input class="ui-range" type="range" min="' + min + '" max="' + max + '" step="' + step + '" value="' + value +
        '" data-motion-range="' + key + '" aria-label="' + t(labelKey) + '" style="--val:' + rangeVal(value, min, max) + '">' +
      '</div>';
  }

  function readoutValues() {
    var p = S.motionParams;
    var adj = toks('expand');
    var k = Math.pow((2 * Math.PI) / adj.T, 2);
    var c = (4 * Math.PI * adj.z) / adj.T;
    var settle = adj.z >= 0.99 ? (0.93 * adj.T) : (0.622 * adj.T / adj.z);
    return { k: k, c: c, settle: settle };
  }

  function readoutHTML() {
    var r = readoutValues();
    return '<div class="readout" data-readout>' +
      '<div class="readout-cell"><span>' + t('console.readoutK') + '</span><b>' + Math.round(r.k) + '</b></div>' +
      '<div class="readout-cell"><span>' + t('console.readoutC') + '</span><b>' + r.c.toFixed(1) + '</b></div>' +
      '<div class="readout-cell"><span>' + t('console.readoutSettle') + '</span><b>' + r.settle.toFixed(2) + 's</b></div>' +
      '</div>';
  }

  function fmtSeconds(v) { return v.toFixed(2) + ' s'; }
  function fmtRatio(v) { return v.toFixed(2); }
  function fmtLag(v) { return Math.round(v * 100) + '%'; }

  function renderConsole() {
    if (!consoleEl) return;
    var scroll = $('.console-body');
    var scrollTop = scroll ? scroll.scrollTop : 0;
    consoleEl.innerHTML =
      '<div class="console-head">' +
        '<span class="console-mark">' + icon('islandIcon', 15) + '</span>' +
        '<div class="console-titles"><div class="console-title">' + t('console.title') + '</div></div>' +
        '<span class="version-chip">' + t('console.version') + '</span>' +
        '<div class="fps"><span class="fps-dot"></span><span data-fps>' + fpsLast + '</span><span>fps</span></div>' +
        '<button class="console-collapse" type="button" data-action="console-collapse" aria-label="' + t('console.collapse') + '">' + icon('chevronRight', 14) + '</button>' +
      '</div>' +
      '<div class="console-body">' +
        '<section class="console-sec">' +
          '<div class="sec-label"><span>' + t('console.controls') + '</span><span class="live">' + t('console.live') + '</span></div>' +
          '<div class="field-label">' + t('console.mode') + '</div>' +
          segControl('mode', [['float', t('console.modeFloat')], ['flush', t('console.modeFlush')], ['dock', t('console.modeDock')]], S.mode) +
          '<div class="console-row2" style="margin-top:8px">' +
            '<div><div class="field-label">' + t('console.material') + '</div>' +
              segControl('material', [['obsidian', t('console.obsidian')], ['acrylic', t('console.acrylic')]], S.material) +
            '</div>' +
            '<div><div class="field-label">' + t('console.theme') + '</div>' +
              segControl('theme', [['dark', t('console.dark')], ['light', t('console.light')]], S.theme) +
            '</div>' +
          '</div>' +
          '<div class="console-row2" style="margin-top:8px">' +
            '<div><div class="field-label">' + t('console.lang') + '</div>' +
              segControl('lang', [['zh', t('console.zh')], ['en', t('console.en')]], S.lang) +
            '</div>' +
            '<div><div class="field-label">' + t('console.motion') + '</div>' +
              segControl('motion', [['normal', t('console.normal')], ['reduced', t('console.reduced')]], S.motion) +
            '</div>' +
          '</div>' +
        '</section>' +
        '<section class="console-sec">' +
          '<div class="sec-label"><span>' + t('console.triggers') + '</span><span class="live">' + (S.autoDemo ? '3.0s' : t('console.manual')) + '</span></div>' +
          '<div class="trigger-grid">' +
            TRIGGERS.map(function (tr) { return triggerButton(tr[0], tr[1], tr[2]); }).join('') +
          '</div>' +
          '<button class="wide-btn' + (S.autoDemo ? ' active' : '') + '" type="button" data-trigger="demo">' +
            icon(S.autoDemo ? 'pause' : 'play', 13) +
            '<span>' + t(S.autoDemo ? 'console.trig.stop' : 'console.trig.demo') + '</span>' +
          '</button>' +
        '</section>' +
        '<section class="console-sec">' +
          '<div class="sec-label"><span>' + t('console.motionLab') + '</span><span class="live">' + t('console.live') + '</span></div>' +
          motionRow('expandT', S.motionParams.expandT, 0.2, 0.6, 0.01, 'console.expandT', fmtSeconds) +
          motionRow('expandZ', S.motionParams.expandZ, 0.4, 1.2, 0.01, 'console.expandZ', fmtRatio) +
          motionRow('collapseT', S.motionParams.collapseT, 0.2, 0.6, 0.01, 'console.collapseT', fmtSeconds) +
          motionRow('collapseZ', S.motionParams.collapseZ, 0.4, 1.2, 0.01, 'console.collapseZ', fmtRatio) +
          motionRow('heightLag', S.heightLag, 0, 0.2, 0.01, 'console.heightLag', fmtLag) +
          readoutHTML() +
          '<div class="button-row">' +
            '<button class="small-btn" type="button" data-action="replay">' + icon('refresh', 11) + t('console.replay') + '</button>' +
            '<button class="small-btn" type="button" data-action="reset-motion">' + icon('refresh', 11) + t('console.resetMotion') + '</button>' +
          '</div>' +
        '</section>' +
      '</div>' +
      '<div class="console-foot">' +
        '<div class="foot-row">' +
          '<button class="foot-btn" type="button" data-action="open-settings">' + icon('settings', 13) + t('console.settings') + '</button>' +
          '<button class="foot-btn" type="button" data-action="open-market">' + icon('storefront', 13) + t('console.market') + '</button>' +
        '</div>' +
        '<button class="foot-btn' + (S.annotations ? ' accent' : '') + '" type="button" data-action="annotations">' + icon('sliders', 13) + '<span>' + t(S.annotations ? 'console.annotationsOn' : 'console.annotations') + '</span></button>' +
        '<div class="console-note">' + t('console.note') + '</div>' +
      '</div>';
    var body = $('.console-body', consoleEl);
    if (body) body.scrollTop = scrollTop;
    fpsText = $('[data-fps]', consoleEl);
    updateFPS();
  }

  function updateConsoleActive() {
    if (!consoleEl) return;
    $$('[data-set]', consoleEl).forEach(function (b) {
      var prop = b.dataset.set;
      b.classList.toggle('active', String(S[prop]) === b.dataset.value);
    });
    $$('[data-trigger]', consoleEl).forEach(function (b) {
      b.classList.toggle('active', b.dataset.trigger === S.activeTrigger);
    });
    var demo = $('[data-trigger="demo"]', consoleEl);
    if (demo) {
      demo.classList.toggle('active', S.autoDemo);
      demo.innerHTML = icon(S.autoDemo ? 'pause' : 'play', 13) + '<span>' + t(S.autoDemo ? 'console.trig.stop' : 'console.trig.demo') + '</span>';
    }
    var trigSec = $$('.sec-label .live', consoleEl)[1];
    if (trigSec) trigSec.textContent = S.autoDemo ? '3.0s' : t('console.manual');
    var anno = $('[data-action="annotations"]', consoleEl);
    if (anno) {
      anno.classList.toggle('accent', S.annotations);
      anno.innerHTML = icon('sliders', 13) + '<span>' + t(S.annotations ? 'console.annotationsOn' : 'console.annotations') + '</span>';
    }
    updateMotionUI();
  }

  function updateMotionUI() {
    if (!consoleEl) return;
    $$('[data-motion-range]', consoleEl).forEach(function (input) {
      var key = input.dataset.motionRange;
      var value = key === 'heightLag' ? S.heightLag : S.motionParams[key];
      input.value = value;
      input.style.setProperty('--val', rangeVal(value, parseFloat(input.min), parseFloat(input.max)));
      var out = $('[data-mv="' + key + '"]', consoleEl);
      if (out) out.textContent = key === 'heightLag' ? fmtLag(value) : (key.endsWith('T') ? fmtSeconds(value) : fmtRatio(value));
    });
    var readout = $('[data-readout]', consoleEl);
    if (readout) readout.outerHTML = readoutHTML();
  }

  /* ==========================================================================
     09. SETTINGS + PLUGIN MARKET
     ========================================================================== */
  function switchControl(key, on, labelKey) {
    return '<button class="switch' + (on ? ' on' : '') + '" type="button" role="switch" aria-checked="' + on +
      '" data-toggle="' + key + '" aria-label="' + t(labelKey) + '"></button>';
  }

  function settingRow(titleKey, descKey, controlHTML) {
    return '<div class="setting-row">' +
      '<div class="setting-text"><div class="setting-title">' + t(titleKey) + '</div>' +
      (descKey ? '<div class="setting-desc">' + t(descKey) + '</div>' : '') + '</div>' +
      '<div class="setting-control">' + controlHTML + '</div>' +
      '</div>';
  }

  function rangeMeta(key) {
    return {
      radius: { min: 16, max: 36, step: 1 },
      opacity: { min: 85, max: 100, step: 1 },
      fontScale: { min: 90, max: 115, step: 1 },
      autoCollapse: { min: 1.2, max: 4.8, step: 0.2 },
      motionStrength: { min: 0.5, max: 1.5, step: 0.05 }
    }[key];
  }

  function fmtSettingValue(key, v) {
    if (key === 'radius') return Math.round(v) + ' px';
    if (key === 'opacity' || key === 'fontScale') return Math.round(v) + '%';
    if (key === 'autoCollapse') return v.toFixed(1) + ' s';
    if (key === 'motionStrength') return '×' + v.toFixed(2);
    return String(v);
  }

  function settingRange(key, value) {
    var m = rangeMeta(key);
    return '<div class="setting-range">' +
      '<input class="ui-range" type="range" min="' + m.min + '" max="' + m.max + '" step="' + m.step + '" value="' + value +
        '" data-range="' + key + '" aria-label="' + key + '" style="--val:' + rangeVal(value, m.min, m.max) + '">' +
      '<span class="value-tag" data-range-value="' + key + '">' + fmtSettingValue(key, value) + '</span>' +
      '</div>';
  }

  function paneHeader(key, descKey) {
    return '<div class="pane-head-lg">' + t(key) + '</div><div class="pane-desc">' + t(descKey) + '</div>';
  }

  function renderGeneral() {
    return paneHeader('general.title', 'general.desc') +
      '<div class="setting-group">' +
        settingRow('general.language', 'general.languageDesc', segControl('lang', [['zh', t('console.zh')], ['en', t('console.en')]], S.lang)) +
        settingRow('general.startup', 'general.startupDesc', switchControl('startup', S.startup, 'general.startup')) +
        settingRow('general.autoUpdate', 'general.autoUpdateDesc', switchControl('autoUpdate', S.autoUpdate, 'general.autoUpdate')) +
        settingRow('general.tray', 'general.trayDesc', switchControl('trayIcon', S.trayIcon, 'general.tray')) +
      '</div>' +
      '<div class="section-caption">' + t('general.shortcuts') + '</div>' +
      '<div class="setting-group">' +
        shortcutRow('general.shortcutOpen', ['Ctrl', 'Alt', 'Space']) +
        shortcutRow('general.shortcutPlay', ['Ctrl', 'Alt', 'M']) +
        shortcutRow('general.shortcutClip', ['Ctrl', 'Alt', 'C']) +
        shortcutRow('general.shortcutHide', ['Alt', 'H']) +
      '</div>' +
      '<div class="settings-footer-note">' + t('settings.prototypeNote') + '</div>';
  }

  function shortcutRow(labelKey, keys) {
    return '<div class="shortcut-row"><strong>' + t(labelKey) + '</strong><span class="keycaps">' +
      keys.map(function (k) { return '<span class="keycap">' + k + '</span>'; }).join('') + '</span></div>';
  }

  function renderAppearance() {
    var swatches = Object.keys(ACCENTS).map(function (k) {
      return '<button class="accent-swatch' + (S.accentKey === k ? ' active' : '') + '" type="button" data-accent="' + k + '" style="--sw:' + ACCENTS[k] + '" title="' + t('accent.' + k) + '" aria-label="' + t('accent.' + k) + '"></button>';
    }).join('');
    return paneHeader('appearance.title', 'appearance.desc') +
      '<div class="setting-group">' +
        settingRow('appearance.theme', 'appearance.themeDesc', segControl('theme', [['dark', t('console.dark')], ['light', t('console.light')]], S.theme)) +
        settingRow('appearance.material', 'appearance.materialDesc', segControl('material', [['obsidian', t('console.obsidian')], ['acrylic', t('console.acrylic')]], S.material)) +
        settingRow('appearance.accent', 'appearance.accentDesc', '<div class="accent-row">' + swatches + '</div>') +
        settingRow('appearance.radius', 'appearance.radiusDesc', settingRange('radius', S.radius)) +
        settingRow('appearance.opacity', 'appearance.opacityDesc', settingRange('opacity', S.opacity)) +
        settingRow('appearance.font', 'appearance.fontDesc', settingRange('fontScale', S.fontScale)) +
      '</div>' +
      '<div class="appearance-preview" aria-hidden="true">' +
        '<div class="ap-head"><span>' + t('appearance.preview') + '</span><span class="ap-meta" data-ap-meta></span></div>' +
        '<div class="ap-canvas">' +
          '<div class="ap-shell" id="apShell" style="--ap-fill:' + (S.opacity / 100).toFixed(2) + ';border-radius:' + S.radius + 'px">' +
            '<span class="art ap-art" data-art="0"><span class="art-orb"></span><span class="art-orb2"></span></span>' +
            '<div class="ap-copy"><b>14:32</b><span>Afterglow · Nova Lane</span></div>' +
            '<div class="ap-wave"><i></i><i></i><i></i><i></i></div>' +
            '<span class="ap-play">' + icon('pause', 11) + '</span>' +
            '<span class="ap-progress"><i></i></span>' +
          '</div>' +
        '</div>' +
      '</div>';
  }

  function updateAppearancePreview() {
    var shell = $('#apShell');
    if (!shell) return;
    shell.style.setProperty('--ap-fill', (S.opacity / 100).toFixed(2));
    shell.style.borderRadius = S.radius + 'px';
    var meta = $('[data-ap-meta]');
    if (meta) meta.textContent = S.radius + 'px · ' + S.opacity + '%';
  }

  function renderIslandSettings() {
    return paneHeader('island.title', 'island.desc') +
      '<div class="setting-group">' +
        settingRow('island.defaultMode', 'island.defaultModeDesc', segControl('mode', [['float', t('console.modeFloat')], ['flush', t('console.modeFlush')], ['dock', t('console.modeDock')]], S.mode)) +
        settingRow('island.anchor', 'island.anchorDesc', segControl('anchor', [['center', t('island.anchorCenter')], ['left', t('island.anchorLeft')], ['right', t('island.anchorRight')]], S.anchor)) +
        settingRow('island.strategy', 'island.strategyDesc', segControl('strategy', [['stack', t('island.strategyStack')], ['latest', t('island.strategyLatest')]], S.strategy)) +
        settingRow('island.hover', 'island.hoverDesc', switchControl('hoverPreview', S.hoverPreview, 'island.hover')) +
        settingRow('island.autoCollapse', 'island.autoCollapseDesc', settingRange('autoCollapse', S.autoCollapse)) +
        settingRow('island.motionStrength', 'island.motionStrengthDesc', settingRange('motionStrength', S.motionStrength)) +
      '</div>';
  }

  function pluginSearchString(p) {
    return (loc(t(p.name)) + ' ' + t(p.name) + ' ' + t(p.desc) + ' ' + p.author + ' ' + p.category).toLowerCase();
  }

  function renderPlugins() {
    var installed = ALL_PLUGINS.filter(function (p) { return S.installedPlugins.has(p.id); });
    var rows = installed.map(function (p) {
      var perms = p.permissions.length ? '<div class="perms">' + p.permissions.map(function (perm) {
        return '<span class="perm">' + icon(PERM_ICONS[perm] || 'shield', 7) + t('plugins.perm.' + perm) + '</span>';
      }).join('') + '</div>' : '';
      return '<div class="plugin-row" data-plugin-row data-search="' + esc(pluginSearchString(p)) + '">' +
        '<span class="plugin-icon" style="--p:' + p.color + '">' + icon(p.icon, 17) + '</span>' +
        '<div class="plugin-main">' +
          '<div class="plugin-name-row"><span class="plugin-name">' + t(p.name) + '</span><span class="plugin-version">v' + p.version + '</span>' +
            '<span class="badge ' + (p.builtin ? 'builtin' : 'community') + '">' + t(p.builtin ? 'common.builtin' : 'common.community') + '</span>' +
            (p.verified ? '<span class="badge verified">' + icon('check', 7) + t('common.verified') + '</span>' : '') +
          '</div>' +
          '<div class="plugin-meta">' + esc(p.author) + '</div>' +
          perms +
        '</div>' +
        '<div class="plugin-actions">' +
          '<button class="icon-btn" type="button" data-plugin-settings="' + p.id + '" aria-label="' + t('plugins.settings') + '">' + icon('settings', 12) + '</button>' +
          '<button class="switch' + (S.pluginEnabled[p.id] ? ' on' : '') + '" type="button" role="switch" aria-checked="' + !!S.pluginEnabled[p.id] +
            '" data-toggle-plugin="' + p.id + '" aria-label="' + t('plugins.title') + '"></button>' +
        '</div>' +
        '</div>';
    }).join('');
    return paneHeader('plugins.title', 'plugins.desc') +
      '<div class="search-field">' + icon('search', 13) +
        '<input type="text" data-search-input="pluginQuery" placeholder="' + t('plugins.search') + '" aria-label="' + t('plugins.search') + '" value="' + esc(S.pluginQuery) + '">' +
      '</div>' +
      '<div class="plugin-list">' + rows + '</div>' +
      '<div class="plugin-toolbar"><span class="setting-desc">' + t('plugins.folderDesc') + '</span>' +
        '<button class="open-folder" type="button" data-action="open-plugin-folder">' + icon('folder', 11) + t('plugins.openFolder') + '</button>' +
      '</div>';
  }

  function installButton(id, featured) {
    var installed = S.installedPlugins.has(id);
    var installing = !!S.installing[id];
    if (featured) {
      if (installed) return '<button class="featured-install" type="button" disabled>' + icon('check', 10) + '<span>' + t('common.installed') + '</span></button>';
      if (installing) return '<button class="featured-install" type="button" disabled><span>' + t('common.installing') + '</span></button>';
      return '<button class="featured-install" type="button" data-install="' + id + '"><span>' + t('common.install') + '</span></button>';
    }
    if (installed) return '<button class="mc-install installed" type="button" disabled>' + icon('check', 9) + '<span>' + t('common.installed') + '</span></button>';
    if (installing) return '<button class="mc-install installing" type="button" disabled data-install="' + id + '"><span class="install-fill"></span><span>' + t('common.installing') + '</span></button>';
    return '<button class="mc-install" type="button" data-install="' + id + '"><span>' + t('common.install') + '</span></button>';
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
          '<div class="mc-title"><div class="mc-name">' + t(p.name) + (p.verified ? icon('shield', 9) : '') + '</div>' +
            '<div class="mc-author">' + esc(p.author) + ' · v' + p.version + '</div>' +
          '</div>' +
        '</div>' +
        '<div class="mc-desc">' + t(p.desc) + '</div>' +
        '<div class="mc-foot">' +
          '<span class="mc-rating">' + icon('star', 8) + p.rating.toFixed(1) + '</span>' +
          '<span class="mc-downloads">' + t('market.downloads', { n: p.downloads }) + '</span>' +
          installButton(p.id, false) +
        '</div>' +
        '</article>';
    }).join('');
    var featured = ALL_PLUGINS.filter(function (p) { return p.id === 'agent-bridge'; })[0];
    return paneHeader('market.title', 'market.desc') +
      '<div class="market-featured">' +
        '<div class="featured-copy">' +
          '<div class="featured-kicker">' + t('market.featured') + '</div>' +
          '<div class="featured-title">' + t(featured.name) + '</div>' +
          '<div class="featured-desc">' + t('market.featuredDesc') + '</div>' +
          '<div class="featured-actions">' + installButton('agent-bridge', true) +
            '<span class="featured-meta">' + t('market.featuredMeta') + '</span>' +
          '</div>' +
        '</div>' +
        '<div class="featured-art">' + icon('agent', 32) + '</div>' +
      '</div>' +
      '<div class="market-toolbar"><div class="search-field">' + icon('search', 13) +
        '<input type="text" data-search-input="marketQuery" placeholder="' + t('market.search') + '" aria-label="' + t('market.search') + '" value="' + esc(S.marketQuery) + '">' +
      '</div></div>' +
      '<div class="market-cats">' + cats.map(function (c) {
        return '<button class="cat-chip' + (S.marketCat === c[0] ? ' active' : '') + '" type="button" data-cat="' + c[0] + '">' + t(c[1]) + '</button>';
      }).join('') + '</div>' +
      '<div class="market-grid">' + cards + '<div class="market-empty" hidden>' + t('market.noResults') + '</div></div>' +
      '<div class="market-footnote">' + icon('shield', 10) + t('market.footnote') + '</div>';
  }

  function renderAdvanced() {
    return paneHeader('advanced.title', 'advanced.desc') +
      '<div class="section-caption">' + t('advanced.dataFolder') + '</div>' +
      '<div class="path-field">' + icon('folder', 12) + '<span>C:\\Users\\Doulor\\AppData\\Roaming\\FluidBar</span></div>' +
      '<div class="adv-actions">' +
        '<button class="small-action" type="button" data-action="open-data-folder">' + icon('folder', 11) + t('advanced.openFolder') + '</button>' +
        '<button class="small-action" type="button" data-action="view-logs">' + icon('file', 11) + t('advanced.logs') + '</button>' +
        '<button class="small-action" type="button" data-action="export-config">' + icon('download', 11) + t('advanced.export') + '</button>' +
        '<button class="small-action" type="button" data-action="import-config">' + icon('refresh', 11) + t('advanced.import') + '</button>' +
      '</div>' +
      '<div class="setting-group" style="margin-top:12px">' +
        settingRow('advanced.devMode', 'advanced.devModeDesc', switchControl('devMode', S.devMode, 'advanced.devMode')) +
        settingRow('advanced.api', 'advanced.apiDesc', '<span class="value-tag" style="min-width:unset">v2.4.0</span>') +
      '</div>' +
      '<div class="adv-actions"><button class="small-action danger" type="button" data-action="reset-settings">' + icon('trash', 11) +
        (S.confirmReset ? t('advanced.confirmReset') : t('advanced.reset')) + '</button></div>' +
      '<div class="settings-footer-note">' + t('settings.prototypeNote') + '</div>';
  }

  function renderAbout() {
    var updating = S.updateState === 'checking';
    return paneHeader('about.title', 'about.desc') +
      '<div class="about-pane">' +
        '<span class="about-mark">' + icon('islandIcon', 30) + '</span>' +
        '<div class="about-name">FluidBar</div>' +
        '<div class="about-version">' + t('about.version') + '</div>' +
        '<div class="about-actions">' +
          '<button class="about-btn" type="button" data-action="update-check">' + icon('refresh', 11) +
            (updating ? t('about.checking') : (S.updateState === 'done' ? t('about.upToDate') : t('about.update'))) + '</button>' +
          '<button class="about-btn" type="button" data-action="proto-link">' + icon('external', 11) + t('about.github') + '</button>' +
        '</div>' +
        '<div class="license-line">' + t('about.license') + '</div>' +
        '<div class="contributors">' +
          '<div class="contributors-title">' + t('about.contributors') + '</div>' +
          '<div class="contrib-row">' +
            [['doulor', '#4CC9F0'], ['Vera Chen', '#F2B35E'], ['Kai', '#7F9BFF'], ['Mori', '#8FD9A8']].map(function (c) {
              return '<span class="contrib" style="--c:' + c[1] + '">' + c[0] + '</span>';
            }).join('') +
          '</div>' +
        '</div>' +
        '<div class="about-links">' +
          '<button class="about-link" type="button" data-action="proto-link">' + icon('file', 10) + t('about.docs') + '</button>' +
          '<button class="about-link" type="button" data-action="proto-link">' + icon('github', 10) + t('about.github') + '</button>' +
          '<button class="about-link" type="button" data-action="proto-link">' + icon('chat', 10) + t('about.feedback') + '</button>' +
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
    return '<button class="nav-item' + (S.settingsTab === tab ? ' active' : '') + '" type="button" data-nav="' + tab + '">' +
      icon(iconName, 15) + '<span>' + t(labelKey) + '</span>' +
      (count ? '<span class="nav-count">' + count + '</span>' : '') + '</button>';
  }

  function renderSettings(opts) {
    if (!settingsWindow) return;
    opts = opts || {};
    var oldPane = $('#settingsPane');
    var scroll = opts.resetScroll ? 0 : (oldPane ? oldPane.scrollTop : S.settingsScroll);
    var nav =
      '<div class="nav-kicker">' + t('settings.preferences') + '</div>' +
      navItem('general', 'settings', 'nav.general') +
      navItem('appearance', 'palette', 'nav.appearance') +
      navItem('island', 'islandIcon', 'nav.island') +
      navItem('plugins', 'puzzle', 'nav.plugins', S.installedPlugins.size) +
      navItem('market', 'storefront', 'nav.market', ALL_PLUGINS.length) +
      navItem('advanced', 'sliders', 'nav.advanced') +
      navItem('about', 'info', 'nav.about');

    settingsWindow.innerHTML =
      '<div class="settings-titlebar">' +
        '<span class="sw-mark">' + icon('islandIcon', 12) + '</span>' +
        '<span class="sw-title">' + t('settings.title') + '</span>' +
        '<span class="sw-sub">' + t('settings.sub') + '</span>' +
        '<div class="spacer"></div>' +
        '<button class="sw-close" type="button" data-action="close-modal" aria-label="' + t('common.close') + '">' + icon('close', 14) + '</button>' +
      '</div>' +
      '<div class="settings-body">' +
        '<nav class="settings-nav" aria-label="' + t('settings.preferences') + '">' + nav +
          '<div class="nav-version"><b>FluidBar 2.0.0</b>' + t('settings.prototypeNote') + '</div>' +
        '</nav>' +
        '<div class="settings-pane" id="settingsPane">' + renderSettingsPane() + '</div>' +
      '</div>';

    var pane = $('#settingsPane');
    if (pane) pane.scrollTop = scroll;
    applySearchFilters();
    if (S.settingsTab === 'appearance') updateAppearancePreview();
    requestAnimationFrame(function () {
      $$('.mc-install.installing', settingsWindow).forEach(function (b) { b.classList.add('started'); });
    });
  }

  function applySearchFilters() {
    var q1 = S.pluginQuery.trim().toLowerCase();
    $$('[data-plugin-row]', settingsWindow).forEach(function (el) {
      el.hidden = !!q1 && (el.dataset.search || '').indexOf(q1) === -1;
    });
    var q2 = S.marketQuery.trim().toLowerCase();
    var visible = 0;
    $$('[data-market-card]', settingsWindow).forEach(function (el) {
      var show = !q2 || (el.dataset.search || '').indexOf(q2) !== -1;
      el.hidden = !show;
      if (show) visible++;
    });
    var empty = $('.market-empty', settingsWindow);
    if (empty) empty.hidden = visible !== 0;
  }

  function openSettings(tab, opts) {
    opts = opts || {};
    S.settingsOpen = true;
    S.settingsTab = tab || 'general';
    if (opts.resetScroll) S.settingsScroll = 0;
    stopDemo();
    renderSettings({ resetScroll: true });
    modalLayer.hidden = false;
    document.body.classList.add('modal-open');
    if (opts.instant) {
      modalLayer.classList.add('open');
    } else {
      requestAnimationFrame(function () { modalLayer.classList.add('open'); });
    }
    clearTimeout(openSettings._closeTimer);
    setTimeout(function () {
      if (settingsWindow && S.settingsOpen) settingsWindow.focus({ preventScroll: true });
    }, 60);
  }

  function closeSettings() {
    if (!S.settingsOpen) return;
    S.settingsOpen = false;
    modalLayer.classList.remove('open');
    document.body.classList.remove('modal-open');
    clearTimeout(openSettings._closeTimer);
    openSettings._closeTimer = setTimeout(function () {
      if (!S.settingsOpen) modalLayer.hidden = true;
    }, 260);
    stopDemo();
  }

  /* ==========================================================================
     10. DESIGN ANNOTATIONS
     ========================================================================== */
  function renderSpecCard() {
    if (!specCard) return;
    specCard.innerHTML =
      '<div class="spec-head"><span class="spec-title">' + t('anno.title') + '</span><span class="spec-live">' + t('common.live') + '</span></div>' +
      '<div class="spec-grid">' +
        '<div class="spec-row"><label>' + t('anno.size') + '</label><b data-spec="size">—</b></div>' +
        '<div class="spec-row"><label>' + t('anno.radiusLabel') + '</label><b data-spec="radius">—</b></div>' +
        '<div class="spec-row"><label>' + t('anno.alpha') + '</label><b data-spec="alpha">—</b></div>' +
        '<div class="spec-row"><label>T / ζ</label><b data-spec="spring">—</b></div>' +
        '<div class="spec-row"><label>' + t('anno.kc') + '</label><b data-spec="kc">—</b></div>' +
        '<div class="spec-row"><label>' + t('anno.settle') + '</label><b data-spec="settle">—</b></div>' +
      '</div>' +
      '<div class="spec-tokens">' +
        ['#08080A', '#141418', '#1E1E22', '#FFFFFF', '#9B9BA5', '#4CC9F0', '#34C759', '#FF9500'].map(function (c) {
          return '<span class="spec-token" style="--sw:' + c + '"></span>';
        }).join('') +
        '<span class="spec-token-label">OBSIDIAN</span>' +
      '</div>';
  }

  function toggleAnnotations(force) {
    S.annotations = typeof force === 'boolean' ? force : !S.annotations;
    if (annotationsEl) annotationsEl.hidden = !S.annotations;
    document.body.classList.toggle('annotations-on', S.annotations);
    if (S.annotations) {
      renderSpecCard();
      updateAnnotations();
    }
    updateConsoleActive();
  }

  function updateAnnotations() {
    if (!S.annotations || !ISLAND || !measureSvg) return;
    var r = ISLAND.getBoundingClientRect();
    var pad = 8;
    var x = r.left - pad, y = r.top - pad, w = r.width + pad * 2, h = r.height + pad * 2;
    var belowY = r.bottom + 26;
    var useBelow = belowY + 20 < window.innerHeight - 60;
    var dimY = useBelow ? belowY : Math.max(22, r.top - 28);
    var rightX = r.right + 30;
    var useRight = rightX < window.innerWidth - 360;
    var dimX = useRight ? rightX : Math.max(26, r.left - 30);
    var rad = S.mode === 'flush' ? 22 : (S.expanded ? S.radius : Math.min(r.height / 2, 44));
    var svg =
      '<rect class="measure-line dashed" x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" rx="' + Math.min(rad + pad, 40) + '"/>' +
      '<line class="measure-line" x1="' + r.left + '" y1="' + dimY + '" x2="' + r.right + '" y2="' + dimY + '"/>' +
      '<line class="measure-tick" x1="' + r.left + '" y1="' + (dimY - 5) + '" x2="' + r.left + '" y2="' + (dimY + 5) + '"/>' +
      '<line class="measure-tick" x1="' + r.right + '" y1="' + (dimY - 5) + '" x2="' + r.right + '" y2="' + (dimY + 5) + '"/>' +
      '<line class="measure-line" x1="' + dimX + '" y1="' + r.top + '" x2="' + dimX + '" y2="' + r.bottom + '"/>' +
      '<line class="measure-tick" x1="' + (dimX - 5) + '" y1="' + r.top + '" x2="' + (dimX + 5) + '" y2="' + r.top + '"/>' +
      '<line class="measure-tick" x1="' + (dimX - 5) + '" y1="' + r.bottom + '" x2="' + (dimX + 5) + '" y2="' + r.bottom + '"/>' +
      '<line class="measure-line soft" x1="' + r.right + '" y1="' + r.top + '" x2="' + (r.right + 24) + '" y2="' + (r.top + 18) + '"/>';
    measureSvg.innerHTML = svg;
    if (measureTags) {
      measureTags.innerHTML =
        '<span class="measure-tag" style="left:' + ((r.left + r.right) / 2) + 'px;top:' + (dimY - 18) + 'px;transform:translate(-50%,-50%)">' +
          '<span class="tag-key">' + t('anno.width') + '×' + t('anno.height') + '</span>' + Math.round(r.width) + ' × ' + Math.round(r.height) + '</span>' +
        '<span class="measure-tag" style="left:' + (dimX + 24) + 'px;top:' + ((r.top + r.bottom) / 2) + 'px;transform:translate(-50%,-50%)">' +
          '<span class="tag-key">' + t('anno.height') + '</span>' + Math.round(r.height) + '</span>' +
        '<span class="measure-tag" style="left:' + (r.right + 37) + 'px;top:' + (r.top + 7) + 'px;transform:translate(-50%,-50%)">' +
          '<span class="tag-key">' + t('anno.radius') + '</span>' + rad + '</span>';
    }
    var spec = specCard;
    if (spec) {
      var sizeEl = $('[data-spec="size"]', spec);
      if (sizeEl) sizeEl.textContent = Math.round(r.width) + ' × ' + Math.round(r.height);
      var radEl = $('[data-spec="radius"]', spec);
      if (radEl) radEl.textContent = S.mode === 'flush' ? '0 0 22 22' : rad + ' px';
      var alphaEl = $('[data-spec="alpha"]', spec);
      if (alphaEl) alphaEl.textContent = computeAlpha(targetSize()).toFixed(2);
      var adj = toks('expand');
      var springEl = $('[data-spec="spring"]', spec);
      if (springEl) springEl.textContent = adj.T.toFixed(2) + ' / ' + adj.z.toFixed(2);
      var kEl = $('[data-spec="kc"]', spec);
      var k = Math.pow((2 * Math.PI) / adj.T, 2);
      var c = (4 * Math.PI * adj.z) / adj.T;
      if (kEl) kEl.textContent = Math.round(k) + ' / ' + c.toFixed(1);
      var settleEl = $('[data-spec="settle"]', spec);
      if (settleEl) settleEl.textContent = (adj.z >= 0.99 ? 0.93 * adj.T : 0.622 * adj.T / adj.z).toFixed(2) + 's';
    }
  }

  /* ==========================================================================
     TOASTS
     ========================================================================== */
  var toastTimer = 0;
  function showToast(message, opts) {
    opts = opts || {};
    if (!toastEl) return;
    var ico = opts.icon || (opts.kind === 'error' ? 'alert' : 'check');
    toastEl.className = 'toast ' + (opts.kind || '');
    toastEl.innerHTML = '<span class="toast-ico">' + icon(ico, 12) + '</span><span>' + esc(message) + '</span>' +
      (opts.sub ? '<small>' + esc(opts.sub) + '</small>' : '');
    requestAnimationFrame(function () { toastEl.classList.add('show'); });
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { toastEl.classList.remove('show'); }, opts.duration || 2600);
  }

  /* ==========================================================================
     STATE TRANSITIONS
     ========================================================================== */
  var transientTimer = 0;
  var S_PINNED = new Set(['gh']);
  S.pinned = S_PINNED;
  S.todos = [
    { key: 'widgets.todo0', done: true },
    { key: 'widgets.todo1', done: true },
    { key: 'widgets.todo2', done: false }
  ];

  function clearTransient() {
    if (transientTimer) {
      clearTimeout(transientTimer);
      transientTimer = 0;
    }
  }

  function nudge() {
    if (S.motion === 'reduced' || S.expanded) return;
    nudgeSpring.snap(-4);
    var tk = toks('nudge');
    nudgeSpring.retarget(0, tk.T, tk.z);
    wake();
  }

  function scheduleTransient() {
    clearTransient();
    if (S.autoDemo) return;
    var ms = Math.max(800, S.autoCollapse * 1000);
    transientTimer = window.setTimeout(function () {
      transientTimer = 0;
      if (S.expanded) return;
      if (S.peek) { scheduleTransient(); return; }
      if (S.view === 'hud' || S.view === 'clipboard') {
        S.view = S.view === 'hud' ? (S.hudPrevView || 'idle') : 'idle';
        renderCompact();
        renderShellAttrs();
        syncTargets('collapse');
      }
    }, ms);
  }

  function setView(view, options) {
    options = options || {};
    S.view = view;
    if (options.stack != null) S.stack = clamp(options.stack, 1, 3);
    renderCompact();
    renderShellAttrs();
    if (S.expanded) {
      if (S.tab !== tabForView(view)) {
        S.tab = tabForView(view);
        updateRail();
        renderPane();
      }
      syncTargets('morph');
    } else {
      syncTargets(options.reason || 'morph');
    }
    if (options.nudge) nudge();
    updateDockActive();
  }

  function tabForView(view) {
    if (view === 'media') return 'media';
    if (view === 'notifications') return 'notifications';
    if (view === 'clipboard') return 'clipboard';
    if (view === 'agent') return 'agent';
    return 'home';
  }

  function expandTo(tab, opts) {
    opts = opts || {};
    clearTransient();
    S.activeTrigger = null;
    if (S.expanded && S.tab === tab && S.mode !== 'dock') return;
    S.peek = false;
    S.tab = tab || 'home';
    S.expanded = true;
    renderShellAttrs();

    if (S.mode === 'dock') {
      if (!S.dockOpen) {
        widthSpring.snap(EXPANDED_W);
        heightSpring.snap(46);
        radiusSpring.snap(S.radius);
      }
      S.dockOpen = true;
      document.body.classList.add('dock-panel-open');
      renderRail();
      renderPane();
      syncTargets(opts.instant ? 'snap' : 'expand', { instant: !!opts.instant });
      updateDockActive();
      updateConsoleActive();
      return;
    }

    renderRail();
    renderPane();
    clampDragForWidth(EXPANDED_W);
    syncTargets(opts.instant ? 'snap' : 'expand', { instant: !!opts.instant });
    updateConsoleActive();
  }

  function setTab(tab) {
    if (!S.expanded) return;
    S.tab = tab;
    updateRail();
    renderPane();
    syncTargets('morph');
    if (S.mode === 'dock') {
      var app = DOCK_APPS.filter(function (a) { return a.tab === tab; })[0];
      S.dockActiveApp = app ? app.id : S.dockActiveApp;
      updateDockActive();
    }
  }

  function collapse() {
    clearTransient();
    if (S.mode === 'dock') {
      if (!S.dockOpen) return;
      S.expanded = false;
      S.dockOpen = false;
      document.body.classList.remove('dock-panel-open');
      renderShellAttrs();
      syncTargets('collapse');
      updateDockActive();
      updateConsoleActive();
      return;
    }
    if (!S.expanded) return;
    S.expanded = false;
    S.peek = false;
    renderShellAttrs();
    syncTargets('collapse');
    updateConsoleActive();
  }

  /* ==========================================================================
     11. PLAYBACK / TIMER / AUTO DEMO
     ========================================================================== */
  function refreshTransport() {
    $$('[data-act="toggle-play"]').forEach(function (b) {
      var isBig = b.classList.contains('play-btn');
      var isMini = b.classList.contains('mini-play');
      var size = isBig ? 16 : (isMini ? 12 : 13);
      b.innerHTML = icon(S.playing ? 'pause' : 'play', size);
      b.setAttribute('aria-label', t(S.playing ? 'media.pause' : 'media.play'));
    });
    $$('.wave').forEach(function (w) { w.classList.toggle('paused', !S.playing); });
  }

  function updatePlaybackUI() {
    var pct = progressPct();
    $$('.pill-progress span').forEach(function (el) { el.style.width = pct.toFixed(2) + '%'; });
    $$('.mini-progress span').forEach(function (el) { el.style.width = pct.toFixed(2) + '%'; });
    $$('.js-seek-fill').forEach(function (el) { el.style.width = pct.toFixed(2) + '%'; });
    $$('.js-seek-thumb').forEach(function (el) { el.style.left = pct.toFixed(2) + '%'; });
    $$('.js-time-current').forEach(function (el) { el.textContent = fmtTime(S.progress); });
    var remaining = Math.max(0, currentTrack().duration - S.progress);
    $$('.js-time-remaining').forEach(function (el) { el.textContent = fmtTime(remaining); });
    $$('.js-seek').forEach(function (el) {
      el.setAttribute('aria-valuenow', String(Math.round(S.progress)));
      el.setAttribute('aria-valuetext', fmtTime(S.progress));
    });
    if (S.view === 'timer') {
      var timeEl = $('.pv-timer .timer-time');
      if (timeEl) timeEl.textContent = fmtTime(S.timerRemaining);
      var fill = $('.pv-timer .tr-fill');
      if (fill) {
        var circ = 2 * Math.PI * 8.4;
        fill.setAttribute('stroke-dashoffset', (circ * (1 - clamp(S.timerRemaining / S.timerTotal, 0, 1))).toFixed(2));
      }
    }
  }

  function startTrack(index, keepPlaying) {
    S.track = ((index % TRACKS.length) + TRACKS.length) % TRACKS.length;
    S.progress = 0;
    S.playing = keepPlaying !== false;
    if (ISLAND) ISLAND.style.setProperty('--track-accent', currentTrack().accent);
    if (!S.expanded) {
      renderCompact();
      if (S.view === 'media') syncTargets('morph');
    } else if (S.tab === 'media' || S.tab === 'home') {
      renderPane();
    }
    updatePlaybackUI();
    refreshTransport();
  }

  function togglePlay() {
    S.playing = !S.playing;
    refreshTransport();
  }

  function seekFromEvent(e, seekEl) {
    var seek = seekEl || (e.target.closest ? e.target.closest('.js-seek') : null);
    if (!seek) return;
    var rect = seek.getBoundingClientRect();
    var frac = clamp((e.clientX - rect.left) / rect.width, 0, 1);
    S.progress = frac * currentTrack().duration;
    updatePlaybackUI();
  }

  function trigger(kind, fromDemo) {
    if (!fromDemo) stopDemo();
    S.activeTrigger = kind;
    clearTransient();
    if (kind === 'media') {
      S.playing = true;
      S.stack = 1;
      setView('media', { nudge: true });
    } else if (kind === 'pause') {
      S.playing = false;
      S.stack = 1;
      setView('media');
      refreshTransport();
    } else if (kind === 'notification') {
      if (S.cleared) { S.cleared = false; S.dismissed.clear(); S.unread = 0; }
      S.latestNotif = (S.latestNotif + 1) % Math.max(1, visibleNotifications().length);
      S.unread = Math.min(9, S.unread + 1);
      S.stack = Math.min(3, S.stack + 1);
      S.view = 'notifications';
      if (S.expanded) {
        setTab('notifications');
        renderCompact();
      } else {
        setView('notifications', { nudge: true });
      }
    } else if (kind === 'clipboard') {
      S.stack = Math.min(3, S.stack + 1);
      S.view = 'clipboard';
      renderCompact();
      if (S.expanded) setTab('clipboard');
      else setView('clipboard', { nudge: true });
      scheduleTransient();
    } else if (kind === 'volume' || kind === 'brightness') {
      S.hudPrevView = S.view === 'hud' ? S.hudPrevView : S.view;
      S.hudType = kind;
      S.stack = 1;
      S.view = 'hud';
      if (S.expanded) {
        S.expanded = false;
        renderShellAttrs();
        renderCompact();
        syncTargets('collapse');
      } else {
        setView('hud', { nudge: true });
      }
      scheduleTransient();
    } else if (kind === 'agentDone' || kind === 'agentFail') {
      S.agentStatus = kind === 'agentDone' ? 'done' : 'error';
      S.stack = Math.min(3, S.stack + 1);
      S.view = 'agent';
      renderCompact();
      if (S.expanded) setTab('agent');
      else setView('agent', { nudge: true });
    } else if (kind === 'timer') {
      S.timerRemaining = 1500;
      S.timerTotal = 1500;
      S.timerRunning = true;
      S.stack = 1;
      S.view = 'timer';
      renderCompact();
      if (S.expanded) setTab('home');
      else setView('timer', { nudge: true });
      updatePlaybackUI();
    } else if (kind === 'battery') {
      S.stack = 1;
      S.view = 'battery';
      renderCompact();
      if (S.expanded) setTab('home');
      else setView('battery', { nudge: true });
    }
    updateConsoleActive();
    updateDockActive();
  }

  var demoIndex = 0;
  var demoTimer = 0;
  var DEMO_STEPS = [
    function () { trigger('media', true); },
    function () { trigger('notification', true); },
    function () { trigger('notification', true); },
    function () { trigger('volume', true); },
    function () { trigger('clipboard', true); },
    function () { trigger('agentDone', true); },
    function () { trigger('timer', true); },
    function () { trigger('battery', true); },
    function () { expandTo('media'); },
    function () { setTab('widgets'); },
    function () { collapse(); },
    function () { expandTo('notifications'); },
    function () { collapse(); setView('idle'); }
  ];

  function runDemoStep() {
    var step = DEMO_STEPS[demoIndex % DEMO_STEPS.length];
    demoIndex++;
    try { step(); } catch (err) { stopDemo(); }
  }

  function startDemo() {
    if (S.autoDemo) { stopDemo(); return; }
    stopDemo(true);
    S.autoDemo = true;
    S.activeTrigger = 'demo';
    demoIndex = 0;
    updateConsoleActive();
    showToast(t('toast.demoStarted'), { icon: 'play' });
    runDemoStep();
    demoTimer = window.setInterval(runDemoStep, 3000);
  }

  function stopDemo(silent) {
    if (!S.autoDemo) return;
    S.autoDemo = false;
    if (demoTimer) { clearInterval(demoTimer); demoTimer = 0; }
    if (S.activeTrigger === 'demo') S.activeTrigger = null;
    updateConsoleActive();
    if (!silent) showToast(t('toast.demoStopped'), { icon: 'pause' });
  }

  /* ==========================================================================
     12. LANGUAGE / SETTINGS / MODE / ACCENT
     ========================================================================== */
  function applyGlobalCSS() {
    var root = document.documentElement;
    root.style.setProperty('--accent', ACCENTS[S.accentKey] || ACCENTS.aqua);
    root.style.setProperty('--island-fill', (S.opacity / 100).toFixed(3));
    root.style.setProperty('--island-radius', S.radius + 'px');
    root.style.setProperty('--font-scale', (S.fontScale / 100).toFixed(3));
    root.style.colorScheme = S.theme;
    document.body.dataset.mode = S.mode;
    document.body.dataset.material = S.material;
    document.body.dataset.theme = S.theme;
    document.body.dataset.motion = S.motion;
    document.body.dataset.lang = S.lang;
    document.documentElement.lang = S.lang === 'zh' ? 'zh-CN' : 'en-US';
    document.title = S.lang === 'zh' ? 'FluidBar 2.0 · 交互原型' : 'FluidBar 2.0 · Interactive prototype';
  }

  function applyLanguage(lang) {
    if (lang !== 'zh' && lang !== 'en') return;
    if (S.lang === lang) return;
    S.lang = lang;
    applyGlobalCSS();
    renderScene();
    renderTaskbar();
    renderDock();
    renderCompact();
    renderRail();
    if (S.expanded) renderPane();
    renderConsole();
    updateConsoleActive();
    renderSpecCard();
    if (S.annotations) updateAnnotations();
    if (S.settingsOpen) renderSettings();
    if (consoleHandle) {
      consoleHandle.title = t('console.open');
      consoleHandle.setAttribute('aria-label', t('console.open'));
    }
    updateFPS();
    updateDockActive();
    updatePlaybackUI();
  }

  function setMode(mode) {
    if (mode === S.mode) return;
    var old = S.mode;
    stopDemo();
    S.mode = mode;
    S.expanded = false;
    S.dockOpen = false;
    S.tab = 'home';
    document.body.classList.remove('dock-panel-open');
    applyGlobalCSS();
    renderScene();
    renderShellAttrs();
    if (mode === 'dock') {
      widthSpring.snap(EXPANDED_W);
      heightSpring.snap(46);
      radiusSpring.snap(S.radius);
      renderDock();
      updateDockActive();
      applyShellFrame();
    } else {
      renderCompact();
      renderRail();
      var tgt = targetSize();
      widthSpring.snap(tgt.w);
      heightSpring.snap(tgt.h);
      radiusSpring.snap(Math.min(tgt.h / 2, 44));
      S.shellPhase = 'compact';
      applyShellFrame();
    }
    if (old === 'dock') renderDock();
    updateConsoleActive();
    if (S.settingsOpen) renderSettings();
    updateFPS();
  }

  function setAnchor(anchor) {
    S.anchor = anchor;
    if (S.mode === 'dock') return;
    var max = maxDrag(widthSpring.target || targetSize().w);
    S.dragX = anchor === 'left' ? -max : (anchor === 'right' ? max : 0);
    applyDragX(true);
  }

  function setTheme(theme) {
    if (theme !== 'dark' && theme !== 'light') return;
    S.theme = theme;
    applyGlobalCSS();
    renderCompact();
    if (S.expanded) renderPane();
    updateConsoleActive();
    if (S.settingsOpen) renderSettings();
  }

  function applySetting(prop, value) {
    if (prop === 'lang') { applyLanguage(value); return; }
    if (prop === 'mode') { setMode(value); return; }
    if (prop === 'material') {
      S.material = value;
      applyGlobalCSS();
    } else if (prop === 'theme') {
      setTheme(value);
    } else if (prop === 'motion') {
      S.motion = value;
      applyGlobalCSS();
    } else if (prop === 'anchor') {
      setAnchor(value);
    } else if (prop === 'strategy') {
      S.strategy = value;
      updateStackVisibility(widthSpring.x, heightSpring.x);
    }
    updateConsoleActive();
    if (S.settingsOpen) renderSettings();
    if (S.annotations) updateAnnotations();
  }

  function applyRange(key, value) {
    if (key === 'radius') {
      S.radius = value;
      document.documentElement.style.setProperty('--island-radius', value + 'px');
      if (S.expanded) {
        var tk = toks('morph');
        radiusSpring.retarget(value, tk.T, 0.9);
        wake();
      }
    } else if (key === 'opacity') {
      S.opacity = value;
      document.documentElement.style.setProperty('--island-fill', (value / 100).toFixed(3));
    } else if (key === 'fontScale') {
      S.fontScale = value;
      document.documentElement.style.setProperty('--font-scale', (value / 100).toFixed(3));
      if (ISLAND) ISLAND.style.setProperty('--font-scale', (value / 100).toFixed(3));
    } else if (key === 'autoCollapse') {
      S.autoCollapse = value;
    } else if (key === 'motionStrength') {
      S.motionStrength = value;
    }
    var out = $('[data-range-value="' + key + '"]');
    if (out) out.textContent = fmtSettingValue(key, value);
    if (key === 'radius' || key === 'opacity' || key === 'fontScale') updateAppearancePreview();
    if (S.annotations) updateAnnotations();
  }

  function resetMotionParams() {
    S.motionParams = Object.assign({}, DEFAULTS);
    S.heightLag = 0.08;
    updateConsoleActive();
  }

  function resetAll() {
    stopDemo(true);
    clearTransient();
    S.theme = 'dark';
    S.material = 'obsidian';
    S.motion = 'normal';
    S.accentKey = 'aqua';
    S.radius = 28;
    S.opacity = 96;
    S.fontScale = 100;
    S.startup = true;
    S.autoUpdate = true;
    S.trayIcon = true;
    S.devMode = false;
    S.hoverPreview = true;
    S.strategy = 'stack';
    S.autoCollapse = 2.6;
    S.motionStrength = 1;
    S.anchor = 'center';
    S.dragX = 0;
    S.confirmReset = false;
    S.motionParams = Object.assign({}, DEFAULTS);
    S.heightLag = 0.08;
    ALL_PLUGINS.forEach(function (p) { S.pluginEnabled[p.id] = true; });
    applyGlobalCSS();
    applyDragX(true);
    setMode('float');
    S.view = 'idle';
    S.expanded = false;
    S.tab = 'home';
    document.body.classList.remove('dock-panel-open');
    renderScene();
    renderTaskbar();
    renderDock();
    renderCompact();
    renderRail();
    renderConsole();
    updateConsoleActive();
    syncTargets('snap');
    if (S.settingsOpen) renderSettings();
    if (S.annotations) { renderSpecCard(); updateAnnotations(); }
    showToast(t('toast.reset'), { icon: 'refresh' });
  }

  /* ==========================================================================
     13. ISLAND ACTIONS
     ========================================================================== */
  function openNotification(id) {
    var n = notifById(id);
    if (!n) return;
    showToast(t('toast.openedNotification', { app: loc(n.app) }), { icon: n.appIcon, sub: loc(n.title) });
  }

  function copyItem(id) {
    var item = CLIP_ITEMS.filter(function (c) { return c.id === id; })[0];
    if (!item) return;
    var text = loc(item.content);
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(function () {});
      }
    } catch (err) { /* clipboard unavailable on file:// — prototype still confirms */ }
    showToast(t('toast.copied'), { icon: 'copy', sub: text.slice(0, 48) });
  }

  function handleIslandAction(act, el) {
    var id = el.dataset.id;
    switch (act) {
      case 'toggle-play':
        togglePlay();
        break;
      case 'prev':
        startTrack(S.track - 1, true);
        break;
      case 'next':
        startTrack(S.track + 1, true);
        break;
      case 'output':
        showToast(t('toast.output'), { icon: 'speaker' });
        break;
      case 'queue':
        showToast(t('toast.queue', { n: TRACKS.length }), { icon: 'queue' });
        break;
      case 'collapse':
        collapse();
        break;
      case 'go-media':
        setTab('media');
        break;
      case 'clear-notifs':
        S.cleared = true;
        S.dismissed.clear();
        NOTIFICATIONS.forEach(function (n) { S.dismissed.add(n.id); });
        S.unread = 0;
        S.stack = 1;
        renderPane();
        renderCompact();
        break;
      case 'notif-open':
        openNotification(id);
        break;
      case 'notif-dismiss':
        S.dismissed.add(id);
        S.unread = Math.max(0, S.unread - 1);
        renderPane();
        renderCompact();
        break;
      case 'copy':
        copyItem(id);
        break;
      case 'pin': {
        var wasPinned = S.pinned.has(id);
        if (wasPinned) S.pinned.delete(id); else S.pinned.add(id);
        renderPane();
        showToast(t(wasPinned ? 'clipboard.unpinned' : 'clipboard.pinned'), { icon: 'pin' });
        break;
      }
      case 'quick': {
        var q = el.dataset.quick;
        if (q === 'dark') {
          applySetting('theme', S.theme === 'dark' ? 'light' : 'dark');
          renderPane();
        } else {
          S.toggles[q] = !S.toggles[q];
          renderPane();
        }
        break;
      }
      case 'agent-open': {
        var a = AGENTS.filter(function (x) { return x.id === id; })[0];
        if (a) showToast(t('toast.openedAgent', { tool: a.tool, project: a.project }), { icon: a.icon });
        break;
      }
      case 'cal-prev':
        S.calMonth--;
        if (S.calMonth < 0) { S.calMonth = 11; S.calYear--; }
        renderPane();
        break;
      case 'cal-next':
        S.calMonth++;
        if (S.calMonth > 11) { S.calMonth = 0; S.calYear++; }
        renderPane();
        break;
      case 'todo': {
        var idx = parseInt(el.dataset.index, 10);
        if (!isNaN(idx) && S.todos[idx]) {
          S.todos[idx].done = !S.todos[idx].done;
          renderPane();
        }
        break;
      }
    }
  }

  function isInteractive(el) {
    return !!(el.closest && el.closest('button, input, [role="slider"], [data-act], [data-tab], .js-seek, .mini-media, .notif-row, .clip-row, .agent-row'));
  }

  /* ==========================================================================
     14. EVENTS
     ========================================================================== */
  var hoverTimer = 0;
  var pressing = false;
  var dragCandidate = null;
  var dragging = false;
  var suppressClickUntil = 0;
  var seeking = false;
  var seekEl = null;

  function bindIsland() {
    ISLAND.addEventListener('pointerenter', function () {
      if (S.expanded || !S.hoverPreview || S.mode === 'dock' || dragging) return;
      clearTimeout(hoverTimer);
      hoverTimer = window.setTimeout(function () {
        if (S.expanded || dragging || !ISLAND.matches(':hover')) return;
        S.peek = true;
        renderShellAttrs();
        syncTargets('hover');
      }, 90);
    });
    ISLAND.addEventListener('pointerleave', function () {
      clearTimeout(hoverTimer);
      if (!S.peek) return;
      S.peek = false;
      renderShellAttrs();
      if (!S.expanded) syncTargets('hover');
    });

    ISLAND.addEventListener('pointerdown', function (e) {
      if (S.mode === 'dock') return;
      if (e.button != null && e.button !== 0) return;
      if (S.expanded) return;
      stopDemo();
      clearTimeout(hoverTimer);
      if (S.peek) {
        S.peek = false;
        renderShellAttrs();
        syncTargets('hover');
      }
      pressing = true;
      var pt = toks('press');
      pressSpring.retarget(0.97, pt.T, pt.z);
      wake();
      if (!isInteractive(e.target)) {
        dragCandidate = { x: e.clientX, pointerId: e.pointerId, startX: S.dragX, moved: false };
      }
    });

    window.addEventListener('pointermove', function (e) {
      if (seeking) { seekFromEvent(e, seekEl); return; }
      if (!dragCandidate || e.pointerId !== dragCandidate.pointerId) return;
      var dx = e.clientX - dragCandidate.x;
      if (!dragging && Math.abs(dx) > 5) {
        dragging = true;
        ANCHOR.classList.add('dragging');
        S.peek = false;
        renderShellAttrs();
      }
      if (dragging) {
        var max = maxDrag(widthSpring.x);
        S.dragX = clamp(dragCandidate.startX + dx, -max, max);
        applyDragX(false);
      }
    }, { passive: true });

    window.addEventListener('pointerup', function (e) {
      if (seeking) { seeking = false; seekEl = null; }
      if (pressing) {
        pressing = false;
        pressSpring.retarget(1, toks('press').T, toks('press').z);
        wake();
      }
      if (dragCandidate) {
        if (dragging) {
          suppressClickUntil = performance.now() + 260;
          ANCHOR.classList.remove('dragging');
          var max = maxDrag(widthSpring.x);
          S.dragX = clamp(S.dragX, -max, max);
          applyDragX(true);
        }
        dragging = false;
        dragCandidate = null;
      }
    });

    ISLAND.addEventListener('click', function (e) {
      stopDemo();
      if (performance.now() < suppressClickUntil) return;
      var actEl = e.target.closest('[data-act]');
      if (actEl) {
        if (actEl.matches('input, .js-seek')) return;
        e.stopPropagation();
        handleIslandAction(actEl.dataset.act, actEl);
        return;
      }
      var tabBtn = e.target.closest('[data-tab]');
      if (tabBtn) {
        e.stopPropagation();
        setTab(tabBtn.dataset.tab);
        return;
      }
      if (S.expanded) return;
      if (S.mode === 'dock') return;
      var view = S.view;
      expandTo(view === 'idle' || view === 'hud' || view === 'timer' || view === 'battery' ? 'home' : tabForView(view));
    });

    ISLAND.addEventListener('keydown', function (e) {
      if (e.target.closest && e.target.closest('.js-seek')) {
        var tr = currentTrack();
        if (e.key === 'ArrowRight' || e.key === 'ArrowLeft' || e.key === 'Home' || e.key === 'End') {
          if (e.key === 'ArrowRight') S.progress = clamp(S.progress + 5, 0, tr.duration);
          else if (e.key === 'ArrowLeft') S.progress = clamp(S.progress - 5, 0, tr.duration);
          else if (e.key === 'Home') S.progress = 0;
          else S.progress = tr.duration;
          updatePlaybackUI();
          e.preventDefault();
        }
        return;
      }
      if ((e.key === 'Enter' || e.key === ' ') && e.target.matches && e.target.matches('.notif-row, .clip-row, .agent-row')) {
        var rowAct = e.target.getAttribute('data-act');
        if (rowAct) {
          e.preventDefault();
          handleIslandAction(rowAct, e.target);
        }
        return;
      }
      if (e.target !== ISLAND) return;
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        if (!S.expanded && S.mode !== 'dock') {
          var view = S.view;
          expandTo(view === 'idle' || view === 'hud' || view === 'timer' || view === 'battery' ? 'home' : tabForView(view));
        }
      }
    });

    ISLAND.addEventListener('change', function (e) {
      var el = e.target;
      if (!el.matches('input[type="range"]')) return;
      if (el.dataset.act === 'volume-range') {
        S.volume = parseInt(el.value, 10);
        el.style.setProperty('--val', S.volume + '%');
        var pct = el.parentElement.querySelector('.slider-pct');
        if (pct) pct.textContent = String(S.volume);
      } else if (el.dataset.act === 'brightness-range') {
        S.brightness = parseInt(el.value, 10);
        el.style.setProperty('--val', S.brightness + '%');
        var pct2 = el.parentElement.querySelector('.slider-pct');
        if (pct2) pct2.textContent = String(S.brightness);
      }
    });

    ISLAND.addEventListener('input', function (e) {
      var el = e.target;
      if (!el.matches('input[type="range"]')) return;
      if (el.dataset.act === 'volume-range' || el.dataset.act === 'brightness-range') {
        var v = parseInt(el.value, 10);
        if (el.dataset.act === 'volume-range') S.volume = v; else S.brightness = v;
        el.style.setProperty('--val', v + '%');
        var pct = el.parentElement.querySelector('.slider-pct');
        if (pct) pct.textContent = String(v);
      }
    });

    ISLAND.addEventListener('pointerdown', function (e) {
      var seek = e.target.closest('.js-seek');
      if (seek) {
        seeking = true;
        seekEl = seek;
        seekFromEvent(e, seek);
        e.preventDefault();
      }
    });
  }

  function bindScene() {
    $('#scene').addEventListener('dblclick', function (e) {
      if (S.settingsOpen) return;
      if (e.target.closest('.mock-window')) return;
      if (S.expanded) collapse();
      else if (S.mode === 'dock' && S.dockOpen) collapse();
    });
  }

  function bindConsole() {
    consoleEl.addEventListener('click', function (e) {
      var setBtn = e.target.closest('[data-set]');
      if (setBtn) {
        stopDemo(true);
        applySetting(setBtn.dataset.set, setBtn.dataset.value);
        return;
      }
      var trg = e.target.closest('[data-trigger]');
      if (trg) {
        if (trg.dataset.trigger === 'demo') startDemo();
        else trigger(trg.dataset.trigger, false);
        return;
      }
      var act = e.target.closest('[data-action]');
      if (!act) return;
      var a = act.dataset.action;
      if (a === 'console-collapse') {
        S.consoleCollapsed = true;
        consoleEl.classList.add('collapsed');
        consoleHandle.classList.add('visible');
        consoleHandle.setAttribute('aria-expanded', 'false');
      } else if (a === 'open-settings') {
        openSettings('general');
      } else if (a === 'open-market') {
        openSettings('market');
      } else if (a === 'annotations') {
        toggleAnnotations();
      } else if (a === 'replay') {
        replayExpand();
      } else if (a === 'reset-motion') {
        resetMotionParams();
      }
    });
    consoleEl.addEventListener('input', function (e) {
      var el = e.target;
      if (!el.matches('[data-motion-range]')) return;
      var key = el.dataset.motionRange;
      var value = parseFloat(el.value);
      if (key === 'heightLag') S.heightLag = value;
      else S.motionParams[key] = value;
      el.style.setProperty('--val', rangeVal(value, parseFloat(el.min), parseFloat(el.max)));
      var out = $('[data-mv="' + key + '"]', consoleEl);
      if (out) out.textContent = key === 'heightLag' ? fmtLag(value) : (key.endsWith('T') ? fmtSeconds(value) : fmtRatio(value));
      var readout = $('[data-readout]', consoleEl);
      if (readout) readout.outerHTML = readoutHTML();
    });
    consoleHandle.addEventListener('click', function () {
      S.consoleCollapsed = false;
      consoleEl.classList.remove('collapsed');
      consoleHandle.classList.remove('visible');
      consoleHandle.setAttribute('aria-expanded', 'true');
    });
  }

  function replayExpand() {
    stopDemo(true);
    var tab = S.expanded ? S.tab : 'home';
    if (S.mode === 'dock' && !S.dockOpen) {
      expandTo(tab);
      return;
    }
    if (!S.expanded) {
      expandTo(tab);
      return;
    }
    S.shellPhase = 'expanding';
    if (S.mode === 'dock') {
      widthSpring.snap(EXPANDED_W);
      heightSpring.snap(46);
      radiusSpring.snap(S.radius);
    } else {
      widthSpring.snap(132);
      heightSpring.snap(36);
      radiusSpring.snap(18);
    }
    applyShellFrame();
    syncTargets('expand');
  }

  function bindModal() {
    modalLayer.addEventListener('click', function (e) {
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
            resetAll();
          }
          return;
        }
        if (a === 'open-plugin-folder' || a === 'open-data-folder') {
          showToast(t('toast.folderMock'), { icon: 'folder' });
          return;
        }
        if (a === 'view-logs') {
          showToast(t('advanced.logs'), { icon: 'file', sub: 'fluidbar-2026-10-18.log' });
          return;
        }
        if (a === 'export-config') {
          showToast(t('toast.exported'), { icon: 'download' });
          return;
        }
        if (a === 'import-config') {
          showToast(t('toast.imported'), { icon: 'refresh' });
          return;
        }
        if (a === 'proto-link') {
          showToast(t('toast.linkDisabled'), { icon: 'external' });
          return;
        }
        return;
      }

      var nav = e.target.closest('[data-nav]');
      if (nav) {
        S.settingsTab = nav.dataset.nav;
        S.confirmReset = false;
        renderSettings({ resetScroll: true });
        return;
      }
      var setBtn = e.target.closest('[data-set]');
      if (setBtn) {
        applySetting(setBtn.dataset.set, setBtn.dataset.value);
        return;
      }
      var toggle = e.target.closest('[data-toggle]');
      if (toggle) {
        var prop = toggle.dataset.toggle;
        S[prop] = !S[prop];
        toggle.classList.toggle('on', S[prop]);
        toggle.setAttribute('aria-checked', String(S[prop]));
        return;
      }
      var tglPlugin = e.target.closest('[data-toggle-plugin]');
      if (tglPlugin) {
        var pid = tglPlugin.dataset.togglePlugin;
        S.pluginEnabled[pid] = !S.pluginEnabled[pid];
        renderSettings();
        return;
      }
      var pluginSettings = e.target.closest('[data-plugin-settings]');
      if (pluginSettings) {
        var p = ALL_PLUGINS.filter(function (x) { return x.id === pluginSettings.dataset.pluginSettings; })[0];
        if (p) showToast(t('toast.pluginSettings', { name: t(p.name) }), { icon: 'puzzle' });
        return;
      }
      var accent = e.target.closest('[data-accent]');
      if (accent) {
        S.accentKey = accent.dataset.accent;
        applyGlobalCSS();
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

    modalLayer.addEventListener('input', function (e) {
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

    modalLayer.addEventListener('scroll', function (e) {
      if (e.target.id === 'settingsPane') S.settingsScroll = e.target.scrollTop;
    }, true);
  }

  function installPlugin(id) {
    if (!id || S.installedPlugins.has(id) || S.installing[id]) return;
    S.installing[id] = true;
    renderSettings();
    setTimeout(function () {
      S.installing[id] = false;
      S.installedPlugins.add(id);
      var p = ALL_PLUGINS.filter(function (x) { return x.id === id; })[0];
      if (S.settingsOpen) renderSettings();
      showToast(t('toast.installed', { name: p ? t(p.name) : id }), { kind: 'success' });
    }, 1200);
  }

  function bindDock() {
    dockEl.addEventListener('click', function (e) {
      var app = e.target.closest('[data-dock-app], [data-dock-tray]');
      if (!app) return;
      stopDemo();
      var tab = app.dataset.dockTab || 'home';
      var appId = app.dataset.dockApp || 'tray';
      if (S.mode !== 'dock') return;
      if (S.dockOpen && S.dockActiveApp === appId && !app.dataset.dockTray) {
        collapse();
        return;
      }
      S.dockActiveApp = appId;
      expandTo(tab);
    });
  }

  function bindGlobal() {
    document.addEventListener('pointerdown', function (e) {
      if (S.settingsOpen) return;
      if (e.target.closest('#island') || e.target.closest('#console') || e.target.closest('#consoleHandle') ||
          e.target.closest('#fluidDock') || e.target.closest('.modal-layer') || e.target.closest('.annotations')) return;
      if (S.autoDemo) stopDemo(true);
      if (S.expanded) {
        collapse();
      } else if (S.mode === 'dock' && S.dockOpen) {
        collapse();
      }
    }, true);

    document.addEventListener('keydown', function (e) {
      var inField = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement ? document.activeElement.tagName : '');
      if (e.key === 'Escape') {
        if (S.settingsOpen) { closeSettings(); e.preventDefault(); return; }
        if (S.expanded) { collapse(); e.preventDefault(); return; }
        if (S.autoDemo) { stopDemo(); e.preventDefault(); return; }
        return;
      }
      if (S.settingsOpen || inField) return;
      if (e.target.closest && e.target.closest('input, textarea, select, .js-seek, [role="slider"]')) return;
      if (S.expanded && (e.key === 'ArrowUp' || e.key === 'ArrowDown')) {
        var idx = TAB_META.findIndex(function (m) { return m.id === S.tab; });
        if (idx < 0) idx = 0;
        var step = e.key === 'ArrowDown' ? 1 : -1;
        var nextTab = TAB_META[(idx + step + TAB_META.length) % TAB_META.length];
        setTab(nextTab.id);
        var activeRailBtn = $('.rail-btn.active', RAIL);
        if (activeRailBtn) activeRailBtn.focus({ preventScroll: true });
        e.preventDefault();
        return;
      }
      if (S.expanded && /^[1-6]$/.test(e.key)) {
        setTab(TAB_META[parseInt(e.key, 10) - 1].id);
        e.preventDefault();
      }
    });

    window.addEventListener('resize', function () {
      var width = widthSpring.x || 132;
      clampDragForWidth(width);
      if (S.annotations) updateAnnotations();
      updateScrollFades();
    });
  }

  /* ==========================================================================
     15. RENDER ALL / BOOT
     ========================================================================== */
  function renderAll() {
    applyGlobalCSS();
    renderScene();
    renderTaskbar();
    renderDock();
    renderCompact();
    renderRail();
    if (S.expanded) renderPane();
    renderConsole();
    updateConsoleActive();
    renderSpecCard();
    if (S.annotations) updateAnnotations();
    if (S.settingsOpen) renderSettings();
    applyDragX(false);
    updatePlaybackUI();
    updateFPS();
  }

  var SETTINGS_TABS = ['general', 'appearance', 'island', 'plugins', 'market', 'advanced', 'about'];
  var ISLAND_TABS = ['home', 'media', 'notifications', 'clipboard', 'agent', 'widgets'];

  function applyURLState(state, opts) {
    opts = opts || {};
    if (!state) return;
    var wantTab = opts.tab;
    if (state === 'settings') {
      openSettings(SETTINGS_TABS.indexOf(wantTab) !== -1 ? wantTab : 'general', { instant: true });
      return;
    }
    if (state === 'market') { openSettings('market', { instant: true }); return; }

    if (state === 'expanded') {
      var tab = ISLAND_TABS.indexOf(wantTab) !== -1 ? wantTab : 'home';
      if (S.mode === 'dock') {
        var dockApp = DOCK_APPS.filter(function (a) { return a.tab === tab && a.id !== 'files' && a.id !== 'terminal' && a.id !== 'settings'; })[0];
        S.dockActiveApp = dockApp ? dockApp.id : 'tray';
        expandTo(tab, { instant: true });
        return;
      }
      S.expanded = true;
      S.tab = tab;
      renderShellAttrs();
      renderRail();
      renderPane();
      syncTargets('snap');
      updateConsoleActive();
      return;
    }

    if (state === 'home' || state === 'widgets') {
      if (S.mode === 'dock') {
        S.dockActiveApp = 'tray';
        expandTo(state, { instant: true });
        return;
      }
      S.expanded = true;
      S.tab = state;
      renderShellAttrs();
      renderRail();
      renderPane();
      syncTargets('snap');
      updateConsoleActive();
      return;
    }

    if (S.mode === 'dock') {
      if (state === 'idle') return;
      var tab = tabForView(state === 'hud' || state === 'timer' || state === 'battery' ? 'home' : state);
      S.dockActiveApp = state === 'media' ? 'music' : (state === 'notifications' ? 'notifications' : (state === 'clipboard' ? 'clipboard' : (state === 'agent' ? 'agent' : 'tray')));
      expandTo(tab, { instant: true });
      return;
    }

    if (state === 'idle' || state === 'media' || state === 'notifications' || state === 'clipboard' ||
        state === 'agent' || state === 'hud' || state === 'timer' || state === 'battery') {
      S.view = state;
      if (state === 'hud') {
        S.hudType = opts.hud === 'brightness' ? 'brightness' : 'volume';
        S.stack = 1;
      }
      if (state === 'timer') S.timerRunning = true;
      if (state === 'notifications') S.stack = 3;
      renderCompact();
      renderShellAttrs();
      syncTargets('snap');
    }
  }

  function parseParams() {
    var q = new URLSearchParams(window.location.search);
    var valid = {
      mode: ['float', 'flush', 'dock'],
      material: ['obsidian', 'acrylic'],
      theme: ['dark', 'light'],
      lang: ['zh', 'en'],
      motion: ['normal', 'reduced']
    };
    Object.keys(valid).forEach(function (k) {
      var v = q.get(k);
      if (v && valid[k].indexOf(v) !== -1) S[k] = v;
    });
    return {
      state: q.get('state') || 'idle',
      tab: q.get('tab') || '',
      hud: q.get('hud') || ''
    };
  }

  function boot() {
    ISLAND = $('#island');
    ANCHOR = $('#islandAnchor');
    COMPACT_LAYER = $('#compactLayer');
    expandedLayer = $('#expandedLayer');
    PANEL = $('#panel');
    RAIL = $('#sectionRail');
    ghosts = [$('#ghostA'), $('#ghostB')];
    stackBadge = $('#stackBadge');
    consoleEl = $('#console');
    consoleHandle = $('#consoleHandle');
    settingsWindow = $('#settingsWindow');
    modalLayer = $('#modalLayer');
    toastEl = $('#toast');
    annotationsEl = $('#annotations');
    measureSvg = $('#measureSvg');
    specCard = $('#specCard');
    dockEl = $('#fluidDock');
    taskbarEl = $('#taskbar');
    sceneEl = $('#scene');

    measureTags = document.createElement('div');
    measureTags.className = 'measure-tags';
    annotationsEl.insertBefore(measureTags, specCard);
    measureTags.style.cssText = 'position:fixed;inset:0;pointer-events:none;z-index:2';

    consoleHandle.innerHTML = icon('sliders', 16);
    consoleHandle.setAttribute('aria-expanded', 'true');
    consoleHandle.title = t('console.open');
    consoleHandle.setAttribute('aria-label', t('console.open'));

    var params = parseParams();
    applyGlobalCSS();
    document.body.classList.add('dock-material');
    S.boot = true;

    renderScene();
    renderTaskbar();
    renderDock();
    renderCompact();
    renderRail();
    renderConsole();
    renderSpecCard();
    updateConsoleActive();

    applyURLState(params.state, { instant: true, tab: params.tab, hud: params.hud });

    // Any initial state is applied without a morph, then the boot shade clears.
    if (!S.expanded && S.mode !== 'dock') {
      syncTargets('snap');
    }
    if (S.expanded && S.mode === 'dock') {
      // expandTo already snapped
    }
    updatePlaybackUI();

    /* On a compact desktop, start with the console out of the hero's way. */
    if (window.innerWidth < 1120) {
      S.consoleCollapsed = true;
      consoleEl.classList.add('collapsed');
      consoleHandle.classList.add('visible');
      consoleHandle.setAttribute('aria-expanded', 'false');
    }

    bindIsland();
    bindScene();
    bindConsole();
    bindModal();
    bindDock();
    bindGlobal();

    // Auto-collapse HUD/clipboard never applies to URL-seeded states.
    requestAnimationFrame(function () {
      document.body.classList.add('ready');
      document.documentElement.dataset.ready = '1';
      window.__FB_READY__ = true;
      updateFPS();
    });

    window.setInterval(function () {
      if (S.playing && !document.hidden) {
        var tr = currentTrack();
        S.progress += 1;
        if (S.progress >= tr.duration) {
          startTrack(S.track + 1, true);
        } else {
          updatePlaybackUI();
        }
      }
    }, 1000);

    window.setInterval(function () {
      if (!S.timerRunning || document.hidden) return;
      if (S.timerRemaining > 0) {
        S.timerRemaining--;
        if (S.view === 'timer') updatePlaybackUI();
      } else {
        S.timerRunning = false;
        showToast(S.lang === 'zh' ? '专注完成 · 25:00' : 'Focus complete · 25:00', { kind: 'success', icon: 'timer' });
      }
    }, 1000);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();

