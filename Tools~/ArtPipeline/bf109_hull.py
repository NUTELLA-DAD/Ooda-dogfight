# A clean Bf 109 G airframe, lofted from sections instead of generated: fuselage, chin cooler, wing radiators, cowl
# bulges, wings, tailplane, fin, hinged control surfaces, the angular framed canopy, exhausts, and a new spinner and
# three-blade propeller whose spinner continues the cowl's lines. It keeps the old model's length, cockpit opening and
# cockpit parts (Pit, PilotMount, CockpitCamera), so the pilot's seat fits unchanged. The side profile follows a Bf 109 F
# side drawing (Kenny's "Vati"): the propeller axis sits low on the cowl, the spine runs nearly level from the cockpit to
# the fin, the belly sweeps up to meet the axis at the rudder, and the fin stands tall; the old model's fuselage drooped
# behind the cockpit. Every part is unwrapped into its own chart; the charts and a per-texel map of where each texel sits on the
# aircraft are written out for bf109_paint.py. Built the same way as p51_hull.py.
# Run: python -I bf109_hull.py -- <in.blend (old Bf 109)> <out.blend> <out_dir for charts.json / texmap.npz>
import bpy, bmesh, sys, math, json, numpy as np
from mathutils import Vector
from scipy.interpolate import PchipInterpolator

a = sys.argv[sys.argv.index('--') + 1:]
SRC, DST, OUT = a[:3]
SIZE, PPU, PAD = 4096, 380, 12
KEEP = (0, 0, 0, 0)                       # nothing of the old atlas is kept: the propeller is rebuilt too

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
# The nose is round where it meets the spinner (the cowl's first ring is the spinner's base circle, so the spinner
# carries the cowl's lines on to its point with no step), deepens into the cowl and the belly, and tapers to the tail.
TZ = 0.20                                   # the thrust line: the propeller's axis (canopy top 0.76 above it)
YS, R0 = -3.36, 0.37                        # the spinner's base station and radius
ST = np.array([   # y, top, bottom, half width, centre fraction (0.5 is round about the middle), squareness
    (YS, TZ + R0, TZ - R0, R0, 0.50, 2.0), (-3.22, 0.607, -0.205, 0.418, 0.50, 2.05), (-3.05, 0.625, -0.225, 0.465, 0.505, 2.15),
    (-2.80, 0.650, -0.235, 0.500, 0.52, 2.3), (-2.40, 0.690, -0.250, 0.520, 0.54, 2.45), (-1.90, 0.735, -0.270, 0.525, 0.55, 2.6),
    (-1.40, 0.765, -0.285, 0.525, 0.55, 2.7), (-0.80, 0.785, -0.290, 0.515, 0.55, 2.7), (-0.20, 0.805, -0.275, 0.490, 0.55, 2.6),
    (0.40, 0.915, -0.140, 0.440, 0.55, 2.45), (1.00, 0.895, -0.030, 0.375, 0.55, 2.3), (1.60, 0.865, 0.075, 0.310, 0.54, 2.2),
    (2.20, 0.825, 0.180, 0.245, 0.53, 2.15), (2.80, 0.780, 0.280, 0.175, 0.52, 2.1), (3.35, 0.725, 0.370, 0.110, 0.51, 2.05),
    (3.80, 0.655, 0.470, 0.045, 0.50, 2.0), (3.90, 0.625, 0.505, 0.012, 0.50, 2.0)])
fzt, fzb, fw, fcf, fn = (PchipInterpolator(ST[:, 0], ST[:, k]) for k in (1, 2, 3, 4, 5))
Y_END = float(ST[-1, 0])
def sec(y):
    zt, zb, w = float(fzt(y)), float(fzb(y)), float(fw(y)); return zt, zb, w, zb + float(fcf(y)) * (zt - zb), float(fn(y))
def ring_pt(y, th):
    zt, zb, w, zc, n = sec(y); c, s_ = math.cos(th), math.sin(th)
    return Vector((w * math.copysign(abs(c) ** (2 / n), c), y, zc + ((zt - zc) if s_ > 0 else (zc - zb)) * math.copysign(abs(s_) ** (2 / n), s_)))
def hull_top(x, y):
    zt, zb, w, zc, n = sec(y); return zc + (zt - zc) * max(0.0, 1 - min(1.0, abs(x) / w) ** n) ** (1 / n)
