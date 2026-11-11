# The Spitfire Mk IX's four paint jobs for plane_paint.py: the day fighter scheme (Dark Green and Ocean Grey over
# Medium Sea Grey, Sky spinner and band, yellow leading edges), the desert scheme (Middle Stone and Dark Earth over
# Azure, red spinner), Malta (Dark Mediterranean Blue over Azure, white spinner) and D-Day (the day fighter scheme
# with black and white invasion stripes round the rear fuselage and the wings, black spinner). The roundels, the fin
# flash (red at the front on both sides) and the codes (the squadron's two letters ahead of the roundel on both sides,
# the aircraft's own letter behind it) are where the RAF put them.
PREFIX = 'Spit'
DKGREEN, OCEAN, MSG, SKY = C3(62, 72, 46), C3(98, 104, 112), C3(150, 156, 158), C3(196, 204, 170)
STONE, EARTH, AZURE = C3(184, 156, 106), C3(112, 88, 60), C3(108, 146, 186)
MEDBLUE, XDKBLUE = C3(54, 70, 104), C3(40, 50, 76)
RBLUE, RRED, RYEL, RWHITE = C3(38, 50, 98), C3(150, 38, 40), C3(222, 182, 40), C3(232, 232, 226)
BLACK, WHITE = C3(26, 26, 28), C3(236, 236, 232)
SCHEMES = {
    'dayfighter': dict(top=(DKGREEN, OCEAN), under=MSG, spin=SKY, band=SKY, codes=('ZX', 'G'), code_col=SKY, le_strip=True, serial='MH712'),
    'desert':     dict(top=(STONE, EARTH), under=AZURE, spin=RRED, band=None, codes=('AN', 'M'), code_col=RWHITE, le_strip=False, serial='EN204'),
    'malta':      dict(top=(MEDBLUE, XDKBLUE), under=AZURE, spin=RWHITE, band=None, codes=('GN', 'B'), code_col=RWHITE, le_strip=False, serial='BR311', soft=True),
    'invasion':   dict(top=(DKGREEN, OCEAN), under=MSG, spin=BLACK, band=None, codes=('QV', 'K'), code_col=SKY, le_strip=True, serial='ML407', stripes=True),
}

top_z, bot_z = top_line(), bottom_line()
def dem(y): return np.interp(y, [-4.0, -3.3, -2.0, 0.0, 2.0, 3.3, 4.0], [-0.24, -0.36, -0.40, -0.32, -0.06, 0.00, 0.02]).astype(np.float32)

