# Self-hosted webfont sources

Last updated: 2026-10-28. Font payload: four WOFF2 files, **665,952 bytes total (650.3 KiB)**.

These files are referenced by `website/src/styles/fonts.css` from `website/src/assets/fonts/`, so Vite can hash them and apply the GitHub Pages base path. The license files and this document remain in `website/public/fonts/`.

## Satoshi — variable, weights 300–900

- File: `Satoshi-Variable.woff2` — **42,588 bytes** — SHA-256 `e739aff9b4d02c264341d6d4872edcda28e79373aeda936f659566a1cd3eb47f`
- Source (official Fontshare download): <https://api.fontshare.com/v2/fonts/download/satoshi>
  - path inside ZIP: `Satoshi_Complete/Fonts/WEB/fonts/Satoshi-Variable.woff2`
  - byte-identical to the variable WOFF2 served by the Fontshare API for `https://api.fontshare.com/v2/css?f[]=satoshi@1`:
    <https://cdn.fontshare.com/wf/NWBQYJIM7GCZ5XWD7D26ARB3VDY55ZRT/K63EV2KZIGKLE7RANQ2U42S6SVHU5RJ7/X6XYTKIVDUW7GZTZPZNN4EUM5KH54KHF.woff2>
- License: **ITF Free Font License (FFL) v2.0** — see `LICENSE-Satoshi.txt`. Self-hosting is expressly permitted; the file is shipped **unmodified** (the FFL forbids modification/subsetting without written consent).
- Verified: WOFF2 magic `wOF2`, `fvar` axis `wght` 300–900, 504 glyphs.

## IBM Plex Mono — regular 400 and medium 500

- `IBMPlexMono-Regular.woff2` — **14,708 bytes** — SHA-256 `08949f728dc52d528e69b1667d15c89a5686a4ee9a296ff90983985f99c380f7`
- `IBMPlexMono-Medium.woff2` — **14,888 bytes** — SHA-256 `01d285447409c8a588692162439a038b8cbd7871309ee20267b0d2d91c6e8e22`
- Source (Google Fonts CSS API, latin webfont subsets):
  - <https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&display=swap>
  - 400: <https://fonts.gstatic.com/s/ibmplexmono/v20/-F63fjptAgt5VM-kVkqdyU8n1i8q1w.woff2>
  - 500: <https://fonts.gstatic.com/s/ibmplexmono/v20/-F6qfjptAgt5VM-kVkqdyU8n3twJwlBFgg.woff2>
- License: **SIL Open Font License 1.1**, Reserved Font Name "Plex" — see `LICENSE-IBMPlexMono.txt`. Files are the official Google Fonts subsets, unmodified (no local subsetting/renaming, so the reserved font name is not used by a derivative).

## Noto Sans SC — subset variable, weights 100–900 (CSS uses 300–900)

- File: `NotoSansSC-Subset.woff2` — **593,768 bytes** — SHA-256 `0b00fd46f8fa51fd2fe6561c3dd16e52e1f0b5106551c2397b0142bb77db5113`
- Source: Noto Sans SC variable font (`NotoSansSC[wght].ttf`) from the Google Fonts repository under OFL:
  - <https://raw.githubusercontent.com/google/fonts/main/ofl/notosanssc/NotoSansSC%5Bwght%5D.ttf>
- Subset contents: **2,428 codepoints** = 2,147 Hanzi + 281 ASCII / Latin / CJK punctuation / fullwidth forms.
  - 100% of the Chinese characters and CJK punctuation currently used by the site (841 unique codepoints collected from `README.md`, `docs/rewrite/specs/2026-10-27-official-website-design.md`, `docs/rewrite/prototype2/app.js`, `website/**`, `website/index.html`).
  - The **2,100 most frequent** Hanzi from Jun Da's modern-Chinese character frequency list, restricted to the 3,500 common-character set: ≈ **97.5 %** cumulative frequency coverage (top 1,000 ≈ 89.1 %, top 2,500 ≈ 98.5 %, all 3,500 ≈ 99.5 %). Chinese copy is not final, so the frequency list gives headroom for new marketing copy.
  - CJK punctuation blocks U+3000–303F and U+FF01–FF5E, printable ASCII, arrows/math/geometric symbols used by the site.
- Subset command (fontTools 4.65):
  ```
  python -m fontTools.subset "NotoSansSC[wght].ttf" \
    --text-file=chars.txt --flavor=woff2 --output-file=NotoSansSC-Subset.woff2 \
    --layout-features=kern,liga,clig,calt,ccmp \
    --drop-tables+=BASE,vhea,vmtx
  ```
  `chars.txt` = site characters ∪ CJK punctuation ∪ printable ASCII ∪ top-2,100 frequent Hanzi.
- A full 3,500-character variable subset is ~1,007 KB, above the 700 KB payload target; this file is the largest frequency-limited subset that keeps total font payload under budget. To grow coverage, add the extra characters to `chars.txt` and re-run the command (expect ~230 bytes/glyph).
- License: **SIL Open Font License 1.1** — see `LICENSE-NotoSansSC.txt`.

## Verification

- All four files start with the WOFF2 signature `wOF2`, decompress and parse with fontTools, and load in Microsoft Edge (Playwright, headed): all four `@font-face` rules reported `status: "loaded"`; Satoshi and Noto Sans SC variable axes render visibly different weights; Chinese sample text renders with no missing-glyph boxes.
