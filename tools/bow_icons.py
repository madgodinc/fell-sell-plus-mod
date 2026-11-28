# Decodes the drawn bow icons (src/FellSellBows/art/icon_<name>.png) into raw pixels for the mod:
# src/FellSellBows/icon_<name>.rgba = int width, int height, RGBA32 rows from the bottom up
# (Unity's texture row order). The game then needs no PNG decoder (Texture2D.LoadRawTextureData).
#
#   python tools/bow_icons.py
import os, glob, struct
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "src", "FellSellBows")
SIZE = 256   # the size of the game's own item icons

for png in sorted(glob.glob(os.path.join(SRC, "art", "icon_*.png"))):
    im = Image.open(png).convert("RGBA")
    if im.size != (SIZE, SIZE):
        im = im.resize((SIZE, SIZE), Image.LANCZOS)
    im = im.transpose(Image.FLIP_TOP_BOTTOM)
    out = os.path.join(SRC, os.path.splitext(os.path.basename(png))[0] + ".rgba")
    with open(out, "wb") as f:
        f.write(struct.pack("<ii", SIZE, SIZE) + im.tobytes())
    print(os.path.basename(out), os.path.getsize(out), "bytes")
