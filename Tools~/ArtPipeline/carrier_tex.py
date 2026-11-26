# The carrier's colour map, after carrier.py baked it: the same edge-preserving cleanup as the fighters' (texfix.py):
# mean shift then a bilateral pass flatten the AI paint's blotches and the bake's speckle but keep the ink lines and
# the deck's markings sharp; saved as the 4096 px JPG the game loads (Resources/Carrier_BaseColor.jpg).
# Run: python -I carrier_tex.py <Carrier_BaseColor_baked.png> <out.jpg>
import sys, cv2
src, dst = sys.argv[1], sys.argv[2]
img = cv2.imread(src)
clean = cv2.pyrMeanShiftFiltering(img, 4, 14)
clean = cv2.bilateralFilter(clean, 7, 22, 5)
cv2.imwrite(dst, clean, [cv2.IMWRITE_JPEG_QUALITY, 92])
print('wrote', dst, clean.shape)
