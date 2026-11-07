# Maps for a Hi3D FBX character whose metal and roughness come as separate greyscale maps (the penguin: hi3d_prep.py saves
# them as Image, the 8K base colour, Image_002, metal, and Image_003, roughness): the base colour at <base px> and the
# Unity metallic-smoothness map at half that (R metal, A 1 - roughness).
# Run: python -I hi3d_tex.py <texture folder> <out folder> <Name> <base px>
import sys, numpy as np
from PIL import Image
src, out, name, R = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
Image.open(src + '/Image.png').convert('RGB').resize((R, R), Image.LANCZOS).save(out + f'/{name}_BaseColor.png', optimize=True)
h = R // 2
metal = np.asarray(Image.open(src + '/Image_002.png').convert('L').resize((h, h), Image.LANCZOS))
rough = np.asarray(Image.open(src + '/Image_003.png').convert('L').resize((h, h), Image.LANCZOS))
ms = np.zeros((h, h, 4), np.uint8); ms[..., 0] = metal; ms[..., 3] = 255 - rough
Image.fromarray(ms, 'RGBA').save(out + f'/{name}_MetallicSmoothness.png', optimize=True)
print(name, 'metal mean', metal.mean(), 'smooth mean', ms[..., 3].mean())
