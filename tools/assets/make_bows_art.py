# Nexus art for the Bows add-on in the MGI Studio look: a 1920x480 page header and one 1600x900
# showcase image per bow.
# Usage: python -I make_bows_art.py <repo root> <out dir>
import os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageEnhance

root, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
sprites = f"{root}/tools/assets/out/sprites"
fonts = f"{root}/tools/Configurator/Fonts"
mark_path = f"{root}/tools/Configurator/Art/mgi-mark.png"
icons = f"{root}/src/FellSellBows/art"

GROUND = (7, 6, 12)
INK, DIM = (245, 241, 255), (165, 159, 186)
BRAND = [(255, 106, 61), (255, 61, 154), (255, 194, 61)]

def font(name, size): return ImageFont.truetype(f"{fonts}/{name}", size)

def cover(im, w, h):
    s = max(w / im.width, h / im.height)
    im = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
    x, y = (im.width - w) // 2, (im.height - h) // 2
    return im.crop((x, y, x + w, y + h))

def glows(size, spots, blur):
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for cx, cy, r, col, a in spots:
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col + (a,))
    return layer.filter(ImageFilter.GaussianBlur(blur))

def gradient_text(text, f, stops=BRAND):
    b = f.getbbox(text)
    w, h = b[2] - b[0] + 20, b[3] - b[1] + 20
    grad = Image.new("RGBA", (w, h))
    gd = ImageDraw.Draw(grad)
    for x in range(w):
        t = x / max(1, w - 1)
        a, c, k = (stops[0], stops[1], t / 0.55) if t < 0.55 else (stops[1], stops[2], (t - 0.55) / 0.45)
        gd.line([(x, 0), (x, h)], fill=tuple(int(a[i] + (c[i] - a[i]) * k) for i in range(3)) + (255,))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).text((-b[0] + 10, -b[1] + 10), text, font=f, fill=255)
    grad.putalpha(mask)
    return grad

def soft_shadow(canvas, im, xy, blur=16, alpha=190, off=(6, 10)):
    sh = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    sh.paste((0, 0, 0, alpha), (xy[0] + off[0], xy[1] + off[1]), im)
    return Image.alpha_composite(canvas, sh.filter(ImageFilter.GaussianBlur(blur)))

def mgi_sign(canvas, right, bottom, scale=1.0, left=None):
    d = ImageDraw.Draw(canvas)
    mark = Image.open(mark_path).convert("RGBA")
    mark.thumbnail((int(46 * scale), int(50 * scale)), Image.LANCZOS)
    fm, fs = font("Unbounded-Bold.ttf", int(24 * scale)), font("Manrope-SemiBold.ttf", int(15 * scale))
    w_mgi, w_inc = d.textlength("MGI", font=fm), d.textlength("MAD GOD INC", font=fs)
    total = mark.width + 12 + w_mgi + 12 + w_inc
    x, y = int(left if left is not None else right - total), int(bottom - mark.height)
    canvas.alpha_composite(mark, (x, y))
    d.text((x + mark.width + 12, y + int(8 * scale)), "MGI", font=fm, fill=BRAND[0])
    d.text((x + mark.width + 12 + w_mgi + 12, y + int(16 * scale)), "MAD GOD INC", font=fs, fill=DIM)

BOWS = [
    ("hunter", "Hunter's Bow", "UNCOMMON", (95, 211, 95),
     ["Hits like the best green mace", "+5% damage", "+5% attack speed", "+7% crit"]),
    ("swift", "Swift Bow", "RARE", (74, 163, 255),
     ["Light and fast", "+8% attack speed", "+15% crit"]),
    ("heavy", "Heavy Bow", "EPIC", (180, 107, 255),
     ["Slow draw, big hits", "+10% damage", "+25% crit", "40% stun", "Arrows ignore blocking"]),
]

# ---------------- header: drawn at 1920 wide, saved at Nexus's 1300x372 ----------------
W, H = 1920, round(1920 * 372 / 1300)
DY = (H - 480) // 2
bg = ImageEnhance.Brightness(cover(Image.open(f"{sprites}/FirstDungeon.png").convert("RGB"), W, H).filter(ImageFilter.GaussianBlur(2.4))).enhance(0.4)
bg = Image.blend(bg, Image.new("RGB", (W, H), GROUND), 0.38).convert("RGBA")
bg = Image.alpha_composite(bg, glows((W, H), [(260, 500, 460, BRAND[0], 40), (1500, 240, 520, BRAND[1], 34), (900, -60, 360, BRAND[2], 26)], 150))
# darker on the left, where the text sits
shade = Image.linear_gradient("L").rotate(90, expand=True).resize((W, H)).point(lambda v: int(150 * (1 - v / 255)))
bg = Image.composite(Image.new("RGBA", (W, H), GROUND + (255,)), bg, shade)

