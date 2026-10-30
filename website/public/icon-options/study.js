const options = [
  ['01-single', '单岛', '只保留一枚白色胶囊。最简洁的起点，缩小后也容易辨认。'],
  ['02-signal', '一点', '保留一个低饱和蓝色状态点，延续现用图标的识别方式。'],
  ['03-floating', '悬浮', '胶囊下留一条短线，表现小岛与桌面之间的悬浮关系。'],
  ['04-outline', '轮廓', '用一道胶囊轮廓代替实心块，视觉更轻。'],
  ['05-layers', '错层', '两条错开的短胶囊，表达内容切换和层次。'],
  ['06-silver', '银底', '银白底、黑色胶囊。适合更轻、更明亮的图标方向。'],
  ['07-letter-f', '字母 F', '直接使用几何 F 字标，以粗实笔画建立品牌识别。'],
  ['08-monogram', '合字 FB', '将 FluidBar 的两个首字母并排组合，使用黑白双色。'],
  ['09-soft-f', '柔笔 f', '小写 f 配柔和的绿灰底，弧线比几何字标更亲近。'],
  ['10-step-f', '阶梯 F', '直角切口与短横线，偏理性、工具感的字标。'],
  ['11-letter-b', '圆角 b', '以圆角小写 b 为主形，保留一个宽阔的内圆。'],
  ['12-cutout-f', '负形 F', '在白色圆角块中挖出 F，让字母通过留白出现。'],
  ['13-orbit', '轨道', '一圈倾斜的细轨道围住胶囊，表达围绕桌面运行。'],
  ['14-window', '窗口', '将桌面窗口简化为线框和一条悬浮胶囊。'],
  ['15-arch', '拱门', '一条开放的拱线，包住底部的小岛，偏建筑几何。'],
  ['16-negative', '留白', '实心圆中的胶囊留白，适合做单色印记。'],
  ['17-continuous', '连环', '连续的一笔双环，表现流动和不断切换的状态。'],
  ['18-brackets', '留隙', '左右括弧围住短胶囊，以开放边界表达容纳内容。'],
  ['19-ivory', '暖纸', '暖纸色底配棕色胶囊，少一点科技感，多一点日常感。'],
  ['20-blue', '雾蓝', '低饱和蓝底配白色悬浮标记，与新桌面背景呼应。'],
  ['21-sage', '鼠尾草', '绿灰色中的深绿胶囊，用一个缺口增加辨识度。'],
  ['22-horizon', '日界', '陶土色配被地平线分开的圆形，偏生活与桌面氛围。'],
  ['23-titanium', '钛灰', '硬朗圆角、两道细刻线，偏工业工具的视觉。'],
  ['24-ink', '墨线', '稍不规则的墨色轮廓，像纸面上的一笔小岛。'],
];
const container = document.querySelector('#options');
const groups = [
  ['简洁几何', '01—06 · 从小岛本身出发，保留最少的形状。'],
  ['字母字标', '07—12 · 用 F / FB / b 建立更直接的品牌识别。'],
  ['空间负形', '13—18 · 用边界、留白和连续线条表达桌面关系。'],
  ['柔和色彩', '19—24 · 暖纸、雾蓝、绿灰与墨线，尝试不同的性格。'],
];
const grids = groups.map(([name, description]) => {
  const section = document.createElement('section');
  section.className = 'option-family';
  const heading = document.createElement('h2'); heading.textContent = name;
  const summary = document.createElement('p'); summary.textContent = description;
  const grid = document.createElement('div'); grid.className = 'options-grid';
  section.append(heading, summary, grid); container.append(section);
  return grid;
});
function select(index) {
  const [file, name] = options[index];
  const source = `./icon-options/${file}.svg`;
  document.querySelector('#selected-image').src = source;
  document.querySelector('#selected-name').textContent = `${file.slice(0, 2)} · ${name}`;
  const download = document.querySelector('#download');
  download.href = source;
  download.download = `fluidbar-${file}.svg`;
  history.replaceState(null, '', `#${file}`);
}
options.forEach(([file, name, description], index) => {
  const label = document.createElement('label');
  label.className = 'option';
  const source = `./icon-options/${file}.svg`;
  label.innerHTML = `<span class="option-head"><span>${file.slice(0, 2)} · ${name}</span><input type="radio" name="icon" value="${file}" aria-label="${file.slice(0, 2)} · ${name}"></span><span class="large"><img src="${source}" width="88" height="88" alt="${name}图标"></span><span class="description">${description}</span><span class="sizes">${[16, 24, 32].map(size => `<span class="sample"><img src="${source}" width="${size}" height="${size}" alt=""><span>${size}px</span></span>`).join('')}<span class="dark-sample" title="深色背景下的 32px 图标"><img src="${source}" width="32" height="32" alt="深色背景下的${name}图标"></span></span>`;
  label.querySelector('input').addEventListener('change', () => select(index));
  grids[Math.floor(index / 6)].append(label);
});
// A selected candidate can be shared as an ordinary URL fragment.
function selectFromFragment() {
  const index = Math.max(0, options.findIndex(([file]) => `#${file}` === location.hash));
  container.querySelectorAll('input')[index].checked = true;
  select(index);
}
window.addEventListener('hashchange', selectFromFragment);
selectFromFragment();
