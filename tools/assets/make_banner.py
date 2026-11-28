# Builds the Nexus page header (1920x480) in the MGI Studio look: a darkened game shot, warm brand
# glows, the game's wordmark with "PLUS", a one-line summary and the MGI mark.
# Usage: python -I make_banner.py <sprites dir> <configurator dir> <out.png>
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageEnhance

sprites, cfg, out = sys.argv[1], sys.argv[2], sys.argv[3]
W, H = 1920, 480

# Background: two game shots side by side (village and dungeon), darkened and softened.
left = Image.open(f"{sprites}/Shopping.png").convert("RGB")
right = Image.open(f"{sprites}/Elites.png").convert("RGB")
def cover(im, w, h):
    s = max(w / im.width, h / im.height)
    im = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
    x, y = (im.width - w) // 2, (im.height - h) // 2
    return im.crop((x, y, x + w, y + h))
# Village on the left fading into the dungeon on the right through a wide soft blend.
L = cover(left, W, H)
R = cover(right, W, H)
mask = Image.linear_gradient("L").rotate(90, expand=True).resize((W, H))  # 0 at left, 255 at right
mask = mask.point(lambda v: 0 if v < 90 else 255 if v > 165 else int((v - 90) * 255 / 75))
bg = Image.composite(R, L, mask)
bg = ImageEnhance.Brightness(bg.filter(ImageFilter.GaussianBlur(2.2))).enhance(0.42)

# Ground tint and brand glows.
ground = Image.new("RGB", (W, H), (7, 6, 12))
bg = Image.blend(bg, ground, 0.35)
glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
g = ImageDraw.Draw(glow)
for (cx, cy, r, col) in [(250, 470, 460, (255, 106, 61)), (1700, 10, 480, (255, 61, 154)), (960, 560, 380, (255, 194, 61))]:
    g.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col + (38,))
glow = glow.filter(ImageFilter.GaussianBlur(160))
bg = Image.alpha_composite(bg.convert("RGBA"), glow)

# Vignette towards the edges so the centre text reads.
vig = Image.new("L", (W, H), 0)
v = ImageDraw.Draw(vig)
v.ellipse([-200, -260, W + 200, H + 260], fill=255)
vig = vig.filter(ImageFilter.GaussianBlur(120))
dark = Image.new("RGBA", (W, H), (7, 6, 12, 255))
bg = Image.composite(bg, dark, vig)

draw = ImageDraw.Draw(bg)

# Game wordmark (the game's own logo) and PLUS in the brand gradient.
logo = Image.open(f"{sprites}/FellSellLogo.png").convert("RGBA")
logo.thumbnail((900, 200), Image.LANCZOS)
ly = 110
font_b = ImageFont.truetype(f"{cfg}/Fonts/Unbounded-Bold.ttf", 118)
plus = "PLUS"
pb = font_b.getbbox(plus)
pw, ph = pb[2] - pb[0], pb[3] - pb[1]
grad = Image.new("RGBA", (pw + 20, ph + 40))
gd = ImageDraw.Draw(grad)
stops = [(255, 106, 61), (255, 61, 154), (255, 194, 61)]
for x in range(grad.width):
    t = x / max(1, grad.width - 1)
    a, b, f = (stops[0], stops[1], t / 0.55) if t < 0.55 else (stops[1], stops[2], (t - 0.55) / 0.45)
    gd.line([(x, 0), (x, grad.height)], fill=tuple(int(a[i] + (b[i] - a[i]) * f) for i in range(3)) + (255,))
mask = Image.new("L", grad.size, 0)
ImageDraw.Draw(mask).text((-pb[0] + 10, -pb[1] + 10), plus, font=font_b, fill=255)
group = logo.width + 34 + pw
lx = (W - group) // 2
px, py = lx + logo.width + 34, ly + (logo.height - ph) // 2 - 6
shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
shadow.paste((0, 0, 0, 200), (lx + 6, ly + 10), logo)
bg = Image.alpha_composite(bg, shadow.filter(ImageFilter.GaussianBlur(14)))
bg.alpha_composite(logo, (lx, ly))
glowp = Image.new("RGBA", (W, H), (0, 0, 0, 0))
glowp.paste((255, 61, 154, 150), (px - 10, py - 10), mask)
bg = Image.alpha_composite(bg, glowp.filter(ImageFilter.GaussianBlur(22)))
bg.paste(grad, (px - 10, py - 10), mask)

draw = ImageDraw.Draw(bg)
font_t = ImageFont.truetype(f"{cfg}/Fonts/Manrope-SemiBold.ttf", 30)
line = "New prayers and skills  ·  level 120  ·  Grandma's Legacy  ·  shop window  ·  loot filter"
tw = draw.textlength(line, font=font_t)
draw.text(((W - tw) / 2, 345), line, font=font_t, fill=(217, 211, 234))

# MGI mark and name, bottom right.
mark = Image.open(f"{cfg}/Art/mgi-mark.png").convert("RGBA")
mark.thumbnail((46, 50), Image.LANCZOS)
font_m = ImageFont.truetype(f"{cfg}/Fonts/Unbounded-Bold.ttf", 24)
font_s = ImageFont.truetype(f"{cfg}/Fonts/Manrope-SemiBold.ttf", 15)
w_mgi = draw.textlength("MGI", font=font_m)
w_inc = draw.textlength("MAD GOD INC", font=font_s)
total = mark.width + 12 + w_mgi + 12 + w_inc
mx, my = int(W - 40 - total), H - 74
bg.alpha_composite(mark, (mx, my))
draw.text((mx + mark.width + 12, my + 8), "MGI", font=font_m, fill=(255, 106, 61))
draw.text((mx + mark.width + 12 + w_mgi + 12, my + 16), "MAD GOD INC", font=font_s, fill=(165, 159, 186))

bg.convert("RGB").save(out, quality=92)
print(out)
