# Repaint the P-51's markings crisp and straight: the invasion stripes on the wings, the tailplane and the rear fuselage,
# the star-and-bar insignia on the wings and the fuselage, and the fuselage code letters. The generated texture drew them
# freehand, so the stripes wandered and the stars and letters were lumpy. Everything is drawn here as exact shapes in
# a flat view of the part (each texel's place on the plane comes from bakepos.py), then written to whichever texels
# land in it. Old paint inside each area goes back to bare metal first, keeping the thin panel lines.
# Also thins the heavy outline on the fin.
# Run: python -I p51_markings.py <BaseColor in> <pos.npy (bakepos.py, same size)> <BaseColor out> [palette x,y,w,h kept]
import sys, numpy as np, cv2
from PIL import Image, ImageDraw, ImageFont

src, posf, out = sys.argv[1:4]
keep = [int(v) for v in sys.argv[4].split(',')] if len(sys.argv) > 4 else None
tex = cv2.imread(src); R = tex.shape[0]
P = np.load(posf)[::-1]; cov = P[..., 3] > 0.5
X, Y, Z = P[..., 0], P[..., 1], P[..., 2]
AX = np.abs(X)
# metres per texel, for anti-aliasing and the size of the rasters
gy, gx = np.gradient(P[..., :3], axis=(0, 1))
tm = np.where(cov, np.maximum(np.linalg.norm(gx, axis=2), np.linalg.norm(gy, axis=2)), 0)
tm = np.clip(tm, 0.0005, 0.02)
gray = cv2.cvtColor(tex, cv2.COLOR_BGR2GRAY)
bh = cv2.morphologyEx(gray, cv2.MORPH_BLACKHAT, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (11, 11)))
thin = (bh > 50) & (gray < 120)
blob = cv2.morphologyEx((gray < 90).astype(np.uint8), cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (13, 13))) > 0
lines = thin & ~blob                        # panel lines, not the dark paint areas
img = tex.astype(np.float32)
METAL = np.median(tex[cov & (gray > 170) & (AX < 0.7) & (Y > -1) & (Y < 2)], 0).astype(np.float32)
WHITE = np.array([236, 238, 238], np.float32); BLACK = np.array([34, 32, 30], np.float32)
NAVY = np.array([92, 46, 22], np.float32)  # BGR, the deep insignia blue
INK = np.array([28, 26, 26], np.float32)

def to_metal(sel):
    """Old paint in sel goes to bare metal; the thin panel lines stay."""
    img[sel] = METAL
    l = sel & lines
    img[l] = tex[l]

# ---------------- clear the old markings ----------------
wing = cov & (AX > 0.6) & (AX < 3.15) & (Y > -2.75) & (Y < -0.05) & (Z > -0.8) & (Z < 0.15) & ~((Y < -2.1) & (Z < -0.42))
tail = cov & (AX > 0.22) & (AX < 2.4) & (Y > 2.7) & (Y < 4.5) & (Z > -0.4) & (Z < 0.32)
belly = cov & (AX < 0.75) & (Y > 0.1) & (Y < 1.9) & (Z < -0.38)
side = cov & (AX > 0.25) & (AX < 0.8)
ins_fus = side & (Y > -0.1) & (Y < 1.85) & (Z > -0.8) & (Z < 0.32)
letters = side & (((Y > -0.85) & (Y < 0.35)) | ((Y > 1.6) & (Y < 2.1))) & (Z > -0.62) & (Z < 0.11)
ins_wing = cov & (AX > 3.1) & (AX < 5.3) & (Y > -2.35) & (Y < -0.85)
for s in (wing, tail, belly, ins_fus, letters, ins_wing):
    to_metal(s)

# ---------------- invasion stripes ----------------
def stripes(sel, coord, edges, first_white=True):
    for i in range(len(edges) - 1):
        lo, hi = edges[i], edges[i + 1]
        col = WHITE if (i % 2 == 0) == first_white else BLACK
        # coverage across the edge, so the boundary is a clean anti-aliased line
        a = np.clip(np.minimum(coord - lo, hi - coord) / tm + 0.5, 0, 1)
        a[~sel] = 0
        img[:] = img * (1 - a[..., None]) + col * a[..., None]
stripes(wing, AX, [0.62, 1.02, 1.42, 1.82, 2.22, 2.62])
stripes(tail, AX, [0.95, 1.25], first_white=False)
# round the lower rear fuselage, below the insignia line on each side
stripes(belly, Y, [0.20, 0.52, 0.84, 1.16, 1.48, 1.80])

