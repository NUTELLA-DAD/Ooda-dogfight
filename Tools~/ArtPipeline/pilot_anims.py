# Pilot animations for the shared humanoid rig (tiger and wolf): authored with arm IK on target empties plus
# spine/neck/head turns, then baked to plain bone keys so Unity's Humanoid import can retarget them.
# Clips (30 fps): Fly (loop), Instruments, ShakeFist, CheckSix, Cheer, Flinch. Seated is kept.
# Run: python -I pilot_anims.py -- <in.blend> <rig name> <out.blend> <out.fbx>
import bpy, sys, math
from mathutils import Vector, Quaternion, Euler

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, RIG, DST, FBX = argv
bpy.ops.wm.open_mainfile(filepath=SRC)
sc = bpy.context.scene; sc.render.fps = 30
rig = bpy.data.objects[RIG]
seated = bpy.data.actions['Seated']
rig.animation_data.action = seated
sc.frame_set(1)
base = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in rig.pose.bones}
for pb in rig.pose.bones: pb.rotation_mode = 'QUATERNION'

# Where things are, in the pilot's own frame (he faces -Y, up +Z), matching the cockpits built for him:
# stick grip rig(0,-0.31,0.47), throttle knob about rig(0.24,-0.16,0.49), instrument panel face at y -0.48.
WR_STICK = Vector((-0.045, -0.255, 0.455))   # right wrist when the paw is round the grip
WR_THR = Vector((0.215, -0.20, 0.455))       # left wrist on the throttle knob
PANEL = Vector((0.10, -0.40, 0.56))          # left wrist reaching a gauge
FIST_UP = Vector((0.25, -0.19, 0.78))        # left fist raised, shaking
CHEER = Vector((0.20, -0.14, 0.84))

def empty(name, at):
    e = bpy.data.objects.new(name, None); sc.collection.objects.link(e); e.location = at; return e
tgtL, tgtR = empty('IK_L', WR_THR), empty('IK_R', WR_STICK)
poleL, poleR = empty('Pole_L', (0.42, 0.12, 0.28)), empty('Pole_R', (-0.42, 0.12, 0.28))
for side, t, p in (('Left', tgtL, poleL), ('Right', tgtR, poleR)):
    c = rig.pose.bones[side + 'LowerArm'].constraints.new('IK')
    c.target = t; c.pole_target = p; c.chain_count = 2; c.pole_angle = math.radians(-90 if side == 'Left' else -90)

ADD = ('Spine', 'Chest', 'Neck', 'Head', 'LeftHand', 'RightHand', 'LeftShoulder', 'RightShoulder')
def q(x=0, y=0, z=0):  # degrees, in the bone's own axes
    return Euler((math.radians(x), math.radians(y), math.radians(z))).to_quaternion()

def smooth(t): return t * t * (3 - 2 * t)
def lerp(a, b, t): return a + (b - a) * t
def track(keys, f):
    """keys: list of (frame, value); value is a Vector or a float; eased between keys"""
    if f <= keys[0][0]: return keys[0][1]
    for (f0, v0), (f1, v1) in zip(keys, keys[1:]):
        if f <= f1:
            t = smooth((f - f0) / (f1 - f0)) if f1 > f0 else 1
            return lerp(v0, v1, t)
    return keys[-1][1]

def author(name, frames, fn, loop=False):
    """fn(f) -> dict(L=Vector, R=Vector, rot={bone: (x,y,z) degrees})"""
    act = bpy.data.actions.new(name + '_src')
    rig.animation_data.action = act
    for e in (tgtL, tgtR):
        if e.animation_data: e.animation_data_clear()
    for f in range(0, frames + 1):
        sc.frame_set(f)
        d = fn(f)
        tgtL.location = d.get('L', WR_THR); tgtR.location = d.get('R', WR_STICK)
        tgtL.keyframe_insert('location', frame=f); tgtR.keyframe_insert('location', frame=f)
        rot = d.get('rot', {})
        for pb in rig.pose.bones:
            loc, r = base[pb.name]
            pb.location = loc
            pb.rotation_quaternion = r @ q(*rot[pb.name]) if pb.name in rot else r
            pb.keyframe_insert('location', frame=f); pb.keyframe_insert('rotation_quaternion', frame=f)
    # bake what the IK makes of it into plain keys
    for o in bpy.context.selected_objects: o.select_set(False)
    bpy.context.view_layer.objects.active = rig; rig.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.select_all(action='SELECT')
    bpy.ops.nla.bake(frame_start=0, frame_end=frames, only_selected=False, visual_keying=True, clear_constraints=False,
                     use_current_action=False, bake_types={'POSE'})
    bpy.ops.object.mode_set(mode='OBJECT')
    baked = rig.animation_data.action
    baked.name = name; baked.use_fake_user = True
    bpy.data.actions.remove(act)
    print('baked', name, frames)
    return baked

wob = lambda f, per, amp, ph=0.0: amp * math.sin(2 * math.pi * (f / per) + ph)

