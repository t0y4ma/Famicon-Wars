# Point-symmetric map generator + validator + preview renderer.
# Each map: author the bottom half (red side); the top half is the 180-degree rotation.
# For odd heights, 'mid' is the left half of the centre row (mirrored to the right).
import sys, collections
from PIL import Image, ImageDraw

TERR = {}
for line in open('' + __import__('os').path.join(__import__('os').path.dirname(__import__('os').path.abspath(__file__)), '../../Assets/Resources/Data/terrains.csv') + '').read().splitlines()[1:]:
    p = line.split(',')
    TERR[p[2]] = dict(id=p[0], prop=p[6] == '1', foot=int(p[10]), veh=int(p[11]), ship=int(p[13]), color=p[15])

MAPS = {}

def make(mid_id, name, W, bottom, owners_bottom, mid=None, units=(), blue_extra=()):
    H = len(bottom) * 2 + (1 if mid is not None else 0)
    for r in bottom + owners_bottom:
        assert len(r) == W, (mid_id, r, len(r), W)
    rows = [None] * H
    own = [None] * H
    off = H - len(bottom)
    for i, r in enumerate(bottom):
        rows[off + i] = r
        own[off + i] = owners_bottom[i]
    for i, r in enumerate(bottom):
        y = off + i
        rows[H - 1 - y] = r[::-1]
        own[H - 1 - y] = owners_bottom[i][::-1].replace('r', 'b')
    if mid is not None:
        c = len(bottom)
        assert len(mid) * 2 == W, (mid_id, 'mid', len(mid))
        rows[c] = mid + mid[::-1]
        own[c] = '.' * W
    for (bx, by) in blue_extra:
        assert TERR[rows[by][bx]]['prop'], (mid_id, bx, by)
        own[by] = own[by][:bx] + 'b' + own[by][bx + 1:]
    MAPS[mid_id] = dict(name=name, W=W, H=H, rows=rows, own=own, units=list(units))
    return MAPS[mid_id]

def validate(m):
    W, H, rows, own = m['W'], m['H'], m['rows'], m['own']
    errs = []
    hq = {}
    for y in range(H):
        for x in range(W):
            ch = rows[y][x]
            if ch not in TERR: errs.append('bad char %r at %d,%d' % (ch, x, y)); continue
            o = own[y][x]
            if o != '.' and not TERR[ch]['prop']: errs.append('owner on non-property %d,%d' % (x, y))
            if TERR[ch]['id'] == 'HQ': hq[o] = (x, y)
    for a in 'rb':
        if a not in hq: errs.append('no HQ for ' + a); continue
        hx, hy = hq[a]
        fac = [(x, y) for y in range(H) for x in range(W) if TERR[rows[y][x]]['id'] in ('FACTORY',) and own[y][x] == a and abs(x - hx) <= 2 and abs(y - hy) <= 2]
        if not fac: errs.append('no own factory near HQ ' + a)
        for y in range(H):
            for x in range(W):
                t = TERR[rows[y][x]]['id']
                if t in ('AIRPORT', 'PORT', 'FACTORY') and own[y][x] == a and not (abs(x - hx) <= 2 and abs(y - hy) <= 2):
                    errs.append('%s owned by %s outside production range at %d,%d' % (t, a, x, y))
    def reach(cls, start):
        seen = {start}; q = collections.deque([start])
        while q:
            x, y = q.popleft()
            for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < W and 0 <= ny < H and (nx, ny) not in seen and TERR[rows[ny][nx]][cls] > 0:
                    seen.add((nx, ny)); q.append((nx, ny))
        return seen
    if 'r' in hq and 'b' in hq:
        m['footPath'] = hq['b'] in reach('foot', hq['r'])
        m['vehPath'] = hq['b'] in reach('veh', hq['r'])
    props = sum(1 for y in range(H) for x in range(W) if TERR[rows[y][x]]['prop'])
    m['props'] = props
    return errs

def render(m, path, cell=24):
    W, H = m['W'], m['H']
    img = Image.new('RGB', (W * cell, H * cell), 'black')
    d = ImageDraw.Draw(img)
    for y in range(H):
        for x in range(W):
            ch = m['rows'][y][x]
            d.rectangle([x * cell, y * cell, x * cell + cell - 2, y * cell + cell - 2], fill=TERR[ch]['color'])
            t = TERR[ch]['id']
            o = m['own'][y][x]
            if TERR[ch]['prop']:
                col = {'r': '#dc3833', 'b': '#336be6'}.get(o, '#777')
                d.rectangle([x * cell + 3, y * cell + 3, x * cell + cell - 5, y * cell + 8], fill=col)
                d.text((x * cell + 7, y * cell + 9), t[0], fill='black')
            elif t == 'MOUNTAIN': d.polygon([(x*cell+4, y*cell+cell-5), (x*cell+cell//2, y*cell+4), (x*cell+cell-6, y*cell+cell-5)], fill='#5b4a3a')
            elif t == 'FOREST': d.ellipse([x*cell+6, y*cell+5, x*cell+cell-8, y*cell+cell-7], fill='#2e5e22')
    img.save(path)

def write(m, path):
    with open(path, 'w', newline='\r\n') as f:
        f.write('name=%s\nwidth=%d\nheight=%d\n[tiles]\n' % (m['name'], m['W'], m['H']))
        for r in m['rows']: f.write(r + '\n')
        f.write('[owners]\n')
        for r in m['own']: f.write(r + '\n')
        f.write('[units]\n')
        for u in m['units']: f.write(u + '\n')