# ---------------- star-and-bar insignia ----------------
def insignia_raster(n=1200):
    """RGBA raster of the insignia, r = n/4.4 pixels, centre at (n/2, n/2). Up = the star's top point."""
    s = 4; N = n * s; r = N / 4.4; c = N / 2
    im = np.zeros((N, N, 4), np.uint8)
    def rect(hw, hh, dy, col):
        cv2.rectangle(im, (int(c - hw * r), int(c - (dy + hh) * r)), (int(c + hw * r), int(c - (dy - hh) * r)), col, -1)
    rect(1.99, 0.54, 0.05, (*NAVY.astype(int).tolist(), 255))
    cv2.circle(im, (int(c), int(c)), int(r), (*NAVY.astype(int).tolist(), 255), -1)
    bar = np.zeros_like(im); cv2.rectangle(bar, (int(c - 1.85 * r), int(c - 0.45 * r)), (int(c + 1.85 * r), int(c + 0.35 * r)), (*WHITE.astype(int).tolist(), 255), -1)
    circ = np.zeros(im.shape[:2], np.uint8); cv2.circle(circ, (int(c), int(c)), int(r), 1, -1)
    bar[circ > 0] = 0; im[bar[..., 3] > 0] = bar[bar[..., 3] > 0]
    pts = []
    for k in range(10):
        a = np.pi / 2 + k * np.pi / 5; rr = 0.97 * r if k % 2 == 0 else 0.97 * r * 0.382
        pts.append((c + rr * np.cos(a), c - rr * np.sin(a)))
    cv2.fillPoly(im, [np.array(pts, np.int32)], (*WHITE.astype(int).tolist(), 255))
    return cv2.resize(im, (n, n), interpolation=cv2.INTER_AREA), n / 4.4

