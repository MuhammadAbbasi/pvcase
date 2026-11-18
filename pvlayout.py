#!/usr/bin/env python3
"""pvlayout - local, dependency-free utility-scale PV layout generator.

Input : site boundary + exclusion polygons (DXF layers or JSON).
Output: layout DXF (opens in AutoCAD), BOM CSV, summary on stdout.
Covers: single-axis trackers (N-S axis) or fixed-tilt tables, setbacks,
partial trackers, piles, string-inverter grouping, DC/AC cable routing.
Pure stdlib. Units: metres.
"""
import argparse, csv, json, math, sys
from pathlib import Path

DEFAULTS = {
    "mount": "tracker",            # "tracker" (N-S axis, E-W tracking) or "fixed" (E-W rows, south facing)
    "module": {"length": 2.278, "width": 1.134, "power_w": 550, "gap": 0.02},
    "modules_per_string": 28,
    "strings_per_unit": 3,         # strings per full tracker / table
    "min_strings_per_unit": 1,     # partial trackers allowed down to this
    "rows_of_modules": 1,          # fixed only: modules stacked in portrait (2 = 2P table)
    "tilt_deg": 25,                # fixed only
    "gcr": 0.35,                   # ground coverage ratio -> row pitch
    "unit_gap": 1.0,               # gap between consecutive units on the same row (m)
    "motor_gap": 0.6,              # tracker only: drive gap per unit (m)
    "setback": 10.0,               # from boundary and exclusions (m)
    "search_step": 1.0,            # ponytail: greedy first-fit packing; smaller = denser, slower
    "pile_spacing": 7.0,
    "strings_per_inverter": 18,
    "inverter_kw": 300,
    "substation": None,            # [x, y]; default = centroid of inverters
    "cable_slack": 1.05,           # calibration knob: sag, terminations, routing detours
}

# ---------------------------------------------------------------- geometry
def point_in_poly(x, y, poly):
    inside, n = False, len(poly)
    for i in range(n):
        (x1, y1), (x2, y2) = poly[i], poly[(i + 1) % n]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            inside = not inside
    return inside

def seg_hits_rect(p, q, r, eps=1e-9):
    """Liang-Barsky: does segment p-q pass through the rect interior?"""
    x0, y0, x1, y1 = r[0] + eps, r[1] + eps, r[2] - eps, r[3] - eps
    dx, dy = q[0] - p[0], q[1] - p[1]
    t0, t1 = 0.0, 1.0
    for pk, qk in ((-dx, p[0] - x0), (dx, x1 - p[0]), (-dy, p[1] - y0), (dy, y1 - p[1])):
        if pk == 0:
            if qk < 0:
                return False
        else:
            t = qk / pk
            if pk < 0: t0 = max(t0, t)
            else: t1 = min(t1, t)
    return t1 - t0 > eps

def edges(poly):
    return zip(poly, poly[1:] + poly[:1])

def corners(r):
    return ((r[0], r[1]), (r[2], r[1]), (r[2], r[3]), (r[0], r[3]))

def rect_ok(r, boundary, exclusions):
    if not all(point_in_poly(x, y, boundary) for x, y in corners(r)):
        return False
    if any(seg_hits_rect(p, q, r) for p, q in edges(boundary)):
        return False
    for ex in exclusions:
        if any(point_in_poly(x, y, ex) for x, y in corners(r)):
            return False
        if any(seg_hits_rect(p, q, r) for p, q in edges(ex)):
            return False
    return True

# Layout is computed in a local frame where the unit's long axis is +y.
# Trackers: local == global. Fixed rows run E-W, so we swap x/y in and out.
def swap(pts):
    return [(y, x) for x, y in pts]

# ---------------------------------------------------------------- layout
def unit_dims(cfg, n_strings):
    m, mods = cfg["module"], n_strings * cfg["modules_per_string"]
    if cfg["mount"] == "tracker":   # 1P portrait: module width along axis
        length = mods * (m["width"] + m["gap"]) + cfg["motor_gap"]
        width = m["length"]
    else:                           # fixed, landscape-across portrait-stack: modules side by side E-W
        per_row = math.ceil(mods / cfg["rows_of_modules"])
        length = per_row * (m["width"] + m["gap"])
        width = cfg["rows_of_modules"] * (m["length"] + m["gap"]) * math.cos(math.radians(cfg["tilt_deg"]))
    return length, width

