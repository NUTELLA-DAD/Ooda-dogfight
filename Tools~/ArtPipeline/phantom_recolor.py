# New paint jobs for the Phantom, repainted from its SEA camouflage. JetTestBuilder.Paint.cs bakes that camo onto the
# Hi3D texture as  out = lerp(orig, colour * shade, p),  with shade = clamp(lum(orig) / 0.8, 0.45, 1.1) and
# p = smoothstep(0.36..0.55 of lum(orig)) (the old texture's dark parts, nozzles and intakes, keep their own colours).
# So with the old texture beside it, each texel's camo colour is recovered exactly, as a blend of at most two of the
# four (tan, green, dark green, the belly's light grey: the sprayed edges between them), and repainted with the new
# scheme's colours in the same blend, shade and coverage. The canopy glass is left as it is. Each paint job is a
# Phantom_<name>_BaseColor.jpg; they share the camo's gloss map (JetTestBuilder.AddLiveries).
# Run: python -I phantom_recolor.py <orig BaseColor> <Camo_BaseColor> <out dir> <spec.json>
#   spec: {"schemes": {"Navy": {"tan": [r,g,b], "green": [..], "dark": [..], "belly": [..]}, ...}}
import sys, json, itertools, numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
ORIG, BASE, OUT, SPECF = sys.argv[1:5]
SP = json.load(open(SPECF))
rd = lambda p: np.asarray(Image.open(p).convert('RGB')).astype(np.float32) / 255.0
orig, img = rd(ORIG), rd(BASE)
assert orig.shape == img.shape, (orig.shape, img.shape)
W = np.array([0.299, 0.587, 0.114], np.float32)
REF = {'tan': (0.56, 0.45, 0.31), 'green': (0.31, 0.37, 0.21), 'dark': (0.17, 0.22, 0.14), 'belly': (0.80, 0.80, 0.76)}
names = list(REF); ref = np.array([REF[n] for n in names], np.float32)

lo_ = orig @ W
x = np.clip((lo_ - 0.36) / (0.55 - 0.36), 0, 1); p = x * x * (3 - 2 * x)
shade = np.clip(lo_ / 0.8, 0.45, 1.1)
r, g, b = orig[..., 0], orig[..., 1], orig[..., 2]
glass = (b > r + 0.2) & (b > g + 0.1)
ok = (p > 0.08) & ~glass
camo = np.where(ok[..., None], (img - (1 - p[..., None]) * orig) / np.maximum(p, 0.08)[..., None], 0)
base = camo / shade[..., None]

# the best blend of two colours at each texel
bestres = np.full(lo_.shape, 9.0, np.float32); bi = np.zeros(lo_.shape, np.int8); bj = np.zeros_like(bi); bt = np.zeros(lo_.shape, np.float32)
for i, j in itertools.combinations(range(4), 2):
    d = ref[j] - ref[i]
    t = np.clip(((base - ref[i]) @ d) / (d @ d), 0, 1)
    res = np.linalg.norm(base - (ref[i] + t[..., None] * d), axis=-1)
    better = res < bestres
    bestres[better] = res[better]; bi[better] = i; bj[better] = j; bt[better] = t[better]
use = ok & (bestres < 0.08)
print('repainted share', round(float(use.mean()), 3), 'of texels;', 'by colour:',
      {n: round(float((use & (np.where(bt < 0.5, bi, bj) == k)).mean()), 3) for k, n in enumerate(names)})
for name, cols in SP['schemes'].items():
    new = np.array([cols[n] for n in names], np.float32) / 255.0
    c = new[bi] + bt[..., None] * (new[bj] - new[bi])
    out = img.copy()
    rep = orig + p[..., None] * (c * shade[..., None] - orig)
    out[use] = np.clip(rep[use], 0, 1)
    Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(f'{OUT}/Phantom_{name}_BaseColor.jpg', quality=92)
    print('painted', name)