def stamp(sel, u, v, cu, cv_, r, raster):
    ras, rpx = raster
    n = ras.shape[0]
    px = (u - cu) / r * rpx + n / 2; py = n / 2 - (v - cv_) / r * rpx
    ok = sel & (px >= 0) & (px < n - 1) & (py >= 0) & (py < n - 1)
    rgba = cv2.remap(ras, px.astype(np.float32), py.astype(np.float32), cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
    a = rgba[..., 3:4].astype(np.float32) / 255 * ok[..., None]
    img[:] = img * (1 - a) + rgba[..., :3].astype(np.float32) * a

INS = insignia_raster()
# upper or lower skin: above or below the wing's mid-surface at that spot (the wing has dihedral, so no fixed height works)
g = 0.03; key = (np.round(X / g).astype(np.int64) * 100003 + np.round(Y / g).astype(np.int64))[cov]
zc = Z[cov]; order = np.argsort(key); ks, zs = key[order], zc[order]
uniq, start = np.unique(ks, return_index=True)
zmax = np.maximum.reduceat(zs, start); zmin = np.minimum.reduceat(zs, start)
mid = np.empty_like(zc); mid[order] = np.repeat((zmax + zmin) / 2, np.diff(np.append(start, len(ks))))
MID = np.full(Z.shape, np.nan, np.float32); MID[cov] = mid
up_wing = cov & (Z >= MID); low_wing = cov & (Z < MID)
stamp(up_wing & (X > 3.0), X, -Y, 4.18, 1.60, 0.46, INS)                 # upper left wing (the pilot's left is +X)
for sx in (1, -1):
    stamp(low_wing & (X * sx > 3.0), X, -Y, 4.15 * sx, 1.58, 0.46, INS)  # both lower wings, as the cartoon had them
for sx in (1, -1):
    stamp(side & (X * sx > 0) & (Y > -0.3) & (Y < 2.0), Y, Z, 0.85, -0.225, 0.45, INS)

# ---------------- fuselage codes ----------------
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
def text_raster(text, w_m, h_m, ppm=900):
    W, H = int(w_m * ppm), int(h_m * ppm)
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    size = H * 2
    while size > 8:
        f = ImageFont.truetype(FONT, size); l, t, r_, b = d.textbbox((0, 0), text, font=f)
        if r_ - l <= W and b - t <= H: break
        size -= 2
    # stretch the glyphs to fill the box height: block capitals, as the squadrons painted them
    tmp = Image.new('RGBA', (r_ - l, b - t), (0, 0, 0, 0)); ImageDraw.Draw(tmp).text((-l, -t), text, font=f, fill=(*INK.astype(int).tolist(), 255))
    tmp = tmp.resize((min(W, int((r_ - l) * H / (b - t))), H), Image.LANCZOS)
    im.paste(tmp, ((W - tmp.width) // 2, 0), tmp)
    return np.array(im), ppm
def put_text(sel, h, v, h0, v0, ras):
    ras, ppm = ras; Hh, Ww = ras.shape[:2]
    px = (h - h0) * ppm; py = (v0 - v) * ppm
    ok = sel & (px >= 0) & (px < Ww - 1) & (py >= 0) & (py < Hh - 1)
    rgba = cv2.remap(ras, px.astype(np.float32), py.astype(np.float32), cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT)
    a = rgba[..., 3:4].astype(np.float32) / 255 * ok[..., None]
    img[:] = img * (1 - a) + rgba[..., :3].astype(np.float32) * a
# squadron code forward of the star, the aircraft's letter behind it, reading normally on both sides
LH = text_raster('LH', 0.74, 0.42); I_ = text_raster('I', 0.16, 0.42)
for sx in (1, -1):
    s = side & (X * sx > 0)
    hh = Y if sx > 0 else -Y          # the right side is seen with the nose to the left, the left side the other way
    put_text(s & (Y > -0.8) & (Y < 0.3), hh, Z, -0.80 if sx > 0 else 0.06, 0.06, LH)
    put_text(s & (Y > 1.6) & (Y < 2.1), hh, Z, 1.78 if sx > 0 else -1.94, 0.06, I_)

# ---------------- tail number: one clean serial across fin and rudder, reading the right way on both sides --------
# (the generated art mirrored it on the left and split it over a white patch on the rudder)
finside = cov & (AX < 0.13) & (Y > 2.85) & (Y < 4.2) & (Z > -0.02) & (Z < 0.62)
hsv = cv2.cvtColor(tex, cv2.COLOR_BGR2HSV)
RED = np.median(tex[cov & (AX < 0.13) & (Y > 3.6) & (Z > 0.6) & (hsv[..., 1] > 150) & ((hsv[..., 0] < 8) | (hsv[..., 0] > 172))], 0).astype(np.float32)
HINGE = 3.56
img[finside & (Y < HINGE)] = METAL     # bare fin, the old stencils with it
img[finside & (Y >= HINGE)] = RED
a = np.clip(0.5 - (np.abs(Y - HINGE) - 0.006) / tm, 0, 1) * finside
img[:] = img * (1 - a[..., None]) + INK * a[..., None]
SER = text_raster('472216', 1.04, 0.22)
for sx in (1, -1):
    put_text(finside & (X * sx > 0), Y if sx > 0 else -Y, Z, 2.98 if sx > 0 else -4.02, 0.37, SER)

# ---------------- the fin's outline: thinner ----------------
fin = cov & (Y > 2.55) & (Z > 0.38) & (AX < 0.13)
dark = (cv2.cvtColor(np.clip(img, 0, 255).astype(np.uint8), cv2.COLOR_BGR2GRAY) < 70) & fin & ~finside
thinner = cv2.erode(dark.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (25, 25))) > 0
gone = dark & ~thinner
if gone.any():
    # fill from the paint beside the line (red rudder, metal fin), not flat metal
    fill = cv2.inpaint(np.clip(img, 0, 255).astype(np.uint8), gone.astype(np.uint8), 3, cv2.INPAINT_TELEA)
    img[gone] = fill[gone]

# ---------------- seams: push the islands' colours outward so mipmaps don't pull in old paint ----------------
outimg = np.clip(img, 0, 255)
m = cov.astype(np.float32); filled = outimg * m[..., None]; cur = m.copy()
for _ in range(24):
    if cur.min() > 0: break
    s = cv2.blur(filled, (3, 3)); c = cv2.blur(cur, (3, 3)); new = (cur == 0) & (c > 0)
    filled[new] = s[new] / c[new][:, None]; cur[new] = 1
res = np.where(cur[..., None] > 0, filled, outimg).astype(np.uint8)
if keep:
    x0, y0, w, h = keep; res[y0:y0 + h, x0:x0 + w] = tex[y0:y0 + h, x0:x0 + w]
cv2.imwrite(out, res)
print('markings repainted ->', out)
