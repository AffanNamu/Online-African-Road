#!/usr/bin/env python3
"""
Generates the two E5 engine TEST routes from compact design tables (docs/ROAD_ENGINE_E5.md):

  ng-kano-e5-roundabouts  six roundabout designs on one chain (Main -> ring -> link -> ring ...), every ring with its own legs
  ng-kano-e5-f1           the flagship interchange: a ground ring (R7) with a dual expressway flying over it (Main on a deck), an off ramp and an on ramp

The output is plain route JSON (schemaVersion 3). The generator exists so the numbers that must agree (a link's end on a ring's paved edge, a ramp's end on the main road's edge, a
deck's pier gaps over the ring carriageway) are computed, not typed. Run: python3 tools/e5/gen_e5_routes.py   (writes Assets/_Project/Resources/Routes/*.route.json)
Both routes are ENGINE TESTS on fictional layouts. They are not Kano, not survey data, and nothing in them is approved for production.
"""
import json, math, os

ROUTES = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', '_Project', 'Resources', 'Routes')
CAMBER = 0.02


def prof(id, L=1, w=3.5, shoulder=0.5, verge=0.6, kerb=0.0, kerb_paint='none', sidewalk=0.0, drain=0.0, centre='dashed', color='yellow',
         median=0.0, inner_shoulder=0.6, median_style='jersey', cls='', barrier='none'):
    return dict(id=id, roadClass=cls, lanesPerDirection=L, laneWidth=w, medianWidth=median, centreLine=centre, centreLineColor=color, shoulderWidth=shoulder,
                innerShoulderWidth=inner_shoulder, vergeWidth=verge, camberPercent=CAMBER * 100, edgeBarrier=barrier, ditchWidth=2.0, ditchDepth=0.4,
                laneLineDash=3.0, laneLineGap=9.0, kerbHeight=kerb, kerbPaint=kerb_paint, sidewalkWidth=sidewalk, drainWidth=drain, medianStyle=median_style)


def geom(p):
    """The numbers the C# RoadCrossSection derives: paved half width, formation half width, surface drop from the crown to the paved edge."""
    L, w = p['lanesPerDirection'], p['laneWidth']
    divided = p['medianWidth'] > 0.01
    m = p['medianWidth'] * 0.5
    ish = p['innerShoulderWidth'] if divided else 0.0
    styled = divided and p['medianStyle'] != 'jersey'
    xi = m + (0.08 if styled else 0.0) + ish
    xo = xi + L * w
    xs = xo + p['shoulderWidth']
    kerb = p['kerbHeight'] > 0.001
    tail = (p['drainWidth'] + 0.07 + 0.15 + p['sidewalkWidth']) if kerb else 0.0
    xv = xs + tail + p['vergeWidth']
    yi = -CAMBER * ish if divided else 0.0
    y = yi - CAMBER * (xo - xi) - CAMBER * 1.5 * p['shoulderWidth']          # surface at the paved edge, relative to the crown
    return dict(paved=xs, formation=xv, edge_drop=y)


def cp(id, name, x, z, y, profile, zone='urban', surface='good'):
    return dict(id=id, name=name, x=round(x, 3), z=round(z, 3), elevation=round(y, 3), zone=zone, surface=surface, profile=profile)


def base_route(route_id, name, coverage, seed, terrain):
    return dict(schemaVersion=3, routeId=route_id, name=name, seed=seed,
                meta=dict(country='NG', from_='E5 test start', to='E5 test end', status='planned', worldMapRouteId='kaduna-kano', realRoadKm=209.6, gameLengthKm=0.0,
                          gameCoverage=coverage, elevationSource='design', notes='Fictional engine test layout. Not Kano, not a survey, not approved for production. docs/ROAD_ENGINE_E5.md.'),
                terrain=terrain, profiles=[], controlPoints=[], bridges=[], junctions=[], propRules=[], props=[], traffic=dict(densityScale=0.0, speedLimitScale=1.0, vehicleMix=[]),
                waypoints=[], symbols=[], sites=[], roads=[], rings=[])