def hull_x(y, z):
    """The hull's half width at height z."""
    zt, zb, w, zc, n = sec(y); h = (zt - zc) if z > zc else (zc - zb)
    return w * max(0.0, 1 - min(1.0, abs(z - zc) / h) ** n) ** (1 / n)

# the cockpit opening: between OY0 and OY1, as wide as xs(y); wide, so the sill sits low on the hull's shoulder as the
# 109's does, and the wolf's shoulders show over it
OY0, OY1 = -1.13, 0.31
fxs = PchipInterpolator([-3.36, -1.13, -0.95, -0.55, -0.05, 0.20, 0.31, 1.5, Y_END], [0.20, 0.34, 0.425, 0.445, 0.435, 0.40, 0.34, 0.15, 0.008])
def xs(y): return float(fxs(y))
def th_split(y):
    zt, zb, w, zc, n = sec(y); return math.acos(min(0.98, xs(y) / w) ** (n / 2))

M2, T = 22, 12
ys = sorted(set(np.round(np.concatenate([np.linspace(YS, OY0, 34), np.linspace(OY0, OY1, 30), np.linspace(OY1, Y_END, 44)]), 5)))
fus = part('Bf109_Body')
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
c0 = fus.v((0, ys[0] + 0.03, TZ)); c1 = fus.v((0, Y_END + 0.02, float(sec(Y_END)[3])))
for j in range(len(P[0]) - 1):
    fus.f((c0, I[0][j], I[0][j + 1]), 'sw_ink', [(0, 0)] * 3)          # behind the spinner, never seen
    fus.f((c1, I[-1][j + 1], I[-1][j]), 'sw_under', [(0, 0)] * 3)
# the opening's lip: the rim turned down into the hull, so the skin has thickness there
iy = [i for i, y in enumerate(ys) if OY0 - 1e-6 <= y <= OY1 + 1e-6]
rim = [(i, M2) for i in iy] + [(iy[-1], j) for j in range(M2 + 1, M2 + T)] + [(i, M2 + T) for i in reversed(iy)] + [(iy[0], j) for j in range(M2 + T - 1, M2, -1)]
rimv = []
for (i, j) in rim:
    p = P[i][j]; c = Vector((0, (OY0 + OY1) / 2, p.z)); d = (c - p); d.z = 0; d.normalize()
    rimv.append(fus.v(p + d * 0.03 + Vector((0, 0, -0.07))))
for k in range(len(rim)):
    k2 = (k + 1) % len(rim)
    fus.f((I[rim[k][0]][rim[k][1]], I[rim[k2][0]][rim[k2][1]], rimv[k2], rimv[k]), 'sw_ink', [(0, 0)] * 4)

# ------------------------------------------------------------------ canopy: the 109's angular framed hood
# Each section is a trapezoid with rounded corners: from the sill (xs, on the hull) up a sloping side to a narrower flat
# roof. The windscreen's flat front panel rises from OY0 to YW; the hood's roof runs level to its back, which drops
# steeply to the spine.
YW, YB = -0.80, 0.22
def roof(y):
    if y <= YW: return 0.945 - (0.945 - (hull_top(0, OY0) + 0.01)) * ((YW - y) / (YW - OY0))
    if y <= YB: return 0.945 + 0.004 * math.sin(math.pi * (y - YW) / (YB - YW))
    return 0.945 - (0.945 - hull_top(0, OY1) - 0.01) * ((y - YB) / (OY1 - YB)) ** 1.4
def roof_w(y):
    if y <= YW: return 0.20 + 0.06 * (y - OY0) / (YW - OY0)
    return 0.26 - 0.03 * max(0.0, (y - YB) / (OY1 - YB))
