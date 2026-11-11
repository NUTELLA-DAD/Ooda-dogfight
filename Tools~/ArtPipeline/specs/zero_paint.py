# The Zero's four paint jobs for plane_paint.py: the A6M2's overall grey-green (ameiro) with a carrier's red band and
# tail code, the A6M5's dark green over grey with yellow leading edges, the field-applied green mottle over the grey,
# and the A6M2-K trainer's orange. All with the black cowl, hinomaru on the fuselage sides and on both wings (white
# edged on the dark schemes) and the code across the fin and rudder.
PREFIX = 'Zero'
AMEIRO, NGREEN, LGREY, ORANGE = C3(178, 172, 142), C3(52, 68, 48), C3(176, 178, 170), C3(230, 132, 44)
COWL_C, HRED, WHITE, YEL, BLACK = C3(34, 36, 42), C3(184, 32, 38), C3(236, 236, 230), C3(226, 176, 36), C3(24, 24, 26)
METAL = C3(150, 152, 156)
SCHEMES = {
    'grey':    dict(top=AMEIRO, under=AMEIRO, mott=None, edge=False, band=HRED, code='AI-102', code_col=BLACK, le=False, spin=METAL),
    'green':   dict(top=NGREEN, under=LGREY, mott=None, edge=True, band=None, code='53-122', code_col=WHITE, le=True, spin=C3(110, 72, 52)),
    'mottle':  dict(top=AMEIRO, under=AMEIRO, mott=NGREEN, edge=True, band=None, code='V-173', code_col=BLACK, le=True, spin=METAL),
    'trainer': dict(top=ORANGE, under=ORANGE, mott=None, edge=True, band=None, code='YO-153', code_col=BLACK, le=False, spin=COWL_C),
}
def dem(y): return np.interp(y, [-4.6, -3.7, -1.0, 2.0, 3.96], [-0.42, -0.50, -0.48, -0.40, -0.30]).astype(np.float32)
COWL = FUS & (Y < -3.70)

def paint(L):
    TOP, UND = L['top'], L['under']
    img[:] = UND
    upper = np.clip(UPPER_W + FUS * aa(dem(Y) - Z, 0.04) + (FIN | RUD), 0, 1)       # soft, sprayed demarcation
    lay(upper, TOP)
    if L['mott'] is not None:
        nm = noise3(616, cell=0.05, sigma=2.2) + 0.2 * noise3(17, cell=0.05, sigma=1.0)
        lay(upper * sstep(nm, 0.15, 0.35) * 0.95, L['mott'])
    lay(EXTRA * 1.0, UND)
    # the black cowl and its intakes, the engine's face inside the ring
    lay(COWL * 1.0 + fam('carb') * 1.0 + fam('chin') * 1.0, COWL_C)
    face = FUS * (Y < -4.47) * (NY < -0.5)
    r = np.hypot(X, Z + 0.42); ang = np.arctan2(X, Z + 0.42)
    cyl = face * band(r, 0.36, 0.58) * (np.cos(ang * 14) > 0.2)
    lay(face * 1.0, C3(30, 30, 32)); lay(cyl, C3(70, 70, 72))
    if L['band'] is not None: lay(FUS * band(Y, 2.05, 2.30), L['band'])
    if L['le']: lay(WING * (WC < 0.07) * (WC > -0.2) * aa(np.abs(X) - 2.3), YEL)
    lay(SPIN * 1.0, L['spin'])
    soot = FUS * sstep(-Y, 0.6, 3.6) * np.exp(-((Z + 0.46) / (0.10 + 0.05 * np.clip(Y + 3.7, 0, 3))) ** 2) * (Y > -3.72) * (np.abs(NX) > 0.3)
    lay(soot * 0.45, C3(46, 44, 42))
    lay(WING * (AX < 1.1) * (NZ > 0.4) * sstep(noise2(9, X, Y, 0.03, 1.2), 0.5, 1.3) * 0.30, C3(120, 118, 110) if TOP is NGREEN else C3(90, 86, 72))
    # panel lines
    add_lines(FUS, Y, [-4.47, -4.05, -3.70, -3.00, -2.30, -1.80, 0.60, 1.10, 1.60, 2.10, 2.60, 3.10, 3.55], Vc)
    zc_ = np.interp(Y, [-4.6, -2.0, 0.0, 2.0, 3.96], [-0.42, -0.36, -0.30, -0.28, -0.16])
    ANG = np.degrees(np.arctan2(np.abs(X), Z - zc_))
    add_lines(FUS & (Y > -3.70), ANG * 0.0115, [45 * 0.0115, 115 * 0.0115], Y)
    add_lines(COWL, ANG * 0.0115, [70 * 0.0115], Y, rivets=False)
    add_lines(WING, AX, [1.10, 2.20, 3.55, 4.50, 5.48], Y)
    add_lines(WING, WC, [0.25, 0.66], AX, scale=1.8)
    add_lines(WING & (AX > 1.0) & (AX < 3.45), WC, [0.80], AX, scale=1.8, rivets=False)
    add_lines(TAIL, AX, [0.55, 1.25], Y); add_lines(TAIL, TC, [0.30], AX)
    add_lines(FIN | RUD, Z, [0.35, 0.80], Y)
    # markings
    ring_ = [(1.0, WHITE), (0.86, HRED)] if L['edge'] else [(1.0, HRED)]
    both_sides(FUS, roundel(0.44, ring_), 1.30, -0.30)
    both_sides(FIN | RUD, text(L['code'], 0.20, L['code_col']), 3.52, 0.55)
    on_wings(roundel(0.58, ring_), 4.25, upper=True, lower=True)
    GLOSS[:] = SPIN * 0.6 + COWL * 0.45
    return {'sw_paint': TOP, 'sw_under': UND, 'sw_ink': C3(30, 32, 34), 'sw_frame': TOP, 'sw_steel': C3(70, 66, 64),
            'sw_glass': (200, 220, 230), 'sw_glassframe': TOP, 'sw_prop': METAL}
