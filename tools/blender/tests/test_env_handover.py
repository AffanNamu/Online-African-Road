"""The environmental library handover is generated from facts and states what is NOT claimed. Pure text rendering (no Blender needed)."""
from env_kit import handover as H

CATS = {"markets_roadside_businesses": {"title": "Markets and roadside businesses"}, "traditional_architecture": {"title": "Traditional and local architecture"}}
REGIONS = {"ngn": "Northern Nigeria"}


def entry(**kw):
    e = {"id": "rg_ngn_kiosk_lockup_a", "assetClass": "REGIONAL", "region": "ngn", "library": "markets_roadside_businesses", "concepts": ["R-016", "R-021"], "status": "blender_validation_passed",
         "blenderSource": "blender/projects/props/rg_ngn_kiosk_lockup_a/v001/rg_ngn_kiosk_lockup_a.blend", "export": "unity/Models/rg_ngn_kiosk_lockup_a/v001/rg_ngn_kiosk_lockup_a.fbx", "lodTriangles": [900, 28], "materials": ["M_a", "M_b"],
         "unity": {"imported": False, "inGameVerified": False}}
    e.update(kw); return e


def test_every_asset_gets_a_row_with_its_facts():
    t = H.render([entry()], {"rg_ngn_kiosk_lockup_a": {"passed": True}}, CATS, REGIONS, "2026-11-02")
    row = [l for l in t.splitlines() if l.startswith("| `rg_ngn_kiosk_lockup_a`")][0]
    for must in ("REGIONAL", "Markets and roadside businesses", "Northern Nigeria", "R-016, R-021", "v001/rg_ngn_kiosk_lockup_a.blend", "Blender validation passed", "not imported", "900/28", "| 2 |"): assert must in row, must


def test_a_failed_report_is_shown_as_failed_and_an_unbuilt_asset_as_its_status():
    assert "FAILED" in H.render([entry()], {"rg_ngn_kiosk_lockup_a": {"passed": False}}, CATS, REGIONS, "d")
    t = H.render([entry(status="concept_approved", blenderSource=None, export=None, lodTriangles=None, materials=None)], {}, CATS, REGIONS, "d")
    assert "not built" in t and "not exported" in t and "concept_approved" in t


def test_an_asset_without_a_reference_says_so_and_a_global_asset_has_no_region():
    t = H.render([entry(id="rg_ngn_hausa_compound_b", concepts=[], ownerRequest="x"), entry(id="aro_drain_open_slab_a", assetClass="GLOBAL", region=None)], {}, CATS, REGIONS, "d")
    assert "owner request; no reference (M-01)" in t and "any (GLOBAL)" in t


def test_the_text_never_claims_unity_verification_or_approval():
    t = H.render([entry()], {}, CATS, REGIONS, "d")
    assert "None is imported into Unity or verified in the game, and none is approved" in t and "verified in the game |" not in t
    assert "lfs.github.com" in t and "RB-18 to RB-25 are not started" in t


def test_an_imported_asset_is_not_called_verified():
    t = H.render([entry(unity={"imported": True, "inGameVerified": False})], {}, CATS, REGIONS, "d")
    assert "imported; not verified in the game" in t
