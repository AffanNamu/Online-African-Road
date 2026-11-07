import sys
from pathlib import Path
import pytest
sys.path.insert(0, str(Path(__file__).resolve().parent))
import library_audit as L


def e(i, **k): return dict({"id": i, "assetClass": L.expected_class(i), "region": None, "city": None, "status": "concept_approved"}, **k)


def test_three_way_takes_the_side_that_changed_and_keeps_new_entries():
    base = {"a": e("aro_a"), "b": e("aro_b")}
    a = {"a": e("aro_a", status="unity_import_verified"), "b": e("aro_b"), "c": e("aro_c")}
    b = {"a": e("aro_a"), "b": e("aro_b", status="blender_validation_passed")}
    m, c = L.three_way(base, a, b)
    assert not c and m["a"]["status"] == "unity_import_verified" and m["b"]["status"] == "blender_validation_passed" and "c" in m


def test_three_way_reports_an_entry_both_sides_changed_differently():
    base = {"a": e("aro_a")}; m, c = L.three_way(base, {"a": e("aro_a", status="x")}, {"a": e("aro_a", status="y")})
    assert c == [("a", ["status"])] and "a" not in m


def test_merge_registries_stops_on_a_conflict_and_orders_by_first_appearance():
    d = lambda assets, **k: dict({"assets": assets, "note": "n"}, **k)
    merged = L.merge_registries(d([e("aro_a")]), d([e("aro_a"), e("aro_c")], note="n2"), d([e("aro_a"), e("aro_d")]))
    assert [x["id"] for x in merged["assets"]] == ["aro_a", "aro_c", "aro_d"] and merged["note"] == "n2"
    with pytest.raises(SystemExit): L.merge_registries(d([e("aro_a")]), d([e("aro_a", status="x")]), d([e("aro_a", status="y")]))


def test_class_problems_checks_prefix_region_and_city_fields():
    reg = {"regions": {"ngn": "Northern Nigeria"}, "cities": {"kano": "Kano"}, "assets": [
        e("aro_ok"), e("rg_ngn_ok", region="ngn"), e("kano_ok", city="kano"), e("lmk_kano_ok", city="kano"),
        e("aro_bad", region="ngn"), e("rg_ngn_bad", region="xxx"), e("kano_bad", city="lagos"), dict(e("rg_ngn_cls", region="ngn"), assetClass="GLOBAL")]}
    bad = {i for i, _ in L.class_problems(reg)}
    assert bad == {"aro_bad", "rg_ngn_bad", "kano_bad", "rg_ngn_cls"}
