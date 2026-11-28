# Nexus page header (1300x372) for the Character add-on: the hero from an in-game third-person shot on the right.
# Usage: python -I make_horde_art.py <repo root> <out dir>
import os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageEnhance

root, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
sprites = f"{root}/tools/assets/out/sprites"
fonts = f"{root}/tools/Configurator/Fonts"
mark_path = f"{root}/tools/Configurator/Art/mgi-mark.png"

GROUND = (7, 6, 12)
INK, DIM = (245, 241, 255), (165, 159, 186)
BRAND = [(255, 106, 61), (255, 61, 154), (255, 194, 61)]
W, H = 1920, round(1920 * 372 / 1300)  # saved at Nexus's 1300x372
DY = (H - 480) // 2

def font(name, size): return ImageFont.truetype(f"{fonts}/{name}", size)

def cover(im, w, h):
    s = max(w / im.width, h / im.height)
    im = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
    x, y = (im.width - w) // 2, (im.height - h) // 2
    return im.crop((x, y, x + w, y + h))

def glows(spots, blur):
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for cx, cy, r, col, a in spots:
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col + (a,))
    return layer.filter(ImageFilter.GaussianBlur(blur))

def gradient_text(text, f):
    b = f.getbbox(text)
    w, h = b[2] - b[0] + 20, b[3] - b[1] + 20
    grad = Image.new("RGBA", (w, h))
    gd = ImageDraw.Draw(grad)
    for x in range(w):
        t = x / max(1, w - 1)
        a, c, k = (BRAND[0], BRAND[1], t / 0.55) if t < 0.55 else (BRAND[1], BRAND[2], (t - 0.55) / 0.45)
        gd.line([(x, 0), (x, h)], fill=tuple(int(a[i] + (c[i] - a[i]) * k) for i in range(3)) + (255,))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).text((-b[0] + 10, -b[1] + 10), text, font=f, fill=255)
    grad.putalpha(mask)
    return grad

def soft_shadow(canvas, im, xy, blur=14, alpha=200):
    sh = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    sh.paste((0, 0, 0, alpha), (xy[0] + 6, xy[1] + 10), im)
    return Image.alpha_composite(canvas, sh.filter(ImageFilter.GaussianBlur(blur)))

# The boss hall on the right, brighter there, fading into the dark ground under the text.
shot = Image.open(f"{root}/docs/character-shots/final_tp_idle.png").convert("RGB")
bg = Image.new("RGBA", (W, H), GROUND + (255,))
bg = Image.alpha_composite(bg, glows([(240, 520, 460, BRAND[0], 44), (1500, 100, 520, BRAND[1], 34), (980, -80, 360, BRAND[2], 24)], 150))
# The hero over the shoulder, cut from an in-game shot (clear of the HUD), fading in from the left.
pic = shot.crop((250, 230, 1000, 700))
s_ = H / pic.height
pic = pic.resize((int(pic.width * s_), H), Image.LANCZOS).convert("RGBA")
fade = Image.linear_gradient("L").rotate(90, expand=True).resize(pic.size).point(lambda v: min(255, int(v * 2.2)))
pic.putalpha(fade)
bg.alpha_composite(pic, (W - pic.width, 0))

logo = Image.open(f"{sprites}/FellSellLogo.png").convert("RGBA")
logo.thumbnail((560, 150), Image.LANCZOS)
lx, ly = 110, 92 + DY
bg = soft_shadow(bg, logo, (lx, ly))
bg.alpha_composite(logo, (lx, ly))
word = gradient_text("CHARACTER", font("Unbounded-Bold.ttf", 78))
wx, wy = lx + logo.width + 30, ly + (logo.height - word.height) // 2 - 4
halo = Image.new("RGBA", (W, H), (0, 0, 0, 0))
halo.paste((255, 61, 154, 140), (wx, wy), word)
bg = Image.alpha_composite(bg, halo.filter(ImageFilter.GaussianBlur(22)))
bg.alpha_composite(word, (wx, wy))

d = ImageDraw.Draw(bg)
d.text((lx + 4, 290 + DY), "Third-person view and a hero look editor", font=font("Manrope-SemiBold.ttf", 32), fill=INK)
d.text((lx + 4, 338 + DY), "over the right shoulder  ·  638 parts from the game  ·  colours, body shape", font=font("Manrope-SemiBold.ttf", 24), fill=DIM)

mark = Image.open(mark_path).convert("RGBA")
mark.thumbnail((46, 50), Image.LANCZOS)
fm, fs = font("Unbounded-Bold.ttf", 24), font("Manrope-SemiBold.ttf", 15)
x, y = lx + 4, H - 40 - DY - mark.height
bg.alpha_composite(mark, (x, y))
w_mgi = d.textlength("MGI", font=fm)
d.text((x + mark.width + 12, y + 8), "MGI", font=fm, fill=BRAND[0])
d.text((x + mark.width + 12 + w_mgi + 12, y + 16), "MAD GOD INC", font=fs, fill=DIM)

bg.convert("RGB").resize((1300, 372), Image.LANCZOS).save(f"{out}/header.png", quality=92)
print(out)
