# Rebuild the Hi3D missile: a clean lathed body along its true axis, painted from the original by sampling the nearest
# body vertices (the fins' own vertices left out, so their roots don't smear onto it), and an even cruciform set of four
# tail fins and four canards in place of the generator's three uneven tail fins and two canards. Writes an OBJ one unit
# long (nose toward +z, fins at 45 degrees to the vertical, origin on the axis at mid-length) and its colour map.
# Steps 1-4: missile_prep.py, missile_dump.py, missile_axis.py, missile_profile.py (bpy); this is step 5 (numpy, scipy, Pillow).
# Run: python -I missile.py -- <src.npz> <axis.npy> <prof.npy> <albedo.png> <out.obj> <out_basecolor.jpg>
import sys, numpy as np
from PIL import Image
from scipy.spatial import cKDTree
a = sys.argv[sys.argv.index('--')+1:]
src = np.load(a[0]); ca = np.load(a[1]); prof = np.load(a[2])
Image.MAX_IMAGE_PIXELS = None
alb = np.asarray(Image.open(a[3]).convert('RGB'), dtype=np.float32) / 255
c, ax = ca[:3], ca[3:]
u0 = np.cross(ax, [0, 0, 1.0]); u0 /= np.linalg.norm(u0); w0 = np.cross(ax, u0)
X = src['co'] - c; t = X @ ax; px, py = X @ u0, X @ w0; r = np.hypot(px, py)
vuv = src['uv']

# ---- the profile: radius along the axis (t from the nose tip to the tail), fins and bumps smoothed out
T0, T1 = t.min(), t.max()
pt, pr = prof[:, 0], prof[:, 1].copy()
k = np.convolve(np.pad(pr, 2, mode='edge'), np.ones(5) / 5, mode='valid'); body = pt > -5.9; pr[body] = k[body]
nz = pt <= -5.7   # the nose: a smooth ogive fitted through the measured radii (the sector medians are noisy there)
cf = np.polyfit(pt[nz] - T0, pr[nz] ** 2, 3); pr[nz] = np.sqrt(np.maximum(np.polyval(cf, pt[nz] - T0), 0)); pr[pt <= T0 + 1e-6] = 0
def body_r(tt): return np.interp(tt, pt, pr)
# profile polyline in (t, r): nose tip, the measured body, the nozzle lip and its throat
NOZ_R, NOZ_D = 0.36, 0.22
ts = np.r_[np.linspace(T0, -5.8, 18), np.arange(-5.6, T1 - 0.05, 0.2), T1]
poly = [(T0, 0.0)] + [(x, body_r(x)) for x in ts[1:]] + [(T1, NOZ_R), (T1 - NOZ_D, NOZ_R), (T1 - NOZ_D, 0.0)]
poly = np.array(poly)
seg = np.r_[0, np.cumsum(np.hypot(*np.diff(poly, axis=0).T))]; S_LEN = seg[-1]

# ---- texture: body u in [0, BU) (angle), v along the profile; fins in the strip past BU
W, H, BU = 1024, 2048, 0.875
SEAM = 90.0   # degrees: the seam runs along the top, under the pylon
def ang_of_u(uu): return np.radians(SEAM + uu / BU * 360)

keep = np.ones(len(t), bool)
for lo, hi in ((-5.2, -4.1), (0.95, 2.65)):   # the fin zones: drop the fins' own vertices
    z = (t > lo) & (t < hi); keep[z] = r[z] <= body_r(t[z]) + 0.02
P = np.c_[t, px, py][keep]; UV = vuv[keep]
tree = cKDTree(P)
def sample(img, uv):
    h, w = img.shape[:2]
    x = np.clip(uv[..., 0] * (w - 1), 0, w - 1); y = np.clip((1 - uv[..., 1]) * (h - 1), 0, h - 1)
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int); x1 = np.minimum(x0 + 1, w - 1); y1 = np.minimum(y0 + 1, h - 1)
    fx = (x - x0)[..., None]; fy = (y - y0)[..., None]
    return (img[y0, x0] * (1 - fx) + img[y0, x1] * fx) * (1 - fy) + (img[y1, x0] * (1 - fx) + img[y1, x1] * fx) * fy

