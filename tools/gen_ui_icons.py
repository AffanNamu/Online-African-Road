#!/usr/bin/env python3
"""Draws the login/menu icons and the logo mark -> Assets/_Project/Resources/UI/*.png (128px, white or brand colour, tinted in Unity).
Placeholder brand art, generated from code. Replace any PNG with hand-made art of the same name to override it."""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets/_Project/Resources/UI"
os.makedirs(OUT, exist_ok=True)
S = 4; N = 128; M = N * S           # supersample 4x
GOLD = (249, 181, 33, 255); WHITE = (255, 255, 255, 255)

def canvas(): 
    im = Image.new("RGBA", (M, M), (0, 0, 0, 0)); return im, ImageDraw.Draw(im)
def save(im, name): im.resize((N, N), Image.LANCZOS).save(f"{OUT}/{name}.png")
def P(x, y): return (x * M, y * M)
def line(d, pts, w, col=WHITE, joint=True):
    pts = [P(*p) for p in pts]; d.line(pts, fill=col, width=int(w * M), joint="curve" if joint else None)
    for p in (pts[0], pts[-1]):
        r = w * M / 2; d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=col)

def mail():
    im, d = canvas()
    d.rounded_rectangle([P(.14, .24), P(.86, .76)], radius=.08 * M, outline=WHITE, width=int(.07 * M))
    line(d, [(.17, .30), (.5, .56), (.83, .30)], .07); save(im, "icon_mail")
def lock():
    im, d = canvas()
    d.arc([P(.30, .12), P(.70, .56)], 180, 360, fill=WHITE, width=int(.07 * M))
    line(d, [(.31, .34), (.31, .46)], .07); line(d, [(.69, .34), (.69, .46)], .07)
    d.rounded_rectangle([P(.22, .44), P(.78, .86)], radius=.07 * M, outline=WHITE, width=int(.07 * M))
    d.ellipse([P(.45, .58), P(.55, .68)], fill=WHITE); d.rectangle([P(.485, .64), P(.515, .76)], fill=WHITE); save(im, "icon_lock")
def eye(off=False):
    im, d = canvas()
    top = [(0.10 + 0.8 * t / 24, 0.5 - 0.27 * math.sin(math.pi * t / 24) ** 1.0) for t in range(25)]
    bot = [(0.10 + 0.8 * t / 24, 0.5 + 0.27 * math.sin(math.pi * t / 24) ** 1.0) for t in range(25)]
    line(d, top, .065); line(d, bot, .065)
    d.ellipse([P(.38, .38), P(.62, .62)], outline=WHITE, width=int(.065 * M))
    if off: line(d, [(.18, .86), (.84, .16)], .075)
    save(im, "icon_eye_off" if off else "icon_eye")
def user():
    im, d = canvas(); w = int(.065 * M)
    d.ellipse([P(.33, .14), P(.67, .48)], outline=WHITE, width=w)
    d.arc([P(.16, .56), P(.84, 1.20)], 180, 360, fill=WHITE, width=w)
    line(d, [(.16, .88), (.84, .88)], .065); save(im, "icon_user")
def check():
    im, d = canvas(); line(d, [(.22, .52), (.43, .72), (.80, .30)], .13); save(im, "icon_check")
def arrow():
    im, d = canvas(); line(d, [(.14, .5), (.84, .5)], .09); line(d, [(.58, .24), (.86, .5), (.58, .76)], .09); save(im, "icon_arrow")
def chevron():
    im, d = canvas(); line(d, [(.22, .38), (.5, .66), (.78, .38)], .10); save(im, "icon_chevron")
def globe():
    im, d = canvas(); w = int(.06 * M)
    d.ellipse([P(.14, .14), P(.86, .86)], outline=WHITE, width=w)
    d.ellipse([P(.36, .14), P(.64, .86)], outline=WHITE, width=w)
    line(d, [(.14, .5), (.86, .5)], .06); line(d, [(.22, .31), (.78, .31)], .05); line(d, [(.22, .69), (.78, .69)], .05); save(im, "icon_globe")
