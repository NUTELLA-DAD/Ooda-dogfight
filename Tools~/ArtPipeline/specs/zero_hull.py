# The A6M Zero for plane_hull.py, at the real aircraft's proportions (12 m span, 9.1 m long) but for a fuselage as wide as
# the old model's roomy cartoon cockpit (1.4 m), built round that cockpit (its instrument panel at y -1.70, the seat and the eye point stay where they were). The radial's
# cowl is round and open at the front: the spinner leaves its first ring, the engine's face fills the ring behind it.
# The greenhouse keeps its long glazing and the rails along its sides. Nose is -y, up is +z, the pilot's left is +x.
SPEC = {
    'prefix': 'Zero', 'ppu': 300,
    'bodies': [{
        'sections': [   # y, top, bottom, half width, centre fraction, squareness
            (-4.600, -0.120, -0.720, 0.300, 0.50, 2.00), (-4.575, 0.050, -0.890, 0.470, 0.50, 2.00), (-4.540, 0.160, -1.000, 0.580, 0.50, 2.00),
            (-4.480, 0.225, -1.065, 0.645, 0.50, 2.00), (-4.350, 0.255, -1.095, 0.675, 0.50, 2.00), (-4.050, 0.265, -1.105, 0.688, 0.50, 2.05),
            (-3.700, 0.275, -1.100, 0.690, 0.52, 2.05), (-3.300, 0.300, -1.090, 0.690, 0.52, 2.20), (-2.800, 0.340, -1.075, 0.695, 0.52, 2.20),
            (-2.300, 0.380, -1.060, 0.700, 0.52, 2.20), (-1.800, 0.420, -1.050, 0.705, 0.52, 2.20), (-1.300, 0.440, -1.040, 0.705, 0.52, 2.20),
            (-0.800, 0.440, -1.020, 0.700, 0.52, 2.20), (-0.300, 0.430, -1.000, 0.685, 0.52, 2.20), (0.200, 0.400, -0.965, 0.650, 0.52, 2.20),
            (0.700, 0.340, -0.920, 0.595, 0.48, 2.05), (1.200, 0.270, -0.860, 0.530, 0.48, 2.05), (1.700, 0.200, -0.790, 0.460, 0.48, 2.05),
            (2.200, 0.135, -0.710, 0.385, 0.48, 2.05), (2.700, 0.080, -0.620, 0.305, 0.48, 2.00), (3.200, 0.035, -0.515, 0.225, 0.50, 2.00),
            (3.600, 0.002, -0.410, 0.148, 0.50, 2.00), (3.850, -0.020, -0.330, 0.078, 0.50, 2.00), (3.960, -0.040, -0.280, 0.020, 0.50, 2.00)],
        'spinner': True,
    }],
    'cockpit': {'body': 0, 'y0': -1.80, 'y1': 0.55,
                'xs': [(-4.60, 0.24), (-1.80, 0.50), (-1.50, 0.60), (-0.80, 0.64), (0.00, 0.60), (0.40, 0.52), (0.55, 0.46), (1.50, 0.15), (3.96, 0.008)]},
    # the greenhouse: a framed windscreen, the sliding hood with its bows and side rails, the fixed glazing tapering aft
    'canopy': {'roof': [(-1.80, 0.54), (-1.42, 1.02), (-1.00, 1.12), (-0.40, 1.13), (0.10, 1.05), (0.55, 0.64)],
               'roof_w': [(-1.80, 0.34), (-1.42, 0.42), (-0.80, 0.46), (0.00, 0.42), (0.55, 0.26)],
               'round': 2, 'screen': -1.42, 'bows': [-0.98, -0.52, -0.06, 0.28], 'rails': [7, 15]},
    'wing': {'le': [(0, -3.20), (1.0, -3.17), (2.0, -3.12), (3.0, -3.06), (4.0, -2.99), (4.8, -2.92), (5.3, -2.84),
                    (5.6, -2.72), (5.85, -2.52), (6.0, -2.20)],
             'te': [(0, -0.55), (1.0, -0.80), (2.0, -1.05), (3.0, -1.30), (4.0, -1.55), (4.8, -1.74), (5.3, -1.85),
                    (5.6, -1.92), (5.85, -2.00), (6.0, -2.10)],
             'mid': [(0, -0.80), (1.0, -0.695), (3.0, -0.485), (6.0, -0.17)],
             'tc': [(0, 0.15), (2.0, 0.14), (6.0, 0.09)], 'camber': 0.012,
             'tip': 5.98, 'tip_r': 0.40, 'aileron': {'s': (3.55, 5.45), 'f': 0.76, 'f1': 0.70}},
    'tail': {'le': [(0, 2.45), (0.5, 2.55), (1.0, 2.66), (1.5, 2.80), (1.9, 2.97), (2.2, 3.18), (2.40, 3.45)],
             'te': [(0, 3.65), (0.5, 3.66), (1.0, 3.64), (1.5, 3.60), (1.9, 3.56), (2.2, 3.52), (2.40, 3.48)],
             'mid': [(0, -0.18), (2.4, -0.17)], 'tc': [(0, 0.10), (2.4, 0.08)],
             'tip': 2.40, 'tip_r': 0.26, 'elevator': {'s': (0.22, 2.20), 'y': 3.24}},
    'fins': [{'le': [(0.0, 2.55), (0.2, 2.80), (0.4, 3.00), (0.6, 3.17), (0.8, 3.32), (1.0, 3.45), (1.15, 3.56), (1.30, 3.72)],
              'te': [(0.0, 4.02), (0.4, 4.00), (0.8, 3.96), (1.0, 3.93), (1.15, 3.88), (1.30, 3.80)],
              'tc': [(0, 0.09), (1.3, 0.08)], 'h0': -0.04, 'h1': 1.30, 'tip_r': 0.24,
              'rudder': {'y': 3.62, 'h': (-0.04, 1.18)}}],
    'pit_squeeze': {'y': (-1.86, -1.68), 'z': (-0.35, 0.45)},     # the old instrument panel, a little wider than the new hull
    'props': [{'name': 'Zero_Propeller', 'body': 0, 'blades': 3, 'radius': 1.45, 'pitch': 2.6, 'chord': 0.27,
               'spinner_len': 0.55, 'blade_y': 0.20, 'shape': 0.55}],
}