tex = np.zeros((H, W, 3), np.float32)
bw = int(W * BU)
uu = (np.arange(bw) + 0.5) / W
vv = (np.arange(H) + 0.5) / H   # v from the top row down: row 0 is v = 1 (the nose)
sv = (1 - vv) * S_LEN
qt = np.interp(sv, seg, poly[:, 0]); qr = np.interp(sv, seg, poly[:, 1])
th = ang_of_u(uu)
Q = np.stack(np.broadcast_arrays(qt[:, None], qr[:, None] * np.cos(th)[None, :], qr[:, None] * np.sin(th)[None, :]), -1).reshape(-1, 3)
d, idx = tree.query(Q, k=4, workers=-1)
wgt = 1 / np.maximum(d, 1e-4); wgt /= wgt.sum(1, keepdims=True)
col = (sample(alb, UV[idx]) * wgt[..., None]).sum(1)
tex[:, :bw] = col.reshape(H, bw, 3)
# where the old fins' roots and brackets were (and the streaky nose tip), the paint goes round the body evenly:
# each row there is its own median, eased in over a short blend so the bands and collars carry on
def even(lo, hi, ease=0.08):
    wt = np.clip(np.minimum(qt - lo, hi - qt) / ease + 0.5, 0, 1)[:, None, None]
    med = np.median(tex[:, :bw], axis=1, keepdims=True)
    tex[:, :bw] = tex[:, :bw] * (1 - wt) + med * wt
even(-5.25, -4.05); even(0.9, 2.55)
# the nose cone: the source's dark grey dome samples into streaks; paint it one smooth grey down to its black ring
lum = tex[:, :bw].mean(axis=(1, 2))
edge = int(np.argmax(lum > 0.3))          # the first (olive) row past the dome
ring = edge - 12                          # keep the thin black ring just above it
clean = np.median(tex[ring - 80:ring - 10, :bw].reshape(-1, 3), axis=0)
shade = np.linspace(0.92, 1.0, ring)[:, None, None]
tex[:ring, :bw] = clean[None, None, :] * shade

# ---- the fins' paint: the original fins' olive, darker at the root
fin = (r > body_r(t) + 0.5)
olive = np.median(sample(alb, vuv[fin]), axis=0); print('olive', olive)
span = np.linspace(0, 1, W - bw)
tex[:, bw:] = olive[None, None, :] * (0.82 + 0.18 * span)[None, :, None]
img = Image.fromarray((np.clip(tex, 0, 1) * 255).astype(np.uint8))
img.save(a[5], quality=92)

# ---- geometry, in the source's units (t, x, y); scaled to one unit long at the end
verts, uvs, norms, faces = [], [], [], []
def add_v(p, uv, n):
    verts.append(p); uvs.append(uv); norms.append(n); return len(verts)
# lathe: rings along the polyline, SEG segments, with the seam doubled
SEG = 36
segn = []
for i in range(len(poly)):   # normal of the profile at each ring: average of the neighbouring segments' outward normals
    nn = np.zeros(2)
    for j in (i - 1, i):
        if 0 <= j < len(poly) - 1:
            d2 = poly[j + 1] - poly[j]; L = np.hypot(*d2)
            if L > 0: nn += np.array([d2[1], -d2[0]]) / L   # (dt, dr) rotated: outward for a profile running nose to tail
    segn.append(nn / max(np.hypot(*nn), 1e-9))
# sharp corners at the nozzle lip and throat: split those rings so they are flat-shaded across the edge
sharp = {len(poly) - 3, len(poly) - 2}
rings = []
for i, (pt_, pr_) in enumerate(poly):
    reps = [None] if i not in sharp else [i - 1, i]   # per side: the normal of the segment on that side
    ids = []
    for side in reps:
        if side is None: nt, nr = segn[i]
        else:
            d2 = poly[side + 1] - poly[side]; L = np.hypot(*d2); nt, nr = d2[1] / L, -d2[0] / L
        ring = []
        for s in range(SEG + 1):
            uu_ = s / SEG * BU; th_ = ang_of_u(uu_)
            p = (pt_, pr_ * np.cos(th_), pr_ * np.sin(th_)); n = (nt, nr * np.cos(th_), nr * np.sin(th_))
            ring.append(add_v(p, (uu_, 1 - seg[i] / S_LEN), n))
        ids.append(ring)
    rings.append(ids)