def google():
    im, d = canvas(); box = [P(.14, .14), P(.86, .86)]; w = int(.17 * M)
    for a0, a1, col in [(215, 325, (234, 67, 53, 255)), (135, 215, (251, 188, 5, 255)), (45, 135, (52, 168, 83, 255)), (-35, 45, (66, 133, 244, 255))]:
        d.arc(box, a0, a1, fill=col, width=w)
    d.rectangle([P(.50, .43), P(.86, .57)], fill=(66, 133, 244, 255)); save(im, "icon_google")
def apple():
    im, d = canvas()
    d.ellipse([P(.20, .30), P(.52, .88)], fill=WHITE); d.ellipse([P(.48, .30), P(.80, .88)], fill=WHITE)
    d.rounded_rectangle([P(.30, .36), P(.70, .86)], radius=.1 * M, fill=WHITE)
    d.ellipse([P(.72, .30), P(.98, .56)], fill=(0, 0, 0, 0))                       # bite (cut out below)
    im2 = Image.new("RGBA", (M, M), (0, 0, 0, 0)); d2 = ImageDraw.Draw(im2)
    d2.ellipse([P(.72, .30), P(.98, .56)], fill=(255, 255, 255, 255))
    a = im.split()[3]; b = im2.split()[3]; a = Image.composite(Image.new("L", (M, M), 0), a, b); im.putalpha(a); d = ImageDraw.Draw(im)
    d.ellipse([P(.38, .27), P(.50, .34)], fill=(0, 0, 0, 0))
    d.polygon([P(.50, .30), P(.56, .12), P(.68, .08), P(.62, .24)], fill=WHITE)    # leaf
    d.rectangle([P(.46, .20), P(.52, .32)], fill=(0, 0, 0, 0)); save(im, "icon_apple")

def feat_sign():     # warning road sign
    im, d = canvas(); d.polygon([P(.50, .12), P(.92, .84), P(.08, .84)], fill=GOLD)
    d.polygon([P(.50, .30), P(.76, .74), P(.24, .74)], fill=(0, 0, 0, 0)); d.polygon([P(.50, .12), P(.92, .84), P(.08, .84)], outline=GOLD, width=int(.04 * M))
    # carve inner triangle then draw "!"
    a = im.split()[3]; m = Image.new("L", (M, M), 0); ImageDraw.Draw(m).polygon([P(.50, .30), P(.77, .76), P(.23, .76)], fill=255)
    a = Image.composite(Image.new("L", (M, M), 0), a, m); im.putalpha(a); d = ImageDraw.Draw(im)
    d.polygon([P(.50, .36), P(.72, .72), P(.28, .72)], fill=GOLD)
    d.polygon([P(.50, .12), P(.92, .84), P(.08, .84)], outline=GOLD, width=int(.05 * M))
    d.rounded_rectangle([P(.47, .44), P(.53, .63)], radius=.02 * M, fill=(30, 26, 22, 255)); d.ellipse([P(.465, .66), P(.535, .73)], fill=(30, 26, 22, 255)); save(im, "feat_world")
def feat_people():
    im, d = canvas()
    for cx, cy, r, bw in [(.5, .30, .12, .24), (.24, .40, .09, .18), (.76, .40, .09, .18)]:
        d.ellipse([P(cx - r, cy - r), P(cx + r, cy + r)], fill=GOLD)
        d.rounded_rectangle([P(cx - bw, cy + r + .03), P(cx + bw, cy + r + .03 + bw * 1.9)], radius=bw * .8 * M, fill=GOLD)
    save(im, "feat_people")
def feat_trucks():
    im, d = canvas()
    d.rounded_rectangle([P(.06, .30), P(.58, .68)], radius=.03 * M, fill=GOLD)
    d.polygon([P(.60, .38), P(.78, .38), P(.92, .54), P(.92, .68), P(.60, .68)], fill=GOLD)
    d.polygon([P(.66, .42), P(.76, .42), P(.85, .53), P(.66, .53)], fill=(30, 26, 22, 255))
    for cx in (.22, .45, .76): d.ellipse([P(cx - .075, .60), P(cx + .075, .75)], fill=(30, 26, 22, 255)); d.ellipse([P(cx - .04, .64), P(cx + .04, .71)], fill=GOLD)
    save(im, "feat_vehicles")
