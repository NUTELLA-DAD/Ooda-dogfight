# A clean P-51 airframe, lofted from sections instead of generated: fuselage, wings, tail, hinged control surfaces,
# canopy and its frame, belly scoop, exhaust stacks, guns and mast. It keeps the old model's size, its cockpit
# opening, its hinge lines, its propeller and its cockpit parts, so the game's mounts and scripts fit unchanged.
# Every part is unwrapped into its own chart; the charts and a per-texel map of where each texel sits on the aircraft
# are written out for p51_paint.py.
# Run: python -I p51_hull.py -- <in.blend (old P-51)> <out.blend> <out_dir for charts.json / texmap.npz>
import bpy, bmesh, sys, math, json, numpy as np
from mathutils import Vector
from scipy.interpolate import PchipInterpolator

a = sys.argv[sys.argv.index('--') + 1:]
SRC, DST, OUT = a[:3]
SIZE, PPU, PAD = 4096, 280, 10
KEEP = (2880, 2608, 288, 288)             # the propeller's palette square (x, y from the top, w, h): left where it is

# ------------------------------------------------------------------ mesh accumulator
class Part:
    def __init__(s, name): s.name = name; s.V = []; s.F = []; s.UV = []      # UV: (chart, [(u, v), ...]) per face
    def v(s, p): s.V.append(tuple(float(x) for x in p)); return len(s.V) - 1
    def f(s, idx, chart, uv): s.F.append(tuple(idx)); s.UV.append((chart, [tuple(map(float, t)) for t in uv]))
    def grid(s, P, U, chart, wrap=False, skip=None):
        """P[i][j] points, U[i][j] chart coords; quads between rows i, i+1 and columns j, j+1."""
        n, m = len(P), len(P[0]); I = [[s.v(P[i][j]) for j in range(m)] for i in range(n)]
        for i in range(n - 1):
            for j in range(m if wrap else m - 1):
                if skip and skip(i, j): continue
                k = (j + 1) % m
                s.f((I[i][j], I[i][k], I[i + 1][k], I[i + 1][j]), chart, (U[i][j], U[i][k], U[i + 1][k], U[i + 1][j]))
        return I
    def cap(s, idx, chart): s.f(idx, chart, [(0, 0)] * len(idx))
PARTS = {}
def part(name):
    if name not in PARTS: PARTS[name] = Part(name)
    return PARTS[name]

# ------------------------------------------------------------------ fuselage sections
ST = np.array([   # y, top, bottom, half width
    (-3.97, 0.756, -0.304, 0.530), (-3.60, 0.860, -0.520, 0.590), (-3.20, 0.890, -0.680, 0.620),
    (-2.60, 0.890, -0.800, 0.630), (-1.90, 0.870, -0.860, 0.630), (-1.00, 0.750, -0.900, 0.630),
    (0.00, 0.600, -0.900, 0.600), (0.90, 0.440, -0.860, 0.520), (1.50, 0.390, -0.760, 0.430),
    (2.10, 0.350, -0.620, 0.330), (2.70, 0.320, -0.480, 0.240), (3.30, 0.290, -0.360, 0.160),
    (3.90, 0.220, -0.250, 0.085), (4.25, 0.060, -0.100, 0.020)])
fzt, fzb, fw = (PchipInterpolator(ST[:, 0], ST[:, k]) for k in (1, 2, 3))
fn = PchipInterpolator([-3.97, -3.2, -1.9, -1.2, 0.2, 0.9, 2.7, 4.25], [2.0, 2.35, 2.9, 4.0, 4.0, 2.9, 2.2, 2.0])    # boxier beside the cockpit: room for the pilot's elbows
def sec(y):
    zt, zb, w = float(fzt(y)), float(fzb(y)), float(fw(y)); return zt, zb, w, zb + 0.55 * (zt - zb), float(fn(y))
