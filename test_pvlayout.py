import pvlayout as pv, tempfile, os

B = [(0, 0), (300, 0), (300, 300), (0, 300)]
EX = [[(100, 100), (160, 100), (160, 160), (100, 160)]]

def overlaps(a, b):
    return a[0] < b[2] and b[0] < a[2] and a[1] < b[3] and b[1] < a[3]

for mount in ("tracker", "fixed"):
    cfg = pv.load_cfg(None); cfg["mount"] = mount
    d = pv.design(B, EX, cfg)
    us = d["units"]
    assert us, mount
    s = cfg["setback"]
    for u in us:
        r = u["rect"]
        assert r[0] >= s - 1e-6 and r[1] >= s - 1e-6 and r[2] <= 300 - s + 1e-6 and r[3] <= 300 - s + 1e-6, (mount, r)
        assert not overlaps((r[0]-s, r[1]-s, r[2]+s, r[3]+s), (100, 100, 160, 160)), (mount, r)
    for i, a in enumerate(us):
        for b in us[i+1:]:
            assert not overlaps(a["rect"], b["rect"]), mount
    assert all(i["strings"] <= cfg["strings_per_inverter"] for i in d["inverters"])
    assert sum(i["strings"] for i in d["inverters"]) == sum(u["strings"] for u in us)
    # DXF round trip: boundary readable back
    f = os.path.join(tempfile.mkdtemp(), "t.dxf")
    pv.write_dxf(f, d, B, EX, cfg)
    back = pv.read_dxf_polys(f)
    assert back["PV_BOUNDARY"][0] == B, back["PV_BOUNDARY"]
    assert len(back["PV_UNITS"]) == len(us)
    print(mount, "ok:", len(us), "units,", len(d["inverters"]), "inverters")
