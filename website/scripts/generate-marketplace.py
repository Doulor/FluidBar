"""Build the static FluidBar marketplace layer from the signed source index.

The signed index is copied byte-for-byte. Nothing in this script parses and
rewrites `market/index.json`; only the derived shards, assets and static
listing pages are generated.
"""

from __future__ import annotations

import html
import json
import shutil
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
PUBLIC = ROOT / "website" / "public"
SOURCE_INDEX = ROOT / "marketplace" / "index.json"
SOURCE_SCHEMAS = ROOT / "schemas"
SOURCE_SHOTS = ROOT / "docs" / "rewrite" / "prototype2"
MARKET = PUBLIC / "market"
SCHEMAS = PUBLIC / "schemas"
SITE = "https://fluidbar.doulor.cn"

PLUGIN_SHOTS = {
    "sample-minimal": ("compact.png", "shot-01-rest.png"),
    "sample-rich": ("expanded-home.png", "shot-04-expanded-home.png"),
}

LISTING_CSS = """
:root { color-scheme: light; --ink:#10151b; --muted:#5d6874; --line:#d8dee5; --paper:#f3f5f6; --card:#fff; --accent:#2447ff; }
* { box-sizing:border-box; }
body { margin:0; background:var(--paper); color:var(--ink); font:15px/1.65 "Segoe UI", system-ui, sans-serif; }
main { width:min(980px, calc(100% - 40px)); margin:0 auto; padding:64px 0 80px; }
a { color:var(--accent); }
.eyebrow { margin:0 0 16px; color:var(--muted); font:11px/1.4 "Cascadia Mono", Consolas, monospace; letter-spacing:.12em; }
h1 { margin:0; font-size:clamp(38px,6vw,64px); line-height:1; letter-spacing:-.055em; }
.lede { max-width:700px; margin:20px 0 0; color:var(--muted); font-size:17px; }
.meta { display:flex; flex-wrap:wrap; gap:10px 20px; margin:26px 0 0; color:var(--muted); font-size:12px; }
.badge { display:inline-block; margin-right:6px; padding:2px 7px; border:1px solid var(--line); border-radius:99px; background:var(--card); font-size:11px; }
.hero-shot { display:block; width:100%; height:auto; margin:34px 0 0; border:1px solid var(--line); border-radius:18px; background:#080a0d; box-shadow:0 24px 55px -30px rgba(12,20,30,.4); }
.grid { display:grid; grid-template-columns:minmax(0,1fr) 280px; gap:28px; margin-top:38px; }
.panel { padding:26px; border:1px solid var(--line); border-radius:18px; background:var(--card); }
.panel h2 { margin:0 0 14px; font-size:20px; letter-spacing:-.02em; }
.panel p, .panel li { color:var(--muted); }
.panel ul { margin:0; padding-left:20px; }
.code { overflow:auto; margin:14px 0 0; padding:14px; border-radius:10px; background:#11161c; color:#dbe4ec; font:12px/1.6 "Cascadia Mono", Consolas, monospace; }
.download { display:inline-block; margin-top:12px; padding:10px 14px; border-radius:8px; background:var(--ink); color:#fff; text-decoration:none; font-size:13px; }
.back { margin:48px 0 0; color:var(--muted); font-size:12px; }
@media (max-width:760px) { .grid { grid-template-columns:1fr; } main { padding-top:36px; } .panel { padding:20px; } }
"""


def localized(value, locale: str = "en-US") -> str:
    if isinstance(value, str):
        return value
    return value.get(locale) or next(iter(value.values()), "")


