"""Generate two self-hosted public-domain song sketches and covers.

Compositions and lyrics are public domain:
- 送别: 李叔同 (lyrics, 1915) / John P. Ordway (melody, 1851)
- 康定情歌: traditional Sichuan folk song
The recordings/arrangements here are original FluidBar assets.
"""
from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SR = 44_100


def midi_hz(note: float) -> float:
    return 440.0 * (2.0 ** ((note - 69) / 12.0))


def env(n: int, attack: float, decay: float, sustain: float, release: float) -> np.ndarray:
    a, d, r = int(attack * SR), int(decay * SR), int(release * SR)
    hold = max(0, n - a - d - r)
    out = np.concatenate([
        np.linspace(0, 1, max(1, a), endpoint=False),
        np.linspace(1, sustain, max(1, d), endpoint=False),
        np.full(hold, sustain),
        np.linspace(sustain, 0, max(1, r)),
    ])
    return out[:n] if len(out) >= n else np.pad(out, (0, n - len(out)))


def pluck(freq: float, dur: float, amp: float) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    sig = np.sin(2 * np.pi * freq * t) + 0.3 * np.sin(4 * np.pi * freq * t)
    return sig * np.exp(-t * 4.8) * (1 - np.exp(-t * 160)) * amp


def pad(freq: float, dur: float, amp: float) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    sig = np.sin(2 * np.pi * freq * t) + 0.18 * np.sin(4 * np.pi * freq * 1.002 * t)
    return sig * env(n, 0.55, 0.6, 0.58, 0.9) * amp


def lead(freq: float, dur: float, amp: float) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.0025 * np.sin(2 * np.pi * 5.2 * t)
    sig = np.sin(2 * np.pi * freq * vib * t) + 0.16 * np.sin(4 * np.pi * freq * t)
    return sig * env(n, 0.01, 0.1, 0.55, 0.18) * amp


def kick(amp: float) -> np.ndarray:
    n = int(0.3 * SR)
    t = np.arange(n) / SR
    f = 88 * np.exp(-t * 18) + 38
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 10) * amp


def snare(amp: float) -> np.ndarray:
    n = int(0.18 * SR)
    t = np.arange(n) / SR
    return (np.random.default_rng(5).normal(0, 1, n) * 0.8 + np.sin(2 * np.pi * 190 * t)) * np.exp(-t * 24) * amp


def add(buf: np.ndarray, at: float, sig: np.ndarray, pan: float = 0) -> None:
    i = int(at * SR)
    if i >= len(buf):
        return
    j = min(len(buf), i + len(sig))
    left = math.sqrt((1 - pan) / 2)
    right = math.sqrt((1 + pan) / 2)
    buf[i:j, 0] += sig[: j - i] * left
    buf[i:j, 1] += sig[: j - i] * right


def render(name: str, bpm: float, chords: list[list[int]], melody: list[tuple[float, float, int]], cover_title: str, accent: tuple[int, int, int]) -> None:
    beat = 60 / bpm
    bars = 16
    total = bars * beat * 4
    n = int(total * SR)
    buf = np.zeros((n, 2), dtype=np.float64)
    for bar in range(bars):
        chord = chords[bar % len(chords)]
        start = bar * beat * 4
        for i, note in enumerate(chord):
            add(buf, start, pad(midi_hz(note), beat * 4, 0.032), [-0.35, 0.35, -0.12, 0.12][i])
        add(buf, start, pad(midi_hz(chord[0] - 12), beat * 2, 0.07))
        for step in range(8):
            add(buf, start + step * beat / 2, pluck(midi_hz(chord[[0, 2, 1, 3][step % 4]] + 12), beat * 0.5, 0.035), -0.4 if step % 2 == 0 else 0.4)
    for beat_pos, duration, note in melody:
        add(buf, beat_pos * beat, lead(midi_hz(note), duration * beat, 0.11), 0.05)
    for bar in range(2, bars):
        start = bar * beat * 4
        add(buf, start, kick(0.3))
        add(buf, start + beat * 2, kick(0.28))
        add(buf, start + beat, snare(0.07))
        add(buf, start + beat * 3, snare(0.07))
    # Simple stereo echo, fade, normalize.
    delay = int(0.26 * SR)
    buf[delay:, 0] += buf[:-delay, 1] * 0.12
    buf[delay:, 1] += buf[:-delay, 0] * 0.12
    t = np.arange(n) / SR
    buf *= (np.minimum(1, t / 1.2) * np.minimum(1, (total - t) / 2.2))[:, None]
    buf *= 0.86 / max(1e-6, np.max(np.abs(buf)))
    pcm = (buf * 32767).astype("<i2")
    out = ROOT / "public" / "audio" / f"{name}.wav"
    out.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(out), "wb") as f:
        f.setnchannels(2)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(pcm.tobytes())

    # Cover art.
    size = 800
    img = Image.new("RGBA", (size, size), (8, 9, 12, 255))
    px = np.array(img).astype(np.float32)
    yy, xx = np.mgrid[0:size, 0:size]
    glow = np.exp(-(((xx - 560) / 410) ** 2 + ((yy - 180) / 330) ** 2) * 3)
    px[..., :3] += glow[..., None] * np.array(accent, dtype=np.float32) * 0.42
    img = Image.fromarray(np.clip(px, 0, 255).astype(np.uint8), "RGBA")
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((52, 52, 748, 748), radius=42, outline=(255, 255, 255, 34), width=2)
    d.ellipse((430, 110, 700, 380), outline=(255, 255, 255, 42), width=2)
    d.rounded_rectangle((92, 360, 410, 462), radius=42, fill=(5, 6, 8, 220), outline=(255, 255, 255, 42))
    title_font = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 86)
    label_font = ImageFont.truetype(r"C:\Windows\Fonts\consola.ttf", 17)
    d.text((92, 530), cover_title, font=title_font, fill=(244, 246, 248, 255))
    d.text((96, 646), "PUBLIC DOMAIN · FLUIDBAR SOUND", font=label_font, fill=(160, 225, 235, 210))
    d.text((96, 676), "SELF-HOSTED INSTRUMENTAL", font=label_font, fill=(244, 246, 248, 90))
    img.save(ROOT / "public" / "posters" / f"cover-{name}.png", "PNG", optimize=True)


