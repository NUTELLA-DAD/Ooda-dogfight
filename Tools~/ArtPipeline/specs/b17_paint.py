# The B-17G's four paint jobs for plane_paint.py: natural metal with a red tail and red wingtips and a square-letter
# group marking (the standard), the Olive Drab over Neutral Gray of 1943 with a triangle-letter group marking,
# natural metal with a black square, and natural metal with invasion stripes. All with the star and bars on both
# sides of the fuselage, on the upper left wing and the lower right wing, the squadron codes either side of the
# fuselage star, the serial on the fin, black de-icer boots on the leading edges, and the glazing (the Plexiglas nose,
# the flight deck, the radio room, the waist, the tail gunner's and the turrets') painted on, as the bomber flies
# with no crew in the game.
PREFIX = 'B17'
OD, NG, METALC = C3(86, 82, 58), C3(134, 136, 134), C3(178, 182, 186)
WHITE, BLACK, RED, YEL, IBLUE = C3(236, 236, 232), C3(24, 24, 26), C3(176, 34, 36), C3(226, 180, 40), C3(36, 46, 96)
GLASS, GLASS_HI = C3(46, 60, 74), C3(120, 146, 166)
SCHEMES = {
    'redtail':  dict(top=METALC, under=METALC, metal=True, codes=('LN', 'T'), code_col=BLACK, group=('square', 'J'), serial='4231909', red=True),
    'olive':    dict(top=OD, under=NG, metal=False, codes=('BK', 'D'), code_col=C3(150, 150, 150), group=('triangle', 'A'), serial='4229921'),
    'silver':   dict(top=METALC, under=METALC, metal=True, codes=('QE', 'Z'), code_col=BLACK, group=('square', 'D'), serial='4338047'),
    'invasion': dict(top=METALC, under=METALC, metal=True, codes=('XR', 'N'), code_col=BLACK, group=('triangle', 'L'), serial='4297344', stripes=True),
}
XI, XO = 4.42, 9.07
NAC = BOOM
NX_ = np.where(np.abs(X) > (XI + XO) / 2, np.sign(X) * XO, np.sign(X) * XI).astype(np.float32)      # the near nacelle's line
NZC = np.where(np.abs(X) > (XI + XO) / 2, 0.02, -0.33).astype(np.float32)
TOPT, BALLT, CHINT = famp('topT'), famp('ballT'), famp('chinT')
SX = np.abs(X)

def glass(mask, frames_y=(), frames_z=()):
    """Paint a window: dark glazing, lighter where it faces up to the sky, and its frame bars."""
    m = np.clip(mask, 0, 1)
    lay(m, GLASS); lay(m * np.clip(NZ, 0, 1) * 0.20, GLASS_HI)
    for y_ in frames_y: lay(m * band(Y, y_ - 0.02, y_ + 0.02), C3(70, 72, 74))
    for z_ in frames_z: lay(m * band(Z, z_ - 0.02, z_ + 0.02), C3(70, 72, 74))

