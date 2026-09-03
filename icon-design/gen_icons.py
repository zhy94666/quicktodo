import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps

ROOT = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(ROOT)
ASSETS = os.path.join(PROJ, "MyTodo", "Assets")
os.makedirs(ASSETS, exist_ok=True)

def _gv(size):
    return Image.linear_gradient("L").resize((size, size), Image.BILINEAR)

def diag_gradient(size, top_left, bottom_right):
    v = _gv(size)
    h = _gv(size).transpose(Image.ROTATE_90)
    t = ImageChops.add(v, h, scale=2)
    return ImageOps.colorize(t, black=top_left, white=bottom_right).convert("RGBA")

def anti_diag_gradient(size, top_right, bottom_left):
    v = _gv(size)
    h = _gv(size).transpose(Image.ROTATE_90).transpose(Image.FLIP_LEFT_RIGHT)
    t = ImageChops.add(v, h, scale=2)
    return ImageOps.colorize(t, black=top_right, white=bottom_left).convert("RGBA")

def stroke(draw, pts, width, fill):
    draw.line(pts, fill=fill, width=width, joint="curve")
    r = width // 2
    for x, y in (pts[0], pts[-1]):
        draw.ellipse([x - r, y - r, x + r, y + r], fill=fill)

def sc(v, s):
    return int(round(v * s))

# ---------- app icon (256-canvas, supersampled 8x) ----------
APP = 8
W = 256 * APP

def app_master():
    tile_r = sc(58, APP)
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, W - 1], radius=tile_r, fill=255)
    img = diag_gradient(W, (51, 58, 66), (22, 24, 28))   # #333A42 -> #16181C
    img.putalpha(mask)

    rim = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(rim)
    inset = sc(2.5, APP)
    d.rounded_rectangle([inset, inset, W - 1 - inset, W - 1 - inset],
                        radius=tile_r - inset, outline=(255, 255, 255, 26), width=sc(3, APP))
    img = Image.alpha_composite(img, rim)

    pts = [(sc(104, APP), sc(148, APP)), (sc(142, APP), sc(186, APP)), (sc(206, APP), sc(96, APP))]
    w = sc(30, APP)

    glow = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    stroke(ImageDraw.Draw(glow), pts, w, (92, 194, 255, 80))
    glow = glow.filter(ImageFilter.GaussianBlur(sc(14, APP)))
    img = Image.alpha_composite(img, glow)

    bars = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    db = ImageDraw.Draw(bars)
    db.rounded_rectangle([sc(60, APP), sc(74, APP), sc(152, APP), sc(96, APP)],
                         radius=sc(11, APP), fill=(255, 255, 255, 77))
    db.rounded_rectangle([sc(60, APP), sc(118, APP), sc(122, APP), sc(140, APP)],
                         radius=sc(11, APP), fill=(255, 255, 255, 41))
    img = Image.alpha_composite(img, bars)

    sh = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    stroke(ImageDraw.Draw(sh), [(x, y + sc(6, APP)) for x, y in pts], w, (0, 0, 0, 70))
    sh = sh.filter(ImageFilter.GaussianBlur(sc(3, APP)))
    img = Image.alpha_composite(img, sh)

    ck = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    stroke(ImageDraw.Draw(ck), pts, w, (255, 255, 255, 255))
    grad = anti_diag_gradient(W, (159, 226, 255), (62, 168, 255))  # #9FE2FF -> #3EA8FF
    grad.putalpha(ck.getchannel("A"))
    img = Image.alpha_composite(img, grad)
    return img

# ---------- tray icon (256-canvas, supersampled 4x) ----------
TR = 4
TW = 256 * TR

def tray_master():
    img = Image.new("RGBA", (TW, TW), (0, 0, 0, 0))
    grad = diag_gradient(TW, (159, 226, 255), (62, 168, 255))
    mask = Image.new("L", (TW, TW), 0)
    ImageDraw.Draw(mask).ellipse([sc(2, TR), sc(2, TR), TW - 1 - sc(2, TR), TW - 1 - sc(2, TR)], fill=255)
    grad.putalpha(mask)
    img = Image.alpha_composite(img, grad)

    rim = Image.new("RGBA", (TW, TW), (0, 0, 0, 0))
    ImageDraw.Draw(rim).ellipse([sc(4.5, TR), sc(4.5, TR), TW - 1 - sc(4.5, TR), TW - 1 - sc(4.5, TR)],
                                outline=(0, 0, 0, 40), width=sc(2.5, TR))
    img = Image.alpha_composite(img, rim)

    pts = [(sc(80, TR), sc(134, TR)), (sc(116, TR), sc(170, TR)), (sc(184, TR), sc(94, TR))]
    w = sc(27, TR)

    sh = Image.new("RGBA", (TW, TW), (0, 0, 0, 0))
    stroke(ImageDraw.Draw(sh), [(x, y + sc(2, TR)) for x, y in pts], w, (0, 0, 0, 60))
    sh = sh.filter(ImageFilter.GaussianBlur(sc(2, TR)))
    img = Image.alpha_composite(img, sh)

    ck = Image.new("RGBA", (TW, TW), (0, 0, 0, 0))
    stroke(ImageDraw.Draw(ck), pts, w, (255, 255, 255, 255))
    img = Image.alpha_composite(img, ck)
    return img

def build_ico(master, path, sizes):
    m = max(sizes)
    base = master.resize((m, m), Image.LANCZOS)
    others = [master.resize((s, s), Image.LANCZOS) for s in sorted(sizes) if s != m]
    base.save(path, format="ICO", sizes=[(s, s) for s in sorted(sizes)], append_images=others)

