# Builds one mesh per bow from the stringless game bow (src/FellSellBows/bow_nostring.bin, see
# bow_nostring.py): reshaped limbs, parts recoloured by pointing their UVs at other swatches of the
# game palette texture, and a few extra low-poly pieces (charm, iron bands, gems).
# Output per bow: src/FellSellBows/bow_<name>.bin, the same layout as bow_nostring.bin followed by
# the string anchor: float tipY, float tipZ (mesh units; the string ends at (0, +-tipY, tipZ)).
#
#   python tools/bow_variants.py <palette.png> [preview_dir]
#
# palette.png is the base map of the bow material (PolygonFantasyKingdom_Texture_01_A) exported from
# the game with UnityPy; it is only read to find swatch coordinates. preview_dir gets PNG renders.
import sys, os, struct, math
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "src", "FellSellBows")

# The three swatches of the base mesh (rounded UVs) by role.
WOOD, DARK, LIGHT = (0.027, 0.302), (0.047, 0.254), (0.050, 0.351)


def load(path):
    d = open(path, "rb").read()
    nv, ni = struct.unpack_from("<ii", d, 0)
    o = 8
    V = np.frombuffer(d, "<f4", nv * 3, o).reshape(-1, 3).astype(np.float64); o += nv * 12
    N = np.frombuffer(d, "<f4", nv * 3, o).reshape(-1, 3).astype(np.float64); o += nv * 12
    U = np.frombuffer(d, "<f4", nv * 2, o).reshape(-1, 2).astype(np.float64); o += nv * 8
    I = np.frombuffer(d, "<i4", ni, o).reshape(-1, 3).copy()
    return V, N, U, I


def save(path, V, N, U, I, tip_y, tip_z):
    out = struct.pack("<ii", len(V), I.size)
    out += V.astype("<f4").tobytes() + N.astype("<f4").tobytes() + U.astype("<f4").tobytes() + I.astype("<i4").tobytes()
    out += struct.pack("<ff", tip_y, tip_z)
    open(path, "wb").write(out)


def face_normals(V, I):
    return np.cross(V[I[:, 1]] - V[I[:, 0]], V[I[:, 2]] - V[I[:, 0]])


def vertex_normals(V, I, sign):
    fn = face_normals(V, I) * sign
    N = np.zeros_like(V)
    for k in range(3):
        np.add.at(N, I[:, k], fn)
    return N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)


class Palette:
    """Finds a flat swatch of the palette texture closest to a colour."""
    B = 8

    def __init__(self, path):
        im = np.asarray(Image.open(path).convert("RGB")).astype(np.float64)
        self.size = im.shape[0]
        g = self.size // self.B
        blocks = im.reshape(g, self.B, g, self.B, 3)
        self.mean = blocks.mean(axis=(1, 3))
        flat = blocks.std(axis=(1, 3)).max(axis=2) < 1.5
        # the swatch must stay flat one block around it too (filtering, mip levels)
        ok = flat.copy()
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                sh = np.roll(np.roll(self.mean, dy, 0), dx, 1)
                ok &= np.roll(np.roll(flat, dy, 0), dx, 1) & (np.abs(sh - self.mean).max(axis=2) < 1.5)
        ys, xs = np.mgrid[0:g, 0:g]
        u = (xs + 0.5) / g
        v = 1 - (ys + 0.5) / g
        ok &= (u < 0.5) & (v > 0.22) & (v < 0.5)   # the colour-swatch area of the atlas
        self.ok, self.u, self.v = ok, u, v
        self.img = im

    def uv(self, rgb):
        d = np.abs(self.mean - np.array(rgb, dtype=np.float64)).sum(axis=2)
        d[~self.ok] = 1e9
        y, x = np.unravel_index(np.argmin(d), d.shape)
        return (float(self.u[y, x]), float(self.v[y, x]))

    def color(self, uv):
        x = min(self.size - 1, int(uv[0] * self.size)); y = min(self.size - 1, int((1 - uv[1]) * self.size))
        return self.img[y, x]


