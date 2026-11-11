# A clean airframe for any of the propeller fighters, lofted from a spec instead of generated: one or more fuselage
# bodies (a twin-boom aircraft has a nacelle and two booms), wings, tailplane and fins with hinged control surfaces,
# a canopy and its frames, scoops, exhausts and guns, and a spinner and propeller per engine whose spinner continues
# the cowl's lines (the cowl's first ring is the spinner's base circle, and the spinner leaves it at the cowl's slope).
# It is bf109_hull.py with the numbers moved into a spec (specs/<plane>_hull.py), so the Spitfire, the Zero and the
# P-38 are built by the same code as the 109. It keeps the old model's cockpit parts (Pit, PilotMount, CockpitCamera,
# Pit_Static trimmed to the new skin), so the game's mounts and scripts fit unchanged. Every part is unwrapped into its
# own chart; the charts and a per-texel map of where each texel sits on the aircraft are written for plane_paint.py.
# Run: python -I plane_hull.py -- <in.blend (old aircraft)> <out.blend> <out_dir for charts.json / texmap.npz> <spec.py>
import bpy, bmesh, sys, math, json, numpy as np
from mathutils import Vector
from scipy.interpolate import PchipInterpolator

a = sys.argv[sys.argv.index('--') + 1:]
SRC, DST, OUT, SPECF = a[:4]
G = {}; exec(open(SPECF).read(), G); S = G['SPEC']
P_ = S['prefix']
SIZE, PPU, PAD = 4096, S.get('ppu', 280), 10

def pchip(pts):
    pts = np.array(sorted(pts), float)
    f = PchipInterpolator(pts[:, 0], pts[:, 1], extrapolate=True)
    return lambda s: float(f(s))

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
BODY = P_ + '_Body'

# ------------------------------------------------------------------ fuselage bodies
class Body:
    """A lofted body along y: sections (y, top, bottom, half width, centre fraction, squareness), centred on x."""
    def __init__(s, d):
        s.d = d; s.x = d.get('x', 0.0); st = np.array(d['sections'], float)
        s.fzt, s.fzb, s.fw, s.fcf, s.fn = (PchipInterpolator(st[:, 0], st[:, k]) for k in (1, 2, 3, 4, 5))
        s.y0, s.y1 = float(st[0, 0]), float(st[-1, 0])
        s.chart = d.get('chart', 'fus')
    def sec(s, y):
        zt, zb, w = float(s.fzt(y)), float(s.fzb(y)), float(s.fw(y)); return zt, zb, w, zb + float(s.fcf(y)) * (zt - zb), float(s.fn(y))
    def ring_pt(s, y, th):
        zt, zb, w, zc, n = s.sec(y); c, s_ = math.cos(th), math.sin(th)
        return Vector((s.x + w * math.copysign(abs(c) ** (2 / n), c), y, zc + ((zt - zc) if s_ > 0 else (zc - zb)) * math.copysign(abs(s_) ** (2 / n), s_)))
    def top(s, x, y):
        zt, zb, w, zc, n = s.sec(y); return zc + (zt - zc) * max(0.0, 1 - min(1.0, abs(x - s.x) / w) ** n) ** (1 / n)
    def half_x(s, y, z):
        zt, zb, w, zc, n = s.sec(y); h = (zt - zc) if z > zc else (zc - zb)
        return w * max(0.0, 1 - min(1.0, abs(z - zc) / h) ** n) ** (1 / n)
    def inside(s, c, shrink=0.94):
        if not (s.y0 < c.y < s.y1): return False
        zt, zb, w, zc, n = s.sec(c.y); hh = (zt - zc) if c.z > zc else (zc - zb)
        return (abs(c.x - s.x) / w) ** n + (abs(c.z - zc) / hh) ** n < shrink
bodies = [Body(d) for d in S['bodies']]
CP = S.get('cockpit')
cb = bodies[CP['body']] if CP else None
if CP:
    OY0, OY1 = CP['y0'], CP['y1']; fxs = pchip(CP['xs'])
    def xs(y): return fxs(y)