def row_pitch(cfg):
    m = cfg["module"]
    collector = m["length"] if cfg["mount"] == "tracker" else cfg["rows_of_modules"] * (m["length"] + m["gap"])
    return collector / cfg["gcr"]

def fill(boundary, exclusions, cfg):
    s, step = cfg["setback"], cfg["search_step"]
    pitch = row_pitch(cfg)
    xs, ys = [p[0] for p in boundary], [p[1] for p in boundary]
    options = [(n, *unit_dims(cfg, n)) for n in range(cfg["strings_per_unit"], cfg["min_strings_per_unit"] - 1, -1)]
    width = options[0][2]
    units, x, col = [], min(xs) + s, 0
    while x + width + s <= max(xs):
        y = min(ys) + s
        while y < max(ys) - s:
            for n, length, _ in options:
                if rect_ok((x - s, y - s, x + width + s, y + length + s), boundary, exclusions):
                    units.append({"col": col, "strings": n, "rect": (x, y, x + width, y + length)})
                    y += length + cfg["unit_gap"]
                    break
            else:
                y += step
        x += pitch
        col += 1
    return units

def piles(u, cfg):
    x0, y0, x1, y1 = u["rect"]
    n = math.ceil((y1 - y0) / cfg["pile_spacing"]) + 1
    lines = [(x0 + x1) / 2] if cfg["mount"] == "tracker" else [x0 + 0.15 * (x1 - x0), x1 - 0.15 * (x1 - x0)]
    return [(lx, y0 + i * (y1 - y0) / (n - 1)) for lx in lines for i in range(n)]

def electrical(units, cfg):
    # snake order (up one row, down the next) keeps each inverter's units adjacent
    order = sorted(units, key=lambda u: (u["col"], u["rect"][1] * (1 if u["col"] % 2 == 0 else -1)))
    groups, cur, cur_s = [], [], 0
    for u in order:
        if cur and cur_s + u["strings"] > cfg["strings_per_inverter"]:
            groups.append(cur); cur, cur_s = [], 0
        cur.append(u); cur_s += u["strings"]
    if cur:
        groups.append(cur)
    inverters = []
    for i, g in enumerate(groups, 1):
        # ponytail: inverter at group centroid, snapped into the inter-row corridor; no road/trench model
        cx = sum((u["rect"][0] + u["rect"][2]) / 2 for u in g) / len(g)
        cy = sum((u["rect"][1] + u["rect"][3]) / 2 for u in g) / len(g)
        near = min(g, key=lambda u: abs((u["rect"][0] + u["rect"][2]) / 2 - cx))
        cx = near["rect"][2] + (row_pitch(cfg) - (near["rect"][2] - near["rect"][0])) / 2
        inv = {"id": f"INV-{i:02d}", "pos": (cx, cy), "units": g, "strings": sum(u["strings"] for u in g)}
        for u in g:
            x0, y0, x1, y1 = u["rect"]
            start = ((x0 + x1) / 2, y1 if cy > (y0 + y1) / 2 else y0)   # unit end facing the inverter
            u["inverter"], u["dc_route"] = inv["id"], [start, (start[0], cy), (cx, cy)]
        inverters.append(inv)
    return inverters

def route_len(pts):
    return sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))

def design(boundary, exclusions, cfg):
    fixed = cfg["mount"] == "fixed"
    b, ex = (swap(boundary), [swap(e) for e in exclusions]) if fixed else (boundary, exclusions)
    units = fill(b, ex, cfg)
    invs = electrical(units, cfg)
    sub = cfg["substation"]
    if sub is None and invs:
        sub = (sum(i["pos"][0] for i in invs) / len(invs), sum(i["pos"][1] for i in invs) / len(invs))
    elif sub is not None and fixed:
        sub = (sub[1], sub[0])
    for i in invs:
        i["ac_route"] = [i["pos"], (i["pos"][0], sub[1]), tuple(sub)]
    if fixed:   # back to global frame
        for u in units:
            x0, y0, x1, y1 = u["rect"]
            u["rect"] = (y0, x0, y1, x1)
            u["dc_route"] = swap(u["dc_route"])
        for i in invs:
            i["pos"], i["ac_route"] = (i["pos"][1], i["pos"][0]), swap(i["ac_route"])
        sub = (sub[1], sub[0]) if sub else None
    return {"units": units, "inverters": invs, "substation": sub}

