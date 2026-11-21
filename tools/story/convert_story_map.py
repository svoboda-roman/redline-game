"""Converts the user's colour-coded story map drawing into a Redline level text file.

Legend (the user's): black = walls/border, yellow = platforms, red = spikes, green = player start, blue/cyan = lever,
pink = gate, grey = checkpoint (the first one gives the hook), orange = fire/electric trap (toggles, area damage),
brown horizontal = collapsing bridge, brown vertical = platform moving up and down. The word "Boss" marks the boss room.

Scale: 1 drawing pixel = 1 tile, exactly as drawn (the green start mark is 2 px = the player's height; story mode gives the player a double jump for the ~5-tile platform spacing).
Everything outside the drawn border becomes solid rock (flood fill from the image edge).

Output characters (see Core/TileMap.cs and Core/StoryMap.cs):
  # solid   ^ spike   P start   C checkpoint   L lever   G gate (solid until opened)   F fire trap
  = collapsing bridge   | lift track (moving platform path)   B boss room marker   . empty

Run with Blender's bundled Python (has PIL):
  python convert_story_map.py <drawing.png> <out.txt>
"""
import sys
from collections import deque
from PIL import Image

LEGEND = {  # colour -> (char, priority); higher priority wins inside a 2x2 block
    (0x8F, 0xDE, 0x5D): ('P', 9),
    (0x66, 0xFF, 0xE3): ('L', 9),
    (0x60, 0x60, 0x70): ('C', 9),
    (0xFF, 0x6B, 0x97): ('G', 8),
    (0xF2, 0xA6, 0x5E): ('F', 7),
    (0x64, 0x47, 0x2C): ('m', 7),   # brown: bridge or lift, decided by the run's orientation
    (0xE9, 0x0A, 0x0A): ('^', 6),
    (0x10, 0x0E, 0x0E): ('#', 5),
    (0x00, 0x00, 0x00): ('#', 5),
    (0xD7, 0xE9, 0x53): ('#', 4),   # platforms are solid ledges
}
S = 1