def feat_jobs():
    im, d = canvas()
    d.rounded_rectangle([P(.38, .20), P(.62, .34)], radius=.03 * M, outline=GOLD, width=int(.05 * M))
    d.rounded_rectangle([P(.10, .30), P(.90, .80)], radius=.06 * M, fill=GOLD)
    d.rectangle([P(.10, .52), P(.90, .55)], fill=(30, 26, 22, 255)); d.rounded_rectangle([P(.44, .48), P(.56, .62)], radius=.02 * M, fill=(30, 26, 22, 255)); save(im, "feat_jobs")
def feat_weather():
    im, d = canvas()
    cx, cy = .62, .38
    for a in range(0, 360, 45):
        r0, r1 = .20, .30; ax, ay = math.cos(math.radians(a)), math.sin(math.radians(a))
        line(d, [(cx + ax * r0, cy + ay * r0), (cx + ax * r1, cy + ay * r1)], .05, GOLD)
    d.ellipse([P(cx - .15, cy - .15), P(cx + .15, cy + .15)], fill=GOLD)
    for ex, ey, r in [(.30, .66, .15), (.48, .58, .19), (.68, .66, .15)]: d.ellipse([P(ex - r, ey - r), P(ex + r, ey + r)], fill=(30, 26, 22, 255))
    d.rounded_rectangle([P(.16, .62), P(.84, .80)], radius=.09 * M, fill=(30, 26, 22, 255))
    for ex, ey, r in [(.30, .66, .115), (.48, .58, .155), (.68, .66, .115)]: d.ellipse([P(ex - r, ey - r), P(ex + r, ey + r)], fill=GOLD)
    d.rounded_rectangle([P(.19, .64), P(.81, .77)], radius=.07 * M, fill=GOLD); save(im, "feat_weather")