class Builder:
    def __init__(self, base, pal):
        self.V, self.N, self.U, self.I = [a.copy() for a in base]
        self.pal = pal
        # winding of the game mesh: sign so that sign * cross(e1, e2) points along the normal
        fn = face_normals(self.V, self.I)
        self.sign = 1.0 if (fn * self.N[self.I[:, 0]]).sum() > 0 else -1.0
        self.role = np.array([self._role(u) for u in self.U])
        self.base_count = len(self.V)

    @staticmethod
    def _role(u):
        best = min((WOOD, DARK, LIGHT), key=lambda s: abs(s[0] - u[0]) + abs(s[1] - u[1]))
        return (WOOD, DARK, LIGHT).index(best)

    # ---- shape ----
    def rings(self):
        """Centre z, width and depth of the limb per ring of base vertices, sorted by y."""
        V = self.V[:self.base_count]
        yy = np.round(V[:, 1], 3); ys = np.unique(yy)
        zc = np.array([(V[yy == v, 2].min() + V[yy == v, 2].max()) / 2 for v in ys])
        zt = np.array([(V[yy == v, 2].max() - V[yy == v, 2].min()) for v in ys])
        xt = np.array([(V[yy == v, 0].max() - V[yy == v, 0].min()) for v in ys])
        return ys, zc, xt, zt

    def reshape(self, length=1.0, width=1.0, thick=1.0, grip=1.0, curve=1.0, recurve=0.0, recurve_from=0.72):
        V = self.V
        ys, zc, _, _ = self.rings()
        ymax = np.abs(V[:, 1]).max()
        c = np.interp(V[:, 1], ys, zc)
        a = np.abs(V[:, 1])
        in_grip = np.clip((0.19 - a) / 0.05, 0, 1)          # 1 inside the grip, fades out by |y| 0.19
        V[:, 0] *= width + (grip - width) * in_grip
        c0 = np.interp(0.0, ys, zc)
        V[:, 2] = c0 + (c - c0) * curve + (V[:, 2] - c) * (thick + (grip - thick) * in_grip)
        if recurve:
            # the end of each limb bends forward (+z): every point turns about the start of the bend,
            # further out turns more
            y0 = recurve_from * ymax
            pz = c0 + (np.interp(y0, ys, zc) - c0) * curve
            m = a > y0
            s = np.sign(V[m, 1])
            ang = recurve * (a[m] - y0) / (ymax - y0)
            dy = a[m] - y0; dz = V[m, 2] - pz
            V[m, 1] = s * (y0 + dy * np.cos(ang) - dz * np.sin(ang))
            V[m, 2] = pz + dy * np.sin(ang) + dz * np.cos(ang)
        V[:, 1] *= length

    def recolor(self, wood=None, dark_grip=None, dark_tips=None, light=None):
        a = np.abs(self.V[:self.base_count, 1]); ymax = a.max()
        r = self.role

        def put(mask, rgb):
            if rgb is not None: self.U[:self.base_count][mask] = self.pal.uv(rgb)
        put(r == 0, wood)
        put((r == 1) & (a < 0.3 * ymax), dark_grip)
        put((r == 1) & (a >= 0.3 * ymax), dark_tips)
        put(r == 2, light)

    # ---- extra pieces (flat shaded: no shared vertices) ----
    def _add(self, pts, tris, rgb, centre=None):
        pts = np.asarray(pts, dtype=np.float64)
        c = pts.mean(axis=0) if centre is None else np.asarray(centre)
        uv = self.pal.uv(rgb)
        for t in tris:
            p = pts[list(t)]
            n = np.cross(p[1] - p[0], p[2] - p[0])
            if np.dot(n, p.mean(axis=0) - c) < 0:
                p = p[[0, 2, 1]]; n = -n
            if self.sign < 0: p = p[[0, 2, 1]]
            n = n / max(np.linalg.norm(n), 1e-12)
            b = len(self.V)
            self.V = np.vstack([self.V, p]); self.N = np.vstack([self.N, np.tile(n, (3, 1))])
            self.U = np.vstack([self.U, np.tile(uv, (3, 1))]); self.I = np.vstack([self.I, [[b, b + 1, b + 2]]])

    @staticmethod
    def _rotx(pts, ang):
        ca, sa = math.cos(ang), math.sin(ang)
        p = np.asarray(pts, dtype=np.float64).copy()
        y, z = p[:, 1].copy(), p[:, 2].copy()
        p[:, 1] = y * ca - z * sa; p[:, 2] = y * sa + z * ca
        return p

    def box(self, centre, size, rgb, tilt=0.0):
        hx, hy, hz = np.array(size) / 2
        p = np.array([[x, y, z] for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)])
        p = self._rotx(p, tilt) + np.array(centre)
        q = [(0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5)]
        self._add(p, [t for a, b, c, d in q for t in ((a, b, c), (a, c, d))], rgb)

    def gem(self, centre, size, rgb, tilt=0.0):
        hx, hy, hz = np.array(size) / 2
        p = np.array([[hx, 0, 0], [-hx, 0, 0], [0, hy, 0], [0, -hy, 0], [0, 0, hz], [0, 0, -hz]], dtype=np.float64)
        p = self._rotx(p, tilt) + np.array(centre)
        self._add(p, [(a, b, c) for a in (0, 1) for b in (2, 3) for c in (4, 5)], rgb)

    def spike(self, base_centre, tip, radius, rgb):
        b = np.array(base_centre, dtype=np.float64); t = np.array(tip, dtype=np.float64)
        ax = (t - b) / np.linalg.norm(t - b)
        u = np.cross(ax, [1, 0, 0]); u /= np.linalg.norm(u); w = np.cross(ax, u)
        ring = [b + radius * (math.cos(k * math.pi / 2) * u + math.sin(k * math.pi / 2) * w) for k in range(4)]
        p = np.array(ring + [t, b])
        self._add(p, [(k, (k + 1) % 4, 4) for k in range(4)] + [(k, (k + 1) % 4, 5) for k in range(4)], rgb,
                  centre=(b * 2 + t) / 3)

    def blade(self, root, tip, width, thick, rgb):
        """A feather or leaf: a flat pointed plate in the y-z plane, from root to tip."""
        r = np.array(root, dtype=np.float64); t = np.array(tip, dtype=np.float64)
        ax = t - r
        side = np.cross(ax, [1, 0, 0]); side = side / np.linalg.norm(side) * width / 2
        mid = r + ax * 0.45
        hx = np.array([thick / 2, 0, 0])
        outline = [r, mid + side, t, mid - side]
        p = np.array([q + hx for q in outline] + [q - hx for q in outline])
        tris = [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7)]
        for k in range(4):
            a, b = k, (k + 1) % 4
            tris += [(a, b, b + 4), (a, b + 4, a + 4)]
        self._add(p, tris, rgb)

    def limb_point(self, y):
        """Centre z, tilt, width and depth of the reshaped limb at height y."""
        ys, zc, xt, zt = self.rings()
        dz = np.interp(y + 0.02, ys, zc) - np.interp(y - 0.02, ys, zc)
        return float(np.interp(y, ys, zc)), math.atan2(dz, 0.04), float(np.interp(y, ys, xt)), float(np.interp(y, ys, zt))

    def band(self, y, height, rgb, grow=1.25):
        z, tilt, xt, zt = self.limb_point(y)
        self.box((0, y, z), (xt * grow, height, max(zt, 0.02) * grow), rgb, tilt)

    def finish(self):
        n = self.base_count
        self.N[:n] = vertex_normals(self.V[:n], self.I[self.I.max(axis=1) < n], self.sign)
        # string anchor: the rearmost point of the limb (closest to the archer) near each end
        V = self.V[:n]
        a = np.abs(V[:, 1]); ymax = a.max()
        end = V[a > 0.75 * ymax]
        zmin = end[:, 2].min()
        tip_y = np.abs(end[end[:, 2] < zmin + 0.004, 1]).max()
        return self.V, self.N, self.U, self.I, float(tip_y), float(zmin + 0.003)