def fix_meta(r):
    m = r['meta']; m['from'] = m.pop('from_')
    return r


TERRAIN = dict(baseElevation=10.0, trendX=0.0, trendZ=0.0, hillAmplitude=0.5, hillWavelength=600.0, detailAmplitude=0.05, detailWavelength=60.0, maxEmbankmentSlope=0.5, minBlendDistance=6.0, materialByZone='')

# ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ profiles
P = {}
P['leg2'] = prof('leg2', 1, 3.5, 0.5, 0.6, 0.12, 'black_white', 2.0, 0.4, 'dashed', 'yellow', cls='collector')
P['leg4'] = prof('leg4', 2, 3.5, 0.5, 0.8, 0.12, 'black_white', 2.5, 0.4, 'double_solid', 'yellow', cls='arterial')
P['legrural'] = prof('legrural', 1, 3.5, 1.2, 1.0, 0.0, 'none', 0.0, 0.0, 'dashed', 'yellow', cls='local')
P['ring_landmark'] = prof('ring_landmark', 1, 4.0, 0.5, 0.5, 0.12, 'black_white', 2.0, 0.4, 'dashed', 'white', cls='arterial')
P['ring_modern'] = prof('ring_modern', 1, 3.5, 0.5, 0.3, 0.12, 'black_white', 0.0, 0.0, 'dashed', 'white', cls='arterial')
P['ring_traditional'] = prof('ring_traditional', 1, 3.4, 1.0, 0.8, 0.0, 'none', 0.0, 0.0, 'none', 'white', cls='local')
P['ring_large'] = prof('ring_large', 2, 3.4, 0.6, 0.5, 0.12, 'black_white', 2.0, 0.4, 'dashed', 'white', cls='arterial')
P['ring_commercial'] = prof('ring_commercial', 1, 3.5, 0.5, 0.3, 0.12, 'black_white', 3.0, 0.0, 'dashed', 'white', cls='collector')
P['ring_green'] = prof('ring_green', 1, 3.8, 1.0, 1.0, 0.0, 'none', 0.0, 0.0, 'dashed', 'white', cls='collector')
G = {k: geom(v) for k, v in P.items()}


def ring_dims(ring_profile, radius):
    g = G[ring_profile]
    rc = radius + g['formation']
    return rc, rc + g['paved']


# ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ route 1: six roundabouts
DESIGNS = [
    dict(id='r_landmark', name='Landmark roundabout (civic island, five legs)', design='landmark', profile='ring_landmark', radius=22.0, apron=0.0, island='paved', splitter=(16.0, 3.4), flare=0.0, mouth=1.9,
         legs=[('n1', 'leg2', 60), ('n2', 'leg2', 120), ('s1', 'leg2', 270)], link_profile='leg2',
         island_props=[('mast', 'junction_mast', 0, 0, 0)] + [('palm%d' % i, 'palm_tree', 13 * math.cos(math.radians(45 * i)), 13 * math.sin(math.radians(45 * i)), 0) for i in range(8)]),
    dict(id='r_modern', name='Modern urban roundabout (two lanes, mountable apron)', design='modern', profile='ring_modern', radius=18.0, apron=2.0, island='planter', splitter=(14.0, 3.0), flare=0.0, mouth=1.9,
         legs=[('n1', 'leg2', 90), ('s1', 'leg2', 270)], link_profile='leg2',
         island_props=[('t%d' % i, 'tropical_tree', 6 * math.cos(math.radians(72 * i)), 6 * math.sin(math.radians(72 * i)), 0) for i in range(5)] + [('b%d' % i, 'bush', 10 * math.cos(math.radians(36 + 72 * i)), 10 * math.sin(math.radians(36 + 72 * i)), 0) for i in range(5)]),
    dict(id='r_traditional', name='Traditional roundabout (three legs, one wide lane, earth island with a ring of palms)', design='traditional', profile='ring_traditional', radius=15.0, apron=1.5, island='earth', splitter=(12.0, 2.6), flare=9.0, mouth=1.8,
         legs=[('s1', 'legrural', 270)], link_profile='legrural',
         island_props=[('palm%d' % i, 'palm_tree', 9 * math.cos(math.radians(30 * i)), 9 * math.sin(math.radians(30 * i)), 0) for i in range(12)]),
    dict(id='r_large', name='Large multi-lane roundabout (four circulating lanes, six legs)', design='large', profile='ring_large', radius=30.0, apron=0.0, island='planter', splitter=(22.0, 4.0), flare=0.0, mouth=1.8,
         legs=[('n1', 'leg4', 60), ('n2', 'leg4', 120), ('s1', 'leg4', 240), ('s2', 'leg4', 300)], link_profile='leg4',
         island_props=[('t%d' % i, 'tropical_tree', 16 * math.cos(math.radians(30 * i)), 16 * math.sin(math.radians(30 * i)), 0) for i in range(12)]),
    dict(id='r_commercial', name='Commercial roundabout (wide market walkways, bus bays on the legs)', design='commercial', profile='ring_commercial', radius=14.0, apron=1.5, island='paved', splitter=(12.0, 2.8), flare=0.0, mouth=1.9,
         legs=[('n1', 'leg2', 90), ('s1', 'leg2', 270)], link_profile='leg2',
         island_props=[('k%d' % i, 'kiosk', 6 * math.cos(math.radians(90 * i + 45)), 6 * math.sin(math.radians(90 * i + 45)), 90 * i) for i in range(4)] + [('p%d' % i, 'palm_tree', 3 * math.cos(math.radians(90 * i)), 3 * math.sin(math.radians(90 * i)), 0) for i in range(4)]),
    dict(id='r_green', name='Green roundabout (grass island, one wide lane, rising exit on sloping ground)', design='green', profile='ring_green', radius=24.0, apron=0.0, island='grass', splitter=(14.0, 3.0), flare=11.0, mouth=1.8,
         legs=[('n1', 'legrural', 90), ('s1', 'legrural', 270)], link_profile=None,
         island_props=[('t%d' % i, 'tropical_tree', 15 * math.cos(math.radians(36 * i)), 15 * math.sin(math.radians(36 * i)), 0) for i in range(10)] + [('b%d' % i, 'bush', 8 * math.cos(math.radians(24 * i)), 8 * math.sin(math.radians(24 * i)), 0) for i in range(15)]),
]
LINK_LENGTH = 260.0