NC = 25
def can_sec(y, lift=0.0):
    """Points across the hood at station y, from the right sill over the roof to the left sill (NC of them)."""
    x0 = xs(y); zs = hull_top(x0, y) - 0.004; zr = max(roof(y), zs + 0.01); xr = min(roof_w(y), x0 * 0.9)
    poly = [Vector((-x0, y, zs)), Vector((-xr, y, zr)), Vector((xr, y, zr)), Vector((x0, y, zs))]
    for _ in range(3):   # Chaikin: round the two roof corners, keep the sill ends
        q = [poly[0]]
        for a_, b_ in zip(poly[:-1], poly[1:]): q += [a_ * 0.75 + b_ * 0.25, a_ * 0.25 + b_ * 0.75]
        q.append(poly[-1]); poly = q
    L = [0.0]
    for a_, b_ in zip(poly[:-1], poly[1:]): L.append(L[-1] + (b_ - a_).length)
    out = []
    for k in range(NC):
        s = L[-1] * k / (NC - 1); j = max(0, min(len(poly) - 2, int(np.searchsorted(L, s)) - 1))
        t = (s - L[j]) / max(1e-9, L[j + 1] - L[j]); p = poly[j].lerp(poly[j + 1], t)
        if lift:
            c = Vector((0, y, zs + 0.12)); d = p - c; d.y = 0; d.normalize(); p = p + d * lift
        out.append(p)
    return out
cys = list(np.linspace(OY0, OY1, 46))
can = part('Bf109_Canopy')
can.grid([can_sec(y) for y in cys], [[(0, 0)] * NC] * len(cys), 'sw_glass')
def strip(y0, y1, k0, k1, n_y, lift=0.014, chart='sw_frame', pname='Bf109_Body'):
    """A raised band over the hood between two stations and two section indices (frame members)."""
    pt = part(pname); yy = np.linspace(y0, y1, n_y)
    top_ = [can_sec(y, lift)[k0:k1 + 1] for y in yy]; bot = [can_sec(y, -0.004)[k0:k1 + 1] for y in yy]
    n_k = k1 - k0 + 1; z = [[(0, 0)] * n_k] * n_y
    It = pt.grid(top_, z, chart); Ib = pt.grid(bot, z, chart)
    edge = [(i, 0) for i in range(n_y)] + [(n_y - 1, j) for j in range(1, n_k)] + [(i, n_k - 1) for i in range(n_y - 2, -1, -1)] + [(0, j) for j in range(n_k - 2, 0, -1)]
    for k in range(len(edge)):
        (i0, j0), (i1, j1) = edge[k], edge[(k + 1) % len(edge)]
        pt.f((It[i0][j0], It[i1][j1], Ib[i1][j1], Ib[i0][j0]), chart, [(0, 0)] * 4)
# the windscreen's frame stays on the hull (the game hides the hood's glass to show the pilot); the hood's own frames
# ride with the glass
strip(OY0, OY0 + 0.05, 0, NC - 1, 2)                                     # windscreen foot
strip(YW - 0.03, YW + 0.03, 0, NC - 1, 2)                                # windscreen arch
for k0 in (5, NC - 7):
    strip(OY0 + 0.02, YW, k0, k0 + 2, 8)                                 # the front panel's corner posts
for yy_ in (-0.38, 0.04):
    strip(yy_ - 0.025, yy_ + 0.025, 0, NC - 1, 2, pname='Bf109_Canopy', chart='sw_glassframe')     # hood bows
for k0 in (0, 7, NC - 10, NC - 3):
    strip(YW, OY1 - 0.01, k0, k0 + 2, 18, pname='Bf109_Canopy', chart='sw_glassframe')               # hood rails

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
# wing: straight taper, the leading edge easing back toward the round tip, a fillet into the fuselage at the root
def root(s): return max(0.0, 1 - (s - 0.50) / 0.55) ** 2 if s > 0.50 else 1.0
w_le = lambda s: -1.86 + 0.035 * s - 0.10 * root(s) + 0.08 * max(0.0, (s - 3.4) / 1.2) ** 2
w_te = lambda s: 0.12 - 0.135 * s + 0.16 * root(s)
w_mid = lambda s: -0.03 + 0.105 * s                              # the root's underside is the belly
w_tc = lambda s: (0.165 - 0.008 * s) * (1 + 0.35 * root(s))
A0, A1, AF = 2.95, 4.50, 0.74                                    # the aileron's span and its hinge's chord fraction
hy = lambda s: w_le(s) + AF * (w_te(s) - w_le(s))
w_h = lin((A0, hy(A0)), (A1, hy(A1))); w_f = lambda s: (w_h(s) - w_le(s)) / (w_te(s) - w_le(s))
for sgn, sd in ((1, 'L'), (-1, 'R')):
    kw = dict(le=w_le, te=w_te, mid=w_mid, tc=w_tc, span_axis=0, thick_axis=2, sgn=sgn, camber=0.012)
    lifting('Bf109_Body', 'wing' + sd, list(np.linspace(0.0, 1.10, 18)) + list(np.linspace(1.10, A0, 18))[1:], caps=(None, 'sw_ink'), **kw)
    lifting('Bf109_Body', 'wing' + sd, list(np.linspace(A0, A1, 16)), c1=w_f, **kw)
    lifting('Bf109_Body', 'wing' + sd, [A1] + list(A1 + 0.45 * np.sin(np.linspace(0, math.pi / 2, 10))[1:]), tip_r=0.45, caps=('sw_ink', 'sw_paint'), **kw)
    lifting('Bf109_Aileron' + sd, 'ail' + sd, list(np.linspace(A0 + 0.015, A1 - 0.015, 14)), c0=lambda s: w_f(s) + 0.012, caps=('sw_ink', 'sw_ink'), **kw)
    HING['Bf109_Aileron' + sd] = ((sgn * A0, w_h(A0), w_mid(A0)), (sgn * A1, w_h(A1), w_mid(A1)))