# the three bows, fanned on the right, each over a glow of its rarity
fan = [("hunter", 1090, 50 + DY, 320, 6), ("heavy", 1310, 20 + DY, 360, 0), ("swift", 1540, 60 + DY, 300, -6)]
for key, x, y, size, rot in fan:
    col = next(b[3] for b in BOWS if b[0] == key)
    bg = Image.alpha_composite(bg, glows((W, H), [(x + size // 2, y + size // 2 + 30, size // 2, col, 70)], 70))
for key, x, y, size, rot in fan:
    ic = Image.open(f"{icons}/icon_{key}.png").convert("RGBA").resize((size + 60, size + 60), Image.LANCZOS).rotate(rot, Image.BICUBIC, expand=True)
    bg = soft_shadow(bg, ic, (x - 30, y), blur=18, alpha=200)
    bg.alpha_composite(ic, (x - 30, y))

logo = Image.open(f"{sprites}/FellSellLogo.png").convert("RGBA")
logo.thumbnail((560, 150), Image.LANCZOS)
lx, ly = 110, 92 + DY
bg = soft_shadow(bg, logo, (lx, ly), blur=14, alpha=200)
bg.alpha_composite(logo, (lx, ly))
word = gradient_text("BOWS", font("Unbounded-Bold.ttf", 104))
wx, wy = lx + logo.width + 30, ly + (logo.height - word.height) // 2 - 4
halo = Image.new("RGBA", (W, H), (0, 0, 0, 0))
halo.paste((255, 61, 154, 140), (wx, wy), word)
bg = Image.alpha_composite(bg, halo.filter(ImageFilter.GaussianBlur(22)))
bg.alpha_composite(word, (wx, wy))

d = ImageDraw.Draw(bg)
d.text((lx + 4, 290 + DY), "Three craftable bows with a hold-to-draw shot", font=font("Manrope-SemiBold.ttf", 32), fill=INK)
d.text((lx + 4, 338 + DY), "hunter's  ·  swift  ·  heavy, with stun and block piercing", font=font("Manrope-SemiBold.ttf", 24), fill=DIM)
mgi_sign(bg, 0, H - 40 - DY, left=lx + 4)
bg.convert("RGB").resize((1300, 372), Image.LANCZOS).save(f"{out}/header.png", quality=92)

# ---------------- one image per bow, 1600x900 ----------------
CW, CH = 1600, 900
for key, name, rarity, col, lines in BOWS:
    c = Image.new("RGBA", (CW, CH), GROUND + (255,))
    c = Image.alpha_composite(c, glows((CW, CH), [(470, 470, 430, col, 70), (1500, -40, 420, BRAND[1], 30), (1200, 980, 380, BRAND[0], 30)], 140))
    ic = Image.open(f"{icons}/icon_{key}.png").convert("RGBA").resize((760, 760), Image.LANCZOS)
    c = soft_shadow(c, ic, (90, 70), blur=24, alpha=210, off=(10, 16))
    c.alpha_composite(ic, (90, 70))

    d = ImageDraw.Draw(c)
    tx = 900
    d.text((tx, 210), " ".join(rarity), font=font("Unbounded-Bold.ttf", 22), fill=col)
    d.text((tx, 250), name, font=font("Unbounded-Bold.ttf", 66), fill=INK)
    fl = font("Manrope-SemiBold.ttf", 34)
    y = 380
    for i, line in enumerate(lines):
        if i == 0:
            d.text((tx, y), line, font=font("Manrope-Regular.ttf", 32), fill=DIM)
            y += 70
            continue
        # a small diamond in the bow's colour, as the configurator marks its rows
        dm = Image.new("RGBA", (40, 40), (0, 0, 0, 0))
        ImageDraw.Draw(dm).rounded_rectangle([12, 12, 28, 28], radius=3, fill=col + (255,))
        dm = dm.rotate(45, Image.BICUBIC)
        c.alpha_composite(dm, (tx - 8, y + 2))
        d.text((tx + 38, y), line, font=fl, fill=INK)
        y += 58
    d.text((tx, CH - 150), "Hold LMB to draw  ·  release to shoot", font=font("Manrope-SemiBold.ttf", 24), fill=DIM)
    mgi_sign(c, CW - 50, CH - 40)
    c.convert("RGB").save(f"{out}/bow_{key}.png", quality=92)

print(out)
