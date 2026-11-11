# Paint an aircraft built by plane_hull.py, in any of its paint schemes. Every texel knows where it sits on the aircraft
# (texmap.npz), so camouflage, bands, panel lines and markings are drawn as exact shapes in aircraft space: a marking
# is drawn flat once and laid on each side by projection, the right way round on each side (lettering reads from left
# to right as you look at it, arrows and chevrons point where they should). It is bf109_paint.py with the aircraft's
# own choices moved into a paint spec (specs/<plane>_paint.py), which runs inside this file and so has all of the tools
# below to hand: it defines PREFIX, SCHEMES (name -> dict, the first is the standard paint) and paint(L), where L is
# the scheme's dict.
# Run: python -I plane_paint.py <dir with charts.json, texmap.npz> <out dir> <hull spec.py> <paint spec.py> [scheme ...]
# The standard scheme is written as <PREFIX>_BaseColor.png, the others as <PREFIX>_<Scheme>_BaseColor.png, each with
# its MetallicSmoothness (half size, smoothness in alpha).
import sys, json, math, numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage
from scipy.interpolate import PchipInterpolator
Image.MAX_IMAGE_PIXELS = None
D, OUT, HULLF, PAINTF = sys.argv[1:5]; WANT = sys.argv[5:]
HG = {}; exec(open(HULLF).read(), HG); HS = HG['SPEC']
C = json.load(open(D + '/charts.json')); T = np.load(D + '/texmap.npz')
S, PPU, PAD = C['size'], C['ppu'], C['pad']; ids = C['ids']; rect = C['rect']; ext = C['ext']
X, Y, Z = (T['pos'][..., k] for k in range(3)); NX, NY, NZ = (T['nrm'][..., k] for k in range(3)); CH = T['chart']
COV0 = CH > 0; AX = np.abs(X); px = 1.0 / PPU
def fam(*names): return np.isin(CH, [ids[n] for n in names if n in ids])
def famp(prefix): return np.isin(CH, [v for n, v in ids.items() if n.startswith(prefix)])
FUS = famp('fus'); BOOM = famp('boom'); SPIN = famp('spin'); FIN = famp('fin'); RUD = famp('rud')
WING = fam('wingL', 'wingR', 'ailL', 'ailR'); AIL = fam('ailL', 'ailR')
TAIL = fam('tailL', 'tailR', 'elevL', 'elevR'); ELEV = fam('elevL', 'elevR')
NAMED = {'fus', 'spin', 'fin', 'rud', 'wingL', 'wingR', 'ailL', 'ailR', 'tailL', 'tailR', 'elevL', 'elevR'}
EXTRA = np.isin(CH, [v for n, v in ids.items() if not n.startswith('sw_') and not any(n.startswith(k) for k in ('fus', 'boom', 'spin', 'fin', 'rud')) and n not in NAMED])
py_, px_ = np.mgrid[0:S, 0:S]
Uc = np.zeros((S, S), np.float32); Vc = np.zeros((S, S), np.float32)       # chart coordinates, in aircraft units
for n, (x, y, w, h) in rect.items():
    if n.startswith('sw_'): continue
    m = CH == ids[n]
    Uc[m] = (px_[m] - x - PAD) / PPU + ext[n][0]; Vc[m] = (py_[m] - y - PAD) / PPU + ext[n][1]
del py_, px_

# ---------------------------------------------------------------- the planform, from the hull spec
def pchip(pts):
    pts = np.array(sorted(pts), float); f = PchipInterpolator(pts[:, 0], pts[:, 1], extrapolate=True)
    return lambda s: f(s).astype(np.float32)
def planform(W, span):
    """Chord fraction (0 at the leading edge, 1 at the trailing edge) and the le/te/mid curves of a wing or tail."""
    le, te, mid = pchip(W['le']), pchip(W['te']), pchip(W['mid'])
    s = np.clip(span, 0, W['tip'])
    return (Y - le(s)) / np.maximum(te(s) - le(s), 1e-3), le, te, mid
WX = HS['wing'].get('x', 0.0)
WC, W_LE, W_TE, W_MID = planform(HS['wing'], np.abs(X - WX) if not WX else np.abs(np.abs(X) - WX))
TC, T_LE, T_TE, T_MID = planform(HS['tail'], AX)
def top_line(body=0):
    """The fuselage's top line z(y), along its middle."""
    st = np.array(HS['bodies'][body]['sections'], float); f = PchipInterpolator(st[:, 0], st[:, 1]); return lambda y: f(np.clip(y, st[0, 0], st[-1, 0])).astype(np.float32)
