# Preview the koi's fishbowl helmet (the same shapes as Assets/OODA/Editor/KoiFishbowl.cs builds in Unity): the koi
# alone (front, 3/4 and seated), or seated in a fighter the way the builders seat him (PilotMount, the seat's lift and
# scale), with the hidden canopy as faint glass; it prints how far the bowl rises over the canopy and any airframe
# vertices inside the bowl. NOSHOT=1 skips the renders.
# Run: python -I koi_fishbowl_preview.py -- <repo> <outdir> alone
#      python -I koi_fishbowl_preview.py -- <repo> <outdir> P51,P51/P51.fbx,P51/P51_BaseColor.png,1.55,-0.33
import bpy, sys, math, os, bmesh
from mathutils import Vector, Matrix
A = sys.argv[sys.argv.index('--')+1:]
REPO, OUT, MODE = A[0], A[1], A[2]
ART = REPO + '/Assets/OODA/Art'
# helmet, in the koi's own height-1 frame (Blender: Z up, facing -Y, its left +X)
C = Vector((0, 0.03, 0.85)); R = 0.35; ZCUT = 0.63; ZW = C.z + 0.29
def mat(name, col, alpha=1, rough=0.3, metal=0, emit=None, tex=None):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = col; b.inputs['Roughness'].default_value = rough; b.inputs['Metallic'].default_value = metal
    b.inputs['Alpha'].default_value = alpha
    if tex:
        t = m.node_tree.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(tex)
        m.node_tree.links.new(t.outputs['Color'], b.inputs['Base Color'])
    if alpha < 1:
        m.surface_render_method = 'BLENDED'
    return m
def obj(name, bm, m):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o
def bowl(z0, z1, r, m, name, seg=48, rings=40):
    bm = bmesh.new(); prev = None
    zs = [z0 + (z1 - z0) * i / rings for i in range(rings + 1)]
    for z in zs:
        rr = math.sqrt(max(0, r*r - (z - C.z)**2)); row = [bm.verts.new((C.x + rr*math.cos(2*math.pi*k/seg), C.y + rr*math.sin(2*math.pi*k/seg), z)) for k in range(seg)]
        if prev:
            for k in range(seg): bm.faces.new((prev[k], prev[(k+1)%seg], row[(k+1)%seg], row[k]))
        prev = row
    return obj(name, bm, m)
def lathe(profile, m, name, seg=48):
    bm = bmesh.new(); rows = [[bm.verts.new((C.x + r*math.cos(2*math.pi*k/seg), C.y + r*math.sin(2*math.pi*k/seg), z)) for k in range(seg)] for r, z in profile]
    for a, b in zip(rows, rows[1:]):
        for k in range(seg): bm.faces.new((a[k], a[(k+1)%seg], b[(k+1)%seg], b[k]))
    return obj(name, bm, m)
def ball(p, r, m, name):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=r)
    for v in bm.verts: v.co += Vector(p)
    return obj(name, bm, m)
WATER = (0.20, 0.66, 0.66, 1); CLEAR = (0.9, 0.97, 1, 1); BRASS = (0.72, 0.52, 0.22, 1)
def helmet():
    water = mat('Water', WATER, 0.42, 0.05); glass = mat('Glass', (0.80, 0.94, 0.96, 1), 0.20, 0.02)
    line = mat('Waterline', (0.85, 0.97, 1, 1), 0.7, 0.1); brass = mat('Brass', BRASS, 1, 0.35, 1)
    bub = mat('Bubble', (0.95, 1, 1, 1), 0.55, 0.02)
    parts = [bowl(ZCUT, ZW, R, water, 'BowlWater'), bowl(ZW, C.z + R, R, glass, 'BowlGlass', rings=14),
             lathe([(math.sqrt(R*R - 0.29**2) + 0.002, ZW - 0.004), (math.sqrt(R*R - 0.29**2) + 0.002, ZW + 0.004)], line, 'Waterline')]
    rs = math.sqrt(R*R - 0.29**2) - 0.002
    bm = bmesh.new(); ring = [bm.verts.new((C.x + rs*math.cos(2*math.pi*k/48), C.y + rs*math.sin(2*math.pi*k/48), ZW)) for k in range(48)]; bm.faces.new(ring)
    parts.append(obj('WaterSurface', bm, mat('Surface', (0.75, 0.95, 0.97, 1), 0.40, 0.05)))
    parts.append(lathe([(0.170, 0.550), (0.225, 0.550), (0.262, 0.578), (0.282, 0.612), (0.286, 0.638), (0.278, 0.650), (0.250, 0.650), (0.185, 0.600)], brass, 'Collar'))
    for k in range(8):
        a = 2*math.pi*(k + 0.5)/8
        parts.append(ball((C.x + 0.284*math.cos(a), C.y + 0.284*math.sin(a), 0.617), 0.012, brass, f'Rivet{k}'))
    for p, r in [((0.20, -0.16, 1.055), 0.024), ((0.222, -0.14, 0.995), 0.016), ((0.215, -0.155, 0.955), 0.011)]:
        parts.append(ball(p, r, bub, 'Bubble'))
    return parts
def koi(seated):
    bpy.ops.import_scene.fbx(filepath=ART + '/KoiPilot/KoiPilot.fbx')
    rig = bpy.data.objects['KoiRig']; body = bpy.data.objects['Koi_Body']
    body.data.materials.clear(); body.data.materials.append(mat('Koi', (1,1,1,1), rough=0.5, tex=ART + '/KoiPilot/Koi_BaseColor.png'))
    parts = helmet()
    head = rig.data.bones['Head']; hm = rig.matrix_world @ head.matrix_local
    for p in parts:
        mw = p.matrix_world.copy(); p.parent = rig; p.parent_type = 'BONE'; p.parent_bone = 'Head'
        bpy.context.view_layer.update(); p.matrix_world = mw
    if seated:
        act = [a for a in bpy.data.actions if a.name.endswith('Seated')][0]
        rig.animation_data_create(); rig.animation_data.action = act
        try: rig.animation_data.action_slot = act.slots[0]
        except Exception: pass
        bpy.context.scene.frame_set(int(act.frame_range[0]))
    return rig, parts