def ring_pt(y, th):
    zt, zb, w, zc, n = sec(y); c, s_ = math.cos(th), math.sin(th)
    return Vector((w * math.copysign(abs(c) ** (2 / n), c), y, zc + ((zt - zc) if s_ > 0 else (zc - zb)) * math.copysign(abs(s_) ** (2 / n), s_)))
def hull_top(x, y):
    zt, zb, w, zc, n = sec(y); return zc + (zt - zc) * max(0.0, 1 - min(1.0, abs(x) / w) ** n) ** (1 / n)

# the cockpit opening: between OY0 and OY1, as wide as xs(y)
OY0, OY1 = -1.75, 0.80
fxs = PchipInterpolator([-3.97, -1.75, -1.30, -0.50, 0.30, 0.80, 2.0, 4.25], [0.30, 0.38, 0.47, 0.48, 0.42, 0.26, 0.15, 0.008])
def xs(y): return float(fxs(y))
def th_split(y):
    zt, zb, w, zc, n = sec(y); return math.acos(min(0.98, xs(y) / w) ** (n / 2))

M2, T = 22, 12
ys = sorted(set(np.round(np.concatenate([np.linspace(-3.97, OY0, 30), np.linspace(OY0, OY1, 36), np.linspace(OY1, 4.25, 44)]), 5)))
fus = part('P51_Body')
P, U = [], []
for y in ys:
    t0 = th_split(y)
    ths = list(np.linspace(-math.pi / 2, t0, M2 + 1)) + list(np.linspace(t0, math.pi - t0, T + 1))[1:] + list(np.linspace(math.pi - t0, 1.5 * math.pi, M2 + 1))[1:]
    row = [ring_pt(y, t) for t in ths]
    arc = np.concatenate([[0], np.cumsum([(row[k + 1] - row[k]).length for k in range(len(row) - 1)])])
    arc -= arc[M2 + T // 2]                              # zero on the top centre line
    P.append(row); U.append([(y, float(v)) for v in arc])
def in_open(i, j): return OY0 - 1e-6 <= ys[i] and ys[i + 1] <= OY1 + 1e-6 and M2 <= j < M2 + T
I = fus.grid(P, U, 'fus', skip=in_open)
c0 = fus.v((0, ys[0], 0.226)); c1 = fus.v((0, 4.27, -0.02))
for j in range(len(P[0]) - 1):
    fus.f((c0, I[0][j], I[0][j + 1]), 'sw_red', [(0, 0)] * 3)
    fus.f((c1, I[-1][j + 1], I[-1][j]), 'sw_metal', [(0, 0)] * 3)
# the opening's lip: the rim turned down into the hull, so the skin has thickness there
iy = [i for i, y in enumerate(ys) if OY0 - 1e-6 <= y <= OY1 + 1e-6]
rim = [(i, M2) for i in iy] + [(iy[-1], j) for j in range(M2 + 1, M2 + T)] + [(i, M2 + T) for i in reversed(iy)] + [(iy[0], j) for j in range(M2 + T - 1, M2, -1)]
rimv = []
for (i, j) in rim:
    p = P[i][j]; c = Vector((0, (OY0 + OY1) / 2, p.z)); d = (c - p); d.z = 0; d.normalize()
    rimv.append(fus.v(p + d * 0.03 + Vector((0, 0, -0.07))))
for k in range(len(rim)):
    k2 = (k + 1) % len(rim)
    fus.f((I[rim[k][0]][rim[k][1]], I[rim[k2][0]][rim[k2][1]], rimv[k2], rimv[k]), 'sw_olive', [(0, 0)] * 4)

# ------------------------------------------------------------------ canopy (glass) and its frame
fH = PchipInterpolator([OY0, -1.55, -1.25, -0.90, -0.40, 0.10, 0.50, OY1], [0.0, 0.27, 0.56, 0.68, 0.66, 0.50, 0.25, 0.0])
CP = 2.5
def can_pt(y, ph, lift=0.0):
    x0 = xs(y); zs = hull_top(x0, y); H = float(fH(y)) + lift
    x = x0 * math.copysign(abs(math.sin(ph)) ** (2 / CP), math.sin(ph)) * (1 + 0.10 * math.sin(math.pi * min(1, max(0, float(fH(y)) / 0.68))) * abs(math.sin(2 * ph)))
    return Vector((x * (1 + lift * 0.6), y, max(hull_top(x, y) - 0.004, zs + H * abs(math.cos(ph)) ** (2 / CP)) + lift * 0.3))
cys = list(np.linspace(OY0, OY1, 44)); phs = list(np.linspace(-math.pi / 2, math.pi / 2, 25))
can = part('P51_Canopy')
can.grid([[can_pt(y, ph) for ph in phs] for y in cys], [[(0, 0)] * len(phs)] * len(cys), 'sw_glass')
def strip(y0, y1, ph0, ph1, n_y, n_ph, lift=0.012, chart='sw_olive'):
    """A raised band on the canopy between two stations and two angles (frame members)."""
    yy = np.linspace(y0, y1, n_y); pp = np.linspace(ph0, ph1, n_ph)
    top_ = [[can_pt(y, ph, lift) for ph in pp] for y in yy]; bot = [[can_pt(y, ph, -0.004) for ph in pp] for y in yy]
    z = [[(0, 0)] * n_ph] * n_y
    It = fus.grid(top_, z, chart); Ib = fus.grid(bot, z, chart)
    edge = [(i, 0) for i in range(n_y)] + [(n_y - 1, j) for j in range(1, n_ph)] + [(i, n_ph - 1) for i in range(n_y - 2, -1, -1)] + [(0, j) for j in range(n_ph - 2, 0, -1)]
    for k in range(len(edge)):
        (i0, j0), (i1, j1) = edge[k], edge[(k + 1) % len(edge)]
        fus.f((It[i0][j0], It[i1][j1], Ib[i1][j1], Ib[i0][j0]), chart, [(0, 0)] * 4)
YW = -1.18                                                            # the windshield's rear arch
strip(YW - 0.035, YW + 0.035, -math.pi / 2, math.pi / 2, 3, 25)
for sgn in (-1, 1):
    strip(OY0 + 0.02, OY1 - 0.02, sgn * math.pi / 2, sgn * (math.pi / 2 - 0.13), 40, 3)            # sill rails
    strip(OY0 + 0.03, YW, sgn * 0.52, sgn * 0.62, 10, 3)                                             # windshield posts
strip(OY0, OY0 + 0.07, -math.pi / 2, math.pi / 2, 3, 25)                                           # windshield foot
strip(OY1 - 0.10, OY1, -math.pi / 2, math.pi / 2, 3, 25)                                           # canopy tail

# ------------------------------------------------------------------ wings, tailplane, fin: lofted aerofoils
def aerofoil(c, t):  # half thickness at chord fraction c for thickness ratio t (closed trailing edge)
    return 5 * t * (0.2969 * math.sqrt(c) - 0.1260 * c - 0.3516 * c * c + 0.2843 * c ** 3 - 0.1036 * c ** 4)
def cfr(c0, c1, n):
    k = (1 - np.cos(np.linspace(0, math.pi, n))) / 2 if c0 == 0 and c1 == 1 else (1 - np.cos(np.linspace(0, math.pi / 2, n))) if c0 == 0 else np.linspace(0, 1, n)
    return list(c0 + (c1 - c0) * k)
def lifting(pname, chart, spans, le, te, mid, tc, span_axis, thick_axis, sgn, c0=None, c1=None, camber=0.0,
            tip_r=0.0, caps=(None, None), n=17, close='sw_ink'):
    """One piece of a wing-like surface. le(s), te(s): the y of the edges; mid(s): its height on thick_axis;
    c0(s), c1(s): the chord fractions it runs between (a hinge cut). Returns its first and last loops."""
    pt = part(pname); P, U = [], []; s_end = spans[-1]
    for s in spans:
        k = 1.0
        if tip_r and s > s_end - tip_r: k = math.sqrt(max(0.0, 1 - ((s - (s_end - tip_r)) / tip_r) ** 2))
        k = max(k, 0.12)
        L, Tt = le(s), te(s); ch = Tt - L; a0 = c0(s) if c0 else 0.0; a1 = c1(s) if c1 else 1.0
        cs = cfr(a0, a1, n)
        loop = [(c, 1) for c in reversed(cs)]
        if a0 > 0: loop += [(a0 - 0.5 * aerofoil(a0, tc(s)) * 0.9, 0.7), (a0 - 0.5 * aerofoil(a0, tc(s)) * 1.3, 0.0), (a0 - 0.5 * aerofoil(a0, tc(s)) * 0.9, -0.7)]
        loop += [(c, -1) for c in cs[(1 if a0 == 0 else 0):]]
        row, uv = [], []; arc = 0.0; prev = None
        for (c, side) in loop:
            cc = min(max(c, 0.0), 1.0); th = aerofoil(cc if abs(side) == 1 else a0, tc(s)) * ch * side * k
            yy = L + ch * (0.4 + (c - 0.4) * k)
            cam = camber * ch * 4 * cc * (1 - cc)
            p = [0, 0, 0]; p[1] = yy; p[span_axis] = s * sgn if span_axis == 0 else s; p[thick_axis] += mid(s) + th + cam if thick_axis == 2 else th
            if thick_axis == 0: p[0] = th
            p = Vector(p)
            if prev is not None: arc += (p - prev).length
            prev = p; row.append(p); uv.append((s, arc))
        half = uv[len(uv) // 2][1]; uv = [(u_, v_ - half) for (u_, v_) in uv]
        P.append(row); U.append(uv)
    m = len(P[0])
    I = pt.grid(P, U, chart)
    for i in range(len(P) - 1):                                           # the closing wall (hinge cove), where there is one
        if (P[i][0] - P[i][-1]).length > 1e-4 or (P[i + 1][0] - P[i + 1][-1]).length > 1e-4:
            pt.f((I[i][-1], I[i][0], I[i + 1][0], I[i + 1][-1]), close, [(0, 0)] * 4)
    if caps[0]: pt.cap(I[0], caps[0])
    if caps[1]: pt.cap(I[-1], caps[1])

HING = {}
def lin(p0, p1): return lambda s: p0[1] + (p1[1] - p0[1]) * (s - p0[0]) / (p1[0] - p0[0])
# wing
def root(s): return max(0.0, 1 - (s - 0.55) / 0.75) ** 2 if s > 0.55 else 1.0          # the fairing into the fuselage
w_le = lambda s: -2.55 + 0.05 * s - 0.20 * root(s); w_te = lambda s: -0.07 - 0.137 * s + 0.30 * root(s); w_mid = lambda s: -0.68 + 0.121 * s
w_tc = lambda s: (0.135 - 0.005 * s) * (1 + 0.55 * root(s))
w_h = lin((3.3, -1.043), (5.3, -1.281)); w_f = lambda s: (w_h(s) - w_le(s)) / (w_te(s) - w_le(s))
for sgn, sd in ((1, 'L'), (-1, 'R')):
    kw = dict(le=w_le, te=w_te, mid=w_mid, tc=w_tc, span_axis=0, thick_axis=2, sgn=sgn, camber=0.012)
    lifting('P51_Body', 'wing' + sd, list(np.linspace(0.0, 1.35, 20)) + list(np.linspace(1.35, 3.30, 18))[1:], caps=(None, 'sw_ink'), **kw)
    lifting('P51_Body', 'wing' + sd, list(np.linspace(3.30, 5.30, 18)), c1=w_f, **kw)
    lifting('P51_Body', 'wing' + sd, [5.30] + list(5.30 + 0.34 * np.sin(np.linspace(0, math.pi / 2, 9))[1:]), tip_r=0.34, caps=('sw_ink', 'sw_metal'), **kw)
    lifting('P51_Aileron' + sd, 'ail' + sd, list(np.linspace(3.315, 5.285, 16)), c0=lambda s: w_f(s) + 0.012, caps=('sw_ink', 'sw_ink'), **kw)
    HING['P51_Aileron' + sd] = ((sgn * 3.3, w_h(3.3), w_mid(3.3)), (sgn * 5.3, w_h(5.3), w_mid(5.3)))
# tailplane
t_le = lambda s: 2.20 + 0.223 * s; t_te = lambda s: 3.43 - 0.01 * s; t_mid = lambda s: -0.09; t_tc = lambda s: 0.10
t_f = lambda s: (2.99 - t_le(s)) / (t_te(s) - t_le(s))
for sgn, sd in ((1, 'L'), (-1, 'R')):
    kw = dict(le=t_le, te=t_te, mid=t_mid, tc=t_tc, span_axis=0, thick_axis=2, sgn=sgn)
    lifting('P51_Body', 'tail' + sd, list(np.linspace(0.05, 0.35, 4)), caps=(None, 'sw_ink'), **kw)
    lifting('P51_Body', 'tail' + sd, list(np.linspace(0.35, 2.00, 14)), c1=t_f, **kw)
    lifting('P51_Body', 'tail' + sd, [2.00] + list(2.00 + 0.16 * np.sin(np.linspace(0, math.pi / 2, 7))[1:]), tip_r=0.16, caps=('sw_ink', 'sw_metal'), **kw)
    lifting('P51_Elevator' + sd, 'elev' + sd, list(np.linspace(0.362, 1.988, 12)), c0=lambda s: t_f(s) + 0.02, caps=('sw_ink', 'sw_ink'), **kw)
    HING['P51_Elevator' + sd] = ((sgn * 0.35, 2.99, -0.09), (sgn * 2.0, 2.99, -0.09))
# fin and rudder (span along z, thickness along x)
f_le = lambda s: 2.50 + 0.56 * (s - 0.2); f_te = lambda s: 4.20 - 0.14 * s; f_tc = lambda s: 0.085
f_f = lambda s: (3.56 - f_le(s)) / (f_te(s) - f_le(s))
kw = dict(le=f_le, te=f_te, mid=lambda s: 0, tc=f_tc, span_axis=2, thick_axis=0, sgn=1)
lifting('P51_Body', 'fin', list(np.linspace(0.12, 1.38, 14)), c1=f_f, **kw)
lifting('P51_Body', 'fin', [1.38] + list(1.38 + 0.13 * np.sin(np.linspace(0, math.pi / 2, 7))[1:]), tip_r=0.13, caps=('sw_ink', 'sw_red'), **kw)
lifting('P51_Rudder', 'rud', list(np.linspace(0.13, 1.37, 12)), c0=lambda s: f_f(s) + 0.02, caps=('sw_ink', 'sw_ink'), **kw)
HING['P51_Rudder'] = ((0, 3.56, 0.12), (0, 3.56, 1.38))
# a dorsal fillet running into the fin
dors = [(1.75, 0.0), (2.2, 0.06), (2.6, 0.16), (2.95, 0.30)]
Pd, Ud = [], []
for (y, h) in dors + [(3.2, 0.36)]:
    z0 = hull_top(0, y) - 0.03
    Pd.append([Vector((-0.05, y, z0)), Vector((-0.022, y, z0 + h * 0.8)), Vector((0, y, z0 + h + 0.03)), Vector((0.022, y, z0 + h * 0.8)), Vector((0.05, y, z0))])
    Ud.append([(0, 0)] * 5)
fus.grid(Pd, Ud, 'sw_metal')

# ------------------------------------------------------------------ belly scoop
SC = [(-0.62, 0.25, -0.86, -1.13), (-0.45, 0.28, -0.84, -1.19), (0.3, 0.31, -0.84, -1.27), (1.0, 0.29, -0.80, -1.22), (1.6, 0.22, -0.72, -1.05), (2.15, 0.10, -0.58, -0.70)]
Ps, Us = [], []
for (y, w, zt, zb) in SC:
    row = []
    for th in np.linspace(0, 2 * math.pi, 25)[:-1]:
        c, s_ = math.cos(th), math.sin(th); n = 3.2
        row.append(Vector((w * math.copysign(abs(c) ** (2 / n), c), y, (zt + zb) / 2 + (zt - zb) / 2 * math.copysign(abs(s_) ** (2 / n), s_))))
    arc = np.concatenate([[0], np.cumsum([(row[k + 1] - row[k]).length for k in range(len(row) - 1)])]); arc -= arc[18]
    Ps.append(row); Us.append([(y, float(v)) for v in arc])
Is = fus.grid(Ps, Us, 'scoop', wrap=True)
mouth = [fus.v(p + Vector((0, 0.10, 0)) + (Vector((0, p.y, -1.0)) - p) * 0.14) for p in Ps[0]]
for j in range(24):
    k = (j + 1) % 24; fus.f((Is[0][j], Is[0][k], mouth[k], mouth[j]), 'sw_metal', [(0, 0)] * 4)
fus.cap(mouth, 'sw_ink')

# ------------------------------------------------------------------ small parts: tubes
def tube(p0, p1, r0, r1, chart, nseg=10, pname='P51_Body', squash=1.0, endcap='sw_ink'):
    pt = part(pname); p0, p1 = Vector(p0), Vector(p1); ax = (p1 - p0).normalized()
    u = ax.cross(Vector((0, 0, 1))); u = u.normalized() if u.length > 1e-3 else Vector((1, 0, 0)); v = ax.cross(u)
    rings = []
    for (p, r) in ((p0, r0), (p1, r1)):
        rings.append([p + (u * math.cos(t) + v * math.sin(t) * squash) * r for t in np.linspace(0, 2 * math.pi, nseg + 1)[:-1]])
    I = pt.grid(rings, [[(0, 0)] * nseg] * 2, chart, wrap=True)
    pt.cap(I[1], endcap); pt.cap(I[0], chart)
for sgn in (-1, 1):
    for k in range(6):                                                  # exhaust stacks
        y = -3.42 + k * 0.17; x = float(fw(y)) * 0.985
        z = 0.30; hx = hull_x = x * (1 - (abs(z - sec(y)[3]) / (sec(y)[0] - sec(y)[3])) ** sec(y)[4]) ** (1 / sec(y)[4])
        tube((sgn * (hx - 0.04), y, z), (sgn * (hx + 0.075), y + 0.10, z - 0.015), 0.058, 0.050, 'sw_steel', nseg=8, squash=0.75)
    for k, s in enumerate((2.05, 2.30, 2.55)):                          # guns in the leading edge
        tube((sgn * s, w_le(s) + 0.10, w_mid(s) + 0.012), (sgn * s, w_le(s) - 0.10 - 0.03 * (1 - k % 2), w_mid(s) + 0.012), 0.032, 0.028, 'sw_steel', nseg=8)
tube((0, 1.20, hull_top(0, 1.20) - 0.03), (0, 1.32, hull_top(0, 1.2) + 0.52), 0.035, 0.018, 'sw_metal', nseg=8, squash=0.45)      # mast
tube((-4.6, w_le(4.6) + 0.3, w_mid(4.6) - 0.09), (-4.6, w_le(4.6) + 0.3, w_mid(4.6) - 0.20), 0.02, 0.016, 'sw_metal', nseg=6)       # pitot
tube((-4.6, w_le(4.6) + 0.34, w_mid(4.6) - 0.20), (-4.6, w_le(4.6) - 0.10, w_mid(4.6) - 0.20), 0.016, 0.012, 'sw_steel', nseg=6)

# ------------------------------------------------------------------ charts: pack into the atlas
ext = {}
for p in PARTS.values():
    for (ch, uv) in p.UV:
        e = ext.setdefault(ch, [1e9, 1e9, -1e9, -1e9])
        for (u_, v_) in uv: e[0] = min(e[0], u_); e[1] = min(e[1], v_); e[2] = max(e[2], u_); e[3] = max(e[3], v_)
G = 8; occ = np.zeros((SIZE // G, SIZE // G), bool)
occ[KEEP[1] // G - 1:(KEEP[1] + KEEP[3]) // G + 2, KEEP[0] // G - 1:(KEEP[0] + KEEP[2]) // G + 2] = True
rect = {}
def size(ch):
    e = ext[ch]
    if ch.startswith('sw_'): return 32, 32
    return int(math.ceil((e[2] - e[0]) * PPU)) + 2 * PAD, int(math.ceil((e[3] - e[1]) * PPU)) + 2 * PAD
for ch in sorted(ext, key=lambda c: -size(c)[1] * size(c)[0]):
    w, h = size(ch); gw, gh = -(-w // G), -(-h // G); done = False
    for gy in range(0, SIZE // G - gh + 1):
        for gx in range(0, SIZE // G - gw + 1):
            if not occ[gy:gy + gh, gx:gx + gw].any():
                occ[gy:gy + gh, gx:gx + gw] = True; rect[ch] = (gx * G, gy * G, w, h); done = True; break
        if done: break
    assert done, ('no room for chart', ch, w, h)
def to_uv(ch, u_, v_):
    x, y, w, h = rect[ch]; e = ext[ch]
    if ch.startswith('sw_'): px, py = x + 16, y + 16
    else: px, py = x + PAD + (u_ - e[0]) * PPU, y + PAD + (v_ - e[1]) * PPU
    return px / SIZE, 1 - py / SIZE
print('charts', {k: v for k, v in rect.items()})

# ------------------------------------------------------------------ into Blender
bpy.ops.wm.open_mainfile(filepath=SRC)
old_body = bpy.data.objects['P51_Body']; mat = bpy.data.materials['P51_Mat']; glass = bpy.data.materials['P51_CanopyGlass']
for nme in [o.name for o in bpy.data.objects if o.type == 'MESH' and (o.name.startswith('P51_') and o.name not in ('P51_Body', 'P51_Propeller') or o.name == 'Pit_Tub')] \
        + [o.name for o in bpy.data.objects if o.name.endswith('_Hinge')]:
    bpy.data.objects.remove(bpy.data.objects[nme], do_unlink=True)
# the propeller keeps its palette square
texmap = {'pos': np.zeros((SIZE, SIZE, 3), np.float32), 'nrm': np.zeros((SIZE, SIZE, 3), np.float32), 'chart': np.zeros((SIZE, SIZE), np.uint8)}
CH = sorted(rect); chid = {c: i + 1 for i, c in enumerate(CH)}
def raster(me, ob_off, nme):
    """Write each chart texel's place on the aircraft, by filling the UV triangles."""
    me.calc_loop_triangles(); uvl = me.uv_layers.active.data
    for t in me.loop_triangles:
        ch = fch[nme][t.polygon_index]
        if ch.startswith('sw_'): continue
        uv = np.array([(uvl[l].uv[0] * SIZE, (1 - uvl[l].uv[1]) * SIZE) for l in t.loops]); pos = np.array([me.vertices[v].co[:] for v in t.vertices]) + ob_off
        nr = np.array([me.vertices[v].normal[:] for v in t.vertices])
        x0, y0 = np.floor(uv.min(0)).astype(int) - 1; x1, y1 = np.ceil(uv.max(0)).astype(int) + 1
        xx, yy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (uv[1, 1] - uv[2, 1]) * (uv[0, 0] - uv[2, 0]) + (uv[2, 0] - uv[1, 0]) * (uv[0, 1] - uv[2, 1])
        if abs(d) < 1e-9: continue
        b0 = ((uv[1, 1] - uv[2, 1]) * (xx - uv[2, 0]) + (uv[2, 0] - uv[1, 0]) * (yy - uv[2, 1])) / d
        b1 = ((uv[2, 1] - uv[0, 1]) * (xx - uv[2, 0]) + (uv[0, 0] - uv[2, 0]) * (yy - uv[2, 1])) / d
        b2 = 1 - b0 - b1; e = -0.02
        m = (b0 >= e) & (b1 >= e) & (b2 >= e)
        if not m.any(): continue
        ix = xx[m].astype(int); iy_ = yy[m].astype(int)
        B = np.stack([b0[m], b1[m], b2[m]], 1)
        texmap['pos'][iy_, ix] = B @ pos; texmap['nrm'][iy_, ix] = B @ nr; texmap['chart'][iy_, ix] = chid[ch]
fch = {}
objs = {}
for nme, p in PARTS.items():
    me = bpy.data.meshes.new(nme + "_new")
    if nme == 'P51_Body':
        ob = old_body; old_me = ob.data; ob.data = me; bpy.data.meshes.remove(old_me)
    else:
        ob = bpy.data.objects.new(nme, me); bpy.context.scene.collection.objects.link(ob); ob.parent = old_body
    org = Vector(HING[nme][0]) if nme in HING else Vector((0, 0, 0))
    me.from_pydata([tuple(Vector(v) - org) for v in p.V], [], p.F); me.update()
    assert len(me.polygons) == len(p.F), (nme, len(me.polygons), len(p.F))
    uvl = me.uv_layers.new(name='UVMap')
    for poly, (ch, uv) in zip(me.polygons, p.UV):
        for li, t in zip(poly.loop_indices, uv): uvl.data[li].uv = to_uv(ch, *t)
    me.materials.append(glass if nme == 'P51_Canopy' else mat)
    ob.matrix_world.translation = org
    # outward normals, welded seams, smooth where the surface is smooth
    bm = bmesh.new(); bm.from_mesh(me)
    chl = bm.faces.layers.int.new('chart')
    for f, (ch, uv) in zip(bm.faces, p.UV): f[chl] = chid[ch]
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.faces.ensure_lookup_table()
    fch[nme] = [CH[f[chl] - 1] for f in bm.faces]
    bm.to_mesh(me); bm.free(); me.update()
    for poly in me.polygons: poly.use_smooth = True
    bpy.context.view_layer.objects.active = ob
    for o in bpy.data.objects: o.select_set(o == ob)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    raster(me, np.array(org), nme)
    objs[nme] = ob
    if nme in HING:
        h = bpy.data.objects.new(nme + '_Hinge', None); bpy.context.scene.collection.objects.link(h); h.parent = ob
        h.matrix_world.translation = Vector(HING[nme][1]); h.empty_display_size = 0.1
    print(nme, len(me.vertices), 'verts', len(me.polygons), 'faces')
# the old cockpit's sill fairing was shaped to the old hull: drop whatever of it now lies outside the new skin
ps = bpy.data.objects.get('Pit_Static')
if ps:
    def inside(c):
        if not (ys[0] < c.y < ys[-1]): return False
        zt, zb, w, zc, n = sec(c.y)
        if OY0 < c.y < OY1 and abs(c.x) < xs(c.y) - 0.012 and c.z > zc: return True
        hh = (zt - zc) if c.z > zc else (zc - zb)
        return (abs(c.x) / w) ** n + (abs(c.z - zc) / hh) ** n < 0.94
    bm = bmesh.new(); bm.from_mesh(ps.data); M = ps.matrix_world
    gone = [f for f in bm.faces if not inside(M @ f.calc_center_median()) or not all(inside(M @ v.co) for v in f.verts)]
    print('Pit_Static faces outside the new hull removed:', len(gone), 'of', len(bm.faces))
    bmesh.ops.delete(bm, geom=gone, context='FACES'); bm.to_mesh(ps.data); bm.free()
bpy.context.view_layer.update()
json.dump({'size': SIZE, 'ppu': PPU, 'pad': PAD, 'keep': KEEP, 'rect': rect, 'ext': ext, 'ids': chid}, open(OUT + '/charts.json', 'w'), indent=1)
np.savez_compressed(OUT + '/texmap.npz', **texmap)
bpy.ops.wm.save_as_mainfile(filepath=DST)
print('saved', DST)
