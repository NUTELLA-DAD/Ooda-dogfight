# The P-38's maps from the Hi3D 8K set (saved by hi3d_prep.py as Image, Image_001 normal, Image_002 metallic,
# Image_003 roughness): base colour at 4K, and the Unity metallic-smoothness map at 2K (R metal, A 1 - roughness),
# the metal cut to about a third so the olive drab reads as paint, not brass, like the Bf 109's.
# Run: python -I p38_tex.py <texture folder> <out folder>
import cv2, numpy as np, sys
src, out = sys.argv[1], sys.argv[2]
b = cv2.imread(src + '/Image.png')
cv2.imwrite(out + '/P38_BaseColor.png', cv2.resize(b, (4096, 4096), interpolation=cv2.INTER_AREA))
R = 2048
met = cv2.resize(cv2.imread(src + '/Image_002.png', cv2.IMREAD_GRAYSCALE), (R, R), interpolation=cv2.INTER_AREA)
rough = cv2.resize(cv2.imread(src + '/Image_003.png', cv2.IMREAD_GRAYSCALE), (R, R), interpolation=cv2.INTER_AREA)
ms = np.zeros((R, R, 4), np.uint8); ms[..., 2] = (met * 0.35).astype(np.uint8); ms[..., 3] = 255 - rough   # BGRA
cv2.imwrite(out + '/P38_MetallicSmoothness.png', ms)