def bottom_line(body=0):
    st = np.array(HS['bodies'][body]['sections'], float); f = PchipInterpolator(st[:, 0], st[:, 2]); return lambda y: f(np.clip(y, st[0, 0], st[-1, 0])).astype(np.float32)
UPPER_W = (WING & ((NZ > 0) | (Z > W_MID(np.clip(AX, 0, HS['wing']['tip']))))) | (TAIL & ((NZ > 0) | (Z > T_MID(np.clip(AX, 0, HS['tail']['tip'])))))

# ---------------------------------------------------------------- painting
img = None
def lay(mask, col):
    global img
    m = np.clip(mask, 0, 1).astype(np.float32)[..., None]; img = img * (1 - m) + np.asarray(col, np.float32) * m
def aa(d, w=None): return np.clip(0.5 - d / (w or px), 0, 1)       # coverage of "d < 0" (d in aircraft units)
def band(v, a, b): return np.minimum(aa(a - v), aa(v - b))
def sstep(x, a, b): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)
def C3(*v): return np.array(v, np.float32)
def noise3(seed, cell=0.10, sigma=5.0, lo=(-7, -6, -2.5), hi=(7, 7, 3.0)):
    """A smooth random field over the whole aircraft (cells of `cell` m, blurred by `sigma` cells), unit variance."""
    rng = np.random.default_rng(seed); shp = tuple(int((h - l) / cell) + 2 for l, h in zip(lo, hi))
    N = ndimage.gaussian_filter(rng.random(shp).astype(np.float32), sigma, mode='wrap'); N = (N - N.mean()) / N.std()
    out = np.zeros((S, S), np.float32)
    co = [np.clip((A[COV0] - l) / cell, 0, s - 1.001) for A, l, s in zip((X, Y, Z), lo, shp)]
    out[COV0] = ndimage.map_coordinates(N, co, order=1); return out
def noise2(seed, a_, b_, cell=0.04, sigma=1.5, lo=(-7, -3), hi=(7, 3)):
    rng = np.random.default_rng(seed); shp = tuple(int((h - l) / cell) + 2 for l, h in zip(lo, hi))
    N = ndimage.gaussian_filter(rng.random(shp).astype(np.float32), sigma, mode='wrap'); N = (N - N.mean()) / N.std()
    out = np.zeros((S, S), np.float32)
    co = [np.clip((A[COV0] - l) / cell, 0, s - 1.001) for A, l, s in zip((a_, b_), lo, shp)]
    out[COV0] = ndimage.map_coordinates(N, co, order=1); return out

# ---------------------------------------------------------------- panel lines and rivets
LINES = RIV = RIVT = PID = None
def add_lines(mask, coord, stations, along, scale=1.0, rivets=True):
    global LINES, RIV, RIVT, PID
    st = np.asarray(sorted(stations), np.float32)
    idx = np.searchsorted(st, coord); d = np.full(coord.shape, 9.0, np.float32)
    for k in (-1, 0):
        j = np.clip(idx + k, 0, len(st) - 1); d = np.minimum(d, np.abs(coord - st[j]))
    d = d * scale
    LINES = np.where(mask, np.minimum(LINES, d), LINES)
    if rivets:
        r = np.abs(d - 0.026); better = mask & (r < RIV)
        RIVT = np.where(better, along, RIVT); RIV = np.where(mask, np.minimum(RIV, r), RIV)
    PID = np.where(mask, PID * 31 + idx + 7, PID)
def ring(mask, a_, b_, ca, cb, r, col, wdt=0.005, op=0.8):
    d = np.abs(np.hypot(a_ - ca, b_ - cb) - r); lay(aa(d - wdt, px * 1.2) * mask * op, col)

# ---------------------------------------------------------------- markings: decals drawn flat, laid on by projection
def decal(draw_fn, w_units, h_units, res=700):
    W, H = max(2, int(w_units * res)), max(2, int(h_units * res)); im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); draw_fn(ImageDraw.Draw(im), W, H, res)
    return np.asarray(im).astype(np.float32), w_units, h_units
