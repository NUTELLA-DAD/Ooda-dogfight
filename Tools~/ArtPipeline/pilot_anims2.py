# Pilot animations, second pass, for a pilot with finger bones (pilot_paws.py): the same clips as pilot_anims.py
# (Fly, Instruments, ShakeFist, CheckSix, Cheer, Flinch; Seated is kept), authored again so they read as alive:
#   - the paws grip: fingers wrap the stick and the throttle, make a fist, point at a gauge, spring open on a hit
#   - nothing moves all at once: the spine leads, the chest, neck and head follow a few frames behind each other,
#     the hand drags behind the arm, and moves settle with a small anticipation and overshoot instead of stopping dead
#   - Fly is an 8 s loop of layered slow motions (breathing, weight shifts, head drift, finger flexing), so it does
#     not read as a 2 s cycle; ears flick if the rig has LeftEar/RightEar
# Arm IK on target empties, baked to plain bone keys for Unity's Humanoid import.
# Run: python -I pilot_anims2.py -- <in.blend> <rig name> <out.blend> [out.fbx]
import bpy, sys, math
from mathutils import Vector, Quaternion, Euler

argv = sys.argv[sys.argv.index('--') + 1:]
SRC, RIG, DST = argv[:3]; FBX = argv[3] if len(argv) > 3 else None
bpy.ops.wm.open_mainfile(filepath=SRC)
sc = bpy.context.scene; sc.render.fps = 30
rig = bpy.data.objects[RIG]
seated = bpy.data.actions['Seated']
for a in list(bpy.data.actions):
    if a is not seated: bpy.data.actions.remove(a)
if not rig.animation_data: rig.animation_data_create()
rig.animation_data.action = seated
sc.frame_set(1)
for pb in rig.pose.bones: pb.rotation_mode = 'QUATERNION'
base = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in rig.pose.bones}
HAS_EARS = 'LeftEar' in rig.pose.bones

# Where the wrists go (rig space: he faces -Y, up +Z). The stick's grip is at (0, -0.31, 0.47) and the throttle knob
# at (0.243, -0.22, 0.472); the wrist sits back from each by the length of the palm, so the paw closes round it.
from mathutils import Matrix
def orient(A, U):
    """A paw's attitude: A the way the fingers point, U the way the back of the paw faces."""
    Y = Vector(A).normalized(); Z = Vector(U); Z = (Z - Y * Z.dot(Y)).normalized(); X = Y.cross(Z)
    return Matrix((X, Y, Z)).transposed().to_quaternion()
A_ST = Vector((0.15, -0.99, 0.0)).normalized(); P_ST = Vector((0.99, 0.15, 0.0)).normalized()
WR_STICK = Vector((0.0, -0.31, 0.47)) - A_ST * 0.085 - P_ST * 0.050
Q_STICK = orient(A_ST, -P_ST)                                   # thumb up, pads against the grip
A_TH = Vector((0.0, -0.906, -0.42)); P_TH = Vector((0.0, 0.42, -0.906))
WR_THR = Vector((0.243, -0.22, 0.472)) - A_TH * 0.070 - P_TH * 0.045
Q_THR = orient(A_TH, -P_TH)                                     # pads down on the knob, fingers draped over it
Q_PANEL = orient((0.0, -1.0, 0.08), (0.0, 0.08, 1.0))           # pointing at a gauge
Q_FISTUP = orient((-0.1, -0.25, 1.0), (0.75, -0.65, 0.0))       # fist up, knuckles at the enemy
print('wrists', WR_STICK, WR_THR)
PANEL = Vector((0.10, -0.40, 0.56))
FIST_UP = Vector((0.34, -0.20, 0.70))
CHEER = Vector((0.34, -0.12, 0.80))

def empty(name, at):
    e = bpy.data.objects.new(name, None); sc.collection.objects.link(e); e.location = at; return e
tgtL, tgtR = empty('IK_L', WR_THR), empty('IK_R', WR_STICK)
poleL, poleR = empty('Pole_L', (0.42, 0.12, 0.28)), empty('Pole_R', (-0.42, 0.12, 0.28))
for side, t, p in (('Left', tgtL, poleL), ('Right', tgtR, poleR)):
    c = rig.pose.bones[side + 'LowerArm'].constraints.new('IK')
    c.target = t; c.pole_target = p; c.chain_count = 2; c.pole_angle = math.radians(-90)

