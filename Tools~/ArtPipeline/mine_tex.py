# The balloon mine's colour map: Hi3D's 8K base colour has its facets' shading baked in as blotches (the balloon reads as
# crumpled foil). At 2K, each plain painted surface (the red balloon cloth, the grey mine casing) is smoothed on its own:
# a blur that only averages pixels of the same surface, so the black seams, the brass bands and the rivets stay crisp.
# Run: python -I mine_tex.py <texture folder (hi3d_prep.py's, Image.png)> <out folder>
import cv2, sys, numpy as np
src, out = sys.argv[1], sys.argv[2]
b = cv2.resize(cv2.imread(src + '/Image.png'), (2048, 2048), interpolation=cv2.INTER_AREA).astype(np.float32)
hsv = cv2.cvtColor(b.astype(np.uint8), cv2.COLOR_BGR2HSV).astype(np.float32)
h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
red = ((h < 8) | (h > 170)) & (s > 70) & (v > 90)
grey = (s < 40) & (v > 60) & (v < 175)
def smooth(img, mask, sigma):
    m = cv2.erode(mask.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(np.float32)
    num = cv2.GaussianBlur(img * m[..., None], (0, 0), sigma)
    den = cv2.GaussianBlur(m, (0, 0), sigma)[..., None]
    o = img.copy(); sel = m > 0
    o[sel] = (num / np.maximum(den, 1e-4))[sel]
    return o
b = smooth(b, red, 22)
b = smooth(b, grey, 5)
print('red px', int(red.sum()), 'grey px', int(grey.sum()))
cv2.imwrite(out + '/Mine_BaseColor.jpg', np.clip(b, 0, 255).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 92])