def put(mask, a_, b_, dec, ca, cb, flip=False, op=1.0):
    """Lay a decal: a_ runs left to right across it as a viewer sees it, b_ runs up; (ca, cb) its centre."""
    global img
    dec, w_units, h_units = dec
    H, W = dec.shape[:2]; u = (a_ - ca) / w_units + 0.5; v = 0.5 - (b_ - cb) / h_units
    if flip: u = 1 - u
    ok = mask & (u > 0) & (u < 1) & (v > 0) & (v < 1)
    xi = np.clip(u[ok] * W - 0.5, 0, W - 1.001); yi = np.clip(v[ok] * H - 0.5, 0, H - 1.001)
    x0 = xi.astype(int); y0 = yi.astype(int); fx = (xi - x0)[:, None]; fy = (yi - y0)[:, None]
    smp = dec[y0, x0] * (1 - fx) * (1 - fy) + dec[y0, x0 + 1] * fx * (1 - fy) + dec[y0 + 1, x0] * (1 - fx) * fy + dec[y0 + 1, x0 + 1] * fx * fy
    al = smp[:, 3:4] / 255.0 * op
    img[ok] = img[ok] * (1 - al) + smp[:, :3] * al
def rgba(c): return tuple(int(v) for v in c) + (255,)
def roundel(r, rings):
    """Concentric rings, outermost first: [(fraction of r, colour), ...]."""
    def fn(d, W, H, res):
        cx, cy = W / 2, H / 2
        for f, col in rings: rr = f * r * res; d.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), fill=rgba(col))
    return decal(fn, 2 * r, 2 * r)
def stripes(w, h, cols, vertical=True):
    """Equal stripes, left to right (or top to bottom)."""
    def fn(d, W, H, res):
        n = len(cols)
        for k, col in enumerate(cols):
            if vertical: d.rectangle((W * k / n, 0, W * (k + 1) / n, H), fill=rgba(col))
            else: d.rectangle((0, H * k / n, W, H * (k + 1) / n), fill=rgba(col))
    return decal(fn, w, h)
def star(r, col, disc=None, disc_r=1.0, bars=None, outline=None):
    """A five-pointed star (US style when given bars): bars = (length, height) either side of the disc, in the star's
    colour, their top edge on the line of the star's arms; outline = (colour, width) round the disc and bars."""
    def fn(d, W, H, res):
        cx, cy = W / 2, H / 2; R = r * res
        if bars:
            bl, bh = bars[0] * res, bars[1] * res; t0 = cy - R * 0.309
            if outline: o = outline[1] * res; d.rectangle((cx - R - bl - o, t0 - o, cx + R + bl + o, t0 + bh + o), fill=rgba(outline[0]))
            d.rectangle((cx - R - bl, t0, cx + R + bl, t0 + bh), fill=rgba(col))
        if disc is not None:
            if outline and bars: o = outline[1] * res; d.ellipse((cx - R * disc_r - o, cy - R * disc_r - o, cx + R * disc_r + o, cy + R * disc_r + o), fill=rgba(outline[0]))
            d.ellipse((cx - R * disc_r, cy - R * disc_r, cx + R * disc_r, cy + R * disc_r), fill=rgba(disc))
        pts = []
        for k in range(10):
            a = -math.pi / 2 + k * math.pi / 5; rr = R if k % 2 == 0 else R * 0.382
            pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
        d.polygon(pts, fill=rgba(col if disc is not None else col))
    wd = 2 * r + (2 * bars[0] + 2 * (outline[1] if outline else 0) + 0.02 if bars else 0)
    return decal(fn, wd, 2 * r + 0.1)
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
def text(s, h_units, col, outline=None, ow=0.06):
    f = ImageFont.truetype(FONT, 400); bb = f.getbbox(s); asp = (bb[2] - bb[0] + 40) / (bb[3] - bb[1] + 40)
    def fn(d, W, H, res):
        ff = ImageFont.truetype(FONT, int(H * 0.86 * 400 / (bb[3] - bb[1] + 40))); b2 = ff.getbbox(s)
        d.text(((W - (b2[2] - b2[0])) / 2 - b2[0], (H - (b2[3] - b2[1])) / 2 - b2[1]), s, font=ff, fill=rgba(col),
               stroke_width=int(H * ow) if outline is not None else 0, stroke_fill=None if outline is None else rgba(outline))
    return decal(fn, h_units * asp, h_units)
def both_sides(mask, dec_fn, y, z, nose_first=None, x0=0.0):
    """Lay a decal on a body's two sides (the body on x = x0), centred at (y, z), reading left to right on each.
    nose_first=True flips it on the right side, for a marking that must point at the nose on both sides."""
    for sx in (1, -1):
        m = mask & ((X - x0) * sx > 0)
        put(m, Y * sx, Z, dec_fn, y * sx, z, flip=(nose_first is not None and sx < 0))
