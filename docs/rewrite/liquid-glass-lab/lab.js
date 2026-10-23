/* FluidBar · liquid-glass lab (v2)
   Variants A–G + live tuner. The refraction variants generate a rounded-rect SDF displacement map
   and a specular map at runtime (ported from archisvaze/liquid-glass, MIT). */

(function () {
  "use strict";

  var VARIANTS = [
    { id: "a", letter: "A", name: "磨砂玻璃", tech: "css", blur: 28, sat: 185, tintRgb: "10,12,16", tintA: 0.34, edge: 0.76, refract: 0, brightness: 1.02,
      desc: "Apple .regular 的保守版本：大半径高斯模糊 + 提饱和 + 34% 深色染色，只有一条 1px 顶部高光。没有折射，最稳、最不花。",
      meta: [["模糊", "28 px"], ["饱和", "185%"], ["染色", "rgba(10,12,16,.34)"], ["折射", "无"], ["边缘", "1px 高光"]] },
    { id: "b", letter: "B", name: "折射玻璃", tech: "svg", thickness: 40, bezel: 18, ior: 1.8, blur: 10, sat: 160, tintRgb: "8,10,14", tintA: 0.30, edge: 0.55, refract: 1.0, brightness: 1.0,
      desc: "网页液态玻璃的主流做法（rdev/archisvaze 路线）：背景先模糊，再按圆角矩形 SDF 做边缘位移，rgb 通道轻微错位产生色散，最后叠一张镜面高光。",
      meta: [["模糊", "10 px + 位移"], ["饱和", "160%"], ["染色", "rgba(8,10,14,.30)"], ["折射", "SDF 位移"], ["色散", "0.05–0.1×"]] },
    { id: "c", letter: "C", name: "清透玻璃", tech: "css", blur: 12, sat: 210, tintRgb: "0,0,0", tintA: 0.14, edge: 0.9, refract: 0, brightness: 1.06,
      desc: "Apple .clear：低模糊、高饱和、几乎不着色，靠 1px 亮边把形状撑起来。适合照片/视频背景。",
      meta: [["模糊", "12 px"], ["饱和", "210%"], ["染色", "rgba(0,0,0,.14)"], ["折射", "无"], ["边缘", "0.7 高光"]] },
    { id: "d", letter: "D", name: "厚镜玻璃", tech: "svg", thickness: 90, bezel: 26, ior: 2.4, blur: 16, sat: 160, tintRgb: "10,12,16", tintA: 0.22, edge: 0.7, refract: 1.45, brightness: 1.0,
      desc: "厚玻璃：更大的折射率与折射带，边缘像真实玻璃一样把背景“拉”进来，中心保持清晰。最接近 iOS 26 的 lensing 观感。",
      meta: [["模糊", "16 px + 位移"], ["饱和", "160%"], ["染色", "rgba(10,12,16,.22)"], ["折射", "厚镜 (IOR 2.4)"], ["色散", "轻"]] },
    { id: "e", letter: "E", name: "水色玻璃", tech: "svg", thickness: 55, bezel: 20, ior: 2.0, blur: 12, sat: 175, tintRgb: "26,96,122", tintA: 0.28, edge: 0.7, refract: 1.1, brightness: 1.0,
      desc: "带品牌色的玻璃：青色染色 + 折射 + 顶部冷光。适合媒体播放态，但会改变背景色相，信息密度高的页面慎用。",
      meta: [["模糊", "12 px + 位移"], ["饱和", "175%"], ["染色", "rgba(26,96,122,.28)"], ["折射", "SDF 位移"], ["色调", "偏青"]] },
    { id: "f", letter: "F", name: "烟灰玻璃", tech: "css", blur: 34, sat: 150, tintRgb: "24,26,32", tintA: 0.46, edge: 0.55, refract: 0, brightness: 0.96,
      desc: "深色玻璃：最大模糊 + 46% 染色，边缘对比最低、阴影最软。在亮背景上最稳，也最接近现在 FluidBar 的深色岛。",
      meta: [["模糊", "34 px"], ["饱和", "150%"], ["染色", "rgba(24,26,32,.46)"], ["折射", "无"], ["边缘", "0.4 高光"]] },
    { id: "g", letter: "G", name: "纯玻璃", tech: "css", blur: 26, sat: 170, tintRgb: "8,10,14", tintA: 0.30, edge: 0, refract: 0, brightness: 1.02,
      desc: "完全没有描边和高光：只有背景模糊 + 染色 + 柔和阴影。如果你觉得“边”很假，这是最干净的方案。",
      meta: [["模糊", "26 px"], ["饱和", "170%"], ["染色", "rgba(8,10,14,.30)"], ["折射", "无"], ["边缘", "无"]] }
  ];

  var state = { variant: "b", blur: true, grid: false, tune: {} };
  var uid = 0;
  var rebuildTimer = 0;

  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }
  function variantById(id) { return VARIANTS.filter(function (v) { return v.id === id; })[0]; }

  function baseTune(v) {
    return { blur: v.blur, tint: Math.round(v.tintA * 100), sat: v.sat, edge: Math.round(v.edge * 100), refract: Math.round(v.refract * 100) };
  }

  /* ------------------------------------------------------------ SDF maps */

  function heightFn(x) {
    var t = Math.max(0, Math.min(1, x));
    return Math.pow(1 - Math.pow(1 - t, 4), 0.25);
  }

  function refractionProfile(thickness, bezel, ior, samples) {
    samples = samples || 96;
    var eta = 1 / ior;
    var profile = new Float64Array(samples);
    for (var i = 0; i < samples; i++) {
      var x = i / samples;
      var y = heightFn(x);
      var dx = x < 1 ? 0.0001 : -0.0001;
      var y2 = heightFn(x + dx);
      var deriv = (y2 - y) / dx;
      var mag = Math.sqrt(deriv * deriv + 1);
      var nx = -deriv / mag, ny = -1 / mag, dot = ny;
      var k = 1 - eta * eta * (1 - dot * dot);
      if (k < 0) { profile[i] = 0; continue; }
      var sq = Math.sqrt(k);
      var rx = -(eta * dot + sq) * nx;
      var ry = eta - (eta * dot + sq) * ny;
      profile[i] = ry === 0 ? 0 : rx * ((y * bezel + thickness) / ry);
    }
    return profile;
  }

  function displacementMap(w, h, radius, bezel, profile, maxDisp) {
    var c = document.createElement("canvas"); c.width = w; c.height = h;
    var ctx = c.getContext("2d");
    var img = ctx.createImageData(w, h), d = img.data;
    for (var i = 0; i < d.length; i += 4) { d[i] = 128; d[i + 1] = 128; d[i + 2] = 0; d[i + 3] = 255; }
    var r = radius, rSq = r * r, r1Sq = (r + 1) * (r + 1);
    var rBSq = Math.max(r - bezel, 0) * Math.max(r - bezel, 0);
    var wB = w - r * 2, hB = h - r * 2, S = profile.length;
    for (var y1 = 0; y1 < h; y1++) {
      for (var x1 = 0; x1 < w; x1++) {
        var px = x1 < r ? x1 - r : x1 >= w - r ? x1 - r - wB : 0;
        var py = y1 < r ? y1 - r : y1 >= h - r ? y1 - r - hB : 0;
        var dSq = px * px + py * py;
        if (dSq > r1Sq || dSq < rBSq) { continue; }
        var dist = Math.sqrt(dSq);
        if (dist === 0) { continue; }
        var fromSide = r - dist;
        var op = dSq < rSq ? 1 : 1 - (dist - Math.sqrt(rSq)) / (Math.sqrt(r1Sq) - Math.sqrt(rSq));
        if (op <= 0) { continue; }
        var cos = px / dist, sin = py / dist;
        var bi = Math.min(((fromSide / bezel) * S) | 0, S - 1);
        var disp = profile[bi] || 0;
        var idx = (y1 * w + x1) * 4;
        d[idx] = (128 + (-cos * disp / maxDisp) * 127 * op + 0.5) | 0;
        d[idx + 1] = (128 + (-sin * disp / maxDisp) * 127 * op + 0.5) | 0;
      }
    }
    ctx.putImageData(img, 0, 0);
    return c.toDataURL("image/png");
  }

  function specularMap(w, h, radius, bezel, angle) {
    angle = angle == null ? Math.PI / 3 : angle;
    var c = document.createElement("canvas"); c.width = w; c.height = h;
    var ctx = c.getContext("2d");
    var img = ctx.createImageData(w, h), d = img.data;
    var r = radius, r1Sq = (r + 1) * (r + 1);
    var rBSq = Math.max(r - bezel, 0) * Math.max(r - bezel, 0);
    var wB = w - r * 2, hB = h - r * 2;
    var sv = [Math.cos(angle), Math.sin(angle)];
    for (var y1 = 0; y1 < h; y1++) {
      for (var x1 = 0; x1 < w; x1++) {
        var px = x1 < r ? x1 - r : x1 >= w - r ? x1 - r - wB : 0;
        var py = y1 < r ? y1 - r : y1 >= h - r ? y1 - r - hB : 0;
        var dSq = px * px + py * py;
        if (dSq > r1Sq || dSq < rBSq) { continue; }
        var dist = Math.sqrt(dSq);
        if (dist === 0) { continue; }
        var fromSide = r - dist;
        var op = dSq < r * r ? 1 : 1 - (dist - r) / (Math.sqrt(r1Sq) - r);
        if (op <= 0) { continue; }
        var cos = px / dist, sin = -py / dist;
        var dot = Math.abs(cos * sv[0] + sin * sv[1]);
        var edge = Math.sqrt(Math.max(0, 1 - (1 - fromSide) * (1 - fromSide)));
        var coeff = dot * edge;
        var col = (255 * coeff) | 0;
        var idx = (y1 * w + x1) * 4;
        d[idx] = col; d[idx + 1] = col; d[idx + 2] = col;
        d[idx + 3] = (col * coeff * op) | 0;
      }
    }
    ctx.putImageData(img, 0, 0);
    return c.toDataURL("image/png");
  }

  function buildFilter(id, w, h, radius, p, tune) {
    var profile = refractionProfile(p.thickness, p.bezel, p.ior, 96);
    var maxDisp = 1;
    for (var i = 0; i < profile.length; i++) { maxDisp = Math.max(maxDisp, Math.abs(profile[i])); }
    var scale = Math.min(maxDisp * 0.5 * (tune.refract / 100), 40);
    var disp = displacementMap(w, h, radius, p.bezel, profile, maxDisp);
    var spec = specularMap(w, h, radius, p.bezel, Math.PI / 3);
    var sat = (tune.sat / 100).toFixed(2);
    return '<filter id="' + id + '" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB">' +
      '<feGaussianBlur in="SourceGraphic" stdDeviation="' + tune.blur + '" result="blurred"/>' +
      '<feImage href="' + disp + '" x="0" y="0" width="' + w + '" height="' + h + '" preserveAspectRatio="none" result="disp"/>' +
      '<feDisplacementMap in="blurred" in2="disp" scale="' + scale.toFixed(2) + '" xChannelSelector="R" yChannelSelector="G" result="displaced"/>' +
      '<feColorMatrix in="displaced" type="saturate" values="' + sat + '" result="sat"/>' +
      '<feImage href="' + spec + '" x="0" y="0" width="' + w + '" height="' + h + '" preserveAspectRatio="none" result="spec"/>' +
      '<feComponentTransfer in="spec" result="specGlow"><feFuncA type="linear" slope="' + (tune.edge / 100 * 0.9).toFixed(2) + '"/></feComponentTransfer>' +
      '<feBlend in="specGlow" in2="sat" mode="screen"/>' +
      '</filter>';
  }

  /* ------------------------------------------------------------ apply */

  function applyGlass(stage, tune, clearDefs) {
    var v = variantById(stage.dataset.variant);
    if (!v) { return; }
    var defs = $("#svg-defs");
    if (clearDefs !== false) { defs.innerHTML = ""; }
    var isSvg = v.tech === "svg";

    $$(".glass", stage).forEach(function (el) {
      var tint = "rgba(" + v.tintRgb + "," + (tune.tint / 100).toFixed(3) + ")";
      el.style.setProperty("--tint", tint);
      el.style.setProperty("--edge-hi", "rgba(255,255,255," + (tune.edge / 100 * 0.72).toFixed(3) + ")");
      el.style.setProperty("--edge", "rgba(255,255,255," + (tune.edge / 100 * 0.14).toFixed(3) + ")");
      if (isSvg) {
        el.style.setProperty("--bd", "none");
      } else {
        el.style.setProperty("--bd", "blur(" + tune.blur + "px) saturate(" + tune.sat + "%) brightness(" + v.brightness + ")");
        el.style.removeProperty("--filter");
      }
    });

    if (!isSvg) { return; }

    $$(".glass", stage).forEach(function (el) {
      var w = el.offsetWidth, h = el.offsetHeight;
      if (!w || !h) { return; }
      var radius = parseFloat(getComputedStyle(el).borderTopLeftRadius) || Math.min(w, h) / 2;
      var id = "lgf-" + (++uid);
      var first = el.offsetWidth > 0 ? el : null;
      if (!first) { return; }
      var parsed = new DOMParser().parseFromString(
        '<svg xmlns="http://www.w3.org/2000/svg">' + buildFilter(id, w, h, radius, v, tune) + "</svg>",
        "image/svg+xml");
      defs.appendChild(document.importNode(parsed.documentElement.firstChild, true));
      el.style.setProperty("--bd", "url(#" + id + ")");
    });
  }

  function renderTuner(v, tune) {
    $("#tBlur").value = tune.blur;
    $("#tTint").value = tune.tint;
    $("#tSat").value = tune.sat;
    $("#tEdge").value = tune.edge;
    $("#tRefract").value = tune.refract;
    $("#tBlurV").textContent = tune.blur + " px";
    $("#tTintV").textContent = tune.tint + "%";
    $("#tSatV").textContent = tune.sat + "%";
    $("#tEdgeV").textContent = tune.edge + "%";
    $("#tRefractV").textContent = v.tech === "svg" ? (tune.refract / 100).toFixed(2) + "×" : "—";
    $("#tRefractRow").style.display = v.tech === "svg" ? "" : "none";
  }

  function renderPanel(v) {
    $("#vKicker").textContent = v.letter;
    $("#vName").textContent = v.name;
    $("#vDesc").textContent = v.desc;
    $("#vMeta").innerHTML = v.meta.map(function (row) {
      return "<dt>" + row[0] + "</dt><dd>" + row[1] + "</dd>";
    }).join("");
  }

  function renderTabs() {
    var tabs = $("#tabs");
    tabs.innerHTML = VARIANTS.map(function (v, i) {
      return '<button type="button" data-variant="' + v.id + '">' + (i + 1) + " · " + v.letter + " " + v.name + "</button>";
    }).join("");
    $$("button", tabs).forEach(function (btn) {
      btn.addEventListener("click", function () { select(btn.dataset.variant); });
    });
  }

  function syncTabs() {
    $$("#tabs button").forEach(function (b) {
      b.classList.toggle("active", b.dataset.variant === state.variant);
    });
  }

  function select(id) {
    state.variant = id;
    state.tune = baseTune(variantById(id));
    document.body.dataset.variant = id;
    var stage = $("#stage");
    stage.dataset.variant = id;
    applyGlass(stage, state.tune);
    renderPanel(variantById(id));
    renderTuner(variantById(id), state.tune);
    syncTabs();
    if (state.grid) { renderGrid(); }
  }

  function setBlur(on) {
    state.blur = on;
    document.body.dataset.blur = on ? "on" : "off";
    $("#blurToggle").checked = on;
  }

  function buildStage(v, clone) {
    var stage = clone ? $("#stage").cloneNode(true) : $("#stage");
    stage.dataset.variant = v.id;
    return stage;
  }

  function renderGrid() {
    var grid = $("#grid");
    grid.innerHTML = "";
    VARIANTS.forEach(function (v, i) {
      var cell = document.createElement("div");
      cell.className = "cell";
      cell.innerHTML = "<h3>" + v.letter + " · " + v.name + "<span>" + v.tech.toUpperCase() + "</span></h3>";
      var stage = buildStage(v, true);
      cell.appendChild(stage);
      grid.appendChild(cell);
      applyGlass(stage, baseTune(v), i === 0);
    });
  }

  function setGrid(on) {
    state.grid = on;
    $("#grid").hidden = !on;
    $("#single").hidden = on;
    if (on) { renderGrid(); }
  }

  function paramsJson() {
    var v = variantById(state.variant);
    var t = state.tune;
    return JSON.stringify({
      variant: v.id + " · " + v.name,
      blurPx: t.blur,
      tint: "rgba(" + v.tintRgb + ", " + (t.tint / 100).toFixed(2) + ")",
      saturation: t.sat + "%",
      edgeHighlight: (t.edge / 100 * 0.72).toFixed(2),
      refraction: v.tech === "svg" ? (t.refract / 100).toFixed(2) + "×" : "none"
    }, null, 2);
  }

  /* ------------------------------------------------------------ boot */

  renderTabs();
  select("b");

  $("#blurToggle").addEventListener("change", function (e) { setBlur(e.target.checked); });
  $("#gridBtn").addEventListener("click", function () { setGrid(!state.grid); });

  function bindTuner(inputId, key) {
    $(inputId).addEventListener("input", function (e) {
      state.tune[key] = parseInt(e.target.value, 10);
      renderTuner(variantById(state.variant), state.tune);
      clearTimeout(rebuildTimer);
      rebuildTimer = setTimeout(function () {
        applyGlass($("#stage"), state.tune);
      }, 90);
    });
  }
  bindTuner("#tBlur", "blur");
  bindTuner("#tTint", "tint");
  bindTuner("#tSat", "sat");
  bindTuner("#tEdge", "edge");
  bindTuner("#tRefract", "refract");

  $("#copyParams").addEventListener("click", function () {
    var text = paramsJson();
    var hint = $("#copyHint");
    function ok() { hint.textContent = "已复制"; setTimeout(function () { hint.textContent = ""; }, 1500); }
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(ok, function () { fallback(); });
    } else { fallback(); }
    function fallback() {
      var ta = document.createElement("textarea");
      ta.value = text;
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand("copy"); ok(); } catch (e) { hint.textContent = text.replace(/\n/g, " "); }
      document.body.removeChild(ta);
    }
  });

  document.addEventListener("keydown", function (e) {
    if (e.target && /input|textarea/i.test(e.target.tagName)) { return; }
    var n = parseInt(e.key, 10);
    if (n >= 1 && n <= VARIANTS.length) { select(VARIANTS[n - 1].id); setGrid(false); }
    if (e.key === "b" || e.key === "B") { setBlur(!state.blur); }
    if (e.key === "g" || e.key === "G") { setGrid(!state.grid); }
  });

  var resizeTimer = 0;
  window.addEventListener("resize", function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      applyGlass($("#stage"), state.tune);
      if (state.grid) { renderGrid(); }
    }, 180);
  });

  var q = new URLSearchParams(location.search);
  var qv = q.get("v");
  if (qv && VARIANTS.some(function (v) { return v.id === qv; })) { select(qv); }
  if (q.get("blur") === "off") { setBlur(false); }
  if (q.get("grid") === "1") { setGrid(true); }
  if (q.get("probe") === "1") {
    setTimeout(function () {
      var stageRect = $("#stage").getBoundingClientRect();
      var glass = $$("#stage .glass").map(function (el) {
        var r = el.getBoundingClientRect();
        return { cls: el.className, x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height), bd: getComputedStyle(el).backdropFilter };
      });
      document.title = JSON.stringify({ stage: { x: Math.round(stageRect.left), y: Math.round(stageRect.top), w: Math.round(stageRect.width), h: Math.round(stageRect.height) }, glass: glass });
    }, 60);
  }
})();