AFRICA = [(-5.9,35.8),(-2,35.1),(3,36.8),(10.2,37.2),(11,33.5),(15,32.3),(19.5,30.3),(24,32.1),(32,31.3),(34.2,27.8),(37,21),(39,15.6),(43,12.7),(51.2,11.8),(48,5),(42,-1),(39.3,-5),(40.5,-10.5),(40.6,-15),(35,-20),(35.5,-24),(32.8,-26.5),(32.5,-29),(27,-33.7),(20,-34.8),(18.4,-34),(15,-27),(12,-18),(13.7,-11),(12.3,-5.5),(9,-1),(9.7,3.9),(5,5.9),(-2,4.8),(-7.5,4.4),(-13,8.5),(-17.5,14.7),(-16.5,19),(-14.5,26),(-10,29.5),(-9.8,31.5),(-8.5,33.3),(-6.8,34.2)]
def logo():
    W = 512; sc = 4; im = Image.new("RGBA", (W * sc, W * sc), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    def Q(x, y): return (x * W * sc, y * W * sc)
    # gold gradient fill mask pieces drawn in a mask, then coloured with a vertical gradient
    mask = Image.new("L", im.size, 0); md = ImageDraw.Draw(mask)
    # A: left leg + right leg (slanted bars), apex at top
    md.polygon([Q(.50, .04), Q(.62, .04), Q(.98, .94), Q(.84, .94), Q(.55, .22)], fill=255)          # right leg
    md.polygon([Q(.50, .04), Q(.40, .04), Q(.04, .94), Q(.18, .94), Q(.45, .22)], fill=255)          # left leg
    # Africa silhouette inside the counter
    lon0, lon1, lat0, lat1 = -18, 52, 38, -35.5; ax0, ax1, ay0, ay1 = .385, .615, .46, .745
    pts = [Q(ax0 + (lo - lon0) / (lon1 - lon0) * (ax1 - ax0), ay0 + (la - lat0) / (lat1 - lat0) * (ay1 - ay0)) for lo, la in AFRICA]
    md.polygon(pts, fill=255)
    # crossbar stripe under Africa (the road)
    md.polygon([Q(.30, .84), Q(.70, .84), Q(.72, .89), Q(.28, .89)], fill=255)
    grad = Image.new("RGBA", im.size); gd = ImageDraw.Draw(grad)
    for y in range(im.size[1]):
        f = y / im.size[1]; c = (int(255 - 40 * f), int(214 - 70 * f), int(92 - 60 * f), 255); gd.line([(0, y), (im.size[0], y)], fill=c)
    im = Image.composite(grad, im, mask)
    # thin dark road-stripe cuts through the A's right leg for the "speed" feel
    cut = ImageDraw.Draw(im)
    im = im.resize((W, W), Image.LANCZOS); im.save(f"{OUT}/logo_mark.png")

for f in (mail, lock, user, lambda: eye(False), lambda: eye(True), check, arrow, chevron, globe, google, apple, feat_sign, feat_people, feat_trucks, feat_jobs, feat_weather, logo): f()
# contact sheet for review (not shipped)
names = ["icon_user","icon_mail","icon_lock","icon_eye","icon_eye_off","icon_check","icon_arrow","icon_chevron","icon_globe","icon_google","icon_apple","feat_world","feat_people","feat_vehicles","feat_jobs","feat_weather","logo_mark"]
sheet = Image.new("RGBA", (9 * 144, 2 * 144), (30, 30, 36, 255))
for i, n in enumerate(names):
    ic = Image.open(f"{OUT}/{n}.png").convert("RGBA").resize((128, 128)); sheet.alpha_composite(ic, ((i % 9) * 144 + 8, (i // 9) * 144 + 8))
sheet.save("/tmp/icon_sheet.png"); print("icons ->", OUT)

# ---------------------------------------------------------------- dashboard glyph set (white, tinted in code)
def ic(name):
    def deco(fn):
        def run():
            im, d = canvas(); fn(im, d); save(im, name)
        run.__name__ = name; GLYPHS.append(run); return run
    return deco
GLYPHS = []
def poly(d, pts, col=WHITE): d.polygon([P(*p) for p in pts], fill=col)
def rrect(d, a, b, r, col=WHITE, outline=None, w=0): d.rounded_rectangle([P(*a), P(*b)], radius=r * M, fill=col, outline=outline, width=int(w * M))
def circle(d, c, r, col=WHITE, outline=None, w=0): d.ellipse([P(c[0] - r, c[1] - r), P(c[0] + r, c[1] + r)], fill=col, outline=outline, width=int(w * M))
def cut(im, fn):
    m = Image.new("L", (M, M), 0); fn(ImageDraw.Draw(m)); a = Image.composite(Image.new("L", (M, M), 0), im.split()[3], m); im.putalpha(a)

@ic("ic_home")
def _(im, d):
    poly(d, [(.5, .12), (.92, .50), (.82, .50), (.82, .86), (.18, .86), (.18, .50), (.08, .50)]); cut(im, lambda c: c.rectangle([P(.42, .56), P(.58, .86)], fill=255))
@ic("ic_jobs")
def _(im, d):
    rrect(d, (.38, .18), (.62, .34), .03, (0, 0, 0, 0), WHITE, .06); rrect(d, (.10, .30), (.90, .82), .07); cut(im, lambda c: (c.rectangle([P(.10, .52), P(.90, .55)], fill=255), c.rounded_rectangle([P(.44, .47), P(.56, .62)], radius=.02 * M, fill=255)))
@ic("ic_people")
def _(im, d):
    for cx, cy, r, bw in [(.5, .30, .13, .26), (.2, .40, .09, .17), (.8, .40, .09, .17)]:
        circle(d, (cx, cy), r); rrect(d, (cx - bw, cy + r + .03), (cx + bw, cy + r + .03 + bw * 1.8), bw * .8)
@ic("ic_bus")
def _(im, d):
    rrect(d, (.14, .14), (.86, .80), .10); cut(im, lambda c: (c.rounded_rectangle([P(.22, .22), P(.78, .50)], radius=.04 * M, fill=255), c.ellipse([P(.24, .60), P(.34, .70)], fill=255), c.ellipse([P(.66, .60), P(.76, .70)], fill=255)))
    rrect(d, (.20, .76), (.34, .92), .03); rrect(d, (.66, .76), (.80, .92), .03)
@ic("ic_wrench")
def _(im, d):
    line(d, [(.30, .72), (.62, .40)], .15); circle(d, (.66, .34), .20); cut(im, lambda c: (c.ellipse([P(.60, .22), P(.82, .44)], fill=255), c.polygon([P(.72, .30), P(.96, .06), P(.96, .36), P(.72, .36)], fill=255)))
    circle(d, (.24, .78), .08)
@ic("ic_map")
def _(im, d):
    poly(d, [(.10, .22), (.36, .12), (.64, .22), (.90, .12), (.90, .78), (.64, .88), (.36, .78), (.10, .88)]); cut(im, lambda c: (c.line([P(.36, .16), P(.36, .78)], fill=255, width=int(.05 * M)), c.line([P(.64, .24), P(.64, .86)], fill=255, width=int(.05 * M))))
@ic("ic_trophy")
def _(im, d):
    poly(d, [(.26, .12), (.74, .12), (.70, .46), (.5, .62), (.30, .46)]); d.arc([P(.06, .16), P(.34, .44)], 90, 270, fill=WHITE, width=int(.055 * M)); d.arc([P(.66, .16), P(.94, .44)], 270, 90, fill=WHITE, width=int(.055 * M))
    rrect(d, (.44, .58), (.56, .76), .02); rrect(d, (.30, .76), (.70, .88), .03)
@ic("ic_cart")
def _(im, d):
    line(d, [(.08, .16), (.22, .16), (.32, .62), (.78, .62), (.88, .28), (.26, .28)], .07); circle(d, (.38, .80), .07); circle(d, (.72, .80), .07)
@ic("ic_gear")
def _(im, d):
    for a in range(0, 360, 45):
        ax, ay = math.cos(math.radians(a)), math.sin(math.radians(a)); line(d, [(.5 + ax * .26, .5 + ay * .26), (.5 + ax * .40, .5 + ay * .40)], .13)
    circle(d, (.5, .5), .30); cut(im, lambda c: c.ellipse([P(.36, .36), P(.64, .64)], fill=255))
@ic("ic_bell")
def _(im, d):
    poly(d, [(.20, .72), (.28, .68), (.28, .44)]); d.pieslice([P(.24, .12), P(.76, .64)], 180, 360, fill=WHITE); rrect(d, (.24, .38), (.76, .72), .02); rrect(d, (.14, .68), (.86, .78), .04); circle(d, (.5, .86), .08); circle(d, (.5, .12), .05)
@ic("ic_coin")
def _(im, d):
    circle(d, (.5, .5), .42); cut(im, lambda c: c.ellipse([P(.18, .18), P(.82, .82)], fill=255)); circle(d, (.5, .5), .34)
    cut(im, lambda c: (c.rectangle([P(.46, .22), P(.54, .78)], fill=255), c.arc([P(.32, .28), P(.68, .52)], 90, 360, fill=255, width=int(.07 * M)), c.arc([P(.32, .48), P(.68, .74)], 270, 180, fill=255, width=int(.07 * M))))
@ic("ic_crown")
def _(im, d):
    poly(d, [(.10, .72), (.14, .30), (.34, .52), (.5, .22), (.66, .52), (.86, .30), (.90, .72)]); rrect(d, (.10, .76), (.90, .88), .03)
@ic("ic_pin")
def _(im, d):
    d.pieslice([P(.22, .10), P(.78, .66)], 180, 360, fill=WHITE); d.ellipse([P(.22, .10), P(.78, .66)], fill=WHITE); poly(d, [(.26, .52), (.74, .52), (.5, .92)]); cut(im, lambda c: c.ellipse([P(.40, .24), P(.60, .44)], fill=255))
@ic("ic_chevron_right")
def _(im, d): line(d, [(.36, .20), (.66, .50), (.36, .80)], .11)
@ic("ic_bars")
def _(im, d):
    for i, h in enumerate((.30, .50, .70)): rrect(d, (.16 + i * .26, .84 - h), (.34 + i * .26, .84), .03)
@ic("ic_box")
def _(im, d):
    d.polygon([P(.50, .10), P(.88, .28), P(.50, .46), P(.12, .28)], outline=WHITE, width=int(.06 * M)); line(d, [(.12, .28), (.12, .70), (.50, .90), (.88, .70), (.88, .28)], .06); line(d, [(.5, .46), (.5, .90)], .06)
@ic("ic_road")
def _(im, d):
    poly(d, [(.38, .10), (.62, .10), (.90, .90), (.10, .90)]); cut(im, lambda c: [c.rectangle([P(.47, .18 + k * .24), P(.53, .30 + k * .24)], fill=255) for k in range(3)])
@ic("ic_arrow_up")
def _(im, d): line(d, [(.5, .86), (.5, .18)], .12); line(d, [(.22, .46), (.5, .16), (.78, .46)], .12)
@ic("ic_dollar")
def _(im, d):
    line(d, [(.5, .08), (.5, .92)], .08); d.arc([P(.26, .18), P(.74, .50)], 90, 360, fill=WHITE, width=int(.10 * M)); d.arc([P(.26, .50), P(.74, .82)], 270, 180, fill=WHITE, width=int(.10 * M))
@ic("ic_news")
def _(im, d):
    rrect(d, (.14, .16), (.86, .84), .05, (0, 0, 0, 0), WHITE, .06); line(d, [(.26, .34), (.74, .34)], .06); line(d, [(.26, .50), (.74, .50)], .05); line(d, [(.26, .66), (.56, .66)], .05)
@ic("ic_lightning")
def _(im, d): poly(d, [(.58, .06), (.22, .54), (.46, .54), (.38, .94), (.78, .40), (.52, .40)])
@ic("ic_check_circle")
def _(im, d): circle(d, (.5, .5), .40); cut(im, lambda c: c.line([P(.30, .52), P(.45, .66), P(.72, .36)], fill=255, width=int(.09 * M)))
@ic("ic_fuel")
def _(im, d):
    rrect(d, (.18, .12), (.58, .90), .05); cut(im, lambda c: c.rounded_rectangle([P(.26, .20), P(.50, .44)], radius=.03 * M, fill=255))
    line(d, [(.58, .34), (.74, .34), (.78, .46), (.78, .72)], .06); circle(d, (.78, .78), .07)
@ic("ic_clock")
def _(im, d):
    circle(d, (.5, .5), .40, (0, 0, 0, 0), WHITE, .07); line(d, [(.5, .26), (.5, .52), (.68, .62)], .07)
@ic("ic_lock")
def _(im, d):
    d.arc([P(.30, .10), P(.70, .56)], 180, 360, fill=WHITE, width=int(.08 * M)); rrect(d, (.20, .42), (.80, .90), .07); cut(im, lambda c: c.ellipse([P(.44, .56), P(.56, .68)], fill=255))
@ic("ic_gauge")
def _(im, d):
    d.arc([P(.12, .20), P(.88, .96)], 180, 360, fill=WHITE, width=int(.08 * M)); line(d, [(.5, .62), (.70, .38)], .08); circle(d, (.5, .62), .07)
@ic("ic_weight")
def _(im, d):
    poly(d, [(.28, .26), (.72, .26), (.88, .88), (.12, .88)]); circle(d, (.5, .20), .10, (0, 0, 0, 0), WHITE, .06); cut(im, lambda c: c.line([P(.34, .60), P(.66, .60)], fill=255, width=int(.06 * M)))
@ic("ic_user_circle")
def _(im, d):
    circle(d, (.5, .5), .42); cut(im, lambda c: [c.ellipse([P(.38, .26), P(.62, .50)], fill=255), c.pieslice([P(.24, .52), P(.76, 1.02)], 180, 360, fill=255)])
for g in GLYPHS: g()
sheet2 = Image.new("RGBA", (9 * 144, 3 * 144), (30, 30, 36, 255))
for i, g in enumerate(GLYPHS):
    icon = Image.open(f"{OUT}/{g.__name__}.png").convert("RGBA").resize((128, 128)); sheet2.alpha_composite(icon, ((i % 9) * 144 + 8, (i // 9) * 144 + 8))
sheet2.save("/tmp/icon_sheet2.png"); print("dashboard glyphs:", len(GLYPHS))
