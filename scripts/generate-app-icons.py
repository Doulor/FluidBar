"""Derive all Windows icon sizes and the in-app bitmap from the browser-rendered SVG."""
from pathlib import Path
import sys
from PIL import Image, ImageDraw

master_path, assets_path, reports_path = map(Path, sys.argv[1:])
master = Image.open(master_path).convert("RGBA")
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
master.save(assets_path / "fluidbar.ico", format="ICO", sizes=[(size, size) for size in sizes])
master.resize((512, 512), Image.Resampling.LANCZOS).save(assets_path / "fluidbar.png")
preview = Image.new("RGB", (880, 330), "#eff1f4")
draw = ImageDraw.Draw(preview)
for row, background in enumerate(["#eff1f4", "#171b22"]):
    top = row * 165
    draw.rectangle((0, top, 880, top + 165), fill=background)
    x = 20
    for size in sizes:
        # Limit the comparison strip to 128 px; the 256 px resource is verified from the ICO below.
        if size > 128:
            break
        icon = master.resize((size, size), Image.Resampling.LANCZOS)
        preview.paste(icon, (x, top + (140 - size) // 2), icon)
        draw.text((x, top + 143), str(size), fill="#697584" if row == 0 else "#b3bdc9")
        x += size + 27
preview.save(reports_path / "sizes.png")
with Image.open(assets_path / "fluidbar.ico") as icon:
    assert icon.ico.sizes() == {(size, size) for size in sizes}
print("Generated fluidbar.ico (16–256 px), fluidbar.png (512 px), and size previews.")
