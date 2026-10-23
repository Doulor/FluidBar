/* FluidBar · yield-animation lab
   Six presets for "what the island does when it yields". Click 让位/恢复, press 1–6 to switch,
   G for the side-by-side grid, Space to play the current preset. */

(function () {
  "use strict";

  var VARIANTS = [
    {
      id: "a", letter: "A", name: "淡出",
      desc: "原地淡到 12% 不透明度，不移动。最安静：看起来像\"岛还在，只是暂时不理你\"。恢复时也是淡回来。",
      meta: [["动作", "opacity 1 → .12"], ["时长", "160 ms"], ["位移", "无"], ["恢复", "淡入 160 ms"], ["风险", "亮背景上可能像\"消失\""]]
    },
    {
      id: "b", letter: "B", name: "滑出屏幕顶部",
      desc: "用 200 ms 滑出屏幕上边缘（向上移 190% 高度）。让位最彻底：视野里完全没有东西，恢复时从顶部滑回来。",
      meta: [["动作", "translateY(0 → -190%)"], ["时长", "200 ms"], ["缓动", "cubic-bezier(.4,0,.2,1)"], ["恢复", "滑回 260 ms"], ["风险", "快速反复触发时\"进出\"感最强"]]
    },
    {
      id: "c", letter: "C", name: "后退",
      desc: "缩小到 84%、下沉 10 px、变淡到 45%：像往后让了一步，但仍然看得见。介于\"淡出\"和\"滑走\"之间。",
      meta: [["动作", "scale .84 + translateY(10px)"], ["时长", "200 ms"], ["缓动", "cubic-bezier(.34,1.4,.64,1)"], ["恢复", "260 ms"], ["风险", "缩放会让文字短暂变糊"]]
    },
    {
      id: "d", letter: "D", name: "角落停靠",
      desc: "滑到屏幕右上角并缩到 62%，保持 80% 不透明度。始终留在视野边缘，想点时随时能找到它。",
      meta: [["动作", "translateX(+400px) + scale(.62)"], ["时长", "260 ms"], ["缓动", "cubic-bezier(.32,.72,0,1)"], ["恢复", "滑回 300 ms"], ["风险", "角落可能压到别的 UI"]]
    },
    {
      id: "e", letter: "E", name: "柔化",
      desc: "模糊 6 px + 降饱和 + 淡到 25%，位置几乎不变。像玻璃\"化开\"，视觉噪音最低。",
      meta: [["动作", "blur(6px) saturate(.55) opacity .25"], ["时长", "240 ms"], ["位移", "scale .98"], ["恢复", "240 ms"], ["风险", "模糊元素多时会吃 GPU"]]
    },
    {
      id: "f", letter: "F", name: "只剩轮廓",
      desc: "填充、模糊、内容全部退掉，只留一圈 1 px 的亮边。\"这里有个东西，但它不挡你\"——最不打扰又不会丢位置。",
      meta: [["动作", "背景 .04 + 描边 .30 + 内容 .12"], ["时长", "200 ms"], ["位移", "无"], ["恢复", "200 ms"], ["风险", "暗背景上轮廓几乎不可见"]]
    }
  ];

  var state = { variant: "a", yielding: false, loop: 0, grid: false };

  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }
  function variantById(id) { return VARIANTS.filter(function (v) { return v.id === id; })[0]; }

  function renderTabs() {
    var tabs = $("#tabs");
    tabs.innerHTML = VARIANTS.map(function (v, i) {
      return '<button type="button" data-variant="' + v.id + '">' + (i + 1) + " · " + v.letter + " " + v.name + "</button>";
    }).join("");
    $$("button", tabs).forEach(function (b) {
      b.addEventListener("click", function () { select(b.dataset.variant); });
    });
  }

  function syncTabs() {
    $$("#tabs button").forEach(function (b) {
      b.classList.toggle("active", b.dataset.variant === state.variant);
    });
  }

  function renderPanel(v) {
    $("#vKicker").textContent = v.letter;
    $("#vName").textContent = v.name;
    $("#vDesc").textContent = v.desc;
    $("#vMeta").innerHTML = v.meta.map(function (row) {
      return "<dt>" + row[0] + "</dt><dd>" + row[1] + "</dd>";
    }).join("");
  }

  function setYield(on) {
    state.yielding = on;
    var stage = $("#stage");
    stage.classList.toggle("yielding", on);
    $("#hud").textContent = on ? "Yielding（不接收点击）" : "Interactive";
  }

  function select(id) {
    state.variant = id;
    var stage = $("#stage");
    stage.dataset.variant = id;
    document.body.dataset.variant = id;
    setYield(false);
    renderPanel(variantById(id));
    syncTabs();
    if (state.grid) { renderGrid(); }
  }

  function toggleLoop() {
    if (state.loop) {
      clearInterval(state.loop);
      state.loop = 0;
      $("#loopBtn").classList.remove("active");
      setYield(false);
      return;
    }

    $("#loopBtn").classList.add("active");
    setYield(true);
    state.loop = setInterval(function () { setYield(!state.yielding); }, 1400);
  }

  function renderGrid() {
    var grid = $("#grid");
    grid.innerHTML = "";
    VARIANTS.forEach(function (v) {
      var cell = document.createElement("div");
      cell.className = "cell";
      cell.innerHTML = "<h3>" + v.letter + " · " + v.name + "<span>yielding</span></h3>";
      var stage = $("#stage").cloneNode(true);
      stage.id = "stage-" + v.id;
      stage.dataset.variant = v.id;
      stage.classList.add("yielding");
      $$(".hud", stage).forEach(function (h) { h.remove(); });
      cell.appendChild(stage);
      grid.appendChild(cell);
    });
  }

  function setGrid(on) {
    state.grid = on;
    $("#grid").classList.toggle("on", on);
    $("#single").style.display = on ? "none" : "";
    if (on) { renderGrid(); }
  }

  renderTabs();
  select("a");

  $("#playBtn").addEventListener("click", function () { setYield(!state.yielding); });
  $("#loopBtn").addEventListener("click", toggleLoop);
  $("#gridBtn").addEventListener("click", function () { setGrid(!state.grid); });

  document.addEventListener("keydown", function (e) {
    if (e.target && /input|textarea/i.test(e.target.tagName)) { return; }
    var n = parseInt(e.key, 10);
    if (n >= 1 && n <= VARIANTS.length) { select(VARIANTS[n - 1].id); setGrid(false); }
    if (e.key === "g" || e.key === "G") { setGrid(!state.grid); }
    if (e.key === " ") { e.preventDefault(); setYield(!state.yielding); }
  });

  var q = new URLSearchParams(location.search);
  var qv = q.get("v");
  if (qv && VARIANTS.some(function (v) { return v.id === qv; })) { select(qv); }
  if (q.get("instant") === "1") { document.body.dataset.instant = "1"; }
  if (q.get("yield") === "1") { setYield(true); }
  if (q.get("grid") === "1") { setGrid(true); }
})();
