# The Su-27's maps (made as f15_tex.py makes the F-15's) from the Hi3D 8K set (saved by hi3d_prep.py as Image, Image_001 normal, Image_002 metallic,
# Image_003 roughness): the base colour cleaned like the art pass (edge-preserving smoothing that keeps the ink lines,
# then a light unsharp) at 4K, and the Unity metallic-smoothness map at 2K (R metal, A 1 - roughness), the metal cut
# to a third and the gloss eased so the Flanker's blue-grey camouflage reads as paint, not chrome.
# Run: python -I f15_tex.py <texture folder> <out folder>
import cv2, numpy as np, sys
src, out = sys.argv[1], sys.argv[2]
R = 4096
b = cv2.resize(cv2.imread(src + '/Image.png'), (R, R), interpolation=cv2.INTER_AREA)
clean = cv2.pyrMeanShiftFiltering(b, 5, 18)
clean = cv2.bilateralFilter(clean, 7, 25, 5)
blur = cv2.GaussianBlur(clean, (0, 0), 1.6)
cv2.imwrite(out + '/Su27_BaseColor.png', cv2.addWeighted(clean, 1.5, blur, -0.5, 0))
R = 2048
met = cv2.resize(cv2.imread(src + '/Image_002.png', cv2.IMREAD_GRAYSCALE), (R, R), interpolation=cv2.INTER_AREA)
rough = cv2.resize(cv2.imread(src + '/Image_003.png', cv2.IMREAD_GRAYSCALE), (R, R), interpolation=cv2.INTER_AREA)
ms = np.zeros((R, R, 4), np.uint8)
ms[..., 2] = (met * 0.33).astype(np.uint8)
ms[..., 3] = ((255 - rough) * 0.8).astype(np.uint8)   # BGRA
cv2.imwrite(out + '/Su27_MetallicSmoothness.png', ms)
print('metal mean', ms[..., 2].mean(), 'smooth mean', ms[..., 3].mean())