# Fly: hands on stick and throttle, the stick stirred in small circles, breathing, a little head bob
def fly(f):
    R = WR_STICK + Vector((wob(f, 60, 0.012), wob(f, 60, 0.010, 1.6), wob(f, 40, 0.004)))
    L = WR_THR + Vector((0, wob(f, 120, 0.006), wob(f, 60, 0.003)))
    br = wob(f, 60, 1.5)
    return dict(L=L, R=R, rot={'Chest': (br, 0, 0), 'Neck': (wob(f, 120, 2), wob(f, 120, 3, 1.0), 0), 'Head': (0, wob(f, 60, 2, 0.5), 0)})

# Instruments: reach to the panel, tap a gauge twice, twist a knob, back to the throttle
def instruments(f):
    L = track([(0, WR_THR), (14, PANEL + Vector((0, 0.03, 0))), (20, PANEL), (24, PANEL + Vector((0, 0.025, 0))), (28, PANEL),
               (32, PANEL + Vector((0, 0.025, 0))), (40, PANEL + Vector((-0.03, 0, -0.04))), (64, PANEL + Vector((-0.03, 0, -0.04))),
               (82, WR_THR), (90, WR_THR)], f)
    twist = track([(0, 0.0), (40, 0.0), (48, 50.0), (54, -10.0), (60, 45.0), (66, 0.0), (90, 0.0)], f)
    look = track([(0, 0.0), (10, 1.0), (72, 1.0), (88, 0.0), (90, 0.0)], f)
    return dict(L=L, R=WR_STICK, rot={'LeftHand': (0, twist, 0), 'Neck': (12 * look, 6 * look, 0), 'Head': (16 * look, 8 * look, 0),
                                       'Chest': (6 * look, 4 * look, 0)})

# ShakeFist: left fist up and shaking at the enemy, leaning in, jaw forward
def shake_fist(f):
    up = track([(0, 0.0), (10, 1.0), (60, 1.0), (75, 0.0)], f)
    shake = wob(f, 6, 0.035) * (1.0 if 10 <= f <= 60 else 0.0)
    L = WR_THR.lerp(FIST_UP, up) + Vector((0, 0, shake))
    lean = up
    return dict(L=L, R=WR_STICK, rot={'Chest': (10 * lean, 8 * lean + wob(f, 6, 2) * lean, 0), 'Neck': (6 * lean, 0, 0),
                                      'Head': (8 * lean + wob(f, 12, 3) * lean, 10 * lean, wob(f, 6, 4) * lean),
                                      'LeftHand': (0, 0, -40 * lean), 'LeftShoulder': (0, 0, 12 * lean)})

# CheckSix: twist and look back over the left shoulder, then the right
def check_six(f):
    turn = track([(0, 0.0), (14, 1.0), (34, 1.0), (50, -1.0), (72, -1.0), (90, 0.0)], f)
    return dict(L=WR_THR, R=WR_STICK, rot={'Spine': (0, 8 * turn, 0), 'Chest': (0, 18 * turn, 0), 'Neck': (0, 24 * turn, 0),
                                           'Head': (-6 * abs(turn), 28 * turn, 0)})

# Cheer: two quick fist pumps with the left
def cheer(f):
    up = track([(0, 0.0), (8, 1.0), (14, 0.55), (20, 1.0), (26, 0.55), (32, 1.0), (48, 0.0), (60, 0.0)], f)
    return dict(L=WR_THR.lerp(CHEER, up), R=WR_STICK, rot={'Chest': (-6 * up, 0, 0), 'Head': (-14 * up, 0, 0), 'LeftHand': (0, 0, -40 * up)})

# Flinch: hit! shoulders up, head ducks, then shake it off
def flinch(f):
    k = track([(0, 0.0), (4, 1.0), (14, 1.0), (30, 0.0)], f)
    return dict(L=WR_THR + Vector((-0.03, 0.03, 0.04)) * k, R=WR_STICK + Vector((0.0, 0.02, 0.03)) * k,
                rot={'Chest': (14 * k, 0, 0), 'Neck': (12 * k, 0, 0), 'Head': (16 * k, wob(f, 8, 6) * k, 0),
                     'LeftShoulder': (0, 0, 14 * k), 'RightShoulder': (0, 0, -14 * k)})

made = [author('Fly', 120, fly, loop=True), author('Instruments', 90, instruments), author('ShakeFist', 75, shake_fist),
        author('CheckSix', 90, check_six), author('Cheer', 60, cheer), author('Flinch', 30, flinch)]

# clean up the authoring rig so the file exports plain FK
for pb in rig.pose.bones:
    for c in list(pb.constraints): pb.constraints.remove(c)
for e in (tgtL, tgtR, poleL, poleR): bpy.data.objects.remove(e)
for a in list(bpy.data.actions):
    if a.name.startswith(('IK_', 'Pole_')): bpy.data.actions.remove(a)
seated.use_fake_user = True
rig.animation_data.action = seated   # the file's default pose stays the seated one the avatar was built from
sc.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=DST)
for o in bpy.data.objects: o.select_set(False)
bpy.ops.export_scene.fbx(filepath=FBX, object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
                         use_space_transform=True, bake_space_transform=False, path_mode='STRIP', add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE')
print('exported', FBX, [a.name for a in bpy.data.actions])