# tailplane: cantilever, round tips, set on the fuselage below the fin
TZT = 0.60                                                        # on the spine, at the fin's root
t_le = lambda s: 2.55 + 0.06 * s; t_te = lambda s: 3.42 - 0.02 * s; t_mid = lambda s: TZT; t_tc = lambda s: 0.10
YE = 3.03; t_f = lambda s: (YE - t_le(s)) / (t_te(s) - t_le(s))
for sgn, sd in ((1, 'L'), (-1, 'R')):
    kw = dict(le=t_le, te=t_te, mid=t_mid, tc=t_tc, span_axis=0, thick_axis=2, sgn=sgn)
    lifting('Bf109_Body', 'tail' + sd, list(np.linspace(0.02, 0.30, 4)), caps=(None, 'sw_ink'), **kw)
    lifting('Bf109_Body', 'tail' + sd, list(np.linspace(0.30, 1.20, 10)), c1=t_f, **kw)
    lifting('Bf109_Body', 'tail' + sd, [1.20] + list(1.20 + 0.30 * np.sin(np.linspace(0, math.pi / 2, 8))[1:]), tip_r=0.30, caps=('sw_ink', 'sw_paint'), **kw)
    lifting('Bf109_Elevator' + sd, 'elev' + sd, list(np.linspace(0.315, 1.185, 10)), c0=lambda s: t_f(s) + 0.02, caps=('sw_ink', 'sw_ink'), **kw)
    HING['Bf109_Elevator' + sd] = ((sgn * 0.30, YE, TZT), (sgn * 1.20, YE, TZT))
# fin and rudder (span along z, thickness along x): a swept leading edge, a round top, the rudder hinged on the post
YR = 3.147
DZF = 0.50                                                        # the fin stands on the raised spine
f_le = lambda s: 2.42 + 0.85 * (s - DZF - 0.30); f_te = lambda s: 3.84 - 0.06 * (s - DZF - 0.30); f_tc = lambda s: 0.09
f_f = lambda s: (YR - f_le(s)) / (f_te(s) - f_le(s))
kw = dict(le=f_le, te=f_te, mid=lambda s: 0, tc=f_tc, span_axis=2, thick_axis=0, sgn=1)
lifting('Bf109_Body', 'fin', list(np.linspace(0.12 + DZF, 0.80 + DZF, 12)), c1=f_f, **kw)
lifting('Bf109_Body', 'fin', [0.80 + DZF] + list(0.80 + DZF + 0.17 * np.sin(np.linspace(0, math.pi / 2, 8))[1:]), tip_r=0.17, caps=('sw_ink', 'sw_paint'), **kw)
lifting('Bf109_Rudder', 'rud', list(np.linspace(0.19 + DZF, 0.79 + DZF, 10)), c0=lambda s: f_f(s) + 0.02, caps=('sw_ink', 'sw_ink'), **kw)
HING['Bf109_Rudder'] = ((0, YR, 0.19 + DZF), (0, YR, 0.79 + DZF))