def preview(master, sizes, path):
    pad, gap, label_h = 44, 40, 56
    big = max(sizes)
    width = pad * 2 + sum(sizes) + gap * (len(sizes) - 1)
    row_h = big + label_h
    height = pad + row_h + 70 + row_h + pad
    canvas = Image.new("RGB", (width, height), (26, 27, 30))
    d = ImageDraw.Draw(canvas)
    y1 = pad
    y2 = pad + row_h + 70
    d.rounded_rectangle([pad - 20, y2 - 26, width - pad + 20, y2 + row_h + 16], radius=24, fill=(238, 240, 244))
    try:
        font = ImageFont.load_default(size=26)
    except Exception:
        font = ImageFont.load_default()

    def row(y0, dark):
        x = pad
        for s in sizes:
            ic = master.resize((s, s), Image.LANCZOS)
            canvas.paste(ic, (x, y0 + big - s), ic)
            lbl = str(s) + "px"
            tw = d.textlength(lbl, font=font)
            d.text((x + s / 2 - tw / 2, y0 + big + 8), lbl, font=font,
                   fill=(176, 178, 184) if dark else (96, 98, 104))
            x += s + gap

    row(y1, True)
    row(y2, False)
    canvas.save(path)

APP_SVG = """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#333A42"/>
      <stop offset="1" stop-color="#16181C"/>
    </linearGradient>
    <linearGradient id="chk" x1="1" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#9FE2FF"/>
      <stop offset="1" stop-color="#3EA8FF"/>
    </linearGradient>
    <filter id="glow" x="-60%" y="-60%" width="220%" height="220%">
      <feGaussianBlur stdDeviation="14"/>
    </filter>
    <filter id="soft" x="-30%" y="-30%" width="160%" height="160%">
      <feGaussianBlur stdDeviation="3"/>
    </filter>
  </defs>
  <rect width="256" height="256" rx="58" fill="url(#bg)"/>
  <rect x="2.5" y="2.5" width="251" height="251" rx="55.5" fill="none" stroke="#FFFFFF" stroke-opacity="0.10" stroke-width="3"/>
  <path d="M104 148 L142 186 L206 96" fill="none" stroke="#5CC2FF" stroke-opacity="0.31" stroke-width="30" stroke-linecap="round" stroke-linejoin="round" filter="url(#glow)"/>
  <rect x="60" y="74" width="92" height="22" rx="11" fill="#FFFFFF" fill-opacity="0.30"/>
  <rect x="60" y="118" width="62" height="22" rx="11" fill="#FFFFFF" fill-opacity="0.16"/>
  <path d="M104 154 L142 192 L206 102" fill="none" stroke="#000000" stroke-opacity="0.27" stroke-width="30" stroke-linecap="round" stroke-linejoin="round" filter="url(#soft)"/>
  <path d="M104 148 L142 186 L206 96" fill="none" stroke="url(#chk)" stroke-width="30" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
"""

TRAY_SVG = """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="tb" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#9FE2FF"/>
      <stop offset="1" stop-color="#3EA8FF"/>
    </linearGradient>
    <filter id="ts" x="-40%" y="-40%" width="180%" height="180%">
      <feGaussianBlur stdDeviation="2"/>
    </filter>
  </defs>
  <circle cx="128" cy="128" r="126" fill="url(#tb)"/>
  <circle cx="128" cy="128" r="123.5" fill="none" stroke="#000000" stroke-opacity="0.16" stroke-width="5"/>
  <path d="M80 136 L116 172 L184 96" fill="none" stroke="#000000" stroke-opacity="0.24" stroke-width="27" stroke-linecap="round" stroke-linejoin="round" filter="url(#ts)"/>
  <path d="M80 134 L116 170 L184 94" fill="none" stroke="#FFFFFF" stroke-width="27" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
"""

with open(os.path.join(ROOT, "app-icon.svg"), "w", encoding="utf-8") as f:
    f.write(APP_SVG)
with open(os.path.join(ROOT, "tray-icon.svg"), "w", encoding="utf-8") as f:
    f.write(TRAY_SVG)
with open(os.path.join(ROOT, "README.txt"), "w", encoding="utf-8") as f:
    f.write(
        "MyTodo icon sources\n"
        "-------------------\n"
        "app-icon.svg   master design for the application icon (exe / window / taskbar)\n"
        "tray-icon.svg  simplified tray variant (blue disc + white check)\n"
        "gen_icons.py   offline generator (Pillow) -> ../MyTodo/Assets/app.ico + tray.ico\n"
        "preview_*.png  rendered previews on dark / light backgrounds\n"
        "\nRegenerate after design tweaks:  python gen_icons.py\n"
    )

app = app_master()
tray = tray_master()
app.resize((512, 512), Image.LANCZOS).save(os.path.join(ROOT, "app-512.png"))
tray.resize((256, 256), Image.LANCZOS).save(os.path.join(ROOT, "tray-256.png"))
build_ico(app, os.path.join(ASSETS, "app.ico"), [16, 20, 24, 32, 48, 64, 128, 256])
build_ico(tray, os.path.join(ASSETS, "tray.ico"), [16, 20, 24, 32, 48])
preview(app, [256, 128, 64, 48, 32, 24, 16], os.path.join(ROOT, "preview_app.png"))
preview(tray, [64, 48, 32, 24, 20, 16], os.path.join(ROOT, "preview_tray.png"))
print("generated:")
for f in sorted(os.listdir(ROOT)):
    print(" ", f)
print("assets:", sorted(os.listdir(ASSETS)))