def unit_piles(u, cfg):
    if cfg["mount"] == "tracker":
        return piles(u, cfg)
    x0, y0, x1, y1 = u["rect"]                 # compute in local frame, swap back
    return swap(piles({"rect": (y0, x0, y1, x1)}, cfg))

# ---------------------------------------------------------------- BOM
def bom(d, cfg):
    strings = sum(u["strings"] for u in d["units"])
    modules = strings * cfg["modules_per_string"]
    dc_m = sum(route_len(u["dc_route"]) * u["strings"] * 2 for u in d["units"]) * cfg["cable_slack"]
    ac_m = sum(route_len(i["ac_route"]) for i in d["inverters"]) * cfg["cable_slack"]
    full = sum(u["strings"] == cfg["strings_per_unit"] for u in d["units"])
    kind = "trackers" if cfg["mount"] == "tracker" else "tables"
    rows = [
        (f"{kind} (full, {cfg['strings_per_unit']} strings)", full, "pcs"),
        (f"{kind} (partial)", len(d["units"]) - full, "pcs"),
        ("strings", strings, "pcs"),
        (f"modules {cfg['module']['power_w']} W", modules, "pcs"),
        ("piles", sum(len(unit_piles(u, cfg)) for u in d["units"]), "pcs"),
        (f"string inverters {cfg['inverter_kw']} kW", len(d["inverters"]), "pcs"),
        ("DC string cable (+/- incl. slack)", round(dc_m), "m"),
        ("AC cable inverter->substation (per circuit)", round(ac_m), "m"),
        ("DC capacity", round(modules * cfg["module"]["power_w"] / 1000, 1), "kWp"),
        ("AC capacity", len(d["inverters"]) * cfg["inverter_kw"], "kW"),
    ]
    return rows

# ---------------------------------------------------------------- DXF I/O (R12 ASCII)
def read_dxf_polys(path):
    """Return {layer: [polygon, ...]} for LWPOLYLINE and POLYLINE entities."""
    lines = Path(path).read_text(errors="replace").splitlines()
    pairs = [(lines[i].strip(), lines[i + 1].strip()) for i in range(0, len(lines) - 1, 2)]
    out, cur, kind, layer, x = {}, None, None, None, None
    def flush():
        if cur and len(cur) >= 3:
            out.setdefault(layer, []).append(cur)
    for code, val in pairs:
        if code == "0":
            if val in ("LWPOLYLINE", "POLYLINE"):
                flush(); cur, kind, layer = [], val, None
            elif val == "VERTEX" and kind == "POLYLINE":
                pass
            elif val == "SEQEND" or kind == "LWPOLYLINE" or kind is None:
                flush(); cur, kind = None, None
        elif cur is not None:
            if code == "8" and layer is None: layer = val
            elif code == "10": x = float(val)
            elif code == "20" and x is not None: cur.append((x, float(val))); x = None
    flush()
    for polys in out.values():   # POLYLINE header carries a dummy 0,0 vertex
        for p in polys:
            if p and p[0] == (0.0, 0.0) and len(p) > 3: p.pop(0)
    return out

LAYERS = {"PV_BOUNDARY": 3, "PV_EXCLUSION": 1, "PV_UNITS": 5, "PV_STRINGS": 8, "PV_PILES": 9,
          "PV_INVERTERS": 6, "PV_CABLE_DC": 30, "PV_CABLE_AC": 2, "PV_SUBSTATION": 1, "PV_TEXT": 7}

class Dxf:
    def __init__(self): self.e = []
    def poly(self, pts, layer, closed=False):
        self.e.append(f"0\nPOLYLINE\n8\n{layer}\n66\n1\n70\n{1 if closed else 0}\n10\n0\n20\n0\n30\n0")
        self.e += [f"0\nVERTEX\n8\n{layer}\n10\n{x:.3f}\n20\n{y:.3f}\n30\n0" for x, y in pts]
        self.e.append(f"0\nSEQEND\n8\n{layer}")
    def circle(self, c, r, layer):
        self.e.append(f"0\nCIRCLE\n8\n{layer}\n10\n{c[0]:.3f}\n20\n{c[1]:.3f}\n30\n0\n40\n{r}")
    def text(self, p, s, h, layer):
        self.e.append(f"0\nTEXT\n8\n{layer}\n10\n{p[0]:.3f}\n20\n{p[1]:.3f}\n30\n0\n40\n{h}\n1\n{s}")
    def save(self, path):
        tbl = "".join(f"0\nLAYER\n2\n{n}\n70\n0\n62\n{c}\n6\nCONTINUOUS\n" for n, c in LAYERS.items())
        Path(path).write_text(
            f"0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n{len(LAYERS)}\n{tbl}0\nENDTAB\n0\nENDSEC\n"
            "0\nSECTION\n2\nENTITIES\n" + "\n".join(self.e) + "\n0\nENDSEC\n0\nEOF\n")

