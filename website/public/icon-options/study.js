const options = [
  ['01-single', '单岛', '只保留一枚白色胶囊。六款里最简洁，缩小后也容易辨认。'],
  ['02-signal', '一点', '保留一个低饱和蓝色状态点，延续现用图标的识别方式。'],
  ['03-floating', '悬浮', '胶囊下留一条短线，表现小岛与桌面之间的悬浮关系。'],
  ['04-outline', '轮廓', '用一道胶囊轮廓代替实心块，视觉更轻。'],
  ['05-layers', '错层', '两条错开的短胶囊，表达内容切换和层次。'],
  ['06-silver', '银底', '银白底、黑色胶囊。适合更轻、更明亮的图标方向。'],
];
const container = document.querySelector('#options');
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
  container.append(label);
});
// A selected candidate can be shared as an ordinary URL fragment.
const initial = Math.max(0, options.findIndex(([file]) => `#${file}` === location.hash));
container.querySelectorAll('input')[initial].checked = true;
select(initial);
