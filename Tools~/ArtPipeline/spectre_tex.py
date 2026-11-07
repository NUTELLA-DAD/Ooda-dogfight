# Maps for the spectre (the chibi secret agent): the 4K base colour with the head darkened to a near-black shadow (the
# eyes left alone), the 2K metallic-smoothness map from Hi3D's separate metal and roughness maps, and a 2K emission map:
# the eyes glowing cyan, the gloves a paler cyan. The head and gloves are found on the T-posed mesh (hi3d_scale.py's
# 1 m frame, facing -Y: the head above z 0.62, the gloves out past |x| 0.37) and painted through its UVs; the eyes by
# their colour (pale cyan) inside the head.
# Run: python -I spectre_tex.py -- <scaled work.blend> <texture folder from hi3d_prep.py> <out folder>
import bpy, sys, numpy as np, cv2
a = sys.argv[sys.argv.index('--')+1:]
WORK, TEX, OUT = a
bpy.ops.wm.open_mainfile(filepath=WORK)
m = [o for o in bpy.data.objects if o.type == 'MESH'][0]
me = m.data; uv = me.uv_layers.active.data
R = 4096
head = np.zeros((R, R), np.uint8); glove = np.zeros((R, R), np.uint8)
for p in me.polygons:
    c = p.center
    tgt = head if c.z > 0.62 else glove if abs(c.x) > 0.37 and c.z > 0.4 else None
    if tgt is None: continue
    pts = np.array([[uv[i].uv.x * R, (1 - uv[i].uv.y) * R] for i in p.loop_indices], np.int32)
    cv2.fillPoly(tgt, [pts], 255)
k = np.ones((9, 9), np.uint8)
head = cv2.dilate(head, k); glove = cv2.dilate(glove, k)   # cover the seams the UV islands leave
base = cv2.resize(cv2.imread(TEX + '/Image.png'), (R, R), interpolation=cv2.INTER_AREA)
hsv = cv2.cvtColor(base, cv2.COLOR_BGR2HSV)
eye = ((hsv[..., 0] > 80) & (hsv[..., 0] < 100) & (hsv[..., 1] > 40) & (hsv[..., 2] > 200)).astype(np.uint8) * 255
eye = cv2.dilate(eye, np.ones((5, 5), np.uint8)) & cv2.dilate(head, np.ones((61, 61), np.uint8))
print('texels: head', int((head > 0).sum()), 'gloves', int((glove > 0).sum()), 'eyes', int((eye > 0).sum()))
# the head: a deep blue-black shadow that keeps the sculpt's shading, the eyes untouched
dark = (base.astype(np.float32) * np.array([0.55, 0.32, 0.28])).astype(np.uint8)   # BGR: keep the blue
hm = cv2.GaussianBlur(((head > 0) & (eye == 0)).astype(np.float32), (7, 7), 0)[..., None]
out = (base * (1 - hm) + dark * hm).astype(np.uint8)
cv2.imwrite(OUT + '/Spectre_BaseColor.png', out)
# emission (2K): eyes near-white cyan, gloves a softer cyan, both kept from the texture's own shading
h = R // 2
em = np.zeros((R, R, 3), np.float32)
lum = base.astype(np.float32).mean(2, keepdims=True) / 255
em += (eye[..., None] > 0) * np.array([255, 255, 140]) * np.clip(lum * 1.2, 0, 1)          # BGR
em += ((glove[..., None] > 0) & (eye[..., None] == 0) & (lum > 0.45)) * np.array([255, 220, 60]) * np.clip(lum, 0, 1) * 0.75   # the pale glove, not its dark cuff
em = cv2.GaussianBlur(em, (5, 5), 0)
cv2.imwrite(OUT + '/Spectre_Emission.png', cv2.resize(np.clip(em, 0, 255).astype(np.uint8), (h, h), interpolation=cv2.INTER_AREA))
# metallic-smoothness (2K): R metal, A 1 - roughness
met = cv2.resize(cv2.imread(TEX + '/Image_002.png', cv2.IMREAD_GRAYSCALE), (h, h), interpolation=cv2.INTER_AREA)
rough = cv2.resize(cv2.imread(TEX + '/Image_003.png', cv2.IMREAD_GRAYSCALE), (h, h), interpolation=cv2.INTER_AREA)
ms = np.zeros((h, h, 4), np.uint8); ms[..., 2] = met; ms[..., 3] = 255 - rough
cv2.imwrite(OUT + '/Spectre_MetallicSmoothness.png', ms)
print('metal mean', met.mean(), 'smooth mean', (255 - rough).mean())
