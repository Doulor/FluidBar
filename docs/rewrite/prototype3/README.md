# FluidBar prototype3 · Quiet Objects

A visual study for making the real FluidBar UI belong to the Daylight Optics website without turning the product into a poster.

## Preview

Open `showcase.html` from a static server. It compares the existing prototype2 UI and this candidate side by side, with three live states:

- **Media / Gallery** — cover, metadata, three-line lyric viewport, continuous word fill.
- **Home / Hierarchy** — clock, quick toggles, a quieter media focus, battery. The crowded dual-slider card is removed from this study.
- **Compact / Lyrics** — the 226×40 pill shows the current lyric line instead of competing track title and artist text.

The prototype's real size, six-page state model, transitions, appearance controls, and lyric clock are retained from prototype2. The debug console is hidden in the preview but its handlers remain part of the real prototype.

## What changes visually

- **Cover as color source**: it creates a restrained ambient tint, not a full-page gradient.
- **Lyrics as content**: a borderless three-line lyric window replaces the small lyric card. The active line has a larger type role, with quieter context lines.
- **Less control noise**: no play/pause transport group. No fabricated audio playback.
- **Material edges**: obsidian, acrylic, and liquid glass remain distinct through borders, translucency, and spacing rather than glow effects.
- **Desktop restraint**: a blue-grey desktop keeps the island visually dominant without another glowing background.

## Product boundary

This is a candidate visual implementation, not an Avalonia redesign and not a new product state machine. It does not claim to implement audio playback, timed song acquisition, or a new layout size. The study deliberately uses the actual prototype2 state model and its existing lyric timeline so that the corresponding Skia changes can be assessed rather than presented as an unrelated website mockup.

## Browser verification

Checked with Microsoft Edge / Playwright at 1440×1000 and 390×844:

- Both prototype iframes report `data-ready="1"` for media, home, and compact states.
- Expanded shell geometry: 440×284; content: 400×232.
- No media-page overflow or horizontal page overflow.
- Compact lyric remains a real `media` state.
- Playback controls remain absent.
- Preview debug console is hidden without disabling the underlying prototype's state handlers.

## Assets

`covers/take-me-hand.jpg` is the album art for DAISHI DANCE feat. Cécile Corbel, Take Me Hand. Lyrics are displayed as a visual demonstration. No audio is loaded or played.

`preview-comparison.png` is a current-UI/candidate-UI comparison. The `assets/fonts` directory contains local display fonts for the comparison page.