# 送别 — C major, lyrical 16 bars.
render(
    "songbie", 72,
    [[48, 52, 55, 59], [43, 47, 50, 55], [45, 48, 52, 55], [41, 45, 48, 52]],
    [
        (0, 1.5, 67), (1.5, .5, 64), (2, 1, 67), (3, 1.5, 72),
        (5, .5, 69), (6, 1, 72), (7, 2, 67),
        (8, 1.5, 67), (9.5, .5, 64), (10, 1, 65), (11, 1, 67), (12, 2, 64),
        (16, 1.5, 69), (17.5, .5, 72), (18, 1, 76), (19, 2, 72),
        (21, 1, 69), (22, 1, 67), (23, 2, 64),
        (24, 1.5, 67), (25.5, .5, 64), (26, 1, 62), (27, 1, 60), (28, 2, 62),
        (32, 1.5, 67), (33.5, .5, 69), (34, 1, 72), (35, 2, 67),
        (37, 1, 69), (38, 1, 67), (39, 2, 64),
        (40, 1.5, 67), (41.5, .5, 64), (42, 1, 62), (43, 1, 60), (44, 2, 62),
        (48, 1.5, 67), (49.5, .5, 64), (50, 1, 67), (51, 2, 72),
        (53, 1, 69), (54, 1, 72), (55, 3, 67),
        (59, 1, 67), (60, 1, 64), (61, 1, 62), (62, 2, 60),
        (64, 1, 62), (65, 1, 64), (66, 1, 67), (67, 3, 60),
    ],
    "送别", (76, 201, 240),
)

# 康定情歌 — D major, brighter 16 bars.
render(
    "kangding", 88,
    [[50, 54, 57, 62], [45, 49, 52, 57], [47, 50, 54, 59], [43, 47, 50, 55]],
    [
        (0, 1, 69), (1, 1, 69), (2, 1, 67), (3, 1, 69),
        (4, 1, 72), (5, 1, 74), (6, 2, 76),
        (8, 1, 74), (9, 1, 72), (10, 1, 69), (11, 2, 67),
        (13, 1, 69), (14, 1, 72), (15, 1, 74),
        (16, 1, 76), (17, 1, 76), (18, 1, 74), (19, 1, 72),
        (20, 1, 69), (21, 1, 72), (22, 2, 74),
        (24, 1, 72), (25, 1, 69), (26, 1, 67), (27, 2, 69),
        (29, 1, 69), (30, 1, 72), (31, 2, 74),
        (32, 1, 76), (33, 1, 76), (34, 1, 79), (35, 2, 76),
        (37, 1, 74), (38, 1, 72), (39, 2, 69),
        (41, 1, 72), (42, 1, 74), (43, 1, 76), (44, 2, 74),
        (48, 1, 72), (49, 1, 69), (50, 1, 67), (51, 2, 69),
        (53, 1, 72), (54, 1, 74), (55, 2, 72),
        (57, 1, 69), (58, 1, 67), (59, 1, 64), (60, 2, 66),
        (64, 1, 67), (65, 1, 69), (66, 1, 72), (67, 3, 69),
    ],
    "康定情歌", (244, 177, 94),
)