def write_dxf(path, d, boundary, exclusions, cfg):
    dx = Dxf()
    dx.poly(boundary, "PV_BOUNDARY", True)
    for e in exclusions: dx.poly(e, "PV_EXCLUSION", True)
    for k, u in enumerate(d["units"], 1):
        x0, y0, x1, y1 = u["rect"]
        dx.poly(corners(u["rect"]), "PV_UNITS", True)
        n = u["strings"]
        for i in range(1, n):   # string divisions
            if cfg["mount"] == "tracker":
                y = y0 + i * (y1 - y0) / n; dx.poly([(x0, y), (x1, y)], "PV_STRINGS")
            else:
                x = x0 + i * (x1 - x0) / n; dx.poly([(x, y0), (x, y1)], "PV_STRINGS")
        for p in unit_piles(u, cfg): dx.circle(p, 0.15, "PV_PILES")
        dx.poly(u["dc_route"], "PV_CABLE_DC")
    for i in d["inverters"]:
        x, y = i["pos"]
        dx.poly([(x - 1, y - 0.5), (x + 1, y - 0.5), (x + 1, y + 0.5), (x - 1, y + 0.5)], "PV_INVERTERS", True)
        dx.text((x + 1.5, y), f"{i['id']} ({i['strings']} str)", 1.5, "PV_TEXT")
        dx.poly(i["ac_route"], "PV_CABLE_AC")
    if d["substation"]:
        sx, sy = d["substation"]
        dx.poly([(sx - 5, sy - 3), (sx + 5, sy - 3), (sx + 5, sy + 3), (sx - 5, sy + 3)], "PV_SUBSTATION", True)
        dx.text((sx - 5, sy + 4), "SUBSTATION", 2.5, "PV_TEXT")
    dx.save(path)

# ---------------------------------------------------------------- CLI
def load_cfg(path):
    cfg = json.loads(json.dumps(DEFAULTS))
    if path:
        user = json.loads(Path(path).read_text())
        cfg.update({k: v for k, v in user.items() if k != "module"})
        cfg["module"].update(user.get("module", {}))
    if cfg["mount"] not in ("tracker", "fixed"):
        sys.exit("mount must be 'tracker' or 'fixed'")
    return cfg

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("site", help="site .dxf (polylines on boundary/exclusion layers) or .json {boundary, exclusions}")
    ap.add_argument("-c", "--config", help="JSON overriding defaults (see DEFAULTS in pvlayout.py)")
    ap.add_argument("-o", "--out", default="layout", help="output prefix -> PREFIX.dxf, PREFIX_bom.csv")
    ap.add_argument("--boundary-layer", default="PV_BOUNDARY")
    ap.add_argument("--exclusion-layer", default="PV_EXCLUSION")
    a = ap.parse_args(argv)
    cfg = load_cfg(a.config)
    if a.site.lower().endswith(".dxf"):
        polys = read_dxf_polys(a.site)
        if a.boundary_layer not in polys:
            sys.exit(f"no closed polyline on layer {a.boundary_layer}; layers found: {sorted(polys)}")
        boundaries, exclusions = polys[a.boundary_layer], polys.get(a.exclusion_layer, [])
    else:
        site = json.loads(Path(a.site).read_text())
        boundaries, exclusions = [site["boundary"]], site.get("exclusions", [])
    for k, boundary in enumerate(boundaries):
        boundary = [tuple(p) for p in boundary]
        excl = [[tuple(p) for p in e] for e in exclusions]
        d = design(boundary, excl, cfg)
        prefix = a.out if len(boundaries) == 1 else f"{a.out}_{k + 1}"
        Path(prefix).parent.mkdir(parents=True, exist_ok=True)
        write_dxf(prefix + ".dxf", d, boundary, excl, cfg)
        rows = bom(d, cfg)
        with open(prefix + "_bom.csv", "w", newline="") as f:
            csv.writer(f).writerows([("item", "qty", "unit"), *rows])
        print(f"== {prefix}.dxf")
        for r in rows: print(f"  {r[0]:<45} {r[1]:>12} {r[2]}")

if __name__ == "__main__":
    main()