def main(src, out):
    im = Image.open(src).convert('RGBA')
    W, H = im.size
    px = im.load()
    # Bounding box of the drawing (ignore isolated specks far from the map).
    # The map is the biggest 8-connected blob of drawn pixels (stray specks elsewhere are ignored).
    lab = {}
    best = []
    for sy in range(H):
        for sx in range(W):
            if px[sx, sy][3] == 0 or (sx, sy) in lab:
                continue
            comp, q = [], deque([(sx, sy)])
            lab[(sx, sy)] = 1
            while q:
                cx, cy = q.popleft()
                comp.append((cx, cy))
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        n = (cx + dx, cy + dy)
                        if 0 <= n[0] < W and 0 <= n[1] < H and n not in lab and px[n[0], n[1]][3] > 0:
                            lab[n] = 1
                            q.append(n)
            if len(comp) > len(best):
                best = comp
    busy = best
    x0 = min(x for x, _ in busy) - 2; x1 = max(x for x, _ in busy) + 2
    y0 = min(y for _, y in busy) - 2; y1 = max(y for _, y in busy) + 2
    tw, th = (x1 - x0 + S) // S, (y1 - y0 + S) // S
    grid = [['.'] * tw for _ in range(th)]
    pri = [[0] * tw for _ in range(th)]
    brown = []
    inked = set()   # cells drawn in black/dark ink (walls and the handwriting)
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            hit = LEGEND.get((r, g, b))
            if hit is None:
                # anti-aliased / handwriting pixels: dark = wall, light = nothing
                hit = ('#', 5) if r + g + b < 200 else None
            if hit is None:
                continue
            tx, ty = (x - x0) // S, (y - y0) // S
            if hit[0] == 'm':
                brown.append((tx, ty))
            if hit[1] > pri[ty][tx]:
                grid[ty][tx], pri[ty][tx] = hit
            if hit == ('#', 5):
                inked.add((tx, ty))
    # Brown runs: the horizontal one is the collapsing bridge, the vertical one the lift track.
    for (tx, ty) in brown:
        grid[ty][tx] = 'm'
    seen = set()
    for (tx, ty) in brown:
        if (tx, ty) in seen:
            continue
        comp, q = [], deque([(tx, ty)])
        seen.add((tx, ty))
        while q:
            cx, cy = q.popleft()
            comp.append((cx, cy))
            for dx in (-2, -1, 0, 1, 2):        # brown is drawn dotted: bridge the 1-px gaps
                for dy in (-2, -1, 0, 1, 2):
                    n = (cx + dx, cy + dy)
                    if n in seen or not (0 <= n[0] < tw and 0 <= n[1] < th) or grid[n[1]][n[0]] != 'm':
                        continue
                    seen.add(n)
                    q.append(n)
        wx = max(c[0] for c in comp) - min(c[0] for c in comp)
        wy = max(c[1] for c in comp) - min(c[1] for c in comp)
        ch = '=' if wx >= wy else '|'
        for cx, cy in comp:
            grid[cy][cx] = ch
    # The lift track: one column (its centre), full height of the run.
    for x in range(tw):
        rows = [y for y in range(th) if grid[y][x] == '|']
        if rows:
            pass
    # Outside the border -> solid: flood from the edge through non-wall cells.
    outside = [[False] * tw for _ in range(th)]
    q = deque((x, y) for x in range(tw) for y in (0, th - 1)) + deque((x, y) for y in range(th) for x in (0, tw - 1))
    for x, y in list(q):
        outside[y][x] = grid[y][x] == '.'
    q = deque((x, y) for x, y in q if outside[y][x])
    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < tw and 0 <= ny < th and not outside[ny][nx] and grid[ny][nx] == '.':
                outside[ny][nx] = True
                q.append((nx, ny))
    for y in range(th):
        for x in range(tw):
            if outside[y][x]:
                grid[y][x] = '#'
    # The "Boss" handwriting: dark-ink islands that touch neither the outside rock nor the border -> air, with a B
    # marker at their centre. Single markers: P, C and L clusters keep one cell (the lowest, i.e. where you stand).
    seen = set()
    for y in range(th):
        for x in range(tw):
            if grid[y][x] != '#' or outside[y][x] or (x, y) in seen or (x, y) not in inked:
                continue
            comp, q, touches = [], deque([(x, y)]), False
            seen.add((x, y))
            while q:
                cx, cy = q.popleft()
                comp.append((cx, cy))
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nx, ny = cx + dx, cy + dy
                        if not (0 <= nx < tw and 0 <= ny < th):
                            continue
                        if outside[ny][nx]:
                            touches = True
                        if grid[ny][nx] == '#' and (nx, ny) in inked and (nx, ny) not in seen and not outside[ny][nx]:
                            seen.add((nx, ny))
                            q.append((nx, ny))
            if not touches and len(comp) < 400 and max(c[1] for c in comp) < th * 0.35:   # only the handwriting up in the boss room
                for cx, cy in comp:
                    grid[cy][cx] = '.'
                if len(comp) > 40:
                    mx = sum(c[0] for c in comp) // len(comp); my = max(c[1] for c in comp)
                    grid[my][mx] = 'B'
    for ch in 'PCL':
        cells = [(x, y) for y in range(th) for x in range(tw) if grid[y][x] == ch]
        done = set()
        for c in cells:
            if c in done:
                continue
            group = [d for d in cells if abs(d[0] - c[0]) <= 2 and abs(d[1] - c[1]) <= 2]
            done.update(group)
            keep = max(group, key=lambda d: (d[1], d[0]))
            for d in group:
                if d != keep:
                    grid[d[1]][d[0]] = '.'
    # The bridge is drawn dotted: make each bridge row continuous between its ends.
    for y in range(th):
        xs = [x for x in range(tw) if grid[y][x] == '=']
        for a, b in zip(xs, xs[1:]):
            if 1 < b - a <= 3:   # only the dotted gaps, never across the hall
                for x in range(a + 1, b):
                    if grid[y][x] == '.':
                        grid[y][x] = '='
    # User's edit (2026-11-21): no fire in the shaft up to the boss - the orange marks above the long hall's ceiling
    # (the boss shaft) become plain wall; the fire in the chute down into the hall stays.
    hall_top = min((y for y in range(th) for x in range(tw) if grid[y][x] == '|'), default=th)
    for y in range(th):
        for x in range(tw):
            if grid[y][x] == 'F' and y < hall_top - 2:
                grid[y][x] = '#'
    # User's edit (2026-11-21): platforms in the boss arena - two low ledges and a high one between them, clear of the
    # shaft hole by the left wall and of the boss's corner on the right.
    bm = [(x, y) for y in range(th) for x in range(tw) if grid[y][x] == 'B']
    if bm:
        bx, by = bm[0]
        fy = by
        while fy < th - 1 and grid[fy + 1][bx] != '#':
            fy += 1
        fy += 1                                   # first solid row under the marker = the arena floor
        lx = rx = bx
        while lx > 0 and grid[fy - 1][lx - 1] != '#':
            lx -= 1
        while rx < tw - 1 and grid[fy - 1][rx + 1] != '#':
            rx += 1
        w = rx - lx + 1
        for x0, length, up in ((lx + 8, 8, 5), (lx + int(w * 0.38), 9, 9), (lx + int(w * 0.6), 7, 5)):
            for x in range(x0, min(rx - 8, x0 + length)):
                if grid[fy - up][x] == '.':
                    grid[fy - up][x] = '#'
        print('boss arena', (lx, rx), 'floor row', fy, 'platforms added')
    # The lift track is drawn dotted too: one solid block of '|' over its bounding box (one lift, not many).
    lift = [(x, y) for y in range(th) for x in range(tw) if grid[y][x] == '|']
    if lift:
        lx0, lx1 = min(c[0] for c in lift), max(c[0] for c in lift)
        ly0, ly1 = min(c[1] for c in lift), max(c[1] for c in lift)
        for y in range(ly0, ly1 + 1):
            for x in range(lx0, lx1 + 1):
                if grid[y][x] in '.|':
                    grid[y][x] = '|'
    # Enemies: "lots in every room". Rooms are tile rectangles of this drawing; each gets its kinds spread evenly
    # (by x) over the cells an enemy can stand on (clear above, solid ledge below, away from the start/checkpoints).
    # The user's wish: a second lever by the checkpoint before the boss (the last one, furthest right) that opens the
    # gate up to the boss. Put it on the nearest free floor cell beside that checkpoint.
    cps = [(x, y) for y in range(th) for x in range(tw) if grid[y][x] == 'C']
    if cps:
        cx, cy = max(cps)
        placed = False
        for d in (3, -3, 4, -4, 5, -5, 2, -2, 6, -6):
            for dy in (0, 1, -1, 2, -2):
                x, y = cx + d, cy + dy
                if 0 < x < tw - 1 and 0 < y < th - 1 and grid[y][x] == '.' and grid[y - 1][x] == '.' and grid[y + 1][x] == '#':
                    grid[y][x] = 'L'
                    placed = True
                    print('second lever at', (x, y), 'beside checkpoint', (cx, cy))
                    break
            if placed:
                break
    ROOMS = [  # (x0, x1, y0, y1 rows top..bottom, kinds)
        (2, 55, 103, 129, 'gsghggsgkgh'),
        (56, 93, 68, 129, 'gdsgkdghsdgkg'),
        (94, 178, 64, 82, 'ghsdgbgdkgshgd'),
        (172, 362, 75, 102, 'gdsgdhkdgsdbgdgkshdgbdg'),
    ]
    marks = [(x, y) for y in range(th) for x in range(tw) if grid[y][x] in 'PC']

    def standable(x, y, tall):
        if not (0 <= x < tw and 0 < y < th - 1) or grid[y + 1][x] != '#':
            return False
        if any(grid[y - k][x] != '.' for k in range(tall) if y - k >= 0):
            return False
        return all(abs(x - mx) > 8 or abs(y - my) > 6 for mx, my in marks)

    for x0, x1, y0, y1, kinds in ROOMS:
        cand = [(x, y) for y in range(y0, y1 + 1) for x in range(x0, x1 + 1) if standable(x, y, 3)]
        cand.sort()
        n = len(kinds)
        used = []
        for i, k in enumerate(kinds):
            if not cand:
                break
            want = cand[min(len(cand) - 1, int((i + 0.5) * len(cand) / n))]
            pick = min((c for c in cand if all(abs(c[0] - u[0]) > 3 or abs(c[1] - u[1]) > 3 for u in used)),
                       key=lambda c: abs(c[0] - want[0]) + abs(c[1] - want[1]), default=None)
            if pick is None:
                break
            used.append(pick)
            grid[pick[1]][pick[0]] = k
        print('room', (x0, y0), 'enemies', len(used), 'of', n)
    with open(out, 'w', newline='\n') as f:
        f.write(';story\n;Story01 - converted from the user\'s drawing by tools/story/convert_story_map.py (1 px = 1 tile)\n')
        for row in grid:
            f.write(''.join(row) + '\n')
    print('wrote', out, tw, 'x', th)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
