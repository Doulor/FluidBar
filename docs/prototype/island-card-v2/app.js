/* FluidBar expanded-card prototype: rail navigation, wheel chaining, hoverable controls. */
(function () {
  'use strict';

  var icon = window.FB_ICONS.icon;
  var PAGES = [
    { id: 'home', icon: 'home', label: '主页' },
    { id: 'media', icon: 'music', label: '媒体' },
    { id: 'notifications', icon: 'bell', label: '通知' },
    { id: 'clipboard', icon: 'clipboard', label: '剪贴板' },
    { id: 'agent', icon: 'agent', label: 'Agent' },
    { id: 'widgets', icon: 'widgets', label: '小组件' }
  ];

  var state = {
    page: 0,
    volume: 64,
    brightness: 78,
    toggles: { wifi: true, bluetooth: true, focus: false, dark: false },
    dismissed: {},
    copied: -1,
    pinned: {},
    todos: [
      { label: '整理市场发布清单', done: true },
      { label: '检查展开卡片布局', done: false },
      { label: '录制交互预览', done: false }
    ],
    calendar: { year: 2026, month: 9 }
  };

  var NOTIFS = [
    { id: 'n1', app: '微信', icon: 'bell', title: '设计组', body: '新版卡片侧栏的间距已经对齐，可以看下今天的预览。', time: '2 分钟前', color: '#73d99a' },
    { id: 'n2', app: 'GitHub', icon: 'agent', title: 'FluidBar / marketplace', body: 'Workflow run completed successfully · 3 jobs passed.', time: '18 分钟前', color: '#9db7ff' },
    { id: 'n3', app: '日历', icon: 'widgets', title: '15:30 · 设计评审', body: '会议室 B · 带上 472px 展开卡片原型。', time: '1 小时前', color: '#f3c66b' },
    { id: 'n4', app: '邮件', icon: 'bell', title: 'Cloudflare', body: 'fluidbar.doulor.cn 部署成功，缓存正在刷新。', time: '2 小时前', color: '#7fc2d4' }
  ];

  var CLIPS = [
    { id: 'c1', type: '文本', text: '灵动岛展开后，左侧的页面栏应该像桌面 Dock 一样，点一下就能到对应卡片。', time: '刚刚', mono: false },
    { id: 'c2', type: '代码', text: 'const rail = pages.map(page => ({ icon: page.icon, label: page.label }));', time: '3 分钟前', mono: true },
    { id: 'c3', type: '链接', text: 'https://fluidbar.doulor.cn/market/', time: '12 分钟前', mono: true },
    { id: 'c4', type: '文本', text: '鼠标悬停时给所有按钮一个柔和的底色，不要只改变文字颜色。', time: '今天 10:42', mono: false },
    { id: 'c5', type: '文本', text: '滚轮先滚动内容，到边界后再切页。', time: '昨天', mono: false }
  ];

  var AGENTS = [
    { tool: 'Claude Code', project: 'FluidBar', task: '重写展开卡片导航与 hover 状态', status: 'running', duration: '02:14' },
    { tool: 'Codex', project: 'marketplace', task: '检查签名索引和 Release 上传', status: 'done', duration: '00:48' },
    { tool: 'Cursor', project: 'website', task: '同步官网市场静态文件', status: 'done', duration: '01:02' },
    { tool: 'Claude Code', project: 'FluidBar', task: '补充滚轮链路回归测试', status: 'failed', duration: '00:16' }
  ];

  var E = {
    island: document.getElementById('island'),
    rail: document.getElementById('rail'),
    viewport: document.getElementById('viewport'),
    pageNumber: document.getElementById('pageNumber'),
    pageTitle: document.getElementById('pageTitle'),
    toast: document.getElementById('toast')
  };

  function esc(value) {
    return String(value).replace(/[&<>"]/g, function (ch) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[ch];
    });
  }

  function pageBody(page) {
    switch (page.id) {
      case 'media': return mediaPage();
      case 'notifications': return notificationsPage();
      case 'clipboard': return clipboardPage();
      case 'agent': return agentPage();
      case 'widgets': return widgetsPage();
      default: return homePage();
    }
  }

  function homePage() {
    var quick = [
      { id: 'wifi', icon: 'wifi', label: 'Wi-Fi' },
      { id: 'bluetooth', icon: 'bluetooth', label: '蓝牙' },
      { id: 'focus', icon: 'moon', label: '专注' },
      { id: 'dark', icon: 'sun', label: '浅色' }
    ];
    return '<div class="home">' +
      '<div class="home-top">' +
        '<div><div class="clock-time">14:32</div><div class="clock-date">10 月 30 日 · 星期五</div></div>' +
        '<div class="weather">' + icon('cloudSun', 18) +
          '<div><span class="weather-temp">18°</span><span class="weather-sub">多云 · 体感 17°</span></div>' +
        '</div>' +
      '</div>' +
      '<div class="quick-grid">' + quick.map(function (q) {
        var on = state.toggles[q.id];
        return '<button class="q-toggle' + (on ? ' on' : '') + '" type="button" data-act="quick" data-quick="' + q.id + '" aria-pressed="' + on + '">' +
          icon(q.icon, 14) + '<span>' + esc(q.label) + '</span></button>';
      }).join('') + '</div>' +
      '<div class="media-card" data-act="media" role="button" tabindex="0">' +
        '<span class="media-art"></span>' +
        '<div class="media-copy"><span class="media-title">Take Me Hand</span><span class="media-sub">DAISHI DANCE · Wonder Tourism</span>' +
          '<span class="media-progress"><span></span></span></div>' +
      '</div>' +
      '<div class="slider-card">' +
        '<div class="slider-cell">' + icon('volume', 13) +
          '<div class="slider-track"><span style="width:' + state.volume + '%"></span></div><span class="slider-pct">' + state.volume + '</span>' +
        '</div><span class="slider-divider"></span>' +
        '<div class="slider-cell">' + icon('brightness', 13) +
          '<div class="slider-track"><span style="width:' + state.brightness + '%"></span></div><span class="slider-pct">' + state.brightness + '</span>' +
        '</div>' +
      '</div>' +
      '<div class="battery-line"><span class="batt-visual"><span></span></span><strong>86%</strong><span>· 充电中</span><span class="right">约 2 小时 15 分</span></div>' +
    '</div>';
  }

  function mediaPage() {
    return '<div class="media-page">' +
      '<div class="media-top">' +
        '<span class="media-art-lg"></span>' +
        '<div class="media-info"><div class="eyebrow">正在播放</div><div class="track-title">Take Me Hand</div>' +
          '<div class="media-artist">DAISHI DANCE feat. Cécile Corbel</div><div class="media-album">Wonder Tourism · 2012</div></div>' +
      '</div>' +
      '<div class="lyric-card"><div class="lyric-head"><span>逐行歌词</span><span>08 / 29</span></div>' +
        '<div class="lyric-lines">' +
          '<span class="lyric-line">Rising stars</span>' +
          '<span class="lyric-line is-active">Remember the day</span>' +
          '<span class="lyric-line">I fell in love with you</span>' +
        '</div></div>' +
      '<div class="seek-row"><span>1:24</span><div class="seek" data-act="seek"><span class="seek-fill"></span><span class="seek-thumb"></span></div><span>3:34</span></div>' +
      '<div class="media-foot"><div class="transport">' +
        '<button type="button" data-act="prev" aria-label="上一首">' + icon('prev', 13) + '</button>' +
        '<button type="button" class="play" data-act="play" aria-label="暂停">' + icon('pause', 13) + '</button>' +
        '<button type="button" data-act="next" aria-label="下一首">' + icon('next', 13) + '</button>' +
      '</div><div class="media-buttons">' +
        '<button class="pill-button" type="button" data-act="output">' + icon('speaker', 11) + '<span>扬声器</span></button>' +
        '<button class="pill-button" type="button" data-act="queue">' + icon('queue', 11) + '<span>队列 · 3</span></button>' +
      '</div></div></div>';
  }

  function notificationsPage() {
    var visible = NOTIFS.filter(function (n) { return !state.dismissed[n.id]; });
    if (!visible.length) return '<div class="empty">' + icon('bell', 24) + '<div class="empty-title">全部处理完了</div><div class="empty-sub">新的消息会出现在这里</div></div>';
    return '<div class="scroll"><div class="list">' + visible.map(function (n) {
      return '<div class="row" data-act="notif-open" data-id="' + n.id + '" role="button" tabindex="0">' +
        '<span class="row-glyph" style="color:' + n.color + ';background:' + n.color + '22">' + icon(n.icon, 13) + '</span>' +
        '<div class="row-main"><div class="row-line"><span class="row-app">' + esc(n.app) + '</span><span class="row-title">' + esc(n.title) + '</span><span class="row-time">' + esc(n.time) + '</span></div>' +
        '<div class="row-body">' + esc(n.body) + '</div></div>' +
        '<div class="row-actions"><button class="icon-btn" type="button" data-act="notif-dismiss" data-id="' + n.id + '" aria-label="关闭">' + icon('close', 10) + '</button></div>' +
      '</div>';
    }).join('') + '</div></div>';
  }

  function clipboardPage() {
    return '<div class="scroll"><div class="list">' + CLIPS.map(function (c) {
      return '<div class="row clip-row" data-act="clip-copy" data-id="' + c.id + '" role="button" tabindex="0">' +
        '<span class="row-glyph">' + icon(c.type === '代码' ? 'agent' : c.type === '链接' ? 'external' : 'clipboard', 13) + '</span>' +
        '<div class="row-main"><div class="clip-copy"' + (c.mono ? ' style="font-family:ui-monospace,monospace"' : '') + '>' + esc(c.text) + '</div>' +
          '<div class="clip-sub"><span>' + esc(c.type) + '</span><span>·</span><span>' + esc(c.time) + '</span></div></div>' +
        '<div class="row-actions"><button class="icon-btn" type="button" data-act="pin" data-id="' + c.id + '" aria-label="置顶">' + icon('pin', 11) + '</button>' +
          '<button class="icon-btn" type="button" data-act="clip-copy" data-id="' + c.id + '" aria-label="复制">' + icon(state.copied === c.id ? 'check' : 'copy', 11) + '</button></div>' +
      '</div>';
    }).join('') + '</div></div>';
  }

  function agentPage() {
    return '<div class="scroll"><div class="list">' + AGENTS.map(function (a) {
      return '<div class="agent-row">' +
        '<span class="agent-state ' + a.status + '">' + (a.status === 'running' ? '<span class="spinner"></span>' : icon(a.status === 'done' ? 'check' : 'alert', 11)) + '</span>' +
        '<div class="agent-main"><div><span class="agent-tool">' + esc(a.tool) + '</span><span class="agent-project">' + esc(a.project) + '</span></div>' +
          '<div class="agent-task">' + esc(a.task) + '</div></div>' +
        '<div class="agent-meta"><span class="agent-status">' + (a.status === 'running' ? '运行中' : a.status === 'done' ? '已完成' : '失败') + '</span><span class="agent-duration">' + esc(a.duration) + '</span></div>' +
      '</div>';
    }).join('') + '</div></div>';
  }

  function widgetPageBody() {
    return '<div class="widgets">' +
      '<div class="cal-card"><div class="cal-head"><span class="cal-month">' + state.calendar.month + ' 月 ' + state.calendar.year + '</span>' +
        '<span class="cal-nav"><button type="button" data-act="cal-prev" aria-label="上个月">' + icon('chevronLeft', 9) + '</button><button type="button" data-act="cal-next" aria-label="下个月">' + icon('chevronRight', 9) + '</button></span></div>' +
        '<div class="cal-week">' + ['一','二','三','四','五','六','日'].map(function (d) { return '<span>' + d + '</span>'; }).join('') + '</div>' +
        '<div class="cal-grid">' + Array.from({ length: 35 }, function (_, i) { var n = i - 2; return n < 1 || n > 31 ? '<span class="cal-day mute">' + (n < 1 ? 29 + n : n - 31) + '</span>' : '<span class="cal-day' + (n === 30 ? ' today' : n === 6 || n === 18 ? ' event' : '') + '">' + n + '</span>'; }).join('') + '</div>' +
        '<div class="cal-foot"><span class="dot"></span><span>15:30 设计评审</span><span style="margin-left:auto">今天</span></div></div>' +
      '<div class="widget-side">' +
        '<div class="widget-card weather-card">' + icon('cloudSun', 18) + '<div><span class="w-temp">18°</span><span class="w-desc">多云</span></div><div class="w-meta"><strong>21° / 14°</strong><span>东南风 12 km/h</span></div></div>' +
        '<div class="widget-card stats-card">' +
          '<div class="stat-row"><span class="stat-label">CPU</span><span class="stat-track"><span class="stat-fill" style="width:38%"></span></span><span>38%</span></div>' +
          '<div class="stat-row"><span class="stat-label">内存</span><span class="stat-track"><span class="stat-fill ram" style="width:64%"></span></span><span>64%</span></div>' +
          '<div class="stat-row"><span class="stat-label">电量</span><span class="stat-track"><span class="stat-fill batt" style="width:86%"></span></span><span>86%</span></div>' +
        '</div>' +
        '<div class="widget-card notes-card"><div class="notes-head">今日任务<span>' + state.todos.filter(function (t) { return t.done; }).length + ' / ' + state.todos.length + '</span></div>' +
          state.todos.map(function (t, i) { return '<button class="todo' + (t.done ? ' done' : '') + '" type="button" data-act="todo" data-index="' + i + '"><span class="check">' + icon('check', 6) + '</span><span class="todo-label">' + esc(t.label) + '</span></button>'; }).join('') +
        '</div></div></div>';
  }

  function widgetsPage() { return '<div class="scroll">' + widgetPageBody() + '</div>'; }

  function renderRail() {
    E.rail.innerHTML = PAGES.map(function (p, i) {
      return '<button class="tab' + (i === state.page ? ' is-active' : '') + '" type="button" data-page="' + i + '" aria-label="' + esc(p.label) + '" aria-current="' + (i === state.page) + '">' +
        icon(p.icon, 17) + '<span class="tab-label">' + esc(p.label) + '</span></button>';
    }).join('');
  }

  function renderPages() {
    E.viewport.innerHTML = PAGES.map(function (p, i) {
      return '<section class="page' + (i === state.page ? ' is-active' : '') + '" data-page="' + p.id + '" aria-label="' + esc(p.label) + '">' + pageBody(p) + '</section>';
    }).join('');
    var p = PAGES[state.page];
    E.pageNumber.textContent = ('0' + (state.page + 1)).slice(-2);
    E.pageTitle.textContent = p.label;
    renderRail();
  }

  function showToast(text) {
    E.toast.textContent = text;
    E.toast.classList.add('is-visible');
    clearTimeout(showToast.timer);
    showToast.timer = setTimeout(function () { E.toast.classList.remove('is-visible'); }, 1400);
  }

  function goPage(index) {
    var next = Math.max(0, Math.min(PAGES.length - 1, index));
    if (next === state.page) return;
    state.page = next;
    renderPages();
  }

  var wheel = { accum: 0, last: 0 };
  function turn(delta) {
    var now = performance.now();
    if (wheel.accum && Math.sign(delta) !== Math.sign(wheel.accum)) wheel.accum = 0;
    wheel.accum += delta;
    if (Math.abs(wheel.accum) < 70 || now - wheel.last < 140) return;
    wheel.last = now;
    var sign = wheel.accum > 0 ? 1 : -1;
    wheel.accum = 0;
    goPage(state.page + sign);
  }

  E.island.addEventListener('wheel', function (e) {
    if (!e.deltaY && !e.deltaX) return;
    var delta = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
    var tab = e.target.closest ? e.target.closest('.tab') : null;
    if (tab) {
      e.preventDefault();
      turn(delta);
      return;
    }
    var scroller = e.target.closest ? e.target.closest('.scroll') : null;
    if (scroller && scroller.scrollHeight > scroller.clientHeight + 1) {
      var down = delta > 0;
      var can = down ? scroller.scrollTop + scroller.clientHeight < scroller.scrollHeight - 1 : scroller.scrollTop > 0;
      if (can) return;
    }
    e.preventDefault();
    turn(delta);
  }, { passive: false });

  E.rail.addEventListener('click', function (e) {
    var tab = e.target.closest('.tab');
    if (tab) goPage(Number(tab.dataset.page));
  });

  E.island.addEventListener('click', function (e) {
    var target = e.target.closest('[data-act]');
    if (!target) return;
    var act = target.dataset.act;
    if (act === 'quick') {
      var key = target.dataset.quick;
      state.toggles[key] = !state.toggles[key];
      target.classList.toggle('on', state.toggles[key]);
      target.setAttribute('aria-pressed', state.toggles[key]);
      showToast(state.toggles[key] ? '已打开' : '已关闭');
    } else if (act === 'media') {
      goPage(1);
    } else if (act === 'notif-dismiss') {
      e.stopPropagation();
      state.dismissed[target.dataset.id] = true;
      renderPages();
    } else if (act === 'notif-open') {
      showToast('打开通知详情');
    } else if (act === 'clip-copy') {
      e.stopPropagation();
      state.copied = target.dataset.id;
      renderPages();
      showToast('已复制到剪贴板');
    } else if (act === 'pin') {
      e.stopPropagation();
      state.pinned[target.dataset.id] = !state.pinned[target.dataset.id];
      showToast(state.pinned[target.dataset.id] ? '已置顶' : '已取消置顶');
    } else if (act === 'cal-prev' || act === 'cal-next') {
      state.calendar.month += act === 'cal-prev' ? -1 : 1;
      if (state.calendar.month < 1) { state.calendar.month = 12; state.calendar.year--; }
      if (state.calendar.month > 12) { state.calendar.month = 1; state.calendar.year++; }
      renderPages();
    } else if (act === 'todo') {
      var todo = state.todos[Number(target.dataset.index)];
      todo.done = !todo.done;
      renderPages();
    } else if (act === 'collapse') {
      showToast('收起灵动岛（原型）');
    } else if (act === 'settings') {
      showToast('打开设置（原型）');
    } else if (act === 'play') {
      showToast('播放 / 暂停（原型）');
    } else if (['prev', 'next', 'output', 'queue', 'seek'].indexOf(act) >= 0) {
      showToast('媒体控制（原型）');
    }
  });

  E.island.addEventListener('keydown', function (e) {
    if (e.key === 'ArrowDown' || e.key === 'PageDown') { e.preventDefault(); goPage(state.page + 1); }
    if (e.key === 'ArrowUp' || e.key === 'PageUp') { e.preventDefault(); goPage(state.page - 1); }
  });

  document.querySelector('[data-act="settings"]').innerHTML = icon('settings', 14);
  document.querySelector('[data-act="collapse"]').innerHTML = icon('chevronDown', 14);
  renderPages();
})();