def paint(L):
    TOP1, TOP2 = L['top']; UND = L['under']
    img[:] = UND
    # the disruptive pattern: one smooth field over the whole aircraft, cut sharply in two
    upper_f = FUS * aa(dem(Y) - Z, 0.006) + (FIN | RUD)
    upper = np.clip(UPPER_W + upper_f, 0, 1)
    n = noise3(1940, cell=0.10, sigma=3.6) + 0.30 * np.sin(X * 1.7 + 0.5)
    n -= np.median(n[upper > 0.5])                                            # half and half
    split = aa(-n, 0.012) if not L.get('soft') else sstep(n, -0.25, 0.25) * 0.6
    lay(upper, TOP1); lay(upper * split, TOP2)
    lay(EXTRA * 1.0, UND)                                                     # radiators and the chin intake
    # the bright parts
    if L['band'] is not None: lay(FUS * band(Y, 2.00, 2.42), L['band'])
    if L['le_strip']:
        s = np.abs(X)
        lay(WING * (WC < 0.055) * band(s, 2.55, 4.85) * (WC > -0.2), RYEL)
    if L.get('stripes'):
        k = np.floor((Y - 1.55) / 0.27); fs = FUS * band(Y, 1.55, 1.55 + 5 * 0.27)
        lay(fs * (k % 2 == 0), WHITE); lay(fs * (k % 2 == 1), BLACK)
        s = np.abs(X); k = np.floor((s - 1.85) / 0.30); ws = WING * band(s, 1.85, 1.85 + 5 * 0.30)
        lay(ws * (k % 2 == 0), WHITE); lay(ws * (k % 2 == 1), BLACK)
    lay(SPIN * 1.0, L['spin'])
    # exhaust staining back along the cowl from the stubs
    soot = FUS * sstep(-Y, 1.4, 2.7) * np.exp(-((Z - 0.20) / (0.06 + 0.06 * np.clip(Y + 2.8, 0, 2))) ** 2) * (Y > -3.62) * (np.abs(NX) > 0.3)
    lay(soot * 0.55, C3(44, 42, 40))
    lay(WING * (AX < 1.0) * (NZ > 0.4) * sstep(noise2(7, X, Y, 0.03, 1.2), 0.6, 1.4) * 0.25, C3(70, 66, 60))   # wear on the wing roots

    # panel lines and rivets
    add_lines(FUS, Y, [-3.62, -2.98, -2.38, -1.95, -1.65, 0.10, 0.55, 1.00, 1.45, 1.90, 2.45, 3.00, 3.50], Vc)
    add_lines(FUS & (Y < -1.95), Z, [0.30, -0.30], Y, rivets=False)
    zc_ = np.interp(Y, [-3.95, -2.5, 0.0, 2.0, 3.97], [0.08, -0.15, -0.14, 0.05, 0.06])
    ANG = np.degrees(np.arctan2(np.abs(X), Z - zc_))
    add_lines(FUS & (Y > -1.65), ANG * 0.0105, [40 * 0.0105, 110 * 0.0105], Y)
    add_lines(FUS & (X > 0) & band(Y, -1.30, -0.35).astype(bool), Z, [0.00], Y, rivets=False)        # the cockpit door, on the left
    add_lines(FUS & (X > 0) & (Z > -0.02) & (Z < 0.40), Y, [-1.30, -0.35], Z, rivets=False)
    add_lines(EXTRA, Y, [-1.55, -1.20], Vc, rivets=False)
    add_lines(WING, AX, [0.75, 1.55, 2.35, 3.20, 4.30, 5.20], Y)
    add_lines(WING, WC, [0.22, 0.62], AX, scale=1.8)
    add_lines(WING & (AX > 0.75) & (AX < 3.10), WC, [0.80], AX, scale=1.8, rivets=False)                # the flaps
    add_lines(TAIL, AX, [0.50, 1.05], Y); add_lines(TAIL, TC, [0.28], AX)
    add_lines(FIN | RUD, Z, [0.73, 1.11], Y)

    # markings
    c1 = roundel(0.45, [(1.0, RYEL), (0.89, RBLUE), (0.444, RWHITE), (0.333, RRED)])
    both_sides(FUS, c1, 0.85, 0.03)
    both_sides(FUS, text(L['codes'][0], 0.46, L['code_col']), 0.00, -0.03)
    both_sides(FUS, text(L['codes'][1], 0.46, L['code_col']), 1.68, 0.01)
    both_sides(FUS, text(L['serial'], 0.10, BLACK), 2.95, -0.07)
    both_sides(FIN, stripes(0.56, 0.60, [RRED, RWHITE, RBLUE]), 3.04, 0.72, nose_first=True)
    on_wings(roundel(0.60, [(1.0, RBLUE), (0.40, RRED)]), 3.75, upper=True, lower=False)
    on_wings(roundel(0.42, [(1.0, RBLUE), (0.50, RWHITE), (0.375, RRED)]), 3.95, upper=False, lower=True)
    GLOSS[:] = SPIN * 0.55
    return {'sw_paint': TOP1, 'sw_under': UND, 'sw_ink': C3(30, 32, 34), 'sw_frame': TOP1, 'sw_steel': C3(70, 66, 64),
            'sw_glass': (200, 220, 230), 'sw_glassframe': TOP1, 'sw_prop': BLACK}
