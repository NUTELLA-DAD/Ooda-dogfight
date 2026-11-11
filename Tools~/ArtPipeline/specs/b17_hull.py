# The B-17G Flying Fortress for plane_hull.py, at the real aircraft's proportions (31.6 m span, 22.7 m long): the
# round fuselage with the stepped flight deck and the Plexiglas nose (both painted on: the bomber has no crew in the
# game), four radial nacelles whose hubs continue their cowls, each turning a three-blade Hamilton propeller, the big
# fin with its long dorsal fillet, and the chin, top, ball and tail turrets. The propellers keep the old model's names
# and places (B17_Prop0 and B17_Prop1 outboard, B17_Prop2 and B17_Prop3 inboard, the even ones on the left), so the
# game's builder fits. Nose is -y, up is +z, the pilot's left is +x.
XI, XO = 4.42, 9.07          # the nacelles' lines
ZI, ZO = -0.33, 0.02         # their thrust lines, on the wing's chord (4.5 degrees of dihedral)
YI, YO = -6.25, -5.20        # their propellers' planes

def nacelle(y0, zc, rear):
    """A radial's nacelle from the hub (its first ring, which the hub dome continues) back over the wing."""
    s = [(0.00, 0.28, 0.28, 0.28), (0.03, 0.48, 0.48, 0.48), (0.08, 0.63, 0.63, 0.63), (0.17, 0.71, 0.71, 0.71),
         (0.45, 0.74, 0.75, 0.74), (1.05, 0.74, 0.77, 0.74), (1.85, 0.69, 0.80, 0.70), (2.80, 0.58, 0.80, 0.64)]
    s += [(y - y0, t, b, w) for (y, t, b, w) in rear]
    return [(y0 + dy, zc + t, zc - b, w, 0.5, 2.0) for (dy, t, b, w) in s]

SPEC = {
    'prefix': 'B17', 'ppu': 118,
    'bodies': [
        {'sections': [   # y, top, bottom, half width, centre fraction, squareness
            (-10.60, 0.050, -0.520, 0.050, 0.50, 2.0), (-10.52, 0.260, -0.760, 0.380, 0.50, 2.0), (-10.25, 0.500, -0.990, 0.680, 0.50, 2.0),
            (-9.75, 0.700, -1.160, 0.900, 0.48, 2.1), (-9.00, 0.840, -1.290, 1.040, 0.47, 2.2), (-8.00, 0.950, -1.370, 1.110, 0.46, 2.3),
            (-7.30, 1.030, -1.410, 1.140, 0.46, 2.3), (-6.70, 1.250, -1.430, 1.150, 0.47, 2.3), (-6.00, 1.370, -1.440, 1.160, 0.47, 2.3),
            (-4.50, 1.390, -1.450, 1.160, 0.47, 2.3), (-2.00, 1.360, -1.450, 1.160, 0.47, 2.3), (0.00, 1.330, -1.430, 1.150, 0.47, 2.3),
            (2.00, 1.290, -1.350, 1.100, 0.47, 2.25), (4.00, 1.230, -1.170, 1.000, 0.48, 2.2), (6.00, 1.130, -0.950, 0.850, 0.50, 2.15),
            (8.00, 1.010, -0.700, 0.660, 0.50, 2.1), (9.50, 0.910, -0.500, 0.510, 0.50, 2.05), (10.80, 0.800, -0.320, 0.400, 0.50, 2.0),
            (11.60, 0.650, -0.200, 0.300, 0.50, 2.0), (12.05, 0.380, -0.080, 0.100, 0.50, 2.0)]},
        {'sections': nacelle(YO, ZO, [(-1.5, 0.42, 0.58, 0.52), (0.0, 0.25, 0.40, 0.38), (1.0, 0.10, 0.20, 0.18), (1.40, 0.01, 0.04, 0.03)]),
         'x': XO, 'spinner': True, 'chart': 'boomLO'},
        {'sections': nacelle(YO, ZO, [(-1.5, 0.42, 0.58, 0.52), (0.0, 0.25, 0.40, 0.38), (1.0, 0.10, 0.20, 0.18), (1.40, 0.01, 0.04, 0.03)]),
         'x': -XO, 'spinner': True, 'chart': 'boomRO'},
        {'sections': nacelle(YI, ZI, [(-1.0, 0.42, 0.72, 0.56), (1.0, 0.26, 0.58, 0.44), (2.6, 0.10, 0.32, 0.22), (3.10, 0.01, 0.06, 0.04)]),
         'x': XI, 'spinner': True, 'chart': 'boomLI'},
        {'sections': nacelle(YI, ZI, [(-1.0, 0.42, 0.72, 0.56), (1.0, 0.26, 0.58, 0.44), (2.6, 0.10, 0.32, 0.22), (3.10, 0.01, 0.06, 0.04)]),
         'x': -XI, 'spinner': True, 'chart': 'boomRI'},
    ],
    'wing': {'le': [(0, -3.60), (1.2, -3.58), (4.0, -3.32), (8.0, -2.70), (12.0, -1.95), (14.6, -1.35), (15.4, -0.95), (15.8, -0.40)],
             'te': [(0, 2.60), (1.2, 2.58), (4.0, 2.40), (8.0, 2.02), (12.0, 1.55), (14.6, 1.12), (15.4, 0.85), (15.8, 0.40)],
             'mid': [(0, -0.58), (1.2, -0.55), (15.8, 0.68)],
             'tc': [(0, 0.180), (4.0, 0.165), (15.8, 0.10)], 'camber': 0.012,
             'tip': 15.75, 'tip_r': 0.85, 'aileron': {'s': (10.3, 14.9), 'f': 0.76}},
    'tail': {'le': [(0, 7.60), (1.0, 7.75), (3.0, 8.30), (5.0, 8.95), (6.0, 9.35), (6.6, 9.90)],
             'te': [(0, 10.95), (2.0, 10.92), (4.0, 10.80), (5.5, 10.62), (6.2, 10.45), (6.6, 10.20)],
             'mid': [(0, 0.38), (6.6, 0.40)], 'tc': [(0, 0.11), (6.6, 0.09)],
             'tip': 6.60, 'tip_r': 0.55, 'elevator': {'s': (0.75, 6.05), 'y': 10.00}},
    'fins': [{'le': [(0.85, 3.20), (1.05, 4.60), (1.25, 6.20), (1.60, 7.60), (2.20, 8.75), (3.00, 9.55), (3.70, 10.05), (4.25, 10.55)],
              'te': [(0.85, 11.85), (2.00, 11.88), (3.00, 11.80), (3.70, 11.55), (4.25, 11.05)],
              'tc': [(0.85, 0.030), (1.60, 0.060), (2.60, 0.090), (4.25, 0.100)], 'h0': 0.85, 'h1': 4.25, 'tip_r': 0.55,
              'rudder': {'y': 10.80, 'h': (0.95, 3.75)}}],
    'props': [{'name': 'B17_Prop0', 'body': 1, 'blades': 3, 'radius': 1.78, 'pitch': 3.6, 'chord': 0.30,
               'spinner_len': 0.42, 'blade_y': 0.16, 'shape': 0.55},
              {'name': 'B17_Prop1', 'body': 2, 'blades': 3, 'radius': 1.78, 'pitch': 3.6, 'chord': 0.30,
               'spinner_len': 0.42, 'blade_y': 0.16, 'shape': 0.55, 'phase': 25},
              {'name': 'B17_Prop2', 'body': 3, 'blades': 3, 'radius': 1.78, 'pitch': 3.6, 'chord': 0.30,
               'spinner_len': 0.42, 'blade_y': 0.16, 'shape': 0.55, 'phase': 50},
              {'name': 'B17_Prop3', 'body': 4, 'blades': 3, 'radius': 1.78, 'pitch': 3.6, 'chord': 0.30,
               'spinner_len': 0.42, 'blade_y': 0.16, 'shape': 0.55, 'phase': 75}],
}

