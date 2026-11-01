"""Regenerate the market CJK font from its real copy (optional fonttools[woff])."""
import json
from pathlib import Path
from fontTools import subset

root = Path(__file__).resolve().parents[1]
copy = ''.join(p.read_text(encoding='utf-8') for p in (root / 'marketplace').glob('*.mjs'))
for path in [root / 'public/market/index.json', *(root / 'public/market/plugins').glob('*.json')]:
    copy += json.dumps(json.loads(path.read_text(encoding='utf-8')), ensure_ascii=False)
options = subset.Options()
options.flavor = 'woff2'
options.layout_features = ['*']
font = subset.load_font(str(root / 'src/assets/fonts/NotoSansSC-Subset.woff2'), options)
subsetter = subset.Subsetter(options=options)
subsetter.populate(text=''.join(sorted(set(copy))))
subsetter.subset(font)
target = root / 'src/assets/fonts/NotoSansSC-Market.woff2'
subset.save_font(font, str(target), options)
print(f'{target.name}: {target.stat().st_size:,} bytes')