def q(x=0, y=0, z=0): return Euler((math.radians(x), math.radians(y), math.radians(z))).to_quaternion()
def smooth(t): return t * t * (3 - 2 * t)
def ease(t, k=0.5):
    """Smoothstep with a little wind-up before the move and a little overshoot as it lands."""
    return smooth(t) + k * math.sin(math.pi * t) ** 2 * (2 * t - 1) * 0.5
def lerp(a, b, t): return a + (b - a) * t
def track(keys, f, k=0.5):
    if f <= keys[0][0]: return keys[0][1]
    for (f0, v0), (f1, v1) in zip(keys, keys[1:]):
        if f <= f1:
            t = ease((f - f0) / (f1 - f0), k) if f1 > f0 else 1
            return lerp(v0, v1, t)
    return keys[-1][1]
wob = lambda f, per, amp, ph=0.0: amp * math.sin(2 * math.pi * (f / per) + ph)

# ------------------------------------------------------------------ paws
GRIP = dict(curl=(44, 40, 24), thumb=(26, 26, 16), splay=0)     # round the stick
KNOB = dict(curl=(34, 32, 20), thumb=(20, 22, 14), splay=3)     # on the throttle knob
FIST = dict(curl=(58, 50, 30), thumb=(38, 34, 22), splay=0)
OPEN = dict(curl=(-6, 4, 2), thumb=(-12, 2, 0), splay=11)       # startled, fingers spread
POINT = dict(curl=(56, 48, 28), thumb=(34, 30, 20), splay=0, index=(2, 5, 4))
RELAX = dict(curl=(20, 20, 12), thumb=(12, 14, 8), splay=4)
def mix(a, b, t):
    out = {}
    for k_ in ('curl', 'thumb'): out[k_] = tuple(lerp(x, y, t) for x, y in zip(a[k_], b[k_]))
    out['splay'] = lerp(a['splay'], b['splay'], t)
    ia, ib = a.get('index', a['curl']), b.get('index', b['curl'])
    out['index'] = tuple(lerp(x, y, t) for x, y in zip(ia, ib))
    return out
def paw(side, pose, flex=0.0):
    """Bone rotations for one paw. Fingers curl about their own X (negative is toward the pads)."""
    r = {}
    sg = 1 if side == 'Left' else -1
    for i, name in enumerate(('Index', 'Middle', 'Ring')):
        c = pose.get('index', pose['curl']) if name == 'Index' else pose['curl']
        lag = 1 + 0.10 * (i - 1)            # the outer finger closes a touch more, as a real grip does
        sp = pose['splay'] * (1 - i) * sg
        for seg, ang in zip(('Proximal', 'Intermediate', 'Distal'), c):
            r[f'{side}{name}{seg}'] = (-(ang * lag + flex * (1 + 0.3 * i)), 0, sp if seg == 'Proximal' else 0)
    t = pose['thumb']
    r[f'{side}ThumbProximal'] = (-t[0] * 0.5, 0, -t[0] * sg)
    r[f'{side}ThumbIntermediate'] = (-t[1] * 0.4, 0, -t[1] * 0.8 * sg)
    r[f'{side}ThumbDistal'] = (-t[2] * 0.4, 0, -t[2] * 0.8 * sg)
    return r

import os
def author(name, frames, fn):
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
            if pb.name in ('LeftHand', 'RightHand'): continue
            pb.rotation_quaternion = r @ q(*rot[pb.name]) if pb.name in rot else r
            pb.keyframe_insert('location', frame=f); pb.keyframe_insert('rotation_quaternion', frame=f)
        # the paws are aimed in the pilot's own frame, whatever the arm under them is doing
        bpy.context.view_layer.update()
        for hn, key, dflt in (('LeftHand', 'qL', Q_THR), ('RightHand', 'qR', Q_STICK)):
            pb = rig.pose.bones[hn]
            Q = d.get(key, dflt) @ (q(*rot[hn]) if hn in rot else Quaternion())
            M = Q.to_matrix().to_4x4(); M.translation = pb.matrix.translation
            pb.matrix = M; pb.location = base[hn][0]
            pb.keyframe_insert('location', frame=f); pb.keyframe_insert('rotation_quaternion', frame=f)
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

def ears(f, flick=0.0, back=0.0):
    if not HAS_EARS: return {}
    return {'LeftEar': (back + flick, 0, 0), 'RightEar': (back - flick * 0.6, 0, 0)}