def on_wings(dec, s, upper=True, lower=True, chord=0.5, op=1.0, wings=(1, -1)):
    """Lay a decal on each wing (or those of `wings`: 1 the left, -1 the right) at span s, mid-chord (or `chord`),
    seen from above and below; upper/lower may also name the one wing for that surface."""
    for sx in (1, -1):
        up = upper if not isinstance(upper, int) or isinstance(upper, bool) else upper == sx
        lo = lower if not isinstance(lower, int) or isinstance(lower, bool) else lower == sx
        if sx not in wings: continue
        yc = float(W_LE(np.float32(s)) + chord * (W_TE(np.float32(s)) - W_LE(np.float32(s))))
        if up: put(WING & (X * sx > 0) & (NZ > 0.25), -X, -Y, dec, -sx * s, -yc, op=op)       # seen from above, nose up
        if lo: put(WING & (X * sx > 0) & (NZ < -0.25), X, -Y, dec, sx * s, -yc, op=op)       # seen from below, nose up

# ---------------------------------------------------------------- the run
G = globals(); exec(open(PAINTF).read(), G)
names = list(SCHEMES)
for i, name in enumerate(names):
    if WANT and name not in WANT: continue
    L = SCHEMES[name]
    img = np.zeros((S, S, 3), np.float32); cov = COV0.copy()
    LINES = np.full((S, S), 9.0, np.float32); RIV = np.full((S, S), 9.0, np.float32); RIVT = np.zeros((S, S), np.float32); PID = np.zeros((S, S), np.int64)
    GLOSS = np.zeros((S, S), np.float32)          # extra smoothness, a spec may add to it (a polished spinner, glossy paint)
    METAL = np.zeros((S, S), np.float32)          # bare metal, a spec may add to it (natural metal finish)
    sw = paint(L)                                 # returns the swatch colours: {'sw_paint': ..., ...}
    side = np.sign(X).astype(np.int64)
    PIDs = PID * 3 + side + np.round(NZ).astype(np.int64) * 5
    h = (np.abs(PIDs * 2654435761) % 1000) / 1000.0
    img *= (1.0 + (h - 0.5) * L.get('panel_var', 0.06))[..., None]
    if 'after_panels' in G: after_panels(L)
    line = aa(LINES - 0.0055, px * 1.2) * cov
    rspace = 0.058
    riv = aa(np.hypot(RIV, (RIVT % rspace) - rspace / 2) - 0.0065, px) * cov * (LINES > 0.02) * (1 - SPIN)
    INK = np.asarray(L.get('ink', (30, 32, 34)), np.float32)
    m = line[..., None] * L.get('line_op', 0.7); img = img * (1 - m) + INK * m
    m = riv[..., None] * L.get('rivet_op', 0.22); img = img * (1 - m) + INK * m
    if 'finish' in G: finish(L)
    for n, col in sw.items():
        if n in rect: x, y, w, h_ = rect[n]; img[y:y + h_, x:x + w] = col; cov[y:y + h_, x:x + w] = True
    _, (iy, ix) = ndimage.distance_transform_edt(~cov, return_indices=True)
    img = img[iy, ix]; line = line[iy, ix]; gl = GLOSS[iy, ix]; mt = METAL[iy, ix]
    steel = np.zeros((S, S), np.float32); x, y, w, h_ = rect['sw_steel']; steel[y:y + h_, x:x + w] = 1; steel = np.maximum(steel, mt)
    smooth = L.get('smooth', 0.40) * (1 - steel) + 0.55 * steel; smooth = np.maximum(smooth * (1 - 0.5 * line), gl)
    out = np.clip(img, 0, 255).astype(np.uint8)
    msm = np.zeros((S, S, 4), np.uint8); msm[..., 0] = msm[..., 1] = msm[..., 2] = (steel * 200).astype(np.uint8); msm[..., 3] = (np.clip(smooth, 0, 1) * 255).astype(np.uint8)
    tag = '' if i == 0 else '_' + name.capitalize()
    Image.fromarray(out).save(f'{OUT}/{PREFIX}{tag}_BaseColor.png')
    Image.fromarray(msm).resize((S // 2, S // 2), Image.LANCZOS).save(f'{OUT}/{PREFIX}{tag}_MetallicSmoothness.png')
    Image.fromarray(out).resize((1024, 1024), Image.LANCZOS).save(f'{OUT}/atlas_preview{tag}.jpg', quality=88)
    print('painted', name, '->', f'{PREFIX}{tag}_BaseColor.png')
