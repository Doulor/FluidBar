const controls = document.querySelector('[data-catalog-controls]');
if (controls) {
  const search = controls.querySelector('input[type="search"]');
  const filters = [...controls.querySelectorAll('[data-filter]')];
  const cards = [...document.querySelectorAll('[data-plugin-card]')];
  const count = document.querySelector('[data-result-count]');
  const empty = document.querySelector('[data-empty]');
  const initial = new URL(location.href);
  let selected = filters.some(button => button.dataset.filter === initial.searchParams.get('kind')) ? initial.searchParams.get('kind') : 'all';
  search.value = initial.searchParams.get('q') || '';
  controls.hidden = false;

  function update(writeUrl = true) {
    const query = search.value.trim().toLocaleLowerCase();
    let visible = 0;
    for (const card of cards) {
      const match = (selected === 'all' || card.dataset.kind === selected) && card.dataset.search.toLocaleLowerCase().includes(query);
      card.hidden = !match;
      if (match) visible++;
    }
    for (const button of filters) button.setAttribute('aria-pressed', String(button.dataset.filter === selected));
    count.textContent = query || selected !== 'all' ? `${visible} 个匹配结果 / 共 ${cards.length} 个插件` : `共 ${cards.length} 个插件`;
    empty.hidden = visible !== 0;
    if (writeUrl) {
      const url = new URL(location.href);
      if (search.value.trim()) url.searchParams.set('q', search.value.trim()); else url.searchParams.delete('q');
      if (selected !== 'all') url.searchParams.set('kind', selected); else url.searchParams.delete('kind');
      history.replaceState(null, '', url);
    }
  }
  search.addEventListener('input', () => update());
  filters.forEach(button => button.addEventListener('click', () => { selected = button.dataset.filter; update(); }));
  document.querySelector('[data-reset]').addEventListener('click', () => { search.value = ''; selected = 'all'; update(); search.focus(); });
  window.addEventListener('pageshow', () => update(false));
  update(false);
}

const copyButton = document.querySelector('[data-copy-hash]');
if (copyButton) {
  copyButton.hidden = false;
  copyButton.addEventListener('click', async () => {
    const hash = document.querySelector('.package-hash');
    const status = document.querySelector('.copy-status');
    try {
      await navigator.clipboard.writeText(hash.textContent.trim());
      status.textContent = 'SHA-256 校验值已复制。';
      copyButton.textContent = '已复制';
    } catch {
      const selection = window.getSelection();
      const range = document.createRange();
      range.selectNodeContents(hash);
      selection.removeAllRanges();
      selection.addRange(range);
      status.textContent = '复制未成功，已选中校验值，请手动复制。';
      copyButton.textContent = '请手动复制';
    }
  });
}
