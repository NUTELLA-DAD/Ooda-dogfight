# Paint the flat-colour palette prop_rebuild.py points its UVs at into an unused rectangle of a base-colour atlas, and
# the matching metal/smoothness texels. Rows top to bottom: spinner, blade strip (dark to the tip band), ink, metal.
# Run: python -I palette.py <spec.json> <base in> <base out> <ms in> <ms out>
import sys, json, cv2, numpy as np
S = json.load(open(sys.argv[1])); X0, Y0, W, H = S['palette']
base = cv2.imread(sys.argv[2]); ms = cv2.imread(sys.argv[4], cv2.IMREAD_UNCHANGED)
bgr = lambda c: np.array(c[::-1], np.uint8)
C = S['colours']; rh = H // 4
base[Y0:Y0 + rh, X0:X0 + W] = bgr(C['spinner'])
strip = np.zeros((rh, W, 3), np.uint8); strip[:] = bgr(C['blade'])
xs = (np.arange(W) - 6) / (W - 12)
strip[:, xs >= S['tip_band']] = bgr(C['tip'])
strip[:, (xs >= S['tip_band'] - 0.012) & (xs < S['tip_band'])] = bgr(C['ink'])
base[Y0 + rh:Y0 + 2 * rh, X0:X0 + W] = strip
base[Y0 + 2 * rh:Y0 + 3 * rh, X0:X0 + W] = bgr(C['ink'])
base[Y0 + 3 * rh:Y0 + H, X0:X0 + W] = bgr(C['metal'])
cv2.imwrite(sys.argv[3], base)
k = ms.shape[0] / S['atlas']; x0, y0, w, rr = int(X0 * k), int(Y0 * k), int(W * k), int(rh * k)
def set_ms(i, metal, smooth): ms[y0 + i * rr:y0 + (i + 1) * rr, x0:x0 + w, 2] = metal; ms[y0 + i * rr:y0 + (i + 1) * rr, x0:x0 + w, 3] = smooth
set_ms(0, 20, 120); set_ms(1, 0, 70); set_ms(2, 0, 60); set_ms(3, 170, 150)
cv2.imwrite(sys.argv[5], ms)
print('palette painted at', X0, Y0, W, H)
