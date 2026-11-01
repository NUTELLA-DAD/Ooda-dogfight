# Repaint the P-51's "MUSTANG" nose bands. The generated texture split the word across UV islands, so in the game one
# side read "MUSTANE" and the other side was mirrored. Each band is redrawn in a side view of the nose (baked position
# map: every texel's place on the plane), then written back to whichever texels land in it, seams and all.
# Run: python -I nose_letters.py <BaseColor.png> <pos.npy> <out.png>
import sys, numpy as np, cv2
from PIL import Image, ImageDraw, ImageFont

src, posf, out = sys.argv[1:4]
tex = cv2.imread(src)
edited = np.zeros(tex.shape[:2], bool)
P = np.load(posf)[::-1]
cov = P[..., 3] > 0.5
Y0, Z1, PX = -4.0, 0.4, 0.002          # side view: column = (Y - Y0) / PX, row = (Z1 - z) / PX
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
RED, WHITE, BLACK = (40, 40, 200), None, (25, 25, 25)   # BGR

# the band on each side, as a quad in side-view pixels (top-left, top-right, bottom-right, bottom-left), inside its
# border lines; +X reads nose to tail as you look at it, -X is seen from the other side, so its words are mirrored here
BANDS = {+1: [(118, 140), (712, 153), (712, 268), (118, 253)],
         -1: [(118, 138), (700, 168), (700, 278), (118, 248)]}

def text_image(w, h):
    img = Image.new('RGBA', (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    size = h
    while True:
        f = ImageFont.truetype(FONT, size)
        l, t, r, b = d.textbbox((0, 0), 'MUSTANG', font=f, stroke_width=max(2, size // 14))
        if r - l <= w * 0.92 and b - t <= h * 0.80: break
        size -= 2
    sw = max(2, size // 14)
    d.text(((w - (r - l)) / 2 - l, (h - (b - t)) / 2 - t), 'MUSTANG', font=f, fill=(200, 40, 40, 255),
           stroke_width=sw, stroke_fill=(25, 25, 25, 255))
    return np.array(img)

for side, quad in BANDS.items():
    m = cov & (P[..., 0] * side > 0.35) & (P[..., 1] > Y0) & (P[..., 1] < -2.4) & (np.abs(P[..., 2]) < 0.5)
    ty, tx = np.nonzero(m)
    col = (P[ty, tx, 1] - Y0) / PX; row = (Z1 - P[ty, tx, 2]) / PX
    q = np.array(quad, np.float32)
    # texels inside the band, with a few pixels more for the border lines
    grow = np.array([[-6, -6], [6, -6], [6, 6], [-6, 6]], np.float32)
    poly = (q + grow).astype(np.int32)
    inside = np.array([cv2.pointPolygonTest(poly, (float(c), float(r)), False) >= 0 for c, r in zip(col, row)])
    ty, tx, col, row = ty[inside], tx[inside], col[inside], row[inside]
    # band white: the light pixels already in it
    px = tex[ty, tx].astype(int); lum = px.sum(1) / 3; sat = px.max(1) - px.min(1)
    white = np.median(px[(lum > 160) & (sat < 30)], axis=0)
    # the new band, drawn in side view
    W, H = 900, 450
    view = np.zeros((H, W, 3), np.uint8); view[:] = white
    for a, b in ((0, 1), (3, 2)):    # border lines along the top and bottom
        cv2.line(view, tuple(int(v) for v in q[a] + (0, -4 if a == 0 else 4)), tuple(int(v) for v in q[b] + (0, -4 if a == 0 else 4)), BLACK, 5, cv2.LINE_AA)
    tw, th = 1200, 230
    txt = text_image(tw, th)
    if side < 0: txt = txt[:, ::-1].copy()
    M = cv2.getPerspectiveTransform(np.float32([[0, 0], [tw, 0], [tw, th], [0, th]]), q)
    warped = cv2.warpPerspective(txt, M, (W, H), flags=cv2.INTER_LINEAR)
    a = warped[..., 3:4] / 255.0
    view = (view * (1 - a) + warped[..., [2, 1, 0]] * a).astype(np.uint8)
    # write back, bilinear
    c0 = np.clip(np.floor(col).astype(int), 0, W - 2); r0 = np.clip(np.floor(row).astype(int), 0, H - 2)
    fc = np.clip(col - c0, 0, 1)[:, None]; fr = np.clip(row - r0, 0, 1)[:, None]
    v = view.astype(np.float32)
    new = ((v[r0, c0] * (1 - fc) + v[r0, c0 + 1] * fc) * (1 - fr) + (v[r0 + 1, c0] * (1 - fc) + v[r0 + 1, c0 + 1] * fc) * fr).astype(np.uint8)
    tex[ty, tx] = new; edited[ty, tx] = True
    print('side', side, 'texels', len(ty), 'white', white)

# re-pad the edited islands into the gutter so mip-maps and filtering don't pull in the old letters
gut = ~cov & cv2.dilate(edited.astype(np.uint8), np.ones((21, 21), np.uint8)).astype(bool)
done = np.zeros_like(gut)
for _ in range(10):
    src_ok = cov | (~cov & ~gut & ~cv2.dilate(edited.astype(np.uint8), np.ones((21, 21), np.uint8)).astype(bool)) | done
    w = cv2.blur(src_ok.astype(np.float32), (3, 3)); grow = gut & (w > 0)
    acc = cv2.blur(tex.astype(np.float32) * src_ok[..., None], (3, 3)) / np.maximum(w, 1e-6)[..., None]
    tex[grow] = acc[grow].clip(0, 255).astype(np.uint8)
    gut = gut & ~grow; done |= grow
cv2.imwrite(out, tex)
print('wrote', out)