# ---- the three bows ----
def hunter(b):
    # The classic wooden bow: wood stays wood, a green leather grip, a leaf-and-feather charm.
    b.reshape(length=1.0, width=1.1, thick=1.1, grip=1.12)
    b.recolor(dark_grip=(72, 112, 52), dark_tips=(60, 48, 34))
    for s in (1, -1):
        b.band(s * 0.16, 0.022, (60, 48, 34), 1.2)
    # the charm hangs below the grip, in front of the bow, on the side away from the arrow
    z, _, xt, zt = b.limb_point(-0.17)
    knot = np.array([xt / 2 + 0.006, -0.17, z + zt / 2 + 0.004])
    b.box(knot, (0.012, 0.012, 0.012), (60, 48, 34))
    b.blade(knot, knot + [0.004, -0.27, 0.05], 0.07, 0.005, (236, 232, 220))
    b.blade(knot + [0.007, 0, 0], knot + [0.014, -0.19, 0.11], 0.08, 0.005, (96, 150, 60))
    b.blade(knot + [0.0035, 0, 0], knot + [0.009, -0.15, -0.005], 0.045, 0.005, (150, 60, 50))


def swift(b):
    # Short, slim recurve: blue limbs, pale grip, golden tips.
    b.reshape(length=0.95, width=0.78, thick=0.75, grip=0.88, curve=1.0, recurve=1.15, recurve_from=0.8)
    b.recolor(wood=(52, 104, 180), dark_grip=(214, 200, 170), dark_tips=(222, 170, 60), light=(150, 196, 232))
    for s in (1, -1):
        b.band(s * 0.14, 0.014, (222, 170, 60), 1.25)