def extras(C):
    loft, tube, BODY, np, math = C['loft'], C['tube'], C['BODY'], C['np'], C['math']
    b = C['bodies'][0]
    # the oil cooler's intake under the cowl lip, and the carburettor's on top of it
    loft(BODY, 'chin', [(-4.40, 0.10, -0.98, -1.12), (-4.30, 0.12, -1.00, -1.17), (-4.00, 0.12, -1.03, -1.18), (-3.70, 0.09, -1.05, -1.15), (-3.55, 0.05, -1.06, -1.11)], mouth=0.03)
    loft(BODY, 'carb', [(-4.42, 0.07, 0.30, 0.21), (-4.30, 0.08, 0.32, 0.21), (-4.05, 0.07, 0.31, 0.22), (-3.85, 0.03, 0.285, 0.24)], mouth=0.03)
    # exhaust pipes out of the cowl's trailing edge, low on each side
    for sgn in (-1, 1):
        for k, zz in enumerate((-0.32, -0.60)):
            y = -3.72; hx = b.half_x(y, zz)
            tube((sgn * (hx - 0.05), y - 0.05, zz), (sgn * (hx + 0.05), y + 0.12, zz - 0.02), 0.055, 0.050, 'sw_steel', nseg=10, squash=0.8)
    # the two cowl guns' troughs' muzzles, the 20 mm cannon out of the wings, the mast and the pitot
    for sgn in (-1, 1):
        tube((sgn * 0.14, -3.20, 0.34), (sgn * 0.14, -3.75, 0.25), 0.028, 0.022, 'sw_steel', nseg=8)
    le = C['WING'][0]; mid = C['WING'][2]
    for sgn in (-1, 1):
        s = 2.35; tube((sgn * s, le(s) + 0.25, mid(s) - 0.03), (sgn * s, le(s) - 0.50, mid(s) - 0.03), 0.040, 0.030, 'sw_steel', nseg=10)
    tube((0, 0.80, b.top(0, 0.80) - 0.03), (0, 0.68, b.top(0, 0.80) + 0.72), 0.030, 0.014, 'sw_paint', nseg=8, squash=0.45)
    s = 4.9; tube((s, le(s) + 0.30, mid(s) - 0.08), (s, le(s) - 0.20, mid(s) - 0.08), 0.014, 0.010, 'sw_steel', nseg=6)
