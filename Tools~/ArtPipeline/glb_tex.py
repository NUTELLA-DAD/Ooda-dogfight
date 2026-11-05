# Maps for a Hi3D GLB (saved by glb_prep.py as Image, the base colour, and Image_001, glTF's packed metallic-roughness:
# G roughness, B metal), all 8K: the base colour at <base px> and the Unity metallic-smoothness map at half that
# (R metal, A 1 - roughness), the metal scaled by <metal> (the Zero's paint at a third, like the P-38's; pilots 1).
# Run: python -I glb_tex.py <texture folder> <out folder> <Name> <base px> <metal>
import cv2, numpy as np, sys
src, out, name, R, k = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), float(sys.argv[5])
b = cv2.imread(src + '/Image.png')
cv2.imwrite(out + f'/{name}_BaseColor.png', cv2.resize(b, (R, R), interpolation=cv2.INTER_AREA))
h = R // 2
mr = cv2.resize(cv2.imread(src + '/Image_001.png'), (h, h), interpolation=cv2.INTER_AREA)   # BGR: B metal, G rough
ms = np.zeros((h, h, 4), np.uint8); ms[..., 2] = (mr[..., 0] * k).astype(np.uint8); ms[..., 3] = 255 - mr[..., 1]   # BGRA
cv2.imwrite(out + f'/{name}_MetallicSmoothness.png', ms)
print(name, 'metal mean', ms[..., 2].mean(), 'smooth mean', ms[..., 3].mean())