def scene_setup(res=(900, 900)):
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 48; sc.cycles.device = 'CPU'
    try: sc.cycles.use_denoising = True
    except Exception: pass
    sc.render.resolution_x, sc.render.resolution_y = res; sc.render.film_transparent = False
    w = bpy.data.worlds.new('W'); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.62, 0.74, 0.88, 1); w.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.2
    sun = bpy.data.objects.new('Sun', bpy.data.lights.new('Sun', 'SUN')); sun.data.energy = 4.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35)); sc.collection.objects.link(sun)
    return sc
def shoot(sc, eye, at, path, lens=50):
    if os.environ.get("NOSHOT"): return
    cam = bpy.data.objects.get('Cam')
    if cam is None:
        cam = bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = lens; cam.location = eye
    cam.rotation_euler = (Vector(at) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = path; bpy.ops.render.render(write_still=True); print('wrote', path)
bpy.ops.wm.read_factory_settings(use_empty=True)
if MODE == 'alone':
    sc = scene_setup((800, 1000)); rig, parts = koi(False)
    shoot(sc, (0, -3.2, 0.62), (0, 0, 0.62), OUT + '/koi_front.png', 50)
    shoot(sc, (1.6, -2.2, 1.2), (0, 0, 0.75), OUT + '/koi_34.png', 60)
    rig.animation_data_create(); act = [a for a in bpy.data.actions if a.name.endswith('Seated')][0]; rig.animation_data.action = act
    try: rig.animation_data.action_slot = act.slots[0]
    except Exception: pass
    sc.frame_set(int(act.frame_range[0]))
    shoot(sc, (1.3, -1.9, 1.15), (0, 0, 0.62), OUT + '/koi_seated.png', 55)
else:
    name, fbx, tex, scale, lift = MODE.split(',')
    scale, lift = float(scale), float(lift)
    sc = scene_setup((1100, 800))
    bpy.ops.import_scene.fbx(filepath=ART + '/' + fbx)
    planeObjs = list(bpy.data.objects)
    body = mat('Body', (1,1,1,1), rough=0.5, tex=ART + '/' + tex); pit = mat('Pit', (0.13,0.15,0.1,1), rough=0.8)
    ghost = mat('CanopyGhost', (0.75, 0.9, 1, 1), 0.12, 0.05)
    for o in planeObjs:
        if o.type != 'MESH': continue
        o.data.materials.clear()
        o.data.materials.append(ghost if 'Canopy' in o.name else pit if o.name.startswith('Pit_') else body)
    mount = [o for o in planeObjs if o.name == 'PilotMount'][0]
    rig, parts = koi(True)
    rig.matrix_world = mount.matrix_world @ Matrix.Translation((0, lift, 0)) @ Matrix.Rotation(math.radians(-90), 4, 'X') @ Matrix.Scale(scale, 4)
    bpy.context.view_layer.update()
    hp = rig.matrix_world @ rig.pose.bones['Head'].head
    # glass clearance: how far the bowl reaches past the canopy (hidden in the game), sampled straight up and to the sides
    can = [o for o in planeObjs if 'Canopy' in o.name and o.type == 'MESH']
    bw = [p for p in parts if p.name in ('BowlGlass', 'BowlWater')]
    dg = bpy.context.evaluated_depsgraph_get()
    top = max((p.matrix_world @ v.co).z for p in bw for v in p.data.vertices)
    print(name, 'head', [round(x, 3) for x in hp], 'bowl top', round(top, 3))
    if can:
        hits = []
        for c in can:
            ce = c.evaluated_get(dg); mw = c.matrix_world; inv = mw.inverted()
            o0 = inv @ Vector((hp.x, hp.y, hp.z)); d0 = (inv.to_3x3() @ Vector((0, 0, 1))).normalized()
            ok, loc, nrm, idx = ce.ray_cast(o0, d0)
            if ok: hits.append((mw @ loc).z)
        print(name, 'canopy over head at', [round(h, 3) for h in hits], 'bowl top minus canopy', round(top - min(hits), 3) if hits else None)
    # the bowl as a sphere, in the world, and what of the airframe is inside it
    hb = rig.pose.bones['Head']; hmw = rig.matrix_world @ hb.matrix
    rest = rig.data.bones['Head'].matrix_local
    cw = hmw @ (rest.inverted() @ C); rw = R * scale
    for o in planeObjs:
        if o.type != 'MESH' or 'Canopy' in o.name: continue
        n = sum(1 for v in o.data.vertices if ((o.matrix_world @ v.co) - cw).length < rw)
        if n:
            pts = [(o.matrix_world @ v.co) - cw for v in o.data.vertices if ((o.matrix_world @ v.co) - cw).length < rw]
            print(name, 'inside bowl:', o.name, n, 'rel', [tuple(round(x, 2) for x in q) for q in pts[:16]])
    print(name, 'bowl centre', [round(x, 3) for x in cw], 'radius', round(rw, 3))
    shoot(sc, hp + Vector((2.6, -3.2, 1.3)), hp + Vector((0, 0.2, -0.25)), OUT + f'/{name}_34.png', 45)
    shoot(sc, hp + Vector((4.2, 0.0, 0.25)), hp + Vector((0, 0.0, -0.25)), OUT + f'/{name}_side.png', 40)
