# Repaint the wings and tailplanes so their panel lines and rivet rows run straight and unbroken.
# In the generated texture the lines are straight within each texture piece, but the pieces don't line up where they
# meet on the model, so on the plane every line jogs and wobbles at the seams. Fixing that in the texture can't work;
# the surface is repainted from the model instead.
# Each flying surface (each half, upper and lower skin) is unrolled into a flat chart: across = distance out from the
# root, down = place along the chord (leading edge 0, trailing edge 1). The chart is filled from the texels that land
# in it, which joins the pieces into one picture. There the ink is found and painted out of the skin, each stroke is
# simplified to the fewest straight pieces that stay within a couple of centimetres of it (near-spanwise and
# near-chordwise ones snapped exactly), and rivet rows are refitted to straight, evenly spaced dots. Every texel of
# the surface then takes its skin colour from the cleaned chart and the new ink is drawn over it from its true
# position, anti-aliased, so the lines match up across the texture pieces. Big dark areas (markings) are left as they are.
# Run: python -I wing_panels.py <BaseColor in> <pos.npy (bakepos.py)> <BaseColor out> <spec.json> [debug prefix]
#   spec: {"surfaces": [{"name": "wing", "s": [root, tip], "y": [ymin, ymax], "z": [zmin, zmax]}, ...],
#          "keep": [x, y, w, h] (optional atlas rectangle left untouched)}
#   per surface, optional: "cross": true (redraw national crosses), "roundel": [ring colours outward of the red centre],
#   "stray_red": true (paint out red that isn't part of the redrawn marking), "eps" (wobble flattened, metres)
import sys, json, numpy as np, cv2
from skimage.morphology import skeletonize

src, posf, out, specf = sys.argv[1:5]
dbg = sys.argv[5] if len(sys.argv) > 5 else None
spec = json.load(open(specf))
tex = cv2.imread(src)
P = np.load(posf)[::-1]; cov = P[..., 3] > 0.5
X, Y, Z = P[..., 0], P[..., 1], P[..., 2]; AX = np.abs(X)
gy, gx = np.gradient(P[..., :3], axis=(0, 1))
tm = np.where(cov, np.maximum(np.linalg.norm(gx, axis=2), np.linalg.norm(gy, axis=2)), 1)
good = cov & (tm < 0.02)                                   # island edges have broken gradients: don't sample from them
tm = np.clip(tm, 0.0008, 0.02)
img = tex.astype(np.float32)
CELL = 0.004                                               # chart resolution, metres
EPS = 0.032                                                # largest wobble flattened into one straight piece, metres
LW = 0.012                                                 # panel line width, metres
RIV = 0.0055                                               # rivet radius, metres
stats = {'strokes': 0, 'rivet rows': 0}

# upper or lower skin: above or below the mid-surface at that spot (dihedral rules out a fixed height)
g = 0.03; key = (np.round(X / g).astype(np.int64) * 100003 + np.round(Y / g).astype(np.int64))[cov]
zc = Z[cov]; order = np.argsort(key); ks, zs = key[order], zc[order]
uniq, start = np.unique(ks, return_index=True)
zmax = np.maximum.reduceat(zs, start); zmin = np.minimum.reduceat(zs, start)
mid = np.empty_like(zc); mid[order] = np.repeat((zmax + zmin) / 2, np.diff(np.append(start, len(ks))))
MID = np.full(Z.shape, np.nan, np.float32); MID[cov] = mid

def dp(pts, eps):
    """Douglas-Peucker: indices of the points kept."""
    keep = [0, len(pts) - 1]; stack = [(0, len(pts) - 1)]
    while stack:
        i, j = stack.pop()
        if j <= i + 1: continue
        a, b = pts[i], pts[j]; d = b - a; L = np.linalg.norm(d)
        q = pts[i + 1:j] - a
        dist = np.abs(q[:, 0] * d[1] - q[:, 1] * d[0]) / L if L > 1e-9 else np.linalg.norm(q, axis=1)
        k = int(np.argmax(dist))
        if dist[k] > eps: keep.append(i + 1 + k); stack += [(i, i + 1 + k), (i + 1 + k, j)]
    return sorted(keep)

