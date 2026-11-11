# The P-38's four paint jobs for plane_paint.py: Olive Drab over Neutral Gray; natural metal with an olive anti-glare
# panel and red spinners; Olive Drab with black and white invasion stripes round the booms and the outer wings; and
# the Pacific's Olive Drab with a white tail and white leading edges. The star and bars sit on both sides of each
# boom, on the upper left wing and the lower right wing as the USAAF put them, the serial on each fin, a number on
# the nose.
PREFIX = 'P38'
BX = 3.24
OD, NG, METALC, IBLUE = C3(84, 80, 58), C3(132, 134, 132), C3(176, 180, 184), C3(36, 46, 96)
WHITE, BLACK, RED, YEL = C3(236, 236, 232), C3(26, 26, 28), C3(170, 36, 36), C3(222, 182, 40)
SCHEMES = {
    'olive':    dict(top=OD, under=NG, metal=False, spin=OD, num='23', num_col=WHITE, serial='267543', ser_col=YEL),
    'silver':   dict(top=METALC, under=METALC, metal=True, spin=RED, num='47', num_col=BLACK, serial='4423715', ser_col=BLACK, tips=RED),
    'invasion': dict(top=OD, under=NG, metal=False, spin=OD, num='61', num_col=WHITE, serial='4223406', ser_col=YEL, stripes=True),
    'pacific':  dict(top=OD, under=NG, metal=False, spin=WHITE, num='12', num_col=WHITE, serial='4266502', ser_col=BLACK, white_tail=True),
}
FINL, FINR = fam('finL', 'rudL'), fam('finR', 'rudR')
BOOMS = BOOM | famp('turbo')
BX_ = np.where(X > 0, BX, -BX).astype(np.float32)          # the near boom's line

def paint(L):
    TOP, UND = L['top'], L['under']
    img[:] = UND
    upper = np.clip(UPPER_W + FUS * aa(-0.28 - Z, 0.03) + BOOMS * aa(-0.08 - Z, 0.03) + (FIN | RUD), 0, 1)
    lay(upper, TOP)
    lay(EXTRA * 1.0, UND); lay(famp('turbo') * 1.0, TOP); lay(famp('rad') * aa(-0.02 - Z, 0.03), TOP)
    if L['metal']:
        METAL[:] = COV0 * 0.75; GLOSS[:] = COV0 * 0.62
        rng_ = noise2(5, X, Y, 0.30, 1.0, lo=(-9, -8), hi=(9, 8))                    # sheet to sheet, the metal's tone varies
        lay(COV0 * (rng_ > 0.4) * 0.25, C3(196, 200, 204)); lay(COV0 * (rng_ < -0.6) * 0.25, C3(150, 154, 160))
        # the olive anti-glare panel ahead of the windscreen
        lay(FUS * (Y < -3.90) * (Z > 0.20) * aa(np.abs(X) - 0.30, 0.01), OD)
    if L.get('tips') is not None:
        lay((FIN | RUD) * aa(1.05 - Z) * (Z > 0), L['tips'])
    if L.get('white_tail'):
        lay((FIN | RUD | TAIL) * 1.0, WHITE)
        lay(WING * (WC < 0.06) * (WC > -0.2) * aa(np.abs(X) - 7.2), WHITE)
    if L.get('stripes'):
        k = np.floor((Y - 0.45) / 0.26); bs = BOOMS * band(Y, 0.45, 0.45 + 5 * 0.26)
        lay(bs * (k % 2 == 0), WHITE); lay(bs * (k % 2 == 1), BLACK)
        s = np.abs(X); k = np.floor((s - 4.20) / 0.30); ws = WING * band(s, 4.20, 4.20 + 5 * 0.30)
        lay(ws * (k % 2 == 0), WHITE); lay(ws * (k % 2 == 1), BLACK)
    lay(SPIN * 1.0, L['spin'])
    # exhaust and turbo staining back along the booms' tops
    soot = BOOMS * sstep(Y, -1.40, -0.9) * sstep(-Y, -1.6, 0.4) * np.exp(-(((X - BX_) / 0.20) ** 2)) * (Z > 0.2)
    lay(soot * 0.45, C3(52, 50, 46))
    # panel lines
    add_lines(FUS, Y, [-5.90, -5.30, -4.70, -3.95, -1.85, -1.20], Vc)
    add_lines(BOOMS, Y, [-4.80, -4.30, -3.60, -2.90, -2.20, -1.30, -0.95, 0.80, 1.80, 2.80, 3.60, 4.30], Vc)
    add_lines(BOOMS & (Y < -2.20), Z, [0.10, -0.40], Y, rivets=False)
    add_lines(WING, AX, [1.00, 2.20, 4.30, 5.40, 6.50], Y)
    add_lines(WING, WC, [0.25, 0.65], AX, scale=1.8)
    add_lines(WING & (AX < 4.9), WC, [0.78], AX, scale=1.8, rivets=False)
    add_lines(TAIL, AX, [1.0, 2.0], Y); add_lines(TAIL, TC, [0.30], AX)
    add_lines(FIN | RUD, Z, [0.45, 0.85], Y)
    # markings
    ins = star(0.23, WHITE, disc=IBLUE, bars=(0.23, 0.115), outline=(IBLUE, 0.025))
    for bx in (BX, -BX): both_sides(BOOMS, ins, 1.25 if not L.get('stripes') else 2.30, 0.00, x0=bx)
    on_wings(star(0.50, WHITE, disc=IBLUE, bars=(0.50, 0.25), outline=(IBLUE, 0.05)), 6.0, upper=1, lower=-1)
    both_sides(FUS, text(L['num'], 0.32, L['num_col']), -5.25, -0.22)
    for bx, m in ((BX, FINL), (-BX, FINR)): both_sides(m, text(L['serial'], 0.14, L['ser_col']), 4.15, 0.55, x0=bx)
    GLOSS[:] = np.maximum(GLOSS, SPIN * 0.6)
    return {'sw_paint': TOP, 'sw_under': UND, 'sw_ink': C3(30, 32, 34), 'sw_frame': TOP, 'sw_steel': C3(70, 66, 64),
            'sw_glass': (200, 220, 230), 'sw_glassframe': TOP, 'sw_prop': BLACK}