def loft(pname, chart, secs, mouth=None, nseg=24, n=3.0, cap_back=True):
    """A rounded box lofted through sections (y, half width, z top, z bottom); a dark mouth on the first section."""
    pt = part(pname); Ps, Us = [], []
    for (y, w, zt, zb) in secs:
        row = []
        for th in np.linspace(0, 2 * math.pi, nseg + 1):        # the first point again at the end: a UV seam, welded later
            c, s_ = math.cos(th), math.sin(th)
            row.append(Vector((w * math.copysign(abs(c) ** (2 / n), c), y, (zt + zb) / 2 + (zt - zb) / 2 * math.copysign(abs(s_) ** (2 / n), s_))))
        arc = np.concatenate([[0], np.cumsum([(row[k + 1] - row[k]).length for k in range(len(row) - 1)])]); arc -= arc[nseg * 3 // 4]
        Ps.append(row); Us.append([(y, float(v)) for v in arc])
    Is = pt.grid(Ps, Us, chart)
    Is = [row[:nseg] for row in Is]; Ps = [row[:nseg] for row in Ps]
    if mouth:
        mv = [pt.v(p + Vector((0, mouth, 0)) + (Vector((0, p.y, (secs[0][2] + secs[0][3]) / 2)) - p) * 0.16) for p in Ps[0]]
        for j in range(nseg):
            k = (j + 1) % nseg; pt.f((Is[0][j], Is[0][k], mv[k], mv[j]), 'sw_ink', [(0, 0)] * 4)
        pt.cap(mv, 'sw_ink')
    if cap_back: pt.cap(list(reversed(Is[-1])), 'sw_ink')
# the chin oil cooler under the cowl
loft('Bf109_Body', 'chin', [(-3.12, 0.17, -0.08, -0.25), (-3.02, 0.20, -0.10, -0.31), (-2.80, 0.215, -0.13, -0.35), (-2.55, 0.205, -0.15, -0.36),
                            (-2.30, 0.175, -0.18, -0.34), (-2.12, 0.11, -0.22, -0.30)], mouth=0.06)
# the wing radiators, one under each wing
for sgn in (1, -1):
    def low(s, y): return w_mid(s) - 0.5 * aerofoil(min(1, max(0, (y - w_le(s)) / (w_te(s) - w_le(s)))), w_tc(s)) * (w_te(s) - w_le(s))
    xc = 1.30
    secs = []
    for (y, d, wf) in ((-1.06, 0.03, 0.80), (-0.98, 0.095, 0.97), (-0.70, 0.11, 1.0), (-0.40, 0.105, 1.0), (-0.22, 0.08, 0.96), (-0.13, 0.04, 0.85)):
        zb = low(xc, y) - d; secs.append((y, 0.46 * wf, low(xc, y) + 0.05, zb))
    pt_before = len(part('Bf109_Body').V)
    loft('Bf109_Body', 'rad' + ('L' if sgn > 0 else 'R'), secs, mouth=0.05)
    pb = part('Bf109_Body')
    for k in range(pt_before, len(pb.V)): v = pb.V[k]; pb.V[k] = (v[0] + sgn * xc, v[1], v[2])
# the cowl's gun bulges and the supercharger intake on the left
def pod(pname, chart, y0, y1, x, r, zf, nseg=14, n_y=12, squash=0.7):
    pt = part(pname); Ps = []
    for t in np.linspace(0, 1, n_y):
        y = y0 + (y1 - y0) * t; rr = max(0.004, r * math.sin(math.pi * t) ** 0.55 * (1 - 0.3 * t))      # a long teardrop
        zc = zf(x, y) - 0.02
        Ps.append([Vector((x + rr * math.cos(a_), y, zc + rr * squash * math.sin(a_))) for a_ in np.linspace(0, 2 * math.pi, nseg + 1)[:-1]])
    I_ = pt.grid(Ps, [[(0, 0)] * nseg] * n_y, chart, wrap=True)
    pt.cap(I_[0], chart); pt.cap(list(reversed(I_[-1])), chart)
for sx in (1, -1):
    pod('Bf109_Body', 'sw_paint', -2.55, -1.80, sx * 0.20, 0.085, hull_top)

# ------------------------------------------------------------------ small parts: tubes
def tube(p0, p1, r0, r1, chart, nseg=10, pname='Bf109_Body', squash=1.0, endcap='sw_ink'):
    pt = part(pname); p0, p1 = Vector(p0), Vector(p1); ax = (p1 - p0).normalized()
    u = ax.cross(Vector((0, 0, 1))); u = u.normalized() if u.length > 1e-3 else Vector((1, 0, 0)); v = ax.cross(u)
    rings = []
    for (p, r) in ((p0, r0), (p1, r1)):
        rings.append([p + (u * math.cos(t) + v * math.sin(t) * squash) * r for t in np.linspace(0, 2 * math.pi, nseg + 1)[:-1]])
    I = pt.grid(rings, [[(0, 0)] * nseg] * 2, chart, wrap=True)
    pt.cap(I[1], endcap); pt.cap(I[0], chart)
for sgn in (-1, 1):
    for k in range(6):                                                  # exhaust stacks along the lower cowl
        y = -3.00 + k * 0.118; z = 0.09
        hx = hull_x(y, z)
        tube((sgn * (hx - 0.04), y, z), (sgn * (hx + 0.065), y + 0.09, z - 0.02), 0.046, 0.040, 'sw_steel', nseg=8, squash=0.7)
tube((0.43, -1.98, 0.30), (0.50, -2.40, 0.30), 0.070, 0.088, 'sw_paint', nseg=12, squash=0.9)                     # supercharger intake
tube((0, 0.50, hull_top(0, 0.50) - 0.03), (0, 0.56, hull_top(0, 0.5) + 0.42), 0.030, 0.014, 'sw_paint', nseg=8, squash=0.45)   # mast
tube((4.15, w_le(4.15) + 0.30, w_mid(4.15) - 0.08), (4.15, w_le(4.15) + 0.30, w_mid(4.15) - 0.17), 0.016, 0.013, 'sw_paint', nseg=6)  # pitot
tube((4.15, w_le(4.15) + 0.34, w_mid(4.15) - 0.17), (4.15, w_le(4.15) - 0.12, w_mid(4.15) - 0.17), 0.013, 0.010, 'sw_steel', nseg=6)

# ------------------------------------------------------------------ propeller: the spinner continues the cowl
# The spinner's base is the cowl's first ring (radius R0 on the thrust line), and its profile leaves the base at the
# cowl's own slope, so from the side the nose runs on into the spinner's point without a step. Three paddle blades.
PROP_ORG = Vector((0, -3.548, TZ))
prop = part('Bf109_Propeller')
SL = 0.56; slope = (float(fw(-3.22)) - R0) / (-3.22 - YS)           # the cowl's growth just behind the base (per metre aft)
A_ = 0.62; B_ = A_ - slope * SL / R0
def spin_r(u):   # u: 0 at the base, SL at the point
    t = min(1.0, u / SL); r = R0 * (1 - t) ** A_ * (1 + B_ * t)
    cap = 0.03
    return max(r, math.sqrt(max(2 * cap * (SL - u) - (SL - u) ** 2, 0))) if SL - u < cap else r
SEG = 48
us = sorted(set(list(SL * (1 - np.cos(np.linspace(0, math.pi / 2, 26)))) ))[:-1]
rows, uvs = [], []
for u in us:
    r = spin_r(u) - 0.002; y = YS - 0.004 - u
    rows.append([Vector((r * math.sin(a_), y, TZ + r * math.cos(a_))) for a_ in np.linspace(0, 2 * math.pi, SEG + 1)])
    uvs.append([(u, a_ * R0) for a_ in np.linspace(0, 2 * math.pi, SEG + 1)])
Isp = prop.grid(rows, uvs, 'spin')                                    # the seam column repeats the first, welded later
tip = prop.v((0, YS - 0.004 - SL, TZ))
for j in range(SEG): prop.f((tip, Isp[-1][j + 1], Isp[-1][j]), 'spin', [uvs[-1][j + 1], uvs[-1][j], uvs[-1][j]])
prop.cap(list(reversed(Isp[0][:SEG])), 'sw_ink')                     # the backplate
Rt, YP, PITCH, CH = 1.42, -3.53, 2.6, 0.26
def bchord(f):
    if f < 0.30: return 0.10 + (CH - 0.10) * max(0, (f - 0.22) / 0.08) ** 1.5 if f > 0.22 else 0.10
    c = CH * (0.90 + 0.10 * math.sin(math.pi * min(1, (f - 0.3) / 0.5)))
    if f > 0.80: c *= math.sqrt(max(1 - ((f - 0.80) / 0.20) ** 2, 0.0)) * 0.92 + 0.08
    return c
def bthick(f): return 0.10 if f < 0.22 else max(0.020, 0.10 - 0.15 * (f - 0.22)) if f < 0.5 else max(0.016, 0.058 - 0.05 * (f - 0.5))
fs = list(np.linspace(0.12, 0.80, 16)) + list(0.80 + 0.20 * np.sin(np.linspace(0, math.pi / 2, 9)[1:]))
MB = 18
for b in range(3):
    th = math.radians(90 + 120 * b)
    er = Vector((math.cos(th), 0, math.sin(th))); ax = Vector((0, -1, 0)); et = ax.cross(er)
    rows = []
    for f in fs:
        r = f * Rt; c = bchord(f); t = bthick(f)
        if f < 0.22: c = t = 0.10
        beta = math.atan(PITCH / (2 * math.pi * max(r, 0.25)))
        cd = et * math.cos(beta) + ax * math.sin(beta); nd = -et * math.sin(beta) + ax * math.cos(beta)
        ring = []
        for k in range(MB):
            an = 2 * math.pi * k / MB; uu = 0.5 * c * math.cos(an); vv = 0.5 * t * math.sin(an)
            if f >= 0.22: vv *= (1.0 + 0.6 * math.cos(an)) * 0.8
            ring.append(Vector((0, YP, TZ)) + er * r + cd * uu + nd * vv)
        rows.append(ring)
    Ib = prop.grid(rows, [[(0, 0)] * MB] * len(rows), 'sw_prop', wrap=True)
    prop.cap(Ib[-1], 'sw_prop')

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
old_body = bpy.data.objects['Bf109_Body']; mat = bpy.data.materials['Bf109_Mat']
glass = bpy.data.materials.get('Bf109_CanopyGlass') or bpy.data.materials.new('Bf109_CanopyGlass')
old_prop = bpy.data.objects['Bf109_Propeller']
old_prop.matrix_world.translation = PROP_ORG                    # the propeller's pivot moves down with the axis
for nme in [o.name for o in bpy.data.objects if o.type == 'MESH' and (o.name.startswith('Bf109_') and o.name not in ('Bf109_Body', 'Bf109_Propeller') or o.name == 'Pit_Tub')] \
        + [o.name for o in bpy.data.objects if o.name.endswith('_Hinge')]:
    bpy.data.objects.remove(bpy.data.objects[nme], do_unlink=True)
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
def in_hull(v, m=0.97):
    x, y, z = v
    if not (ys[0] < y < ys[-1]): return False
    zt, zb, w, zc, n = sec(y); hh = (zt - zc) if z > zc else (zc - zb)
    return (abs(x) / w) ** n + (abs(z - zc) / hh) ** n < m
bp = PARTS['Bf109_Body']; keep = [k for k, (f, (ch, uv)) in enumerate(zip(bp.F, bp.UV)) if not (ch.startswith('wing') and all(in_hull(bp.V[i]) for i in f))]
print('wing faces inside the fuselage dropped:', len(bp.F) - len(keep))
bp.F = [bp.F[k] for k in keep]; bp.UV = [bp.UV[k] for k in keep]
objs = {}
for nme, p in PARTS.items():
    me = bpy.data.meshes.new(nme + "_new")
    if nme in ('Bf109_Body', 'Bf109_Propeller'):
        ob = old_body if nme == 'Bf109_Body' else old_prop; old_me = ob.data; ob.data = me; bpy.data.meshes.remove(old_me)
    else:
        ob = bpy.data.objects.new(nme, me); bpy.context.scene.collection.objects.link(ob); ob.parent = old_body
    org = Vector(HING[nme][0]) if nme in HING else PROP_ORG.copy() if nme == 'Bf109_Propeller' else Vector((0, 0, 0))
    me.from_pydata([tuple(Vector(v) - org) for v in p.V], [], p.F); me.update()
    assert len(me.polygons) == len(p.F), (nme, len(me.polygons), len(p.F))
    uvl = me.uv_layers.new(name='UVMap')
    for poly, (ch, uv) in zip(me.polygons, p.UV):
        for li, t in zip(poly.loop_indices, uv): uvl.data[li].uv = to_uv(ch, *t)
    me.materials.append(glass if nme == 'Bf109_Canopy' else mat)
    if nme != 'Bf109_Propeller': ob.matrix_world.translation = org
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