for bi, b in enumerate(bodies):
    d = b.d; M2, T = d.get('m2', 22), d.get('t', 12)
    if CP and b is cb:
        ys = np.concatenate([np.linspace(b.y0, OY0, 34), np.linspace(OY0, OY1, 30), np.linspace(OY1, b.y1, 44)])
    else:
        ys = np.linspace(b.y0, b.y1, d.get('n', 90))
    ys = sorted(set(np.round(ys, 5)))
    def th_split(y):
        if not (CP and b is cb): return -math.pi / 2 + M2 * 2 * math.pi / (2 * M2 + T)   # evenly round
        zt, zb, w, zc, n = b.sec(y); return math.acos(min(0.98, xs(y) / w) ** (n / 2))
    pt = part(d.get('part', BODY))
    P, U = [], []
    for y in ys:
        t0 = th_split(y)
        ths = list(np.linspace(-math.pi / 2, t0, M2 + 1)) + list(np.linspace(t0, math.pi - t0, T + 1))[1:] + list(np.linspace(math.pi - t0, 1.5 * math.pi, M2 + 1))[1:]
        row = [b.ring_pt(y, t) for t in ths]
        arc = np.concatenate([[0], np.cumsum([(row[k + 1] - row[k]).length for k in range(len(row) - 1)])])
        arc -= arc[M2 + T // 2]                              # zero on the top centre line
        P.append(row); U.append([(y, float(v)) for v in arc])
    in_open = (lambda i, j: OY0 - 1e-6 <= ys[i] and ys[i + 1] <= OY1 + 1e-6 and M2 <= j < M2 + T) if (CP and b is cb) else None
    I = pt.grid(P, U, b.chart, skip=in_open)
    zc0 = b.sec(ys[0])[3]; zc1 = b.sec(ys[-1])[3]
    c0 = pt.v((b.x, ys[0] + (0.03 if d.get('spinner') else -0.02), zc0)); c1 = pt.v((b.x, ys[-1] + 0.02, zc1))
    for j in range(len(P[0]) - 1):
        pt.f((c0, I[0][j], I[0][j + 1]), 'sw_ink' if d.get('spinner') else d.get('nose_chart', 'sw_paint'), [(0, 0)] * 3)
        pt.f((c1, I[-1][j + 1], I[-1][j]), d.get('tail_chart', 'sw_under'), [(0, 0)] * 3)
    if CP and b is cb:
        # the opening's lip: the rim turned down into the hull, so the skin has thickness there
        iy = [i for i, y in enumerate(ys) if OY0 - 1e-6 <= y <= OY1 + 1e-6]
        rim = [(i, M2) for i in iy] + [(iy[-1], j) for j in range(M2 + 1, M2 + T)] + [(i, M2 + T) for i in reversed(iy)] + [(iy[0], j) for j in range(M2 + T - 1, M2, -1)]
        rimv = []
        for (i, j) in rim:
            p = P[i][j]; c = Vector((b.x, (OY0 + OY1) / 2, p.z)); dd = (c - p); dd.z = 0; dd.normalize()
            rimv.append(pt.v(p + dd * 0.03 + Vector((0, 0, -0.07))))
        for k in range(len(rim)):
            k2 = (k + 1) % len(rim)
            pt.f((I[rim[k][0]][rim[k][1]], I[rim[k2][0]][rim[k2][1]], rimv[k2], rimv[k]), 'sw_ink', [(0, 0)] * 4)
    b.ys = ys

# ------------------------------------------------------------------ canopy and its frames
CN = S.get('canopy')
if CN:
    froof = pchip(CN['roof']); froofw = pchip(CN['roof_w']); ROUND = CN.get('round', 3); NC = 25
    def can_sec(y, lift=0.0):
        """Points across the hood at station y, from the right sill over the roof to the left sill (NC of them)."""
        x0 = xs(y); zs = cb.top(cb.x + x0, y) - 0.004; zr = max(froof(y), zs + 0.01); xr = min(froofw(y), x0 * 0.92)
        poly = [Vector((cb.x - x0, y, zs)), Vector((cb.x - xr, y, zr)), Vector((cb.x + xr, y, zr)), Vector((cb.x + x0, y, zs))]
        if CN.get('bulge'):          # a blown hood: the sides bow out past the sill before they turn in to the roof
            bz = zs + (zr - zs) * 0.45; bx = x0 + CN['bulge'] * max(0.0, math.sin(math.pi * min(1, max(0, (y - OY0) / (OY1 - OY0)))))
            poly = [poly[0], Vector((cb.x - bx, y, bz)), poly[1], poly[2], Vector((cb.x + bx, y, bz)), poly[3]]
        for _ in range(ROUND):   # Chaikin: round the corners, keep the sill ends
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
                c = Vector((cb.x, y, zs + 0.12)); dd = p - c; dd.y = 0; dd.normalize(); p = p + dd * lift
            out.append(p)
        return out
    cys = list(np.linspace(OY0, OY1, 46))
    can = part(P_ + '_Canopy')
    can.grid([can_sec(y) for y in cys], [[(0, 0)] * NC] * len(cys), 'sw_glass')
    def strip(y0, y1, k0, k1, n_y, lift=0.014, chart='sw_frame', pname=BODY):
        """A raised band over the hood between two stations and two section indices (frame members)."""
        pt = part(pname); yy = np.linspace(y0, y1, n_y)
        top_ = [can_sec(y, lift)[k0:k1 + 1] for y in yy]; bot = [can_sec(y, -0.004)[k0:k1 + 1] for y in yy]
        n_k = k1 - k0 + 1; z = [[(0, 0)] * n_k] * n_y
        It = pt.grid(top_, z, chart); Ib = pt.grid(bot, z, chart)
        edge = [(i, 0) for i in range(n_y)] + [(n_y - 1, j) for j in range(1, n_k)] + [(i, n_k - 1) for i in range(n_y - 2, -1, -1)] + [(0, j) for j in range(n_k - 2, 0, -1)]
        for k in range(len(edge)):
            (i0, j0), (i1, j1) = edge[k], edge[(k + 1) % len(edge)]
            pt.f((It[i0][j0], It[i1][j1], Ib[i1][j1], Ib[i0][j0]), chart, [(0, 0)] * 4)
    YW = CN['screen']                                                    # the windscreen's arch
    strip(OY0, OY0 + 0.05, 0, NC - 1, 2)                                 # windscreen foot
    strip(YW - 0.03, YW + 0.03, 0, NC - 1, 2)                            # windscreen arch
    for k0 in CN.get('posts', (5, NC - 7)):
        strip(OY0 + 0.02, YW, k0, k0 + 2, 8)                             # the windscreen's corner posts
    for yy_ in CN.get('bows', ()):
        strip(yy_ - 0.022, yy_ + 0.022, 0, NC - 1, 2, pname=P_ + '_Canopy', chart='sw_glassframe')     # hood bows
    for k0 in CN.get('rails', ()):
        strip(YW + 0.03, OY1 - 0.01, k0, k0 + 2, 18, pname=P_ + '_Canopy', chart='sw_glassframe')       # hood rails
    if CN.get('aft_bow') is not None:                                    # the fixed glazing's frame behind the hood
        strip(CN['aft_bow'] - 0.022, CN['aft_bow'] + 0.022, 0, NC - 1, 2)

# ------------------------------------------------------------------ wings, tailplanes, fins: lofted aerofoils
def aerofoil(c, t):  # half thickness at chord fraction c for thickness ratio t (closed trailing edge)
    return 5 * t * (0.2969 * math.sqrt(c) - 0.1260 * c - 0.3516 * c * c + 0.2843 * c ** 3 - 0.1036 * c ** 4)
def cfr(c0, c1, n):
    k = (1 - np.cos(np.linspace(0, math.pi, n))) / 2 if c0 == 0 and c1 == 1 else (1 - np.cos(np.linspace(0, math.pi / 2, n))) if c0 == 0 else np.linspace(0, 1, n)
    return list(c0 + (c1 - c0) * k)
def lifting(pname, chart, spans, le, te, mid, tc, span_axis, thick_axis, sgn, c0=None, c1=None, camber=0.0,
            tip_r=0.0, caps=(None, None), n=17, close='sw_ink', xoff=0.0):
    """One piece of a wing-like surface. le(s), te(s): the y of the edges; mid(s): its height on thick_axis (its x
    offset for a fin); c0(s), c1(s): the chord fractions it runs between (a hinge cut)."""
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
            p = [0.0, 0.0, 0.0]; p[1] = yy
            if span_axis == 0: p[0] = xoff + s * sgn; p[2] = mid(s) + th + cam
            else: p[2] = s; p[0] = xoff + mid(s) + th
            p = Vector(p)
            if prev is not None: arc += (p - prev).length
            prev = p; row.append(p); uv.append((s, arc))
        half = uv[len(uv) // 2][1]; uv = [(u_, v_ - half) for (u_, v_) in uv]
        P.append(row); U.append(uv)
    I = pt.grid(P, U, chart)
    for i in range(len(P) - 1):                                           # the closing wall (hinge cove), where there is one
        if (P[i][0] - P[i][-1]).length > 1e-4 or (P[i + 1][0] - P[i + 1][-1]).length > 1e-4:
            pt.f((I[i][-1], I[i][0], I[i + 1][0], I[i + 1][-1]), close, [(0, 0)] * 4)
    if caps[0]: pt.cap(I[0], caps[0])
    if caps[1]: pt.cap(I[-1], caps[1])

HING = {}
def lin(p0, p1): return lambda s: p0[1] + (p1[1] - p0[1]) * (s - p0[0]) / (p1[0] - p0[0])
def surface(W, key, mover, sides=(('L', 1), ('R', -1))):
    """A wing or tailplane, both halves: the fixed part, its moving surface (hinged on a straight line at chord
    fraction f, or at a constant y), and a rounded tip."""
    le, te, mid, tc = pchip(W['le']), pchip(W['te']), pchip(W['mid']), pchip(W['tc'])
    s_root, s_tip, tip_r = W.get('root', 0.0), W['tip'], W.get('tip_r', 0.3)
    M = W[mover]; A0, A1 = M['s']
    if 'y' in M: hy0 = hy1 = M['y']
    else:
        hy0 = le(A0) + M['f'] * (te(A0) - le(A0)); hy1 = le(A1) + M.get('f1', M['f']) * (te(A1) - le(A1))
    h = lin((A0, hy0), (A1, hy1)); f = lambda s: (h(s) - le(s)) / (te(s) - le(s))
    nm = {'aileron': 'Aileron', 'elevator': 'Elevator'}[mover]; ch_ = {'aileron': 'ail', 'elevator': 'elev'}[mover]
    xo = W.get('x', 0.0)
    for sd, sgn in sides:
        kw = dict(le=le, te=te, mid=mid, tc=tc, span_axis=0, thick_axis=2, sgn=sgn, camber=W.get('camber', 0.0), xoff=xo)
        tip_start = s_tip - tip_r
        inner = [s for s in np.linspace(s_root, A0, max(4, int((A0 - s_root) / 0.12)))]
        lifting(BODY, key + sd, inner, caps=(None, 'sw_ink'), **kw)
        lifting(BODY, key + sd, list(np.linspace(A0, A1, 16)), c1=f, **kw)
        if A1 < tip_start - 0.02:
            lifting(BODY, key + sd, list(np.linspace(A1, tip_start, max(3, int((tip_start - A1) / 0.12)))), **kw)
            t0 = tip_start
        else: t0 = A1
        lifting(BODY, key + sd, [t0] + list(t0 + (s_tip - t0) * np.sin(np.linspace(0, math.pi / 2, 10))[1:]), tip_r=s_tip - t0, caps=('sw_ink', 'sw_paint'), **kw)
        pn = f'{P_}_{nm}{sd}' if not M.get('names') else M['names'][sd]
        lifting(pn, ch_ + sd, list(np.linspace(A0 + 0.015, A1 - 0.015, 14)), c0=lambda s: f(s) + 0.012, caps=('sw_ink', 'sw_ink'), **kw)
        HING[pn] = ((xo + sgn * A0, h(A0), mid(A0)), (xo + sgn * A1, h(A1), mid(A1)))
    return le, te, mid, tc
WING = surface(S['wing'], 'wing', 'aileron')
TAILP = surface(S['tail'], 'tail', 'elevator')
for fi, FN in enumerate(S['fins']):
    le, te, tc = pchip(FN['le']), pchip(FN['te']), pchip(FN['tc'])
    h0, h1, tip_r = FN['h0'], FN['h1'], FN.get('tip_r', 0.17); R = FN['rudder']; YR = R['y']
    f = lambda s, le=le, te=te, YR=YR: (YR - le(s)) / (te(s) - le(s))
    sfx = FN.get('suffix', '')
    kw = dict(le=le, te=te, mid=lambda s: 0.0, tc=tc, span_axis=2, thick_axis=0, sgn=1, xoff=FN.get('x', 0.0))
    if R['h'][0] > h0 + 0.03: lifting(BODY, 'fin' + sfx, list(np.linspace(h0, R['h'][0], 4)), caps=(None, None), **kw)
    lifting(BODY, 'fin' + sfx, list(np.linspace(R['h'][0], h1 - tip_r, 12)), c1=f, **kw)
    lifting(BODY, 'fin' + sfx, [h1 - tip_r] + list(h1 - tip_r + tip_r * np.sin(np.linspace(0, math.pi / 2, 8))[1:]), tip_r=tip_r, caps=('sw_ink', 'sw_paint'), **kw)
    if FN.get('below'):                                                   # a ventral fin under the boom (P-38)
        bl = FN['below']; kb = dict(kw); kb['le'] = pchip(bl['le']); kb['te'] = pchip(bl['te'])
        lifting(BODY, 'fin' + sfx, list(np.linspace(bl['h1'], bl['h0'], 6)), caps=(None, 'sw_paint'), **kb)
    rn = f'{P_}_Rudder{sfx}'
    lifting(rn, 'rud' + sfx, list(np.linspace(R['h'][0] + 0.02, R['h'][1], 10)), c0=lambda s, f=f: f(s) + 0.02, caps=('sw_ink', 'sw_ink'), **kw)
    HING[rn] = ((FN.get('x', 0.0), YR, R['h'][0] + 0.02), (FN.get('x', 0.0), YR, R['h'][1]))

# ------------------------------------------------------------------ scoops, pods, tubes
def loft(pname, chart, secs, mouth=None, nseg=24, n=3.0, cap_back=True, x=0.0):
    """A rounded box lofted through sections (y, half width, z top, z bottom); a dark mouth on the first section."""
    pt = part(pname); Ps, Us = [], []
    for (y, w, zt, zb) in secs:
        row = []
        for th in np.linspace(0, 2 * math.pi, nseg + 1):        # the first point again at the end: a UV seam, welded later
            c, s_ = math.cos(th), math.sin(th)
            row.append(Vector((x + w * math.copysign(abs(c) ** (2 / n), c), y, (zt + zb) / 2 + (zt - zb) / 2 * math.copysign(abs(s_) ** (2 / n), s_))))
        arc = np.concatenate([[0], np.cumsum([(row[k + 1] - row[k]).length for k in range(len(row) - 1)])]); arc -= arc[nseg * 3 // 4]
        Ps.append(row); Us.append([(y, float(v)) for v in arc])
    Is = pt.grid(Ps, Us, chart)
    Is = [row[:nseg] for row in Is]; Ps = [row[:nseg] for row in Ps]
    if mouth:
        mv = [pt.v(p + Vector((0, mouth, 0)) + (Vector((x, p.y, (secs[0][2] + secs[0][3]) / 2)) - p) * 0.16) for p in Ps[0]]
        for j in range(nseg):
            k = (j + 1) % nseg; pt.f((Is[0][j], Is[0][k], mv[k], mv[j]), 'sw_ink', [(0, 0)] * 4)
        pt.cap(mv, 'sw_ink')
    if cap_back: pt.cap(list(reversed(Is[-1])), 'sw_ink')
def tube(p0, p1, r0, r1, chart, nseg=10, pname=BODY, squash=1.0, endcap='sw_ink'):
    pt = part(pname); p0, p1 = Vector(p0), Vector(p1); ax = (p1 - p0).normalized()
    u = ax.cross(Vector((0, 0, 1))); u = u.normalized() if u.length > 1e-3 else Vector((1, 0, 0)); v = ax.cross(u)
    rings = []
    for (p, r) in ((p0, r0), (p1, r1)):
        rings.append([p + (u * math.cos(t) + v * math.sin(t) * squash) * r for t in np.linspace(0, 2 * math.pi, nseg + 1)[:-1]])
    I = pt.grid(rings, [[(0, 0)] * nseg] * 2, chart, wrap=True)
    pt.cap(I[1], endcap); pt.cap(I[0], chart)
def wing_low(s, y):
    le, te, mid, tc = WING; c = min(1, max(0, (y - le(s)) / (te(s) - le(s))))
    return mid(s) - aerofoil(c, tc(s)) * (te(s) - le(s))
def wing_up(s, y):
    le, te, mid, tc = WING; c = min(1, max(0, (y - le(s)) / (te(s) - le(s))))
    return mid(s) + aerofoil(c, tc(s)) * (te(s) - le(s))
CTX = dict(part=part, loft=loft, tube=tube, bodies=bodies, wing_low=wing_low, wing_up=wing_up, WING=WING, TAILP=TAILP, Vector=Vector,
           np=np, math=math, aerofoil=aerofoil, BODY=BODY, P_=P_)
if 'extras' in G: G['extras'](CTX)

# ------------------------------------------------------------------ propellers: the spinner continues the cowl
PROPS = {}
for PR in S['props']:
    b = bodies[PR.get('body', 0)]
    YS = b.y0; zt, zb, R0, TZ, _ = b.sec(YS); R0 = (zt - zb) / 2; TZ = (zt + zb) / 2; X0 = b.x
    ORG = Vector((X0, YS, TZ))
    prop = part(PR['name']); PROPS[PR['name']] = ORG
    SL = PR['spinner_len']; slope = (b.sec(YS + 0.14)[2] - R0) / 0.14 if PR.get('match_slope', True) else 0.0
    A_ = PR.get('shape', 0.62); B_ = A_ - slope * SL / R0
    def spin_r(u, R0=R0, SL=SL, A_=A_, B_=B_):
        t = min(1.0, u / SL); r = R0 * (1 - t) ** A_ * (1 + B_ * t); cap = 0.03
        return max(r, math.sqrt(max(2 * cap * (SL - u) - (SL - u) ** 2, 0))) if SL - u < cap else r
    SEG = 48
    us = sorted(set(list(SL * (1 - np.cos(np.linspace(0, math.pi / 2, 26))))))[:-1]
    rows, uvs = [], []
    for u in us:
        r = spin_r(u) - 0.002; y = YS - 0.004 - u
        rows.append([Vector((X0 + r * math.sin(a_), y, TZ + r * math.cos(a_))) for a_ in np.linspace(0, 2 * math.pi, SEG + 1)])
        uvs.append([(u, a_ * R0) for a_ in np.linspace(0, 2 * math.pi, SEG + 1)])
    Isp = prop.grid(rows, uvs, 'spin' + PR.get('suffix', ''))
    tip = prop.v((X0, YS - 0.004 - SL, TZ))
    for j in range(SEG): prop.f((tip, Isp[-1][j + 1], Isp[-1][j]), 'spin' + PR.get('suffix', ''), [uvs[-1][j + 1], uvs[-1][j], uvs[-1][j]])
    prop.cap(list(reversed(Isp[0][:SEG])), 'sw_ink')                     # the backplate
    Rt, PITCH, CH, NB = PR['radius'], PR['pitch'], PR['chord'], PR['blades']
    YP = YS - PR.get('blade_y', 0.18)
    hub = PR.get('hub', 0.22)
    def bchord(f):
        if f < hub + 0.08: return 0.10 + (CH - 0.10) * max(0, (f - hub) / 0.08) ** 1.5 if f > hub else 0.10
        c = CH * (0.90 + 0.10 * math.sin(math.pi * min(1, (f - 0.3) / 0.5)))
        if f > 0.80: c *= math.sqrt(max(1 - ((f - 0.80) / 0.20) ** 2, 0.0)) * 0.92 + 0.08
        return c
    def bthick(f): return 0.10 if f < hub else max(0.020, 0.10 - 0.15 * (f - hub)) if f < 0.5 else max(0.016, 0.058 - 0.05 * (f - 0.5))
    fs = list(np.linspace(0.12, 0.80, 16)) + list(0.80 + 0.20 * np.sin(np.linspace(0, math.pi / 2, 9)[1:]))
    MB = 18; hand = PR.get('hand', 1)
    for bl in range(NB):
        th = math.radians(90 + 360 / NB * bl + PR.get('phase', 0))
        er = Vector((math.cos(th), 0, math.sin(th))); ax = Vector((0, -1, 0)); et = ax.cross(er) * hand
        rows = []
        for f in fs:
            r = f * Rt; c = bchord(f); t = bthick(f)
            if f < hub: c = t = 0.10
            beta = math.atan(PITCH / (2 * math.pi * max(r, 0.25)))
            cd = et * math.cos(beta) + ax * math.sin(beta); nd = -et * math.sin(beta) + ax * math.cos(beta)
            ring = []
            for k in range(MB):
                an = 2 * math.pi * k / MB; uu = 0.5 * c * math.cos(an); vv = 0.5 * t * math.sin(an)
                if f >= hub: vv *= (1.0 + 0.6 * math.cos(an)) * 0.8
                ring.append(Vector((X0, YP, TZ)) + er * r + cd * uu + nd * vv)
            rows.append(ring)
        Ib = prop.grid(rows, [[(0, 0)] * MB] * len(rows), 'sw_prop', wrap=True)
        prop.cap(Ib[-1], 'sw_prop')

# ------------------------------------------------------------------ charts: pack into the atlas
ext = {}
for p in PARTS.values():
    for (ch, uv) in p.UV:
        e = ext.setdefault(ch, [1e9, 1e9, -1e9, -1e9])
        for (u_, v_) in uv: e[0] = min(e[0], u_); e[1] = min(e[1], v_); e[2] = max(e[2], u_); e[3] = max(e[3], v_)
GR = 8; occ = np.zeros((SIZE // GR, SIZE // GR), bool)
rect = {}
def size(ch):
    e = ext[ch]
    if ch.startswith('sw_'): return 32, 32
    return int(math.ceil((e[2] - e[0]) * PPU)) + 2 * PAD, int(math.ceil((e[3] - e[1]) * PPU)) + 2 * PAD
for ch in sorted(ext, key=lambda c: -size(c)[1] * size(c)[0]):
    w, h = size(ch); gw, gh = -(-w // GR), -(-h // GR); done = False
    for gy in range(0, SIZE // GR - gh + 1):
        for gx in range(0, SIZE // GR - gw + 1):
            if not occ[gy:gy + gh, gx:gx + gw].any():
                occ[gy:gy + gh, gx:gx + gw] = True; rect[ch] = (gx * GR, gy * GR, w, h); done = True; break
        if done: break
    assert done, ('no room for chart', ch, w, h, '- lower the spec ppu')
def to_uv(ch, u_, v_):
    x, y, w, h = rect[ch]; e = ext[ch]
    if ch.startswith('sw_'): px, py = x + 16, y + 16
    else: px, py = x + PAD + (u_ - e[0]) * PPU, y + PAD + (v_ - e[1]) * PPU
    return px / SIZE, 1 - py / SIZE
print('charts', {k: v for k, v in rect.items()})
print('atlas use', round(occ.mean(), 3))

# ------------------------------------------------------------------ into Blender
bpy.ops.wm.open_mainfile(filepath=SRC)
old_body = bpy.data.objects[BODY]
mat = old_body.data.materials[0]; mat.name = P_ + '_Mat'
glass = bpy.data.materials.get(P_ + '_CanopyGlass') or bpy.data.materials.new(P_ + '_CanopyGlass')
keep = {BODY} | set(PROPS)
for nme in [o.name for o in bpy.data.objects if o.type == 'MESH' and ((o.name.startswith(P_ + '_') and o.name not in keep) or o.name == 'Pit_Tub')] \
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
        m = (b0 >= e) & (b1 >= e) & (b2 >= e) & (xx >= 0) & (yy >= 0) & (xx < SIZE) & (yy < SIZE)
        if not m.any(): continue
        ix = xx[m].astype(int); iy_ = yy[m].astype(int)
        B = np.stack([b0[m], b1[m], b2[m]], 1)
        texmap['pos'][iy_, ix] = B @ pos; texmap['nrm'][iy_, ix] = B @ nr; texmap['chart'][iy_, ix] = chid[ch]
fch = {}
for nme, p in PARTS.items():
    me = bpy.data.meshes.new(nme + "_new")
    ob = bpy.data.objects.get(nme)
    if ob is not None and nme in keep:
        old_me = ob.data; ob.data = me; bpy.data.meshes.remove(old_me)
    else:
        ob = bpy.data.objects.new(nme, me); bpy.context.scene.collection.objects.link(ob)
        if nme != BODY: ob.parent = old_body
    org = Vector(HING[nme][0]) if nme in HING else PROPS[nme].copy() if nme in PROPS else Vector((0, 0, 0))
    me.from_pydata([tuple(Vector(v) - org) for v in p.V], [], p.F); me.update()
    assert len(me.polygons) == len(p.F), (nme, len(me.polygons), len(p.F))
    uvl = me.uv_layers.new(name='UVMap')
    for poly, (ch, uv) in zip(me.polygons, p.UV):
        for li, t in zip(poly.loop_indices, uv): uvl.data[li].uv = to_uv(ch, *t)
    me.materials.append(glass if nme == P_ + '_Canopy' else mat)
    ob.matrix_world = __import__('mathutils').Matrix.Translation(org)
    bm = bmesh.new(); bm.from_mesh(me)
    chl = bm.faces.layers.int.new('chart')
    for f_, (ch, uv) in zip(bm.faces, p.UV): f_[chl] = chid[ch]
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.faces.ensure_lookup_table()
    fch[nme] = [CH[f_[chl] - 1] for f_ in bm.faces]
    bm.to_mesh(me); bm.free(); me.update()
    for poly in me.polygons: poly.use_smooth = True
    bpy.context.view_layer.objects.active = ob
    for o in bpy.data.objects: o.select_set(o == ob)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    raster(me, np.array(org), nme)
    if nme in HING:
        hh = bpy.data.objects.new(nme + '_Hinge', None); bpy.context.scene.collection.objects.link(hh); hh.parent = ob
        hh.matrix_world = __import__('mathutils').Matrix.Translation(Vector(HING[nme][1])); hh.empty_display_size = 0.1
    print(nme, len(me.vertices), 'verts', len(me.polygons), 'faces')
bpy.context.view_layer.update()
# the old cockpit's sill fairing was shaped to the old hull: drop whatever of it now lies outside the new skin
ps = bpy.data.objects.get('Pit_Static')
if ps and CP:
    def inside(c):
        if OY0 < c.y < OY1 and abs(c.x - cb.x) < xs(c.y) - 0.012 and c.z > cb.sec(c.y)[3]: return True
        return cb.inside(c)
    bm = bmesh.new(); bm.from_mesh(ps.data); Mw = ps.matrix_world
    SQ = S.get('pit_squeeze')          # parts of the old cockpit (its panel) wider than the new hull: drawn in to fit
    if SQ:
        Mi = Mw.inverted(); nsq = 0
        for f_ in bm.faces:
            W_ = [Mw @ v.co for v in f_.verts]
            if all(SQ['y'][0] <= p.y <= SQ['y'][1] and SQ['z'][0] <= p.z <= SQ['z'][1] for p in W_) and not all(inside(p) for p in W_):
                for v, p in zip(f_.verts, W_):
                    lim = cb.half_x(p.y, p.z) * 0.93 ** (1 / cb.sec(p.y)[4]) - 0.01
                    if abs(p.x - cb.x) > lim: v.co = Mi @ Vector((cb.x + math.copysign(lim, p.x - cb.x), p.y, p.z))
                nsq += 1
        print('Pit_Static faces drawn in to fit:', nsq)
    gone = [f_ for f_ in bm.faces if not inside(Mw @ f_.calc_center_median()) or not all(inside(Mw @ v.co) for v in f_.verts)]
    print('Pit_Static faces outside the new hull removed:', len(gone), 'of', len(bm.faces))
    bmesh.ops.delete(bm, geom=gone, context='FACES'); bm.to_mesh(ps.data); bm.free()
for k, v in S.get('empties', {}).items():                                # seat and eye point moves, where the spec asks
    o = bpy.data.objects.get(k)
    if o: o.location = Vector(v)
bpy.context.view_layer.update()
json.dump({'size': SIZE, 'ppu': PPU, 'pad': PAD, 'rect': rect, 'ext': ext, 'ids': chid}, open(OUT + '/charts.json', 'w'), indent=1)
np.savez_compressed(OUT + '/texmap.npz', **texmap)
bpy.ops.wm.save_as_mainfile(filepath=DST)
print('saved', DST)
