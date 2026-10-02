"""Render BijouHub theme wallpapers with headless Edge.

Run: python tools/themes/render_walls.py [names...]  (needs Edge, Pillow, numpy). Source photos
(aero-sky.jpg, aero-night.jpg, dos-bg.jpg, vapor-clouds.png) come from Bijou Footage's src/assets —
copy them next to this script first. Output lands in ./out; copy the .jpg files to Assets/Themes.

Ported themes reuse the sibling apps' CSS verbatim (BijouDocs styles.css, Bijou Footage
themes-fx.css, BijouMusic themes/*.css). Rendered at 1280x800 CSS px with a 2x device scale,
so CSS-pixel effects (grid spacing, line widths) keep their real-app proportions at 2560x1600.
"""
import os, subprocess, sys
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
os.makedirs(OUT, exist_ok=True)
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
PROFILE = os.path.join(os.environ.get("TEMP", HERE), "bijouhub-wall-edge-profile")

aqua_svg = open(os.path.join(HERE, "aqua-backdrop.txt"), encoding="utf-8").read()
vapor_crt_svg = open(os.path.join(HERE, "vapor-crt.txt"), encoding="utf-8").read()

PAGE = """<!doctype html><html><head><style>
html,body{{margin:0;width:1280px;height:800px;overflow:hidden}}
body{{position:relative;isolation:isolate}}
{css}
</style></head><body>{body}</body></html>"""

walls = {}

# ---- Frutiger Aero (BijouDocs .app-root, aero) ----
walls["aero"] = dict(css="""
body{background:
  linear-gradient(to top, rgba(88,190,70,.6) 0%, rgba(120,205,90,.3) 14%, rgba(140,215,110,0) 34%),
  linear-gradient(180deg, rgba(6,42,69,.45) 0%, rgba(10,77,120,.2) 40%, rgba(255,255,255,0) 100%),
  url('aero-sky.jpg') center / cover no-repeat;}""", body="")

# ---- Frutiger Aero Dark (BijouDocs) ----
walls["aerodark"] = dict(css="body{background:#0a1120 url('aero-night.jpg') center / cover no-repeat;}", body="")

# ---- Frutiger Aqua (BijouMusic frutiger-aqua --app-backdrop) ----
walls["aqua"] = dict(css="body{background:%s center / cover no-repeat, #2bb7cf;}" % aqua_svg, body="")

# ---- MS-DOS (BijouDocs dos .app-root) ----
walls["dos"] = dict(css="body{background:#008080 url('dos-bg.jpg') center / cover no-repeat;}", body="")

# ---- Vaporwave (BijouDocs .app-root + ::after clouds + ::before neon grid) ----
walls["vaporwave"] = dict(css="""
body{overflow:hidden;background:#8a8fd0}
body::after{content:'';position:absolute;inset:0;z-index:-2;
  background:linear-gradient(to top,#1f0a45 0%,rgba(58,20,110,.9) 16%,rgba(120,50,160,.45) 30%,rgba(160,90,190,0) 42%),
             url('vapor-clouds.png') center / cover no-repeat;
  image-rendering:pixelated}
body::before{content:'';position:absolute;left:-50%;right:-50%;bottom:0;height:900px;z-index:-1;
  background:repeating-linear-gradient(0deg,rgba(255,79,216,.85) 0 2px,transparent 2px 56px),
             repeating-linear-gradient(90deg,rgba(1,205,254,.7) 0 2px,transparent 2px 56px);
  transform:perspective(800px) rotateX(45deg);transform-origin:50% 100%;
  -webkit-mask-image:linear-gradient(to top,#000 0%,rgba(0,0,0,.6) 45%,transparent 100%)}""", body="")

# ---- Y2K Chrome / Gunmetal back wall (Bijou Footage: amber LCD brown, backlit edges) ----
walls["y2k"] = dict(css="""
body{background:
  radial-gradient(ellipse 40% 120% at 0% 50%, rgba(255,150,0,.12), rgba(255,150,0,0) 70%),
  radial-gradient(ellipse 40% 120% at 100% 50%, rgba(255,150,0,.12), rgba(255,150,0,0) 70%),
  radial-gradient(ellipse 100% 90% at 50% 45%, #2e1a02 0%, #170c00 70%, #0b0600 100%);}""", body="")

