# The Hi3D aircraft carrier (after hi3d_prep.py with 0 triangles, which keeps all 5 million): cleaned, scaled, cut to a
# near and a far mesh, and its paint baked onto a fresh UV layout, as OBJs for Resources (Y up, metres; the waterline at
# 0, the bow toward Unity's +z). carrier_tex.py then cleans the baked colour map.
#   - welded along Hi3D's UV seams (see below)
#   - the crumbs go: loose pieces under 30 vertices (flecks floating off the hull and the island)
#   - 240 m long over the deck (the model's own build: 77 m across the sponsons, 62 m to the mast top), centred on its
#     length and beam, sunk 2.2 m so the red boot topping shows as a stripe at the waterline
#   - near 90k triangles, far as few as the collapse will go; flat shaded, as the toon shader wants
#   - Hi3D's UVs are many small, stretched charts: collapsing the mesh with them tears the deck's painted stripes into
#     zigzags. So the cut mesh gets its own UVs (below) and the full model's base colour is baked onto it
#     (Cycles, selected to active, the colour only, metal off so metal parts aren't darkened).
# Run: python -I carrier.py -- <carrier_raw.blend> <out folder> [bake px, 4096]
import bpy, bmesh, sys, math, numpy as np
a = sys.argv[sys.argv.index('--')+1:]
src, out = a[0], a[1]
R = int(a[2]) if len(a) > 2 else 4096
LENGTH, DRAUGHT, NEAR, FAR = 240.0, 2.2, 90000, 9000
DECK_LO, DECK_HI = 21.0, 27.5      # the flight deck's height band once sunk (it lies at about 25.5 m)

bpy.ops.wm.open_mainfile(filepath=src)
sc = bpy.context.scene
hi = [o for o in bpy.data.objects if o.type == 'MESH'][0]; hi.name = 'CarrierHigh'

bm = bmesh.new(); bm.from_mesh(hi.data)
bm.verts.ensure_lookup_table()
seen = set(); crumbs = []
for v in bm.verts:
    if v.index in seen: continue
    part, st = [], [v]; seen.add(v.index)
    while st:
        x = st.pop(); part.append(x)
        for e in x.link_edges:
            o = e.other_vert(x)
            if o.index not in seen: seen.add(o.index); st.append(o)
    if len(part) < 30: crumbs += part
bmesh.ops.delete(bm, geom=crumbs, context='VERTS')
print('crumbs removed', len(crumbs), 'vertices')
co = np.array([v.co[:] for v in bm.verts], np.float64)
lo, top = co.min(0), co.max(0)
k = LENGTH / (top[1] - lo[1])
for v in bm.verts:
    v.co.x = (v.co.x - (lo[0] + top[0]) / 2) * k
    v.co.y = (v.co.y - (lo[1] + top[1]) / 2) * k     # the bow is the narrow -y end: Unity's +z once exported
    v.co.z = (v.co.z - lo[2]) * k - DRAUGHT
# Hi3D splits the mesh along every UV seam (each chart its own loose piece): weld it whole, so the collapse can work
# across the seams and the fresh UVs can lay out big charts (the UVs live on the corners, so the source's survive)
n0 = len(bm.verts); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.004); print('welded', n0 - len(bm.verts), 'vertices')
bm.to_mesh(hi.data); bm.free(); hi.data.update()
print('size m', [round((top[i] - lo[i]) * k, 1) for i in range(3)], 'tris', len(hi.data.polygons))

# the source's colour only: metal off, so the bake's diffuse colour is the paint itself
mat = hi.data.materials[0]
bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
for name in ('Metallic', 'Roughness', 'Normal'):
    for l in list(bsdf.inputs[name].links): mat.node_tree.links.remove(l)
bsdf.inputs['Metallic'].default_value = 0

def only(o):
    for x in sc.objects: x.select_set(x == o)
    bpy.context.view_layer.objects.active = o

def decimate(o, tris):
    only(o)
    d = o.modifiers.new('dec', 'DECIMATE'); d.ratio = tris / len(o.data.polygons); d.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier='dec')
    print(o.name, 'decimated to', len(o.data.polygons))

