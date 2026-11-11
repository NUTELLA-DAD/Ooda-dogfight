# The P-38 Lightning for plane_hull.py, at the real aircraft's proportions (15.85 m span, 11.3 m long): the central
# gondola with the cockpit and the guns in its nose, two booms each with an engine, its spinner and three-blade
# propeller (turning opposite ways), the coolant radiators on the booms' sides and the turbochargers on top, and the
# tailplane between the booms with an oval fin above and below each. Built round the old model's cockpit (the panel
# at y -3.75, the seat and the eye point stay where they were) and its engines' places (x ±3.24). The old file calls
# the propeller at -x "PropL" and the rudder at +x "RudderL"; those names are kept, so the game's builder fits.
# Nose is -y, up is +z, the pilot's left is +x.
BX = 3.24
GOND = [   # y, top, bottom, half width, centre fraction, squareness
    (-6.30, -0.140, -0.360, 0.060, 0.50, 2.0), (-6.22, -0.030, -0.490, 0.190, 0.50, 2.0), (-6.05, 0.070, -0.610, 0.320, 0.50, 2.1),
    (-5.75, 0.170, -0.700, 0.425, 0.52, 2.2), (-5.35, 0.270, -0.745, 0.500, 0.54, 2.3), (-4.90, 0.345, -0.765, 0.540, 0.55, 2.35),
    (-4.40, 0.390, -0.775, 0.560, 0.55, 2.35), (-3.95, 0.410, -0.780, 0.575, 0.55, 2.35), (-3.30, 0.425, -0.780, 0.585, 0.54, 2.35),
    (-2.60, 0.420, -0.770, 0.580, 0.53, 2.3), (-2.00, 0.385, -0.740, 0.550, 0.52, 2.25), (-1.50, 0.305, -0.680, 0.490, 0.52, 2.2),
    (-1.10, 0.185, -0.580, 0.400, 0.52, 2.1), (-0.80, 0.065, -0.480, 0.280, 0.52, 2.0), (-0.60, -0.050, -0.400, 0.150, 0.50, 2.0),
    (-0.50, -0.150, -0.320, 0.040, 0.50, 2.0)]
BOOM = [   # the engine's cowl from the spinner's ring, the turbo hump over the wing, then the long taper to the tail
    (-4.95, 0.260, -0.580, 0.420, 0.50, 2.0), (-4.85, 0.335, -0.660, 0.490, 0.50, 2.0), (-4.70, 0.380, -0.720, 0.530, 0.48, 2.05),
    (-4.40, 0.400, -0.820, 0.550, 0.45, 2.1), (-3.90, 0.420, -0.880, 0.560, 0.44, 2.15), (-3.30, 0.425, -0.860, 0.560, 0.44, 2.15),
    (-2.60, 0.430, -0.740, 0.540, 0.46, 2.15), (-1.80, 0.470, -0.620, 0.500, 0.48, 2.1), (-1.20, 0.440, -0.550, 0.460, 0.48, 2.05),
    (-0.50, 0.340, -0.480, 0.400, 0.48, 2.0), (0.30, 0.270, -0.400, 0.330, 0.48, 2.0), (1.20, 0.220, -0.320, 0.270, 0.48, 2.0),
    (2.20, 0.190, -0.250, 0.220, 0.48, 2.0), (3.20, 0.160, -0.190, 0.180, 0.48, 2.0), (4.00, 0.130, -0.150, 0.150, 0.48, 2.0),
    (4.60, 0.090, -0.100, 0.090, 0.50, 2.0), (4.95, 0.030, -0.040, 0.020, 0.50, 2.0)]
FIN = {'le': [(0.0, 3.40), (0.3, 3.45), (0.6, 3.60), (0.9, 3.80), (1.1, 4.02), (1.25, 4.30)],
       'te': [(0.0, 4.95), (0.4, 4.92), (0.8, 4.85), (1.1, 4.70), (1.25, 4.45)],
       'tc': [(0, 0.09), (1.25, 0.08)], 'h0': 0.0, 'h1': 1.30, 'tip_r': 0.26,
       'rudder': {'y': 4.40, 'h': (0.10, 1.16)},
       'below': {'le': [(-0.72, 4.00), (-0.55, 3.78), (-0.30, 3.62), (-0.05, 3.55)], 'te': [(-0.72, 4.55), (-0.40, 4.78), (-0.05, 4.86)], 'h0': -0.72, 'h1': -0.05}}