# the body's idle life, shared by every clip so gestures sit on top of a pilot who is still breathing
def life(f, n=240, amt=1.0):
    br = wob(f, n / 2, 1.0)                                    # breathing, 4 s
    sway = wob(f, n, 1.0, 0.7)                                 # a slow shift of weight, 8 s
    drift = wob(f, n / 3, 1.0, 2.1)                            # 2.7 s
    r = {'Spine': (0.6 * br * amt, 0.8 * sway * amt, 0.5 * sway * amt),
         'Chest': (1.6 * br * amt, 1.0 * sway * amt, 0),
         'LeftShoulder': (0, 0, 1.6 * br * amt), 'RightShoulder': (0, 0, -1.6 * br * amt),
         'Neck': ((1.5 * drift - 0.8 * br) * amt, 2.5 * wob(f, n, 1, 1.9) * amt, 0),
         'Head': (1.5 * wob(f, n / 2, 1, 0.9) * amt, 3.0 * wob(f, n / 3, 1, 0.4) * amt, 1.5 * sway * amt)}
    # the tail never quite rests: a slow wave runs down it, each segment a few frames behind the one before
    for i in range(1, 5):
        if f'Tail{i}' in rig.pose.bones:
            r[f'Tail{i}'] = ((1.5 + i) * wob(f, n / 4, 1, -0.55 * i), 0, (2.5 + 1.5 * i) * wob(f, n / 3, 1, -0.7 * i))
    return r
def add(a, b):
    out = dict(a)
    for k_, v in b.items(): out[k_] = tuple(x + y for x, y in zip(out[k_], v)) if k_ in out else v
    return out

# Fly: hands on stick and throttle, working them a little; breathing; fingers flexing on the grip
def fly(f):
    n = 240
    R = WR_STICK + Vector((wob(f, n / 3, 0.011) + wob(f, n / 8, 0.003), wob(f, n / 3, 0.009, 1.6) + wob(f, n / 5, 0.003, 0.5), wob(f, n / 4, 0.004)))
    L = WR_THR + Vector((0, wob(f, n / 2, 0.007) + wob(f, n / 6, 0.002), wob(f, n / 3, 0.003)))
    rot = life(f, n)
    rot = add(rot, paw('Right', GRIP, flex=wob(f, n / 4, 3.0, 0.3)))
    rot = add(rot, paw('Left', KNOB, flex=wob(f, n / 3, 4.0, 1.1) + max(0, wob(f, n, 6.0, 2.0))))
    # an ear flicks twice in the loop
    fl = max(0, 1 - abs(f - 70) / 5) * 16 - max(0, 1 - abs(f - 76) / 5) * 7 + max(0, 1 - abs(f - 190) / 4) * -12
    rot = add(rot, ears(f, flick=fl, back=wob(f, n / 2, 1.5)))
    rot = add(rot, {'RightHand': (wob(f, n / 3, 2.5, 1.0), 0, wob(f, n / 3, 3.0)), 'LeftHand': (0, wob(f, n / 2, 2.0), 0)})
    return dict(L=L, R=R, rot=rot)

# Instruments: glance down first, then the paw goes; tap a gauge twice with one finger, twist a knob, back
def instruments(f):
    look = track([(0, 0.0), (9, 1.0), (70, 1.0), (86, 0.0), (90, 0.0)], f)
    L = track([(0, WR_THR), (4, WR_THR + Vector((0.01, 0.02, 0.015))), (17, PANEL + Vector((0, 0.035, 0.01))), (22, PANEL), (26, PANEL + Vector((0, 0.028, 0.004))),
               (30, PANEL), (35, PANEL + Vector((0, 0.03, 0))), (43, PANEL + Vector((-0.03, 0, -0.04))), (66, PANEL + Vector((-0.03, 0, -0.04))),
               (84, WR_THR), (90, WR_THR)], f, 0.35)
    reach = track([(0, 0.0), (6, 0.0), (16, 1.0), (36, 1.0), (44, 0.0), (90, 0.0)], f)           # pointing
    knob = track([(0, 0.0), (38, 0.0), (45, 1.0), (66, 1.0), (74, 0.0), (90, 0.0)], f)           # pinching the knob
    twist = track([(0, 0.0), (44, 0.0), (51, 50.0), (57, -10.0), (63, 45.0), (69, 0.0), (90, 0.0)], f, 0.3)
    hand = mix(mix(KNOB, POINT, reach), RELAX, knob * 0.6)
    lk = lambda lag: track([(0, 0.0), (9, 1.0), (70, 1.0), (86, 0.0), (90, 0.0)], f - lag)
    rot = life(f, 240, 0.6)
    rot = add(rot, {'LeftHand': (0, twist, 0), 'Chest': (6 * lk(0), 4 * lk(0), 0), 'Neck': (12 * lk(2), 6 * lk(2), 0), 'Head': (17 * lk(4), 9 * lk(4), 2 * lk(5))})
    rot = add(rot, paw('Left', hand)); rot = add(rot, paw('Right', GRIP))
    rot = add(rot, ears(f, back=4 * look))
    return dict(L=L, R=WR_STICK, rot=rot, qL=Q_THR.slerp(Q_PANEL, track([(0, 0.0), (4, 0.0), (17, 1.0), (66, 1.0), (84, 0.0), (90, 0.0)], f, 0.2)))