def paint(L):
    TOP, UND = L['top'], L['under']
    img[:] = UND
    upper = np.clip(UPPER_W + FUS * aa(-0.30 - Z, 0.05) + NAC * aa(NZC - 0.15 - Z, 0.05) + (FIN | RUD) + TOPT, 0, 1)
    lay(upper, TOP)
    lay((CHINT | BALLT) * 1.0, NG if not L['metal'] else METALC)
    if L['metal']:
        METAL[:] = COV0 * 0.75; GLOSS[:] = COV0 * 0.58
        rng_ = noise2(5, X, Y, 0.45, 1.0, lo=(-17, -12), hi=(17, 14))                    # sheet to sheet, the metal's tone varies
        lay(COV0 * (rng_ > 0.4) * 0.22, C3(198, 202, 206)); lay(COV0 * (rng_ < -0.6) * 0.22, C3(150, 154, 160))
        # the olive anti-glare panels: ahead of the windscreen, and on the inboard nacelles' insides
        lay(FUS * band(Y, -9.50, -7.25) * (Z > 0.45) * aa(np.abs(X) - 0.80, 0.02), OD)
        lay(NAC * (SX < XI + 0.05) * (SX < (XI + XO) / 2) * (Z > NZC + 0.25) * (Y < -3.4), OD)
    # black de-icer boots on the leading edges, outboard of the fuselage
    lay(WING * (WC < 0.055) * (WC > -0.3) * aa(1.25 - SX), BLACK)
    lay(TAIL * (TC < 0.06) * (TC > -0.3) * aa(0.40 - SX), BLACK)
    if L.get('red'):
        lay((FIN | RUD) * aa(2.35 - Z), RED)
        lay(TAIL * aa(4.30 - SX), RED); lay(WING * aa(13.40 - SX), RED)
    if L.get('stripes'):
        k = np.floor((Y - 7.40) / 0.46); fs = FUS * band(Y, 7.40, 7.40 + 5 * 0.46)
        lay(fs * (k % 2 == 0), WHITE); lay(fs * (k % 2 == 1), BLACK)
        k = np.floor((SX - 5.55) / 0.48); ws = WING * band(SX, 5.55, 5.55 + 5 * 0.48)
        lay(ws * (k % 2 == 0), WHITE); lay(ws * (k % 2 == 1), BLACK)
    lay(SPIN * 1.0, C3(140, 142, 144) if L['metal'] else OD)
    # exhaust and turbo staining aft under the nacelles and over the wing behind them
    soot = (NAC | WING) * np.exp(-(((X - NX_) / 0.32) ** 2)) * sstep(Y, -2.6, -1.6) * sstep(-Y, -3.0, 0.5) * (NZ < 0.2)
    lay(soot * 0.40, C3(56, 52, 46))
    lay(WING * np.exp(-(((X - NX_) / 0.40) ** 2)) * sstep(Y, 0.0, 1.0) * (WC > 0.55) * (NZ > 0.3) * 0.22, C3(70, 66, 58))

    # the glazing
    glass(FUS * band(Y, -10.60, -9.50) * (1 - aa(-0.62 - Z)), frames_y=(-10.10,), frames_z=(-0.05, 0.42))     # the Plexiglas nose
    glass(FUS * band(Y, -9.25, -8.45) * band(Z, 0.12, 0.52) * (np.abs(X) > 0.6), frames_y=(-8.85,))            # the navigator's cheeks
    glass(FUS * band(Y, -7.28, -6.66) * (Z > 1.00) * aa(np.abs(X) - 0.98, 0.02), frames_y=(), frames_z=())     # the windscreen
    lay(FUS * band(Y, -7.28, -6.66) * (Z > 1.00) * band(X, -0.03, 0.03), C3(70, 72, 74))                        # its middle post
    glass(FUS * band(Y, -6.62, -5.55) * band(Z, 0.98, 1.28) * (np.abs(X) > 0.62), frames_y=(-6.10,))           # the flight deck's sides
    glass(FUS * band(Y, 1.55, 2.55) * (Z > 1.14) * aa(np.abs(X) - 0.40), frames_y=(2.05,))                     # the radio room's hatch
    glass(FUS * band(Y, 4.55, 5.65) * band(Z, 0.02, 0.62) * (np.abs(X) > 0.4), frames_y=(5.10,))               # the waist windows
    glass(FUS * band(Y, 11.10, 11.95) * band(Z, 0.18, 0.62), frames_y=(11.55,))                                # the tail gunner's
    glass(TOPT * (Z > 1.60), frames_y=(-4.85,))                                                                 # the top turret's dome
    glass(BALLT * band(Y, 3.25, 3.65) * (Z < -1.55) * aa(np.abs(X) - 0.22), frames_y=())                        # the ball turret's window
    glass(CHINT * (Y < -9.45) * (Z < -1.10) * aa(np.abs(X) - 0.20))                                             # the chin turret's

    # panel lines
    add_lines(FUS, Y, [-9.80, -8.40, -7.30, -5.40, -3.60, -1.80, 0.0, 1.30, 2.80, 4.30, 6.00, 7.60, 9.10, 10.40, 11.10], Vc)
    add_lines(FUS & (Y > -5.4) & (Y < 10.4), Z, [0.70, -0.75], Y)
    add_lines(NAC, Y, [-5.95, -5.00, -4.40, -3.50, -2.40, -1.00, 0.40], Vc)
    add_lines(NAC & (Y < -4.0), Z, [0.0, -0.40], Y, rivets=False)
    add_lines(WING, SX, [1.25, 2.80, 6.20, 7.80, 10.30, 12.40, 14.90], Y)
    add_lines(WING, WC, [0.22, 0.62], SX, scale=1.6)
    add_lines(WING & (SX < 10.2), WC, [0.76], SX, scale=1.6, rivets=False)
    add_lines(TAIL, SX, [1.6, 3.4, 5.2], Y); add_lines(TAIL, TC, [0.30], SX)
    add_lines(FIN | RUD, Z, [1.60, 2.40, 3.20], Y)

    # markings
    ins = star(0.48, WHITE, disc=IBLUE, bars=(0.48, 0.24), outline=(IBLUE, 0.05))
    both_sides(FUS, ins, 6.75, 0.20)
    on_wings(star(1.05, WHITE, disc=IBLUE, bars=(1.05, 0.52), outline=(IBLUE, 0.10)), 11.4, upper=1, lower=-1)
    both_sides(FUS, text(L['codes'][0], 0.78, L['code_col']), 3.35, 0.15)
    both_sides(FUS, text(L['codes'][1], 0.62, L['code_col']), 8.55, -0.12)
    shape, letter = L['group']
    gc = WHITE if not L['metal'] else BLACK; lc = BLACK if not L['metal'] else WHITE
    if L.get('red'): gc, lc = WHITE, BLACK
    def group(d, W, H, res):
        if shape == 'square': d.rectangle((0, 0, W, H), fill=rgba(gc))
        else: d.polygon([(W / 2, 0), (W, H), (0, H)], fill=rgba(gc))
        f = ImageFont.truetype(FONT, int(H * (0.62 if shape == 'square' else 0.48))); bb = f.getbbox(letter)
        d.text(((W - (bb[2] - bb[0])) / 2 - bb[0], (H * (0.5 if shape == 'square' else 0.64)) - (bb[3] - bb[1]) / 2 - bb[1]), letter, font=f, fill=rgba(lc))
    both_sides(FIN | RUD, decal(group, 1.15, 1.15), 10.15, 2.95)
    both_sides(FIN | RUD, text(L['serial'], 0.34, YEL if not L['metal'] else BLACK), 10.55, 1.95)
    on_wings(decal(group, 1.6, 1.6), 11.4, upper=-1, lower=False)                       # the group's marking on the right wing too
    GLOSS[:] = np.maximum(GLOSS, SPIN * 0.6)
    return {'sw_paint': TOP, 'sw_under': UND, 'sw_ink': C3(30, 32, 34), 'sw_steel': C3(70, 66, 64), 'sw_prop': BLACK}
