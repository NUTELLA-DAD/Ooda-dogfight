# New paint jobs for an aircraft whose skin is a hand-painted atlas (the F-15 and Su-27): the camouflage's colours are
# found by clustering the paint (markings, glass, nozzles and other saturated or very light or dark parts left out),
# and each cluster is repainted in a new colour with the old shading, panel lines and weathering kept (the new colour
# times the texel's brightness over its cluster's). Markings, nozzles and glass stay as they are.
# Run: python -I jet_recolor.py <BaseColor.png> <MetallicSmoothness.png> <out dir> <prefix> <spec.json>
#   spec: {"mask": {"hue": [lo, hi] or null, "sat": [lo, hi], "val": [lo, hi]}, "k": 3,
#          "schemes": {"Desert": [[r,g,b] per cluster, darkest first], ...}}
import sys, json, shutil, numpy as np
from PIL import Image
from scipy import ndimage
from scipy.cluster.vq import kmeans2
Image.MAX_IMAGE_PIXELS = None
BASE, MS, OUT, PRE, SPECF = sys.argv[1:6]
SP = json.load(open(SPECF))
img = np.asarray(Image.open(BASE).convert('RGB')).astype(np.float32) / 255.0
mx, mn = img.max(2), img.min(2); val = mx; sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)
r, g, b = img[..., 0], img[..., 1], img[..., 2]
hue = (np.degrees(np.arctan2(np.sqrt(3) * (g - b), 2 * r - g - b)) + 360) % 360
M = SP['mask']
mask = (sat >= M['sat'][0]) & (sat <= M['sat'][1]) & (val >= M['val'][0]) & (val <= M['val'][1])
if M.get('hue'):
    lo, hi = M['hue']; mask &= (hue >= lo) & (hue <= hi)
# the paint's colour at each texel, without its panel lines and grime: a blur over the paint only
w = ndimage.uniform_filter(mask.astype(np.float32), 9)
smooth = np.stack([ndimage.uniform_filter(img[..., k] * mask, 9) for k in range(3)], -1) / np.maximum(w, 1e-4)[..., None]
lum = img @ np.array([0.299, 0.587, 0.114], np.float32)
slum = smooth @ np.array([0.299, 0.587, 0.114], np.float32)
rng = np.random.default_rng(1)
sample = smooth[mask][rng.choice(int(mask.sum()), 200000, replace=False)]
cent, _ = kmeans2(sample, SP['k'], seed=1, minit='++')
order = np.argsort(cent @ np.array([0.299, 0.587, 0.114]))
cent = cent[order]
print('clusters (darkest first):', (cent * 255).round().astype(int).tolist())
lab = np.full(mask.shape, -1, np.int32)
d = np.stack([((smooth - c) ** 2).sum(-1) for c in cent], -1)
lab[mask] = np.argmin(d[mask], -1)
clum = cent @ np.array([0.299, 0.587, 0.114])
# a soft edge: texels just outside the mask (anti-aliased borders of the camouflage) follow their nearest painted texel
edge = ndimage.binary_dilation(mask, iterations=2) & ~mask & (sat < max(M['sat'][1], 0.5))
_, (iy, ix) = ndimage.distance_transform_edt(~mask, return_indices=True)
lab_e = lab.copy(); lab_e[edge] = lab[iy[edge], ix[edge]]
use = lab_e >= 0
for name, cols in SP['schemes'].items():
    cols = np.array(cols, np.float32) / 255.0
    out = img.copy()
    L = lab_e[use]
    ratio = np.clip(lum[use] / clum[L], 0.0, 1.6)[:, None]
    new = np.clip(cols[L] * ratio, 0, 1)
    f = np.where(edge[use], 0.7, 1.0)[:, None]
    out[use] = out[use] * (1 - f) + new * f
    Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(f'{OUT}/{PRE}_{name}_BaseColor.png')
    shutil.copy(MS, f'{OUT}/{PRE}_{name}_MetallicSmoothness.png')
    Image.fromarray((out * 255 + 0.5).astype(np.uint8)).resize((1024, 1024), Image.LANCZOS).save(f'{OUT}/atlas_{name}.jpg', quality=85)
    print('painted', name)
