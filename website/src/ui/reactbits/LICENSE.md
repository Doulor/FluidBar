# Third-party attribution: ReactBits

The components in this directory are vendored from **ReactBits** by David Haz
(https://reactbits.dev), using the official **TS-CSS** registry flavor.

- Upstream repository: https://github.com/DavidHDev/react-bits
- Upstream license: https://github.com/DavidHDev/react-bits/blob/main/LICENSE.md
  (MIT + Commons Clause License Condition v1.0)
- Upstream copyright: Copyright (c) 2026 David Haz

## Vendored sources

| Local file | Upstream component | Official source |
| --- | --- | --- |
| `PillNav.tsx`, `PillNav.css` | PillNav | https://reactbits.dev/components/pill-nav — registry: https://reactbits.dev/r/PillNav-TS-CSS.json |
| `SpecularButton.tsx`, `SpecularButton.css` | SpecularButton | https://reactbits.dev/components/specular-button — registry: https://reactbits.dev/r/SpecularButton-TS-CSS.json |
| `SpotlightCard.tsx`, `SpotlightCard.css` | SpotlightCard | https://reactbits.dev/components/spotlight-card — registry: https://reactbits.dev/r/SpotlightCard-TS-CSS.json |
| `BorderGlow.tsx`, `BorderGlow.css` | BorderGlow | https://reactbits.dev/components/border-glow — registry: https://reactbits.dev/r/BorderGlow-TS-CSS.json |
| `Noise.tsx`, `Noise.css` | Noise | https://reactbits.dev/animations/noise — registry: https://reactbits.dev/r/Noise-TS-CSS.json |
| `CountUp.tsx` | CountUp | https://reactbits.dev/text-animations/count-up — registry: https://reactbits.dev/r/CountUp-TS-CSS.json |

## Local modifications

Only the following changes were made to the upstream source so that it works in
this Vite + React 19 TypeScript project:

- Removed the Next.js `'use client'` directive (this is a client-only Vite app).
- Converted `React.*` namespace type references to explicit named type imports
  from `react` (e.g. `React.CSSProperties` -> `CSSProperties`).
- Exported the `BorderGlowProps` and `CountUpProps` interfaces so that
  `index.ts` can re-export the public prop types.
- Fixed module paths so each component keeps its CSS co-located in this
  directory (`import './<Name>.css'`).
- `CountUp` imports its animation primitives from the locally installed
  `motion/react` package (`motion@^14`); no GSAP, three.js, OGL, or Hugeicons
  code was introduced.

No other behavioral changes were made.

---

## MIT + Commons Clause License Condition v1.0

Copyright (c) 2026 David Haz

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, and distribute the Software **as part
of an application, website, or product**, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

### Commons Clause Restriction

You may use this Software, including for any commercial purpose, **so long as
you do not sell, sublicense, or redistribute the components themselves-whether
alone, in a bundle, or as a ported version.**

The same restriction covers the React Bits tools (Background Studio, Texture
Lab, Shape Magic, Center Stage, Sound Lab and any tools added later) and the
sounds in the sound library. **You may not sell, sublicense, or redistribute
the tools or the sounds themselves, or offer the tools or a substantially
similar copy of them as a hosted service-whether alone, in a bundle, or as a
ported version.**

What you make with the tools is yours to use. You can use exported code,
images, videos, and audio files in any personal or commercial application,
website, or product, including paid ones. **You may not sell or redistribute
the exports on their own, either individually or as the product itself, such
as a sound, texture, background, or template pack.**

### No Warranty

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