def extras(C):
    loft, tube, BODY, np, math = C['loft'], C['tube'], C['BODY'], C['np'], C['math']
    b = C['bodies'][0]
    # the turrets: Bendix chin turret under the nose, Sperry top turret behind the flight deck, the ball turret
    # under the radio room, all lofted as rounded boxes (the domes' glazing is painted)
    loft(BODY, 'chinT', [(-9.95, 0.10, -0.95, -1.05), (-9.85, 0.36, -0.90, -1.32), (-9.55, 0.44, -0.92, -1.48), (-9.20, 0.40, -0.98, -1.44), (-8.95, 0.20, -1.05, -1.25)])
    loft(BODY, 'topT', [(-5.55, 0.12, 1.42, 1.32), (-5.45, 0.42, 1.52, 1.30), (-5.10, 0.56, 1.80, 1.30), (-4.70, 0.56, 1.86, 1.30), (-4.35, 0.42, 1.70, 1.30), (-4.20, 0.10, 1.45, 1.32)])
    loft(BODY, 'ballT', [(2.85, 0.10, -1.20, -1.30), (2.95, 0.42, -1.18, -1.62), (3.25, 0.56, -1.18, -2.02), (3.65, 0.56, -1.16, -2.02), (3.95, 0.42, -1.14, -1.62), (4.05, 0.10, -1.12, -1.25)])
    # the guns: the chin pair, the top pair, the ball pair, the cheeks, the waist pair, the tail pair
    for sx in (-1, 1):
        tube((sx * 0.14, -9.70, -1.18), (sx * 0.14, -10.55, -1.20), 0.032, 0.026, 'sw_steel', nseg=8)
        tube((sx * 0.10, -4.95, 1.72), (sx * 0.10, -6.00, 1.80), 0.030, 0.024, 'sw_steel', nseg=8)
        tube((sx * 0.12, 3.45, -1.70), (sx * 0.12, 2.55, -1.92), 0.030, 0.024, 'sw_steel', nseg=8)
        tube((sx * 0.92, -8.55, -0.05), (sx * 1.02, -9.25, -0.05), 0.028, 0.022, 'sw_steel', nseg=8)
        tube((sx * 0.95, 5.10, 0.25), (sx * 1.65, 5.00, 0.25), 0.030, 0.024, 'sw_steel', nseg=8)
        tube((sx * 0.10, 11.80, 0.18), (sx * 0.10, 12.75, 0.18), 0.030, 0.024, 'sw_steel', nseg=8)
    # the radio mast, the pitot under the nose
    tube((0, -3.60, b.top(0, -3.60) - 0.04), (0, -3.30, b.top(0, -3.60) + 0.55), 0.040, 0.016, 'sw_paint', nseg=8, squash=0.45)
    tube((0.55, -9.80, -0.60), (0.55, -10.45, -0.60), 0.016, 0.012, 'sw_steel', nseg=6)
    # the turbo-superchargers' exhausts under each nacelle, behind the wing's leading edge
    for (x, z, y) in ((9.07, 0.02, -2.10), (-9.07, 0.02, -2.10), (4.42, -0.33, -2.70), (-4.42, -0.33, -2.70)):
        tube((x, y, z - 0.70), (x, y + 0.55, z - 0.82), 0.10, 0.09, 'sw_steel', nseg=10, squash=0.7)