# ShakeFist: a beat of wind-up, then the left fist is up and shaking, leaning in; the head shakes a little later
def shake_fist(f):
    up = track([(0, 0.0), (3, -0.08), (12, 1.0), (58, 1.0), (72, 0.0), (75, 0.0)], f, 0.4)
    fist = track([(0, 0.0), (8, 1.0), (60, 1.0), (72, 0.0), (75, 0.0)], f)
    on = 1.0 if 12 <= f <= 58 else 0.0
    env = smooth(min(1, max(0, (f - 10) / 6))) * smooth(min(1, max(0, (60 - f) / 6)))
    shake = wob(f, 7, 0.034) * env
    L = WR_THR.lerp(FIST_UP, max(0, up)) + Vector((wob(f, 7, 0.008, 1.2) * env, up * 0.0 - 0.02 * min(0, up) * 10, shake))
    lean = lambda lag: track([(0, 0.0), (3, -0.1), (12, 1.0), (58, 1.0), (72, 0.0), (75, 0.0)], f - lag, 0.4)
    rot = life(f, 240, 0.4)
    rot = add(rot, {'Spine': (4 * lean(0), 4 * lean(0), 0), 'Chest': (9 * lean(1), 9 * lean(1) + wob(f - 2, 7, 2.2) * env, 0), 'Neck': (6 * lean(3), 0, 0),
                    'Head': (8 * lean(4) + wob(f - 3, 14, 3) * env, 11 * lean(4), wob(f - 4, 7, 4) * env),
                    'LeftHand': (wob(f - 1, 7, 9) * env, 0, 0), 'LeftShoulder': (0, 0, 13 * lean(0))})
    rot = add(rot, paw('Left', mix(KNOB, FIST, fist))); rot = add(rot, paw('Right', GRIP, flex=6 * env))
    rot = add(rot, ears(f, back=14 * lean(2), flick=wob(f, 7, 3) * env))
    return dict(L=L, R=WR_STICK, rot=rot, qL=Q_THR.slerp(Q_FISTUP, min(1, max(0, lean(1)))))

# CheckSix: eyes first: the head leads, the neck, chest and spine follow it round; then the other side
def check_six(f):
    keys = [(0, 0.0), (13, 1.0), (32, 1.0), (49, -1.0), (70, -1.0), (86, 0.0), (90, 0.0)]
    t = lambda lag: track(keys, f - lag, 0.45)
    hd = t(0)
    rot = life(f, 240, 0.4)
    rot = add(rot, {'Head': (-6 * abs(hd), 30 * hd, -4 * hd), 'Neck': (0, 24 * t(2), 0), 'Chest': (0, 18 * t(4), -2 * t(4)), 'Spine': (0, 9 * t(6), 0),
                    'LeftShoulder': (0, 0, 5 * max(0, t(5))), 'RightShoulder': (0, 0, -5 * max(0, -t(5)))})
    rot = add(rot, paw('Left', KNOB, flex=5 * abs(t(4)))); rot = add(rot, paw('Right', GRIP, flex=5 * abs(t(4))))
    rot = add(rot, ears(f, back=6 * abs(hd)))
    # the stick hand is pulled a little by the twisting shoulders
    return dict(L=WR_THR + Vector((0, 0.008 * t(5), 0)), R=WR_STICK + Vector((0.006 * t(5), 0, 0)), rot=rot)