def fit_segment(pts):
    mu = pts.mean(0); _, _, vt = np.linalg.svd(pts - mu, full_matrices=False); d = vt[0]
    t = (pts - mu) @ d
    return mu, d, t.min(), t.max()

def snap(d):
    ang = np.degrees(np.arctan2(d[1], d[0])) % 180
    if min(ang, 180 - ang) < 6: return np.array([1.0, 0.0])
    if abs(ang - 90) < 6: return np.array([0.0, 1.0])
    return d

def remap_big(chart, u, v):
    n_ = len(u); m_ = int(np.ceil(n_ / 4096)) * 4096
    pu = np.zeros(m_, np.float32); pv = np.zeros(m_, np.float32); pu[:n_] = u; pv[:n_] = v
    o = cv2.remap(chart, pu.reshape(-1, 4096), pv.reshape(-1, 4096), cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    return o.reshape(m_, -1)[:n_]

def fill_from_around(img_, mask, avoid, k=61):
    """Fill mask with the skin colour from around it (a wide masked average), not by smearing in what's at its edge."""
    keep = ((mask == 0) & (avoid == 0)).astype(np.float32)
    num = cv2.blur(img_.astype(np.float32) * keep[..., None], (k, k)); den = cv2.blur(keep, (k, k))
    for _ in range(4):                                     # widen where nothing usable was near
        bad = den < 0.02
        if not bad.any(): break
        k = k * 2 + 1; num2 = cv2.blur(img_.astype(np.float32) * keep[..., None], (k, k)); den2 = cv2.blur(keep, (k, k))
        num[bad] = num2[bad]; den[bad] = den2[bad]
    fill = num / np.maximum(den, 1e-4)[..., None]
    out_ = img_.copy(); out_[mask > 0] = np.clip(fill[mask > 0], 0, 255).astype(img_.dtype)
    return out_

def walk(py, px):
    """Order the pixels of one skeleton branch from one end to the other."""
    pts = {(int(a), int(b)) for a, b in zip(py, px)}
    def nbrs(q): return [(q[0] + i, q[1] + j) for i in (-1, 0, 1) for j in (-1, 0, 1) if (i or j) and (q[0] + i, q[1] + j) in pts]
    ends = [q for q in pts if len(nbrs(q)) <= 1]
    cur = ends[0] if ends else next(iter(pts)); seen = {cur}; path = [cur]
    while True:
        nx = [q for q in nbrs(cur) if q not in seen]
        if not nx: break
        cur = min(nx, key=lambda q: abs(q[0] - cur[0]) + abs(q[1] - cur[1])); seen.add(cur); path.append(cur)
    return np.array(path)

for sf in spec['surfaces']:
    S0, S1 = sf['s']
    EPS = sf.get('eps', spec.get('eps', 0.032))
    base = cov & (AX >= S0) & (AX <= S1) & (Y >= sf['y'][0]) & (Y <= sf['y'][1]) & (Z >= sf['z'][0]) & (Z <= sf['z'][1])
    # leading and trailing edge along the span, smoothed
    nb = int((S1 - S0) / 0.02) + 1; sb = np.clip(((AX - S0) / 0.02).astype(int), 0, nb - 1)
    LE = np.full(nb, np.nan); TE = np.full(nb, np.nan)
    for i in range(nb):
        yy = Y[base & (sb == i)]
        if len(yy) > 30: LE[i], TE[i] = np.percentile(yy, 0.5), np.percentile(yy, 99.5)
    idx = np.arange(nb); okb = ~np.isnan(LE)
    LE = np.interp(idx, idx[okb], LE[okb]); TE = np.interp(idx, idx[okb], TE[okb])
    k = np.ones(7) / 7
    LE = np.convolve(np.pad(LE, 3, mode='edge'), k, 'valid'); TE = np.convolve(np.pad(TE, 3, mode='edge'), k, 'valid')
    CH = np.maximum(TE - LE, 0.05)
    sfrac = (AX - S0) / 0.02
    F = (Y - np.interp(sfrac, idx, LE)) / np.interp(sfrac, idx, CH)
    NF = int(float(np.median(CH)) / CELL)
    W = int((S1 - S0) / CELL) + 2; H = NF + 2
    def to_m(cc, rr):                                      # chart (col, row) -> metres (span, y)
        ss = S0 + (cc + 0.5) * CELL; fi = (rr + 0.5) / NF; q = (ss - S0) / 0.02
        return np.stack([ss, np.interp(q, idx, LE) + fi * np.interp(q, idx, CH)], -1)
    all_rings = []
    for side in ('upper', 'lower'):
        for sx in (1, -1):
            sel = base & ((Z >= MID) if side == 'upper' else (Z < MID)) & (X * sx > 0) & (F > -0.03) & (F < 1.03)
            ty, tx = np.nonzero(sel)
            if len(ty) < 2000: continue
            s = AX[ty, tx]; f = F[ty, tx]
            cf = (s - S0) / CELL - 0.5; rf = f * NF - 0.5           # texel -> chart, continuous
            c = np.clip(np.round(cf).astype(int), 0, W - 1); r = np.clip(np.round(rf).astype(int), 0, H - 1)
            gsel = good[ty, tx]
            col = np.zeros((H, W, 3), np.float32); n = np.zeros((H, W), np.float32)
            np.add.at(col, (r[gsel], c[gsel]), tex[ty[gsel], tx[gsel]].astype(np.float32)); np.add.at(n, (r[gsel], c[gsel]), 1)
            have = n > 0; col[have] /= n[have][:, None]
            chart = cv2.inpaint(np.clip(col, 0, 255).astype(np.uint8), (~have).astype(np.uint8), 3, cv2.INPAINT_TELEA)
            gr = cv2.cvtColor(chart, cv2.COLOR_BGR2GRAY)
            # ink: dark, and thin against the skin around it; big dark areas are markings
            bh = cv2.morphologyEx(gr, cv2.MORPH_BLACKHAT, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (27, 27)))
            DARK = min(100, float(np.median(gr)) - 32)        # ink is well below the skin's own tone
            ink = ((bh > 24) & (gr < DARK)).astype(np.uint8)
            big = cv2.morphologyEx((gr < DARK).astype(np.uint8), cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (23, 23)))
            # ...unless the dark area is a long thin bar: an overdrawn panel line, treated as one
            nlb, lb, stb, _ = cv2.connectedComponentsWithStats(big, 8)
            for q in range(1, nlb):
                ys_, xs_ = np.nonzero(lb == q)
                (_, _), (rw, rh), _ = cv2.minAreaRect(np.stack([xs_, ys_], 1).astype(np.float32))
                if max(rw, rh) > 4 * max(min(rw, rh), 1): big[lb == q] = 0
            ink = ((bh > 24) & (gr < DARK)).astype(np.uint8)
            ink &= (cv2.dilate(big, np.ones((5, 5), np.uint8)) == 0).astype(np.uint8)
            ink = cv2.morphologyEx(ink, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
            nlab, lab, st, _ = cv2.connectedComponentsWithStats(ink, 8)
            area = st[:, 4]; ext = np.maximum(st[:, 2], st[:, 3])
            dotc = (area >= 3) & (area <= 70) & (ext <= 14); dotc[0] = False       # rivets and short dashes: <= 5.6 cm
            linec = (ext > 14) & ~dotc; linec[0] = False
            # skin without the ink
            # also the faint remnants: thin darker or lighter wisps (old line edges and highlights) that aren't markings
            th = cv2.morphologyEx(gr, cv2.MORPH_TOPHAT, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (19, 19)))
            wisp = (((bh > 14) | (th > 20)) & (cv2.dilate(big, np.ones((9, 9), np.uint8)) == 0)).astype(np.uint8)
            wisp = cv2.morphologyEx(wisp, cv2.MORPH_OPEN, np.ones((2, 2), np.uint8))
            clean = cv2.inpaint(chart, cv2.dilate(ink | wisp, np.ones((7, 7), np.uint8)), 7, cv2.INPAINT_TELEA)
            # national crosses: measured from the old paint, painted out, redrawn exact (straight edges, square)
            crosses = []; rings = []; painted = np.zeros((H, W), bool)
            if sf.get('cross'):
                nl2, l2, st2, _ = cv2.connectedComponentsWithStats(big, 8)
                cell_a = CELL * float(np.median(CH)) / NF
                for q in range(1, nl2):
                    if st2[q, 4] * cell_a < 0.04: continue
                    yy, xx = np.nonzero(l2 == q); pm = to_m(xx, yy)
                    lo_, hi_ = np.percentile(pm, 1, 0), np.percentile(pm, 99, 0)
                    cen = (lo_ + hi_) / 2; a_ = float(np.mean(hi_ - lo_)) / 2
                    A = st2[q, 4] * cell_a
                    ex_ = hi_ - lo_
                    if A > 2.2 * a_ * a_ or A < 0.08 or not 0.75 < ex_[0] / ex_[1] < 1.33: continue   # not a cross
                    w_ = 2 * a_ - np.sqrt(max(4 * a_ * a_ - A, 0))
                    x0, y0, ww, hh = st2[q, :4]; pad = int(0.08 / CELL)
                    win = np.zeros_like(big); win[max(0, y0 - pad):y0 + hh + pad, max(0, x0 - pad):x0 + ww + pad] = 1
                    hsv_ = cv2.cvtColor(chart, cv2.COLOR_BGR2HSV)
                    wht = (win > 0) & (gr > 185) & (hsv_[..., 1] < 50)
                    Aw = wht.sum() * cell_a
                    if Aw < 0.25 * A: continue                             # crosses have white flanks
                    w2 = 2 * a_ - np.sqrt(max(4 * a_ * a_ - A - Aw, 0))
                    blk = np.median(chart[l2 == q], 0).astype(np.float32)
                    whc = np.median(chart[wht], 0).astype(np.float32) if wht.any() else np.array([235, 235, 235], np.float32)
                    old_ = cv2.dilate(((l2 == q) | wht).astype(np.uint8), np.ones((15, 15), np.uint8))
                    clean = cv2.inpaint(clean, old_, 7, cv2.INPAINT_TELEA)
                    crosses.append((cen, a_, w_, max(w2, w_ + 0.02), blk, whc))
            # roundels: red centre found by colour, rings outward named in the spec; painted out and redrawn round
            if sf.get('roundel') is not None:
                hsv_ = cv2.cvtColor(chart, cv2.COLOR_BGR2HSV); H_, S_, V_ = hsv_[..., 0], hsv_[..., 1], hsv_[..., 2]
                HUE = {'red': ((H_ < 10) | (H_ > 168)) & (S_ > 90) & (V_ > 70),
                       'blue': (H_ > 95) & (H_ < 135) & (S_ > 45) & (V_ > 40),
                       'yellow': (H_ > 15) & (H_ < 38) & (S_ > 110) & (V_ > 120),
                       'white': (S_ < 40) & (V_ > 200)}
                avoid = ((((H_ < 14) | (H_ > 160)) & (S_ > 35)) | ((S_ < 50) & (V_ > 165))).astype(np.uint8)
                cc_, rr_ = np.meshgrid(np.arange(W), np.arange(H))
                MM = to_m(cc_.ravel(), rr_.ravel()).reshape(H, W, 2)
                red8 = cv2.morphologyEx(HUE['red'].astype(np.uint8), cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
                nlr, lr, str_, _ = cv2.connectedComponentsWithStats(red8, 8)
                # one national marking per wing face: the biggest red patch; other stray red patches are painted out
                order_ = sorted(range(1, nlr), key=lambda q: -str_[q, 4])
                for q in order_[1:]:
                    if str_[q, 4] > 40:
                        near_ = cv2.dilate((lr == q).astype(np.uint8), np.ones((25, 25), np.uint8)) > 0
                        loose = (((H_ < 14) | (H_ > 160)) & (S_ > 35)) & near_
                        pm_ = cv2.dilate(((lr == q) | loose).astype(np.uint8), np.ones((11, 11), np.uint8))
                        clean = fill_from_around(clean, pm_, avoid); painted |= pm_ > 0
                for q in order_[:1]:
                    pm = MM[lr == q]; lo_, hi_ = np.percentile(pm, 2, 0), np.percentile(pm, 98, 0); ex_ = hi_ - lo_
                    if ex_.min() < 0.15: continue
                    cen = (lo_ + hi_) / 2; rad = [float(ex_.min()) / 2]; cols = [np.median(chart[lr == q], 0)]
                    dist = np.linalg.norm(MM - cen, axis=2)
                    for name in sf['roundel']:
                        m_ = HUE[name] & (dist < rad[-1] * 3.2) & (dist > rad[-1] * 0.9)
                        if m_.sum() < 30: break
                        rad.append(min(float(np.percentile(dist[m_], 97)), rad[-1] * 1.25 + 0.02)); cols.append(np.median(chart[m_], 0))
                    disc = ((dist < rad[-1] * 1.15) | (cv2.dilate((lr == q).astype(np.uint8), np.ones((9, 9), np.uint8)) > 0)).astype(np.uint8)
                    for name in sf['roundel']: disc |= (HUE[name] & (dist < rad[-1] * 3.2)).astype(np.uint8)
                    disc |= (((S_ < 50) & (V_ > 165)) & (dist < rad[-1] * 3.2)).astype(np.uint8)
                    disc = cv2.dilate(disc, np.ones((7, 7), np.uint8))
                    clean = fill_from_around(clean, disc, avoid); painted |= disc > 0
                    big[disc > 0] = 1                                     # nothing inside is panel line
                    rings.append((cen, rad[::-1], [c.astype(np.float32) for c in cols[::-1]]))
                # any other red-ish patch on this face is a stray piece of a marking (only where the spec says so)
                keep_ = np.zeros((H, W), bool)
                for cen, rads, _ in rings: keep_ |= np.linalg.norm(MM - cen, axis=2) < rads[0] * 1.2
                rest = (((H_ < 14) | (H_ > 160)) & (S_ > 35) & ~keep_).astype(np.uint8)
                rest = cv2.morphologyEx(rest, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
                if sf.get('stray_red') and rest.sum() > 20:
                    rest = cv2.dilate(rest, np.ones((9, 9), np.uint8))
                    clean = fill_from_around(clean, rest, avoid); painted |= rest > 0
            if dbg:
                v = chart.copy(); v[linec[lab]] = (255, 255, 255); v[dotc[lab]] = (0, 0, 255)
                cv2.imwrite(f"{dbg}_{sf['name']}_{side}_{'L' if sx > 0 else 'R'}.png", np.vstack([v, clean]))
            polys, dots = [], []
            # ---- strokes ----
            sk = skeletonize(linec[lab]).astype(np.uint8)
            nb8 = cv2.filter2D(sk, -1, np.ones((3, 3), np.float32), borderType=cv2.BORDER_CONSTANT) - sk
            br = sk & (nb8 <= 2).astype(np.uint8)                              # cut at the junctions
            nbr, blab = cv2.connectedComponents(br, connectivity=8)
            bys, bxs = np.nonzero(br); bid = blab[bys, bxs]; o_ = np.argsort(bid)
            bys, bxs, bid = bys[o_], bxs[o_], bid[o_]; bst = np.searchsorted(bid, np.arange(nbr + 1))
            for b in range(1, nbr):
                py, px = bys[bst[b]:bst[b + 1]], bxs[bst[b]:bst[b + 1]]
                if len(py) < 5: continue
                path = walk(py, px); pm = to_m(path[:, 1], path[:, 0])
                if np.linalg.norm(pm[-1] - pm[0]) < 0.06: continue      # spurs off a thick stroke's skeleton
                poly = pm[dp(pm, EPS)]
                # a long stroke that zigzags about one straight line is that line
                mu, d, t0, t1 = fit_segment(pm)
                res_ = (pm - mu) @ np.array([-d[1], d[0]])
                if len(poly) > 2 and t1 - t0 > 0.2 and np.sqrt(np.mean(res_ ** 2)) < 0.6 * EPS and np.abs(res_).max() < 2 * EPS:
                    poly = pm[[0, -1]]
                if len(poly) == 2:
                    mu, d, t0, t1 = fit_segment(pm); d = snap(d)
                    t = (pm - mu) @ d; poly = np.array([mu + d * t.min(), mu + d * t.max()])
                # reach back to the junction the branch was cut from
                for e, nxt in ((0, 1), (-1, -2)):
                    dv = poly[e] - poly[nxt]; poly[e] = poly[e] + dv / max(np.linalg.norm(dv), 1e-6) * 0.006
                polys.append(poly)
            # a thick freehand stroke can leave two skeleton branches side by side: keep the longer
            def samples(poly):
                out_ = []
                for k_ in range(len(poly) - 1):
                    L = np.linalg.norm(poly[k_ + 1] - poly[k_]); n_ = max(2, int(L / 0.01))
                    out_.append(poly[k_] + (poly[k_ + 1] - poly[k_]) * np.linspace(0, 1, n_)[:, None])
                return np.concatenate(out_)
            def seg_dist(q, poly):
                best = np.full(len(q), 9.0)
                for k_ in range(len(poly) - 1):
                    p0, p1 = poly[k_], poly[k_ + 1]; L = max(np.linalg.norm(p1 - p0), 1e-6); d = (p1 - p0) / L
                    al = np.clip((q - p0) @ d, 0, L); best = np.minimum(best, np.linalg.norm(q - (p0 + al[:, None] * d), axis=1))
                return best
            kept = []
            for poly in sorted(polys, key=lambda q: -np.sum(np.linalg.norm(np.diff(q, axis=0), axis=1))):
                q = samples(poly); near = np.zeros(len(q), bool)
                for kp in kept:
                    if np.all(poly.min(0) > kp.max(0) + 0.03) or np.all(poly.max(0) < kp.min(0) - 0.03): continue
                    near |= seg_dist(q, kp) < 0.028
                if near.mean() < 0.6: kept.append(poly)
            polys = kept
            # ---- rivet rows: RANSAC lines through the dots, evenly re-spaced ----
            di = np.nonzero(dotc)[0]
            cm = to_m(st[di, 0] + st[di, 2] / 2 - 0.5, st[di, 1] + st[di, 3] / 2 - 0.5) if len(di) else np.zeros((0, 2))
            left = np.ones(len(di), bool); rng = np.random.default_rng(1)
            for _ in range(800):
                li = np.nonzero(left)[0]
                if len(li) < 4: break
                i, j = rng.choice(li, 2, replace=False)
                dv = cm[j] - cm[i]; L = np.linalg.norm(dv)
                if L < 0.02 or L > 0.4: continue
                d = dv / L; nrm = np.array([-d[1], d[0]])
                inl = li[np.abs((cm[li] - cm[i]) @ nrm) < 0.015]
                if len(inl) < 4: continue
                al = (cm[inl] - cm[i]) @ d; oo = np.argsort(al); al = al[oo]; inl = inl[oo]
                gaps = np.diff(al); medg = np.median(gaps)
                brk = np.nonzero(gaps > 2.5 * medg + 0.01)[0]
                run = max(np.split(np.arange(len(al)), brk + 1), key=len)
                if len(run) < 4 or medg > 0.15: continue
                inl = inl[run]
                mu, d2, _, _ = fit_segment(cm[inl]); d2 = snap(d2)
                t = (cm[inl] - mu) @ d2
                for tt in np.linspace(t.min(), t.max(), len(inl)): dots.append(mu + d2 * tt)
                left[inl] = False; stats['rivet rows'] += 1
            # stray dots (not in a row) stay as they were: paint them back over the clean skin
            stray = np.isin(lab, di[left]) if len(di) else np.zeros_like(ink, bool)
            stray &= ~painted
            clean[stray] = chart[stray]
            # ---- write back: skin from the chart, new ink drawn from each texel's true position ----
            skin = remap_big(clean.astype(np.float32), cf.astype(np.float32), rf.astype(np.float32))
            pm_t = np.stack([s, Y[ty, tx]], -1)
            a = np.zeros(len(ty), np.float32); tmt = tm[ty, tx]
            for poly in polys:
                for k_ in range(len(poly) - 1):
                    p0, p1 = poly[k_], poly[k_ + 1]
                    L = np.linalg.norm(p1 - p0)
                    if L < 1e-4: continue
                    d = (p1 - p0) / L; nrm = np.array([-d[1], d[0]])
                    lo = np.minimum(p0, p1) - 0.03; hi = np.maximum(p0, p1) + 0.03
                    m = np.nonzero((pm_t[:, 0] > lo[0]) & (pm_t[:, 0] < hi[0]) & (pm_t[:, 1] > lo[1]) & (pm_t[:, 1] < hi[1]))[0]
                    q = pm_t[m] - p0; al = q @ d
                    dd = np.hypot(q @ nrm, np.maximum(0, np.maximum(-al, al - L)))
                    a[m] = np.maximum(a[m], np.clip(0.5 - (dd - LW / 2) / tmt[m], 0, 1))
                stats['strokes'] += 1
            for p0 in dots:
                m = np.nonzero((np.abs(pm_t[:, 0] - p0[0]) < 0.02) & (np.abs(pm_t[:, 1] - p0[1]) < 0.02))[0]
                dd = np.hypot(*(pm_t[m] - p0).T)
                a[m] = np.maximum(a[m], 0.85 * np.clip(0.5 - (dd - RIV) / tmt[m], 0, 1))
            inkcol = np.median(chart[linec[lab]], 0).astype(np.float32) if linec[lab].any() else np.array([30, 30, 30], np.float32)
            inkcol = np.minimum(inkcol, 45)
            for cen, a_, w_, w2, blk, whc in crosses:
                u = pm_t - cen
                def cross_sd(hw):
                    def box(hx, hy):
                        qx = np.abs(u[:, 0]) - hx; qy = np.abs(u[:, 1]) - hy
                        return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0)
                    return np.minimum(box(a_, hw), box(hw, a_))
                for hw, colr in ((w2 / 2, whc), (w_ / 2, blk)):
                    al_ = np.clip(0.5 - cross_sd(hw) / tmt, 0, 1)[:, None]
                    skin = skin * (1 - al_) + colr * al_
            for cen, rads, cols in rings:
                all_rings.append((sx, cen, rads[0]))
                dd = np.linalg.norm(pm_t - cen, axis=1)
                for r_, colr in zip(rads, cols):
                    al_ = np.clip(0.5 - (dd - r_) / tmt, 0, 1)[:, None]
                    skin = skin * (1 - al_) + colr * al_
                a[dd < rads[0] + 0.01] = 0
            img[ty, tx] = skin * (1 - a[:, None]) + inkcol * a[:, None]

    # last sweep, in the texture itself: red paint left on this surface outside every redrawn marking
    # (texels the model shares between faces, or slivers no face's chart claimed)
    if sf.get('stray_red'):
        hsv_t = cv2.cvtColor(np.clip(img, 0, 255).astype(np.uint8), cv2.COLOR_BGR2HSV)
        redt = base & (((hsv_t[..., 0] < 14) | (hsv_t[..., 0] > 160)) & (hsv_t[..., 1] > 35))
        for sx_, cen, r0 in all_rings:
            redt &= ~((X * sx_ > 0) & (np.hypot(AX - cen[0], Y - cen[1]) < r0 * 1.2))
        if redt.sum() > 20:
            mk = cv2.dilate(redt.astype(np.uint8), np.ones((5, 5), np.uint8))
            img = cv2.inpaint(np.clip(img, 0, 255).astype(np.uint8), mk, 9, cv2.INPAINT_TELEA).astype(np.float32)
            print(sf['name'], 'stray red texels', int(redt.sum()))

# seams: push the islands' colours outward so mipmaps don't pull in old paint
outimg = np.clip(img, 0, 255)
m = cov.astype(np.float32); filled = outimg * m[..., None]; cur = m.copy()
for _ in range(24):
    if cur.min() > 0: break
    s_ = cv2.blur(filled, (3, 3)); c_ = cv2.blur(cur, (3, 3)); new = (cur == 0) & (c_ > 0)
    filled[new] = s_[new] / c_[new][:, None]; cur[new] = 1
res = np.where(cur[..., None] > 0, filled, outimg).astype(np.uint8)
if spec.get('keep'):
    x0, y0, w, h = spec['keep']; res[y0:y0 + h, x0:x0 + w] = tex[y0:y0 + h, x0:x0 + w]
cv2.imwrite(out, res)
print(stats, '->', out)
