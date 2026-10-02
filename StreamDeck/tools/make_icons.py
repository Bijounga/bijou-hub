"""Renders the plugin's PNG icons from SVG via headless Edge, then downsamples with Pillow.

    python StreamDeck/tools/make_icons.py

Writes into com.bijounga.bijouhub.sdPlugin/imgs. The key image mirrors src/key-art.ts's
"ready" state, so the action list preview matches what a configured key looks like.
"""
import math
import os
import subprocess
import tempfile

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "com.bijounga.bijouhub.sdPlugin", "imgs")
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
RENDER = 512
CYAN = "#33E1FF"


def arc(cx, cy, r, fraction):
    a = fraction * 2 * math.pi
    x, y = cx + r * math.sin(a), cy - r * math.cos(a)
    return f"M {cx} {cy - r} A {r} {r} 0 {1 if fraction > 0.5 else 0} 1 {x:.2f} {y:.2f}", (x, y)


def gem(cx, cy, w):
    # BijouHub's sidebar gem: a flat hexagon, 24x16 in the app.
    h = w * 16 / 24
    pts = [(6, 0), (18, 0), (24, 8), (18, 16), (6, 16), (0, 8)]
    return " ".join(f"{cx - w / 2 + px / 24 * w:.2f},{cy - h / 2 + py / 16 * h:.2f}" for px, py in pts)


GEM_GRADIENT = ('<linearGradient id="gem" x1="0" y1="0" x2="1" y2="1">'
                '<stop offset="0" stop-color="#FFFFFF"/><stop offset="0.5" stop-color="#EAF6FF"/>'
                '<stop offset="1" stop-color="#A8D8F0"/></linearGradient>')


def plugin_icon():
    path, (tx, ty) = arc(256, 256, 176, 0.72)
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
<defs>{GEM_GRADIENT}<radialGradient id="glow" cx="50%" cy="50%" r="60%">
<stop offset="0" stop-color="{CYAN}" stop-opacity="0.28"/><stop offset="1" stop-color="{CYAN}" stop-opacity="0"/></radialGradient></defs>
<rect width="512" height="512" rx="112" fill="#0B0D12"/><rect width="512" height="512" rx="112" fill="url(#glow)"/>
<circle cx="256" cy="256" r="176" fill="none" stroke="{CYAN}" stroke-opacity="0.2" stroke-width="30"/>
<path d="{path}" fill="none" stroke="{CYAN}" stroke-width="30" stroke-linecap="round"/>
<circle cx="{tx:.2f}" cy="{ty:.2f}" r="20" fill="#F4F6FA"/>
<polygon points="{gem(256, 256, 168)}" fill="url(#gem)" stroke="#071018" stroke-width="8" stroke-linejoin="round"/>
</svg>"""


def category_icon():
    # Monochrome white, per Stream Deck's category icon guidelines.
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
<polygon points="{gem(256, 256, 440)}" fill="none" stroke="#FFFFFF" stroke-width="44" stroke-linejoin="round"/>
</svg>"""


def action_icon():
    path, (tx, ty) = arc(256, 256, 200, 0.75)
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
<circle cx="256" cy="256" r="200" fill="none" stroke="#FFFFFF" stroke-opacity="0.35" stroke-width="40"/>
<path d="{path}" fill="none" stroke="#FFFFFF" stroke-width="40" stroke-linecap="round"/>
<path d="M 214 170 L 344 256 L 214 342 Z" fill="#FFFFFF"/>
</svg>"""


def key_image():
    # Same geometry as key-art.ts (144 viewBox), "ready" state with a 45m preset.
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 144 144">
<defs><radialGradient id="glow" cx="50%" cy="50%" r="60%"><stop offset="0%" stop-color="{CYAN}" stop-opacity="0.24"/>
<stop offset="100%" stop-color="{CYAN}" stop-opacity="0"/></radialGradient></defs>
<rect width="144" height="144" fill="#0B0D12"/><rect width="144" height="144" fill="url(#glow)"/>
<circle cx="72" cy="72" r="60" fill="none" stroke="{CYAN}" stroke-opacity="0.2" stroke-width="7"/>
<circle cx="72" cy="72" r="60" fill="none" stroke="{CYAN}" stroke-width="7"/>
<path d="M66 37 L80 45 L66 53 Z" fill="{CYAN}"/>
<text x="72" y="86" text-anchor="middle" font-family="Segoe UI" font-size="40" font-weight="600" fill="#F4F6FA">45m</text>
<text x="72" y="106" text-anchor="middle" font-family="Segoe UI" font-size="14" fill="#F4F6FA" fill-opacity="0.72">BijouHub</text>
</svg>"""


def render(svg, work):
    html = os.path.join(work, "icon.html")
    with open(html, "w", encoding="utf-8") as f:
        f.write(f"<!doctype html><style>html,body{{margin:0;background:transparent}}svg{{display:block;width:{RENDER}px;height:{RENDER}px}}</style>{svg}")
    png = os.path.join(work, "icon.png")
    subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    f"--user-data-dir={os.path.join(work, 'profile')}", "--force-device-scale-factor=1",
                    "--default-background-color=00000000", f"--window-size={RENDER},{RENDER}",
                    f"--screenshot={png}", "file:///" + html.replace("\\", "/")],
                   check=True, capture_output=True)
    return Image.open(png).convert("RGBA").crop((0, 0, RENDER, RENDER))


def main():
    os.makedirs(OUT, exist_ok=True)
    targets = [
        ("plugin", plugin_icon(), 256),
        ("category", category_icon(), 28),
        ("action", action_icon(), 20),
        ("key", key_image(), 72),
    ]
    with tempfile.TemporaryDirectory() as work:
        for name, svg, size in targets:
            big = render(svg, work)
            for suffix, scale in (("", 1), ("@2x", 2)):
                big.resize((size * scale, size * scale), Image.LANCZOS).save(os.path.join(OUT, f"{name}{suffix}.png"))
            print(name, size)


if __name__ == "__main__":
    main()