# Cheer: a dip, then two fist pumps, each a little smaller, and the head thrown back
def cheer(f):
    up = track([(0, 0.0), (3, -0.10), (10, 1.0), (16, 0.50), (22, 0.95), (28, 0.55), (34, 0.85), (50, 0.0), (60, 0.0)], f, 0.4)
    fist = track([(0, 0.0), (7, 1.0), (40, 1.0), (52, 0.0), (60, 0.0)], f)
    u = lambda lag: track([(0, 0.0), (3, -0.10), (10, 1.0), (16, 0.50), (22, 0.95), (28, 0.55), (34, 0.85), (50, 0.0), (60, 0.0)], f - lag, 0.4)
    rot = life(f, 240, 0.3)
    rot = add(rot, {'Spine': (-3 * u(0), 0, 0), 'Chest': (-7 * u(1), 0, 0), 'Neck': (-5 * u(2), 0, 0), 'Head': (-13 * u(3), 4 * u(3), 0),
                    'LeftShoulder': (0, 0, 10 * u(0))})
    rot = add(rot, paw('Left', mix(KNOB, FIST, fist))); rot = add(rot, paw('Right', GRIP, flex=8 * max(0, up)))
    rot = add(rot, ears(f, back=-8 * u(2)))
    return dict(L=WR_THR.lerp(CHEER, max(0, up)) + Vector((0, 0, -0.03 * min(0, up) * 10)), R=WR_STICK, rot=rot, qL=Q_THR.slerp(Q_FISTUP, min(1, max(0, fist))))

# Flinch: hit! the paws spring open, the shoulders jump, the head ducks; then he shakes it off and grips again
def flinch(f):
    k = track([(0, 0.0), (3, 1.0), (12, 0.85), (30, 0.0)], f, 0.2)
    kk = lambda lag: track([(0, 0.0), (3, 1.0), (12, 0.85), (30, 0.0)], f - lag, 0.2)
    op = track([(0, 0.0), (3, 1.0), (9, 1.0), (20, 0.0), (30, 0.0)], f, 0.2)
    sh = wob(f, 7, 1.0) * smooth(min(1, max(0, (f - 8) / 5))) * smooth(min(1, max(0, (28 - f) / 8)))
    rot = {'Chest': (14 * kk(0), 0, 0), 'Neck': (12 * kk(1), 0, 0), 'Head': (16 * kk(2), 7 * sh, 4 * sh),
           'LeftShoulder': (0, 0, 15 * k), 'RightShoulder': (0, 0, -15 * k)}
    rot = add(rot, paw('Left', mix(KNOB, OPEN, op))); rot = add(rot, paw('Right', mix(GRIP, OPEN, op * 0.75)))
    rot = add(rot, ears(f, back=28 * kk(1), flick=6 * sh))
    return dict(L=WR_THR + Vector((-0.03, 0.03, 0.045)) * k, R=WR_STICK + Vector((0.0, 0.02, 0.03)) * k, rot=rot)

made = [author('Fly', 2, fly)] if os.environ.get('QUICK') else [author('Fly', 240, fly), author('Instruments', 90, instruments), author('ShakeFist', 75, shake_fist),
        author('CheckSix', 90, check_six), author('Cheer', 60, cheer), author('Flinch', 30, flinch)]

for pb in rig.pose.bones:
    for c in list(pb.constraints): pb.constraints.remove(c)
for e in (tgtL, tgtR, poleL, poleR): bpy.data.objects.remove(e)
for a in list(bpy.data.actions):
    if a.name.startswith(('IK_', 'Pole_')): bpy.data.actions.remove(a)
seated.use_fake_user = True
rig.animation_data.action = seated
sc.frame_set(1)
# Lids open in the file: the face is driven in the game (PilotFace), never by a clip.
for key in bpy.data.shape_keys:
    key.animation_data_clear()
    for kb in key.key_blocks[1:]: kb.value = 0.0
bpy.ops.wm.save_as_mainfile(filepath=DST)
if FBX:
    for o in bpy.data.objects: o.select_set(False)
    bpy.ops.export_scene.fbx(filepath=FBX, object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
                             use_space_transform=True, bake_space_transform=False, path_mode='STRIP', add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='OFF')
    print('exported', FBX)
print('actions', [a.name for a in bpy.data.actions])