def icon(path: Path, plugin_id: str) -> None:
    image = Image.new("RGBA", (96, 96), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    if plugin_id == "sample-rich":
        fill = (19, 27, 38, 255)
        accent = (123, 156, 255, 255)
        accent2 = (240, 106, 176, 255)
    else:
        fill = (12, 16, 21, 255)
        accent = (76, 201, 240, 255)
        accent2 = (255, 255, 255, 210)
    draw.rounded_rectangle((5, 5, 91, 91), radius=25, fill=fill, outline=(255, 255, 255, 56), width=2)
    if plugin_id == "sample-rich":
        draw.ellipse((24, 24, 72, 72), outline=accent, width=6)
        draw.ellipse((42, 42, 54, 54), fill=accent2)
        draw.arc((18, 18, 78, 78), 205, 330, fill=(255, 255, 255, 110), width=2)
    else:
        draw.rounded_rectangle((24, 43, 72, 53), radius=5, fill=accent)
        draw.ellipse((63, 44, 72, 53), fill=accent2)
    image.save(path, "PNG", optimize=True)


def listing_html(plugin: dict, shot_path: str) -> str:
    name = localized(plugin["name"])
    summary = localized(plugin["summary"])
    latest = plugin["latest"]
    screenshot = f"{SITE}/market/assets/{plugin['id']}/{shot_path}"
    page_url = f"{SITE}/market/listings/{plugin['id']}/"
    changelog = html.escape(latest.get("changelog") or "")
    capabilities = "".join(f"<li><code>{html.escape(item)}</code></li>" for item in latest["capabilities"])
    locales = ", ".join(html.escape(item) for item in latest.get("locales", []))
    title = html.escape(name)
    desc = html.escape(summary)
    ld = {
        "@context": "https://schema.org",
        "@type": "SoftwareApplication",
        "name": name,
        "description": summary,
        "applicationCategory": "UtilitiesApplication",
        "operatingSystem": "Windows 10, Windows 11",
        "softwareVersion": latest["version"],
        "url": page_url,
        "downloadUrl": latest["artifact"]["url"],
        "screenshot": screenshot,
        "publisher": {"@type": "Organization", "name": plugin["publisher"]["displayName"]},
        "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
    }
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} · FluidBar Marketplace</title>
<meta name="description" content="{desc}">
<link rel="canonical" href="{page_url}">
<meta property="og:type" content="website">
<meta property="og:title" content="{title} · FluidBar Marketplace">
<meta property="og:description" content="{desc}">
<meta property="og:url" content="{page_url}">
<meta property="og:image" content="{screenshot}">
<meta property="og:image:width" content="1440">
<meta property="og:image:height" content="900">
<meta name="twitter:card" content="summary_large_image">
<script type="application/ld+json">{json.dumps(ld, ensure_ascii=False, separators=(",", ":"))}</script>
<style>{LISTING_CSS}</style>
</head>
<body>
<main>
<p class="eyebrow">FLUIDBAR / MARKETPLACE / PLUGIN</p>
<h1>{title}</h1>
<p class="lede">{desc}</p>
<div class="meta"><span class="badge">v{latest["version"]}</span><span>{html.escape(plugin["publisher"]["displayName"])}</span><span>API {latest["apiVersion"]}</span><span>{html.escape(latest["license"])}</span></div>
<img class="hero-shot" src="{screenshot}" alt="{html.escape(name)} screenshot" width="1440" height="900">
<div class="grid">
<section class="panel">
<h2>Install</h2>
<p>Download the signed package, then install it from FluidBar → Settings → Plugins → Install from file.</p>
<a class="download" href="{html.escape(latest["artifact"]["url"])}">Download .fbp</a>
<pre class="code">SHA-256\n{html.escape(latest["artifact"]["sha256"])}</pre>
</section>
<aside class="panel">
<h2>Details</h2>
<ul><li>Version: {latest["version"]}</li><li>API: {latest["apiVersion"]}</li><li>Min app: {latest["minAppVersion"]}</li><li>Locales: {locales}</li></ul>
<h2 style="margin-top:24px">Capabilities</h2>
<ul>{capabilities}</ul>
</aside>
</div>
<div class="grid">
<section class="panel"><h2>Changelog</h2><pre class="code">{changelog}</pre></section>
<section class="panel"><h2>Publisher</h2><p>{html.escape(plugin["publisher"]["displayName"])}</p><p>Signed marketplace index · ECDSA-P256-SHA256</p></section>
</div>
<p class="back"><a href="{SITE}/market/">FluidBar Marketplace</a></p>
</main>
</body>
</html>
"""


def market_index_html(plugins: list[dict]) -> str:
    cards = []
    for plugin in plugins:
        name = html.escape(localized(plugin["name"]))
        summary = html.escape(localized(plugin["summary"]))
        icon = f"{SITE}/market/assets/{plugin['id']}/icon.png"
        href = f"{SITE}/market/listings/{plugin['id']}/"
        cards.append(
            f'<a class="plugin" href="{href}"><img src="{icon}" alt="" width="48" height="48">'
            f'<span><strong>{name}</strong><small>{summary}</small></span><b>v{plugin["latest"]["version"]}</b></a>'
        )
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>FluidBar Marketplace</title>
<meta name="description" content="Plugins for FluidBar 2.0, signed and reviewed for the Windows desktop island.">
<link rel="canonical" href="{SITE}/market/">
<style>
:root {{ color-scheme:light; --ink:#10151b; --muted:#5d6874; --line:#d8dee5; --paper:#f3f5f6; --card:#fff; --accent:#2447ff; }}
* {{ box-sizing:border-box; }}
body {{ margin:0; background:var(--paper); color:var(--ink); font:15px/1.6 "Segoe UI",system-ui,sans-serif; }}
main {{ width:min(860px,calc(100% - 40px)); margin:0 auto; padding:72px 0 90px; }}
.eyebrow {{ margin:0 0 14px; color:var(--muted); font:11px/1.4 "Cascadia Mono",Consolas,monospace; letter-spacing:.13em; }}
h1 {{ margin:0; font-size:clamp(42px,7vw,78px); line-height:.94; letter-spacing:-.065em; }}
.lede {{ max-width:650px; margin:22px 0 34px; color:var(--muted); font-size:17px; }}
.plugins {{ display:grid; gap:10px; }}
.plugin {{ display:flex; align-items:center; gap:15px; padding:16px; border:1px solid var(--line); border-radius:14px; background:var(--card); color:inherit; text-decoration:none; }}
.plugin:hover {{ border-color:#9aa7b4; }}
.plugin img {{ flex:none; border-radius:10px; }}
.plugin span {{ display:flex; min-width:0; flex-direction:column; gap:3px; }}
.plugin strong {{ font-size:16px; }}
.plugin small {{ overflow:hidden; color:var(--muted); font-size:12px; text-overflow:ellipsis; white-space:nowrap; }}
.plugin b {{ margin-left:auto; flex:none; color:var(--muted); font:11px "Cascadia Mono",Consolas,monospace; }}
@media(max-width:560px) {{ .plugin small {{ white-space:normal; }} .plugin b {{ display:none; }} }}
</style>
</head>
<body><main>
<p class="eyebrow">FLUIDBAR / MARKETPLACE</p>
<h1>Tools for<br>the quiet island.</h1>
<p class="lede">Signed sample plugins for FluidBar 2.0. Each package is reviewed, hashed and published through the marketplace index.</p>
<nav class="plugins" aria-label="Plugins">{''.join(cards)}</nav>
</main></body>
</html>
"""