SPEC = {
    'prefix': 'P38', 'ppu': 230,
    'bodies': [{'sections': GOND, 'nose_chart': 'sw_paint'},
               {'sections': BOOM, 'x': BX, 'spinner': True, 'chart': 'boomL'},
               {'sections': BOOM, 'x': -BX, 'spinner': True, 'chart': 'boomR'}],
    'cockpit': {'body': 0, 'y0': -3.95, 'y1': -1.85,
                'xs': [(-6.30, 0.04), (-3.95, 0.40), (-3.65, 0.47), (-3.10, 0.50), (-2.40, 0.48), (-2.00, 0.42), (-1.85, 0.37), (-0.50, 0.02)]},
    # the framed windscreen, the canopy's top with its frame, the rear glazing sloping into the gondola
    'canopy': {'roof': [(-3.95, 0.46), (-3.55, 0.98), (-3.10, 1.06), (-2.50, 1.05), (-2.10, 0.92), (-1.85, 0.56)],
               'roof_w': [(-3.95, 0.30), (-3.55, 0.36), (-3.00, 0.40), (-2.20, 0.34), (-1.85, 0.22)],
               'round': 2, 'screen': -3.55, 'bows': [-2.62], 'rails': [9, 13]},
    'wing': {'le': [(0, -4.30), (3.2, -4.30), (4.0, -4.22), (5.5, -4.02), (7.0, -3.80), (7.6, -3.66), (7.92, -3.40)],
             'te': [(0, -1.35), (3.2, -1.35), (4.0, -1.55), (5.5, -2.05), (7.0, -2.55), (7.6, -2.75), (7.92, -3.00)],
             'mid': [(0, -0.36), (3.2, -0.34), (7.9, -0.16)],
             'tc': [(0, 0.15), (3.2, 0.15), (7.9, 0.10)], 'camber': 0.010,
             'tip': 7.90, 'tip_r': 0.36, 'aileron': {'s': (4.95, 7.30), 'f': 0.74}},
    'tail': {'le': [(0, 3.55), (3.3, 3.55)], 'te': [(0, 4.45), (3.3, 4.45)], 'mid': [(0, 0.0), (3.3, 0.0)],
             'tc': [(0, 0.09), (3.3, 0.09)], 'tip': 3.30, 'tip_r': 0.06, 'elevator': {'s': (0.04, 2.96), 'y': 4.06}},
    'fins': [dict(FIN, x=BX, suffix='L'), dict(FIN, x=-BX, suffix='R')],
    'pit_squeeze': {'y': (-3.86, -3.66), 'z': (-0.30, 0.50)},
    'props': [{'name': 'P38_PropR', 'body': 1, 'blades': 3, 'radius': 1.48, 'pitch': 2.8, 'chord': 0.27,
               'spinner_len': 0.80, 'blade_y': 0.24, 'shape': 0.58, 'suffix': 'R', 'hand': 1},
              {'name': 'P38_PropL', 'body': 2, 'blades': 3, 'radius': 1.48, 'pitch': 2.8, 'chord': 0.27,
               'spinner_len': 0.80, 'blade_y': 0.24, 'shape': 0.58, 'suffix': 'L', 'hand': -1, 'phase': 60}],
}

def extras(C):
    loft, tube, BODY, np = C['loft'], C['tube'], C['BODY'], C['np']
    B = C['bodies']
    for b, sgn in ((B[1], 1), (B[2], -1)):
        x = b.x
        # the intercooler's intakes under the spinner, the coolant radiators on the boom's sides, the turbocharger on top
        loft(BODY, 'chin' + ('L' if sgn > 0 else 'R'), [(-4.78, 0.20, -0.62, -0.80), (-4.65, 0.24, -0.64, -0.92), (-4.30, 0.24, -0.70, -0.96), (-3.90, 0.20, -0.76, -0.94), (-3.50, 0.10, -0.80, -0.88)], mouth=0.04, x=x)
        for side in (1, -1):
            loft(BODY, 'rad' + ('L' if sgn > 0 else 'R') + ('o' if side * sgn > 0 else 'i'),
                 [(-0.70, 0.04, 0.22, -0.18), (-0.58, 0.11, 0.27, -0.25), (-0.20, 0.12, 0.28, -0.26), (0.35, 0.10, 0.24, -0.22), (0.62, 0.04, 0.17, -0.15)], mouth=0.04, x=x + side * 0.36)
        loft(BODY, 'turbo' + ('L' if sgn > 0 else 'R'), [(-2.20, 0.10, 0.50, 0.40), (-2.05, 0.15, 0.58, 0.40), (-1.70, 0.15, 0.60, 0.40), (-1.35, 0.10, 0.55, 0.40)], x=x)
    # the guns out of the gondola's nose: four .50s and the 20 mm cannon below them
    for (gx, gz, L) in ((0.10, -0.06, 0.45), (-0.10, -0.06, 0.45), (0.17, -0.20, 0.40), (-0.17, -0.20, 0.40), (0.0, -0.36, 0.30)):
        tube((gx, -5.95, gz), (gx, -6.30 - L * 0.4, gz), 0.022, 0.018, 'sw_steel', nseg=8)
    # the pitot under the left wing
    le = C['WING'][0]; mid = C['WING'][2]
    s = 6.2; tube((s, le(s) + 0.30, mid(s) - 0.12), (s, le(s) - 0.25, mid(s) - 0.12), 0.014, 0.010, 'sw_steel', nseg=6)