# ---- Studio (Bijou Footage cuttingRoom) ----
walls["studio"] = dict(css="""
body{background:
  radial-gradient(ellipse 60% 55% at 12% 0%, rgba(124,92,255,.24), transparent 70%),
  radial-gradient(ellipse 55% 60% at 100% 100%, rgba(56,110,255,.2), transparent 70%),
  radial-gradient(ellipse 45% 40% at 62% 52%, rgba(170,80,255,.08), transparent 75%),
  #070914;}""", body="")

# ---- Fable (new): aged parchment, fibres, burnt vignette ----
walls["fable"] = dict(css="""
body{background:#e3cf9c}
svg{position:absolute;inset:0;width:100%;height:100%}
.v{position:absolute;inset:0;background:
  radial-gradient(ellipse 75% 70% at 50% 48%, rgba(0,0,0,0) 55%, rgba(110,70,25,.38) 85%, rgba(70,40,12,.7) 100%),
  radial-gradient(ellipse 30% 25% at 18% 22%, rgba(150,105,45,.10), transparent 70%),
  radial-gradient(ellipse 25% 30% at 82% 72%, rgba(150,105,45,.12), transparent 70%)}""",
  body="""<svg viewBox='0 0 1280 800' preserveAspectRatio='none'>
<filter id='mottle'><feTurbulence type='fractalNoise' baseFrequency='0.006' numOctaves='5' seed='4'/>
<feColorMatrix values='0 0 0 0 0.55  0 0 0 0 0.38  0 0 0 0 0.16  0 0 0 0.55 -0.12'/></filter>
<filter id='fibre'><feTurbulence type='fractalNoise' baseFrequency='0.9 0.035' numOctaves='2' seed='9'/>
<feColorMatrix values='0 0 0 0 0.45  0 0 0 0 0.31  0 0 0 0 0.14  0 0 0 0.9 -0.42'/></filter>
<filter id='grain'><feTurbulence type='fractalNoise' baseFrequency='0.8' numOctaves='1' seed='2'/>
<feColorMatrix values='0 0 0 0 0.3  0 0 0 0 0.2  0 0 0 0 0.08  0 0 0 0.35 -0.05'/></filter>
<rect width='1280' height='800' filter='url(#mottle)'/>
<rect width='1280' height='800' filter='url(#fibre)'/>
<rect width='1280' height='800' filter='url(#grain)'/>
</svg><div class='v'></div>""")

# ---- Earthen (new): candlelit tavern — dark oak planks, warm candle glow ----
walls["earthen"] = dict(css="""
body{background:#120b05}
svg{position:absolute;inset:0;width:100%;height:100%}
.planks{position:absolute;inset:0;background:
  repeating-linear-gradient(90deg, rgba(0,0,0,.75) 0 3px, rgba(255,200,140,.06) 3px 4px, rgba(0,0,0,0) 4px 170px)}
.shade{position:absolute;inset:0;background:rgba(18,9,3,.55)}
.glow{position:absolute;inset:0;background:
  radial-gradient(ellipse 42% 60% at 6% 100%, rgba(255,150,40,.42), rgba(255,110,20,.14) 45%, transparent 72%),
  radial-gradient(ellipse 30% 38% at 94% 4%, rgba(255,170,70,.26), transparent 70%),
  radial-gradient(ellipse 85% 80% at 50% 50%, transparent 45%, rgba(4,2,0,.82) 100%)}""",
  body="""<svg viewBox='0 0 1280 800' preserveAspectRatio='none'>
<filter id='grain'><feTurbulence type='fractalNoise' baseFrequency='0.09 0.006' numOctaves='5' seed='17'/>
<feColorMatrix values='0 0 0 0 0.42  0 0 0 0 0.25  0 0 0 0 0.11  0 0 0 1.6 -0.45'/></filter>
<filter id='rings'><feTurbulence type='turbulence' baseFrequency='0.02 0.002' numOctaves='2' seed='8'/>
<feColorMatrix values='0 0 0 0 0.05  0 0 0 0 0.02  0 0 0 0 0.0  0 0 0 1.8 -0.5'/></filter>
<rect width='1280' height='800' fill='#2b1a0c'/>
<rect width='1280' height='800' filter='url(#grain)'/>
<rect width='1280' height='800' filter='url(#rings)'/>
</svg><div class='shade'></div><div class='planks'></div><div class='glow'></div>""")