def build_roundabouts():
    r = base_route('ng-kano-e5-roundabouts', 'Kano E5 roundabout test chain (ENGINE TEST: six roundabout designs on a fictional road, traffic off)',
                   'ENGINE TEST ROUTE, NOT A SURVEY AND NOT A PLAYABLE ROUTE. A fictional chain of six roundabout designs (Landmark, Modern, Traditional, Large, Commercial, Green) joined by short roads, on near-flat ground, for the E5 ring engine. Not Kano.',
                   9051, dict(TERRAIN))
    r['profiles'] = [P[k] for k in ('leg2', 'leg4', 'legrural')] + [P[d['profile']] for d in DESIGNS]
    main_len = 520.0
    r['controlPoints'] = [cp('cp0', 'Start of the chain', -main_len, 0, 10, 'leg2'), cp('cp1', 'First roundabout', 0, 0, 10, 'leg2')]
    r['waypoints'] = [dict(id='wp_start', name='Chain start', kind='district', s=0.0), dict(id='wp_ring1', name='First roundabout', kind='district', s=main_len)]
    x_edge = 0.0                                                                    # the x of the corridor end that the next ring attaches to
    prev = 'ng-kano-e5-roundabouts'
    for i, d in enumerate(DESIGNS):
        rc, rout = ring_dims(d['profile'], d['radius'])
        cx = x_edge + rout
        link_id = 'link%d' % (i + 1)
        legs = [dict(id='%s_%s' % (d['id'], lid), profile=lp, angleDeg=float(a), length=140.0, rise=0.0, zone='', surface='') for lid, lp, a in d['legs']]
        if d['design'] == 'green':                                                    # one exit climbs 4 m over 150 m (2.7 percent) on the sloping side
            legs[0]['length'] = 150.0; legs[0]['rise'] = 4.0
        ring = dict(id=d['id'], name=d['name'], design=d['design'], profile=d['profile'], x=0.0, z=0.0, elevation=0.0, attachTo=prev, attachEnd='end', radius=d['radius'], apron=d['apron'],
                    island=d['island'], zone='urban', surface='good', flare=d['flare'], mouthScale=d.get('mouth', 1.0), splitterLength=d['splitter'][0], splitterWidth=d['splitter'][1], yieldLines=True, zebra=True,
                    legs=legs, connect=[], props=[dict(id=pid, prop=pr, text='', x=round(px, 3), z=round(pz, 3), yawDeg=yaw, scale=1.0) for pid, pr, px, pz, yaw in d['island_props']])
        if i + 1 < len(DESIGNS):
            lp = DESIGNS[i]['link_profile'] or 'legrural'
            x0 = cx + rout; x1 = x0 + LINK_LENGTH
            link = dict(id=link_id, name='Link %d' % (i + 1), role='road', controlPoints=[cp('a', 'a', x0, 0, 10, lp), cp('b', 'b', x1, 0, 10, lp)], decks=[], joins=[])
            r['roads'].append(link)
            ring['connect'] = [link_id]
            x_edge = x1; prev = link_id
        r['rings'].append(ring)
    # commercial ring: bus bays on the right-hand side of its two generated legs; lights at every ring and link
    n = 0
    for d in DESIGNS:
        if d['design'] == 'commercial':
            for lid, _, _ in d['legs']:
                r['symbols'].append(dict(id='bay_%s' % lid, kind='bay', corridor='%s_%s' % (d['id'], lid), s=60.0, side='right', lane=0, length=0.0, count=2, yellow=False))
    # street lights on the outer side of every ring, one every ~32 m of the centre line, arms over the carriageway
    for d in DESIGNS:
        rc, rout = ring_dims(d['profile'], d['radius'])
        g = G[d['profile']]
        n_l = max(6, int(round(2 * math.pi * rc / 32.0)))
        L = 2 * math.pi * rc
        for k in range(n_l):
            r['props'].append(dict(id='%s_light%d' % (d['id'], k), prop='street_light', text='', corridor=d['id'], s=round((k + 0.5) * L / n_l, 2), lateral=round(g['formation'] + 0.6, 2), yawDeg=-90.0, scale=1.0))
    r['meta']['gameLengthKm'] = round((main_len + len(DESIGNS) * 2 * 60 + (len(DESIGNS) - 1) * LINK_LENGTH) / 1000.0, 3)
    return fix_meta(r)


# ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ route 2: F1
def build_f1():
    r = base_route('ng-kano-e5-f1', 'Kano E5 flagship interchange F1 (ENGINE TEST: ground roundabout under a dual expressway deck, off and on ramps, traffic off)',
                   'ENGINE TEST ROUTE, NOT A SURVEY AND NOT A PLAYABLE ROUTE. A fictional grade-separated interchange: a four-lane ground roundabout with arterial and local legs, a dual expressway (the main road) flying over it on a deck, and an off ramp and an on ramp between them. Not Kano.',
                   9052, dict(TERRAIN, hillAmplitude=0.3))
    xway = prof('xway', 2, 3.5, 2.5, 1.0, median=1.2, inner_shoulder=0.6, cls='expressway', centre='none', barrier='none')
    P2 = dict(xway=xway)
    P2['ring_hwy'] = prof('ring_hwy', 2, 3.3, 0.8, 0.8, 0.12, 'black_white', 0.0, 0.0, 'dashed', 'white', cls='arterial')
    P2['arterial_x'] = prof('arterial_x', 2, 3.5, 0.6, 1.0, 0.12, 'black_white', 2.0, 0.4, 'double_solid', 'yellow', cls='arterial')
    P2['local_x'] = prof('local_x', 1, 3.7, 1.2, 0.6, 0.12, 'black_white', 1.8, 0.4, 'dashed', 'yellow', cls='collector')
    P2['ramp'] = prof('ramp', 1, 4.2, 1.8, 1.2, 0.0, 'none', 0.0, 0.0, 'dashed', 'yellow', cls='collector')
    G2 = {k: geom(v) for k, v in P2.items()}
    r['profiles'] = list(P2.values())

    ISL = 38.0
    rc = ISL + G2['ring_hwy']['formation']; rout = rc + G2['ring_hwy']['paved']
    ring_y_crown = 10.0 - G2['ring_hwy']['edge_drop']                                  # the ring's outer paved edge is at 10.0
    xe = G2['xway']['edge_drop']                                                       # expressway edge below its crown
    pav = G2['xway']['paved']

    # Main: the expressway, south to north along x = 0. Knots where the ramps join, so their heights are exact.
    DECK_Y = 17.8
    pts = [(-900, 10.0), (-600, 10.0), (-340, DECK_Y), (340, DECK_Y), (600, 10.0), (900, 10.0)]
    # the on ramp joins at z = +480 where the road is half way down; put a knot there
    z_join_on = 470.0
    y_on = DECK_Y + (10.0 - DECK_Y) * (z_join_on - 340) / (600 - 340)                  # linear between the knots (a knot makes it exact)
    pts = [(-900, 10.0), (-600, 10.0), (-340, DECK_Y), (340, DECK_Y), (z_join_on, y_on), (600, 10.0), (900, 10.0)]
    r['controlPoints'] = [cp('cp%d' % i, 'Expressway %d' % i, 0, z, y, 'xway', 'highway' if abs(z) > 340 else 'urban') for i, (z, y) in enumerate(pts)]
    main_len = 1800.0
    r['waypoints'] = [dict(id='wp_south', name='Expressway south', kind='district', s=0.0), dict(id='wp_over', name='Over the roundabout', kind='district', s=900.0), dict(id='wp_north', name='Expressway north', kind='district', s=1800.0)]

    # the deck: from z = -300 to z = +300, nearly level, piers every 24 m except over the ring carriageway
    ring_in, ring_out = rc - G2['ring_hwy']['formation'] - 0.0, rc + G2['ring_hwy']['formation']
    gaps = []
    for sign in (-1, 1):
        z_a, z_b = sign * (ring_out + 4.0), sign * (ring_in - 4.0)
        lo, hi = sorted((z_a, z_b))
        gaps.append(dict(**{'from': round(900 + lo, 1), 'to': round(900 + hi, 1)}))
    r['bridges'] = [dict(id='f1_deck', name='F1 flyover over the ground roundabout', startS=600.0, endS=1200.0, valleyDepth=0.0, deckThickness=1.2, pierSpacing=24.0, pierGaps=gaps, overRoad=True)]

    ring = dict(id='r_highway', name='Highway interchange roundabout F1 (ground ring under the expressway deck)', design='highway', profile='ring_hwy', x=0.0, z=0.0, elevation=round(ring_y_crown, 4),
                attachTo='', attachEnd='end', radius=ISL, apron=0.0, island='planter', zone='urban', surface='good', flare=0.0, mouthScale=1.65, splitterLength=26.0, splitterWidth=4.5, yieldLines=True, zebra=False,
                legs=[], connect=['ramp_off', 'ramp_on'],
                props=[dict(id='t%d' % i, prop='tropical_tree', text='', x=round(24 * math.cos(math.radians(20 * i)), 3), z=round(24 * math.sin(math.radians(20 * i)), 3), yawDeg=0.0, scale=1.0) for i in range(18)
                       if abs(24 * math.cos(math.radians(20 * i))) > 12])
    for lid, lp, ang, ln in (('e', 'arterial_x', 0, 420.0), ('w', 'arterial_x', 180, 420.0), ('ne', 'local_x', 40, 250.0), ('nw', 'local_x', 140, 250.0), ('sw', 'local_x', 220, 250.0), ('se', 'local_x', 320, 250.0)):
        ring['legs'].append(dict(id='hw_' + lid, profile=lp, angleDeg=float(ang), length=ln, rise=0.0, zone='', surface=''))
    r['rings'] = [ring]

    # ramps (two-way roads in the test; one-way signing is a gameplay concern). The off ramp leaves the expressway's right edge at z = -340 (where the deck begins) and comes down to the ring at 290 degrees.
    def at_angle(deg, r_, back=0.0):
        a = math.radians(deg); return (r_ * math.cos(a), r_ * math.sin(a))
    def with_heights(xz, y0, y1, profile, prefix):
        """Control points with elevation linear in the distance travelled (a constant grade), so the spline keeps it exactly."""
        d = [0.0]
        for i in range(1, len(xz)):
            d.append(d[-1] + math.hypot(xz[i][0] - xz[i - 1][0], xz[i][1] - xz[i - 1][1]))
        return [cp('%s%d' % (prefix, i), '%s%d' % (prefix, i), x, z, y0 + (y1 - y0) * d[i] / d[-1], profile) for i, (x, z) in enumerate(xz)]

    # Off ramp: leaves the expressway's right edge at z = -340 (where the deck begins) and comes down to the ring at 290 degrees, straight and radial for the last 100 m
    off_xz = [(pav, -340.0), (30.0, -295.0), (50.0, -235.0), (57.0, -190.0), at_angle(290, rout + 100), at_angle(290, rout + 45), at_angle(290, rout)]
    ramp_off = dict(id='ramp_off', name='Off ramp (expressway to the roundabout)', role='ramp', decks=[],
                    controlPoints=with_heights(off_xz, DECK_Y + xe, 10.0, 'ramp', 'o'),
                    joins=[dict(end='start', corridor='ng-kano-e5-f1', s=900.0 - 340.0, side=1, gap=30.0)])
    # On ramp: leaves the ring at 70 degrees, radial for 100 m, then curves back to join the expressway's right edge at z = +470
    on_xz = [at_angle(70, rout), at_angle(70, rout + 45), at_angle(70, rout + 100), (57.0, 235.0), (50.0, 325.0), (30.0, 420.0), (pav, z_join_on)]
    ramp_on = dict(id='ramp_on', name='On ramp (roundabout to the expressway)', role='ramp', decks=[],
                   controlPoints=with_heights(on_xz, 10.0, y_on + xe, 'ramp', 'n'),
                   joins=[dict(end='end', corridor='ng-kano-e5-f1', s=900 + z_join_on, side=1, gap=30.0)])
    r['roads'] = [ramp_off, ramp_on]
    r['props'] = []
    r['meta']['gameLengthKm'] = 1.8
    return fix_meta(r)


def main():
    for route in (build_roundabouts(), build_f1()):
        path = os.path.normpath(os.path.join(ROUTES, route['routeId'] + '.route.json'))
        with open(path, 'w', newline='\n') as f:
            json.dump(route, f, indent=1); f.write('\n')
        print('wrote', path, len(json.dumps(route)), 'bytes')
    for d in DESIGNS:
        rc, rout = ring_dims(d['profile'], d['radius'])
        print('%-14s island %4.0f  centre line %5.1f  outer paved %5.1f  circulating lanes %d  carriageway %4.1f m' % (d['design'], d['radius'], rc, rout,
              2 * P[d['profile']]['lanesPerDirection'], 2 * P[d['profile']]['lanesPerDirection'] * P[d['profile']]['laneWidth']))


if __name__ == '__main__':
    main()