# the cut mesh, with its own UVs
low = hi.copy(); low.data = hi.data.copy(); low.name = 'Carrier'; sc.collection.objects.link(low)
decimate(low, NEAR)
for p in low.data.polygons: p.use_smooth = False
while low.data.uv_layers: low.data.uv_layers.remove(low.data.uv_layers[0])
low.data.uv_layers.new(name='UV')
# The faces a pilot sees most get their own straight projections, each a band of the map: the flight deck from above
# (the top 30%, about 17 px a metre) and each side of the hull up to the deck (17% each). Everything else is smart-
# projected into the bottom third: the model's hidden decks and fittings make thousands of small charts, which left
# to share the whole map shrank the deck to 3 px a metre. A face with more of the ship straight in front of it (a
# hangar floor under the deck, the far wall of an opening) is left to the smart projection, or it would land on the
# same pixels as the face that hides it.
from mathutils.bvhtree import BVHTree
from mathutils import Vector
tree = BVHTree.FromObject(low, bpy.context.evaluated_depsgraph_get())
def clear(p, d):
    return tree.ray_cast(p.center + d * 0.05, d, 80.0)[0] is None
UP, PORT, STBD = Vector((0, 0, 1)), Vector((1, 0, 0)), Vector((-1, 0, 0))
band = {}
for p in low.data.polygons:
    n, c = p.normal, p.center
    if n.z > 0.85 and DECK_LO < c.z < DECK_HI and clear(p, UP): band[p.index] = 'deck'
    elif abs(n.x) > 0.75 and c.z < DECK_HI and clear(p, PORT if n.x > 0 else STBD): band[p.index] = 'port' if n.x > 0 else 'stbd'
print('planar faces', {k: sum(1 for v in band.values() if v == k) for k in ('deck', 'port', 'stbd')})
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
for p in low.data.polygons: p.select = p.index not in band
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, area_weight=0.0, correct_aspect=True, scale_to_bounds=True)
bpy.ops.object.mode_set(mode='OBJECT')
uv = low.data.uv_layers['UV'].data
U = lambda y: (y + LENGTH / 2 + 1) / (LENGTH + 2)
SIDE = lambda z: (z + DRAUGHT + 0.5) / (DECK_HI + DRAUGHT + 1)
for p in low.data.polygons:
    k = band.get(p.index)
    for li, vi in zip(p.loop_indices, p.vertices):
        c = low.data.vertices[vi].co
        if k == 'deck': uv[li].uv = (U(c.y), 0.70 + 0.295 * (c.x + 41) / 82)
        elif k == 'port': uv[li].uv = (U(c.y), 0.515 + 0.175 * SIDE(c.z))
        elif k == 'stbd': uv[li].uv = (U(c.y), 0.335 + 0.175 * SIDE(c.z))
        else: uv[li].uv = (uv[li].uv.x, uv[li].uv.y * 0.325)

# bake the full model's colour onto it
img = bpy.data.images.new('Carrier_BaseColor', R, R, alpha=False)
cm = bpy.data.materials.new('Carrier'); cm.use_nodes = True
t = cm.node_tree.nodes.new('ShaderNodeTexImage'); t.image = img; cm.node_tree.nodes.active = t
low.data.materials.clear(); low.data.materials.append(cm)
sc.render.engine = 'CYCLES'; sc.cycles.device = 'CPU'; sc.cycles.samples = 4
bk = sc.render.bake
bk.use_selected_to_active = True; bk.cage_extrusion = 0.25; bk.max_ray_distance = 1.5; bk.margin = 24; bk.margin_type = 'EXTEND'
bk.use_pass_direct = False; bk.use_pass_indirect = False; bk.use_pass_color = True
for x in sc.objects: x.select_set(x in (hi, low))
bpy.context.view_layer.objects.active = low
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
img.filepath_raw = out + '/Carrier_BaseColor_baked.png'; img.file_format = 'PNG'; img.save()
print('baked', img.filepath_raw)

def export(o, path):
    only(o)
    bpy.ops.wm.obj_export(filepath=path, export_selected_objects=True, forward_axis='NEGATIVE_Z', up_axis='Y',
                          export_uv=True, export_normals=True, export_materials=False, export_triangulated_mesh=True, apply_modifiers=True)
    print('wrote', path, len(o.data.polygons))

bpy.data.objects.remove(hi)
export(low, out + '/Carrier.obj')
bpy.ops.wm.save_as_mainfile(filepath=out + '/Carrier.blend')
far = low.copy(); far.data = low.data.copy(); far.name = 'CarrierFar'; sc.collection.objects.link(far)
decimate(far, FAR)
export(far, out + '/Carrier_Far.obj')