for i in range(len(poly) - 1):
    A = rings[i][-1]; B = rings[i + 1][0]
    for s in range(SEG):
        faces.append((A[s], B[s], B[s + 1], A[s + 1]))

# fins: a diamond section (sharp leading and trailing edges, thickest at mid chord), roots sunk into the body
def fin_set(le_root, te_root, le_tip, te_tip, tip_r, th_root, th_tip, vlo, vhi):
    for k in range(4):
        phi = np.radians(45 + 90 * k)
        er = np.array([0, np.cos(phi), np.sin(phi)]); en = np.array([0, -np.sin(phi), np.cos(phi)])
        root_r = body_r(0.5 * (le_root + te_root)) * 0.8
        def P3(tt, rr, side, thick):   # side: -1, 0, 1 across the fin
            return np.array([tt, 0, 0]) + er * rr + en * side * thick / 2
        st = [(le_root, te_root, root_r, th_root), (le_tip, te_tip, tip_r, th_tip)]
        pts = []
        for (le, te, rr, thk) in st:
            mid = 0.5 * (le + te)
            pts.append([P3(le, rr, 0, 0), P3(mid, rr, 1, thk), P3(te, rr, 0, 0), P3(mid, rr, -1, thk)])
        def uvp(tt, rr):
            uu_ = BU + 0.01 + (1 - BU - 0.02) * (rr - root_r) / (tip_r - root_r)
            vv_ = vhi - (vhi - vlo) * (tt - min(le_root, le_tip)) / (max(te_root, te_tip) - min(le_root, le_tip))
            return (uu_, vv_)
        def quad(p0, p1, p2, p3):
            n = np.cross(p1 - p0, p3 - p0); n /= np.linalg.norm(n)
            ids = [add_v(tuple(p), uvp(p[0], np.dot(p, er)), tuple(n)) for p in (p0, p1, p2, p3)]
            faces.append(tuple(ids))
        R, Tp = pts
        for j in range(4):   # the four faces round the diamond, root to tip
            a0, a1 = R[j], R[(j + 1) % 4]; b0, b1 = Tp[j], Tp[(j + 1) % 4]
            quad(a0, b0, b1, a1)
        # tip cap (two triangles as a quad of the diamond)
        n = er; ids = [add_v(tuple(p), uvp(p[0], tip_r), tuple(n)) for p in Tp]; faces.append(tuple(ids))
fin_set(1.05, 2.42, 1.85, 2.30, 2.25, 0.09, 0.05, 0.02, 0.48)      # tail
fin_set(-5.10, -4.18, -4.48, -4.22, 1.22, 0.07, 0.04, 0.52, 0.98)  # canards

V = np.array(verts); N = np.array(norms)
mid = 0.5 * (T0 + T1); L = T1 - T0
# out: x, y across (y up), z toward the nose (= -t); one unit long
out = np.c_[V[:, 1], V[:, 2], -(V[:, 0] - mid)] / L
on = np.c_[N[:, 1], N[:, 2], -N[:, 0]]
# winding: the lathe quads face outward in (t, x, y), which is left-handed after z = -t; flip them for the right-handed OBJ
with open(a[4], 'w') as f:
    f.write('# Hi3D missile, rebuilt (Tools~/ArtPipeline/missile.py): one unit long, nose toward +z, fins at 45 degrees, origin mid-length on the axis\n')
    f.write(f'# body radius {pr[(pt>-4)&(pt<1)].mean()/L:.4f}, tail fin span {2*2.25/L:.4f}\n')
    f.write('o Missile\n')
    for p in out: f.write('v %.5f %.5f %.5f\n' % tuple(p))
    for q in uvs: f.write('vt %.5f %.5f\n' % q)
    for n in on: f.write('vn %.4f %.4f %.4f\n' % tuple(n))
    f.write('s off\n')
    for fc in faces:
        fc = fc[::-1]
        f.write('f ' + ' '.join(f'{i}/{i}/{i}' for i in fc) + '\n')
print('verts', len(verts), 'faces', len(faces), 'length', L, 'body r/L', pr[(pt>-4)&(pt<1)].mean()/L)
