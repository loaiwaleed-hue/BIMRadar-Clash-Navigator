import math, itertools
def rotz(a, t):
    c, s = math.cos(a), math.sin(a)
    return lambda p: (c*p[0]-s*p[1]+t[0], s*p[0]+c*p[1]+t[1], p[2]+t[2])
def inv_rotz(a, t):
    c, s = math.cos(a), math.sin(a)
    def f(p):
        x, y, z = p[0]-t[0], p[1]-t[1], p[2]-t[2]
        return (c*x+s*y, -s*x+c*y, z)
    return f
def aabb(f, lo, hi):
    pts = [f((x, y, z)) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
    return tuple(min(p[i] for p in pts) for i in range(3)), tuple(max(p[i] for p in pts) for i in range(3))
def hint(empty, off, boxm, near):
    if off is not None and off > max(boxm, 2.0):
        return "Element found %.1f m from the Revizto point: shared coordinates may differ. Use 'Go to element'." % off
    if empty:
        lk = [n for n, c, l in near if l and c > 0]
        if lk: return "Nothing from this model here. Elements here belong to link '%s'. Check the zone / anchor in Revizto (e.g. G2 vs G3)." % lk[0]
        if all(c == 0 for n, c, l in near): return "Nothing from this model or its links at this location: probably not from this model, or the coordinates are wrong."
    return ""
# 1. AABB: link rotated 90 deg and 30 deg, translated; host box -> link coords
a = math.radians(90); T = (50, 20, 5)
lo, hi = (0, 0, 0), (10, 4, 3)
link_to_host = rotz(a, T); host_to_link = inv_rotz(a, T)
h_lo, h_hi = aabb(link_to_host, lo, hi)
print("90deg: link box -> host", h_lo, h_hi)
assert all(abs(x-y) < 1e-9 for x, y in zip(h_lo, (46, 20, 5))) and all(abs(x-y) < 1e-9 for x, y in zip(h_hi, (50, 30, 8)))
b_lo, b_hi = aabb(host_to_link, h_lo, h_hi)
print("90deg: round trip", b_lo, b_hi)
assert all(abs(x-y) < 1e-9 for x, y in zip(b_lo, lo)) and all(abs(x-y) < 1e-9 for x, y in zip(b_hi, hi))
a = math.radians(30); T = (-12.5, 7.25, 1.0)
box = ((100, 40, 0), (110, 46, 9))
lo2, hi2 = aabb(inv_rotz(a, T), *box)
print("30deg: host box -> link AABB", [round(v, 4) for v in lo2], [round(v, 4) for v in hi2])
# the AABB must contain every transformed corner and be at least as large as the original box (rotation grows it)
for p in itertools.product(*zip(*box)):
    q = inv_rotz(a, T)(p)
    assert all(lo2[i]-1e-9 <= q[i] <= hi2[i]+1e-9 for i in range(3))
assert (hi2[0]-lo2[0]) > 10 and (hi2[1]-lo2[1]) > 6
# 2. hints
cases = [
 ("far element", False, 7.34, 3.0, [("host", 12, False)], "Element found 7.3 m"),
 ("offset equals limit", False, 3.0, 3.0, [("host", 12, False)], ""),
 ("offset 2.5, box 1: limit 2.0", False, 2.5, 1.0, [("host", 3, False)], "Element found 2.5 m"),
 ("empty, link has elements", True, None, 3.0, [("host", 0, False), ("X-G3", 40, True)], "Elements here belong to link 'X-G3'"),
 ("empty, all zero", True, None, 3.0, [("host", 0, False), ("X-G3", 0, True)], "probably not from this model"),
 ("empty, only host near", True, None, 3.0, [("host", 5, False)], ""),
 ("not empty, no offset", False, None, 3.0, [("host", 12, False)], ""),
 ("far element beats empty", True, 9.0, 3.0, [("host", 0, False), ("L", 4, True)], "Element found 9.0 m"),
]
for name, e, off, bm, near, want in cases:
    h = hint(e, off, bm, near)
    print("%-30s -> %r" % (name, h))
    assert (want in h) if want else h == "", name
print("ALL OK")