# ---- Midnight (new): night sky, sparse stars, teal aurora low on the left ----
walls["midnight"] = dict(css="""
body{background:linear-gradient(180deg,#03050a 0%,#070b16 55%,#0c1526 100%)}
svg{position:absolute;inset:0;width:100%;height:100%}
.a{position:absolute;inset:0;background:
  radial-gradient(ellipse 65% 50% at 10% 100%, rgba(79,209,197,.32), rgba(60,170,200,.10) 50%, transparent 75%),radial-gradient(ellipse 40% 30% at 30% 95%, rgba(120,90,220,.14), transparent 70%),
  radial-gradient(ellipse 30% 22% at 85% 12%, rgba(200,220,255,.10), transparent 70%)}""",
  body="""<svg viewBox='0 0 1280 800' preserveAspectRatio='none'>
<filter id='st' x='0' y='0' width='100%' height='100%'><feTurbulence type='fractalNoise' baseFrequency='0.75' numOctaves='1' seed='3'/>
<feColorMatrix values='0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  19 0 0 0 -13.9'/></filter>
<linearGradient id='fade' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#fff'/><stop offset='0.55' stop-color='#fff' stop-opacity='0.8'/><stop offset='0.9' stop-color='#fff' stop-opacity='0'/></linearGradient><mask id='m'><rect width='1280' height='800' fill='url(#fade)'/></mask>
<rect width='1280' height='800' filter='url(#st)' mask='url(#m)'/>
</svg><div class='a'></div>""")

# ---- MacBook Light (new): soft macOS-style pastel gradient ----
walls["mac"] = dict(css="""
body{background:
  radial-gradient(ellipse 55% 60% at 8% 5%, rgba(140,180,255,.95), rgba(140,180,255,0) 70%),
  radial-gradient(ellipse 55% 55% at 88% 8%, rgba(208,180,253,.95), rgba(208,180,253,0) 70%),
  radial-gradient(ellipse 50% 55% at 92% 95%, rgba(255,200,170,.95), rgba(255,200,170,0) 70%),
  radial-gradient(ellipse 50% 50% at 6% 95%, rgba(170,230,215,.9), rgba(170,230,215,0) 70%),
  radial-gradient(ellipse 45% 40% at 50% 50%, rgba(250,251,255,.9), rgba(250,251,255,0) 70%),
  #eef0f7;}""", body="")

# ---- Vaporwave CRT screen (BijouMusic vaporwave-95 --screen-bg scene), for the timer ----
crt = dict(w=800, h=200, css="html,body{width:800px;height:200px}body{background:%s center bottom / cover no-repeat,#0e0219}" % vapor_crt_svg, body="")


def render(name, spec, w=1280, h=800, scale=2):
    html = os.path.join(HERE, f"_{name}.html")
    with open(html, "w", encoding="utf-8") as f:
        f.write(PAGE.format(css=spec["css"], body=spec["body"]))
    png = os.path.join(OUT, f"{name}.png")
    subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    f"--user-data-dir={PROFILE}", f"--force-device-scale-factor={scale}",
                    f"--window-size={w},{h}", f"--screenshot={png}",
                    "--virtual-time-budget=3000", "file:///" + html.replace("\\", "/")],
                   check=True, capture_output=True)
    return png


def finish(png, name, quality=90, dither=True):
    im = Image.open(png).convert("RGB")
    if dither:
        # A whisper of noise breaks up 8-bit banding in the long smooth gradients.
        import numpy as np
        a = np.asarray(im).astype(np.int16)
        a += np.random.default_rng(7).integers(-2, 3, a.shape[:2] + (1,), dtype=np.int16)
        im = Image.fromarray(a.clip(0, 255).astype("uint8"))
    dest = os.path.join(OUT, f"{name}.jpg")
    im.save(dest, quality=quality, optimize=True, progressive=True)
    print(f"{name}: {im.size} {os.path.getsize(dest)//1024} KB")


if __name__ == "__main__":
    only = set(sys.argv[1:])
    for name, spec in walls.items():
        if only and name not in only:
            continue
        finish(render(name, spec), name)
    if not only or "crt" in only:
        finish(render("vapor-crt", crt, w=800, h=200, scale=2), "vapor-crt", dither=False)