def heavy(b):
    # A war bow: thick dark limbs, a big purple grip, iron bands, spikes and purple gems.
    b.reshape(length=1.0, width=1.75, thick=1.6, grip=1.45, curve=1.15)
    b.recolor(wood=(50, 38, 34), dark_grip=(112, 52, 150), dark_tips=(128, 128, 134), light=(84, 70, 60))
    iron, gem = (128, 128, 134), (176, 84, 226)
    for s in (1, -1):
        for y, h in ((0.17, 0.035), (0.36, 0.03), (0.52, 0.03)):
            b.band(s * y, h, iron, 1.4)
        for y in (0.265, 0.44):
            z, tilt, xt, zt = b.limb_point(s * y)
            for side in (1, -1):
                b.gem((side * xt / 2, s * y, z), (0.03, 0.06, zt * 0.7), gem, tilt)
        # a spike on the back of each limb, pointing at the target
        z, tilt, xt, zt = b.limb_point(s * 0.36)
        b.spike((0, s * 0.36, z + zt / 2), (0, s * 0.39, z + zt / 2 + 0.09), 0.02, iron)
    z, tilt, xt, zt = b.limb_point(0.0)
    for side in (1, -1):
        b.gem((side * xt / 2, 0, z), (0.035, 0.07, zt * 0.6), gem)


BOWS = {"hunter": hunter, "swift": swift, "heavy": heavy}


# ---- preview: a small flat-shaded software render ----
def render(V, N, U, I, pal, view, span, size=512, string=None, bg=(58, 58, 68)):
    R = np.array(view, dtype=np.float64)
    P = V @ R.T
    n = N @ R.T
    sx = P[:, 0] / span * size + size / 2
    sy = size / 2 - P[:, 1] / span * size
    img = np.zeros((size, size, 3)); img[:] = bg
    zb = np.full((size, size), -1e9)
    L = np.array([-0.35, 0.5, 0.8]); L /= np.linalg.norm(L)
    for t in I:
        x, y, z = sx[t], sy[t], P[t, 2]
        x0, x1 = int(max(0, math.floor(x.min()))), int(min(size - 1, math.ceil(x.max())))
        y0, y1 = int(max(0, math.floor(y.min()))), int(min(size - 1, math.ceil(y.max())))
        if x1 < x0 or y1 < y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-9: continue
        w0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
        w1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
        w2 = 1 - w0 - w1
        m = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        zz = w0 * z[0] + w1 * z[1] + w2 * z[2]
        sub = zb[y0:y1 + 1, x0:x1 + 1]
        m &= zz > sub
        if not m.any(): continue
        nn = n[t].mean(axis=0); nn /= max(np.linalg.norm(nn), 1e-12)
        if nn[2] < 0: nn = -nn
        shade = 0.45 + 0.65 * max(0.0, float(nn @ L))
        sub[m] = zz[m]
        img[y0:y1 + 1, x0:x1 + 1][m] = np.clip(pal.color(U[t[0]]) * shade, 0, 255)
    out = Image.fromarray(img.astype(np.uint8))
    if string is not None:
        ty, tz = string
        pts = np.array([[0, ty, tz], [0, -ty, tz]]) @ R.T
        q = [(p[0] / span * size + size / 2, size / 2 - p[1] / span * size) for p in pts]
        ImageDraw.Draw(out).line(q, fill=(235, 230, 215), width=2)
    return out


def rot(ax, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return {"x": np.array([[1, 0, 0], [0, c, -s], [0, s, c]]),
            "y": np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]]),
            "z": np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])}[ax]


# The in-game size of each bow (Def.Scale in Bows.cs), so the preview compares true sizes.
SCALE = {"hunter": 1.1, "swift": 0.9, "heavy": 1.25}


def main():
    pal = Palette(sys.argv[1])
    prev = sys.argv[2] if len(sys.argv) > 2 else None
    base = load(os.path.join(SRC, "bow_nostring.bin"))
    side = rot("y", 90)                       # looking along the width of the bow: the profile
    three = rot("x", 10) @ rot("y", 50)       # three-quarter view (the grip close up)
    sheets = []
    for name, fn in BOWS.items():
        b = Builder(base, pal)
        fn(b)
        V, N, U, I, ty, tz = b.finish()
        save(os.path.join(SRC, f"bow_{name}.bin"), V, N, U, I, ty, tz)
        print(f"{name}: {len(V)} vertices, {len(I)} triangles, bounds {V.min(axis=0).round(3)} .. {V.max(axis=0).round(3)}, string at y +-{ty:.3f} z {tz:.3f}")
        if prev:
            k = SCALE[name]
            sheets.append([render(V * k, N, U, I, pal, side, 1.9, string=(ty * k, tz * k)),
                           render(V, N, U, I, pal, three, 0.8, size=1024, string=(ty, tz)).crop((256, 0, 768, 512)),
                           render(V, N, U, I, pal, rot('z', -35) @ rot('y', 65), 1.5, string=(ty, tz))])
    if prev:
        os.makedirs(prev, exist_ok=True)
        w = sheets[0][0].width
        sheet = Image.new("RGB", (w * len(sheets), w * 3))
        for i, row in enumerate(sheets):
            for j, im in enumerate(row): sheet.paste(im, (i * w, j * w))
        sheet.save(os.path.join(prev, "bow_models.png"))


if __name__ == "__main__":
    main()
