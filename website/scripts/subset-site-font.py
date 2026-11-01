"""Regenerate the marketing font after Chinese copy changes (requires fonttools[woff]).

The original licensed font stays intact. This is an optional asset maintenance
command, not a Python dependency of the Vite build. Missing characters fall back
to the platform's Chinese font.
"""
from pathlib import Path

from fontTools import subset

root = Path(__file__).resolve().parents[1]
source = root / 'src/assets/fonts/NotoSansSC-Subset.woff2'
target = root / 'src/assets/fonts/NotoSansSC-Site.woff2'
copy = ''.join(path.read_text(encoding='utf-8') for path in (root / 'src').rglob('*.tsx'))
copy += (root / 'src/island/compactStates.ts').read_text(encoding='utf-8')
characters = ''.join(sorted(set(copy)))
options = subset.Options()
options.flavor = 'woff2'
options.layout_features = ['*']
font = subset.load_font(str(source), options)
subsetter = subset.Subsetter(options=options)
subsetter.populate(text=characters)
subsetter.subset(font)
subset.save_font(font, str(target), options)
print(f'{target.name}: {target.stat().st_size:,} bytes (source {source.stat().st_size:,})')