def main() -> None:
    data = json.loads(SOURCE_INDEX.read_text(encoding="utf-8"))
    MARKET.mkdir(parents=True, exist_ok=True)
    (MARKET / "plugins").mkdir(exist_ok=True)
    (MARKET / "assets").mkdir(exist_ok=True)
    (MARKET / "listings").mkdir(exist_ok=True)
    SCHEMAS.mkdir(exist_ok=True)
    (MARKET / "index.html").write_text(market_index_html(data["plugins"]), encoding="utf-8")

    # Signed bytes are intentionally copied, never serialized.
    shutil.copyfile(SOURCE_INDEX, MARKET / "index.json")

    for plugin in data["plugins"]:
        plugin_id = plugin["id"]
        shard = {"versions": [plugin["latest"]]}
        (MARKET / "plugins" / f"{plugin_id}.json").write_text(
            json.dumps(shard, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )

        asset_dir = MARKET / "assets" / plugin_id
        asset_dir.mkdir(parents=True, exist_ok=True)
        icon(asset_dir / "icon.png", plugin_id)
        shot_name, source_name = PLUGIN_SHOTS[plugin_id]
        shutil.copyfile(SOURCE_SHOTS / source_name, asset_dir / shot_name)

        page_dir = MARKET / "listings" / plugin_id
        page_dir.mkdir(parents=True, exist_ok=True)
        (page_dir / "index.html").write_text(listing_html(plugin, shot_name), encoding="utf-8")

    for name in ("marketplace-index.schema.json", "plugin.schema.json", "signature.schema.json", "locale.schema.json"):
        shutil.copyfile(SOURCE_SCHEMAS / name, SCHEMAS / name)

    listing_schema = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": f"{SITE}/schemas/listing.schema.json",
        "title": "FluidBar Marketplace Listing",
        "type": "object",
        "additionalProperties": False,
        "required": ["pluginId", "defaultLocale", "availableLocales", "baseUrl"],
        "properties": {
            "pluginId": {"type": "string", "pattern": "^[a-z0-9][a-z0-9-]{1,62}$"},
            "defaultLocale": {"type": "string"},
            "availableLocales": {"type": "array", "items": {"type": "string"}, "minItems": 1, "uniqueItems": True},
            "baseUrl": {"type": "string", "format": "uri", "pattern": "^https://"},
        },
    }
    app_locale_schema = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": f"{SITE}/schemas/app-locale.schema.json",
        "title": "FluidBar App Locale Bundle",
        "type": "object",
        "additionalProperties": False,
        "required": ["locale", "version", "messages"],
        "properties": {
            "$schema": {"type": "string", "format": "uri"},
            "locale": {"type": "string", "pattern": "^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-[A-Z]{2}|-[0-9]{3})?$"},
            "version": {"const": 1},
            "messages": {
                "type": "object",
                "minProperties": 1,
                "additionalProperties": {"type": "string", "minLength": 1, "maxLength": 4000},
            },
        },
    }
    for name, document in (("listing.schema.json", listing_schema), ("app-locale.schema.json", app_locale_schema)):
        (SCHEMAS / name).write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
