from __future__ import annotations

import copy
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS_DIRECTORY = Path(__file__).resolve().parents[1]
if str(TOOLS_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIRECTORY))

from validate_theme_v2 import (  # noqa: E402
    SCHEMA_PATH,
    ThemeV2ValidationError,
    load_theme_v2_manifest,
    validate_theme_v2,
    validate_theme_v2_document,
)
from validate_visual_pack import PROFILE_CONTRACTS  # noqa: E402


EXAMPLE_PATH = TOOLS_DIRECTORY / "examples" / "reference-theme-v2.example.json"
PROTOCOL_PATH = TOOLS_DIRECTORY / "THEME_RUNTIME_PROTOCOL_V2.md"


def _example() -> dict[str, object]:
    return json.loads(EXAMPLE_PATH.read_text(encoding="utf-8"))


def _radial_8() -> dict[str, object]:
    manifest = _example()
    manifest["id"] = "radial-8-v2-fixture"
    manifest["layoutProfile"] = "radial-8"
    manifest["compatibleLayouts"] = ["radial-8"]
    states = manifest["states"]
    hashes = manifest["assetHashes"]
    assert isinstance(states, dict)
    assert isinstance(hashes, dict)
    for slot, digest in ((7, "A" * 64), (8, "B" * 64)):
        path = f"states/selected-{slot}.png"
        states[f"selected-{slot}"] = {"slotId": slot, "assets": {"stateFrame": path}}
        hashes[path] = digest
    return manifest


def _replace_asset_path(manifest: dict[str, object], old: str, new: str) -> None:
    layers = manifest["layers"]
    hashes = manifest["assetHashes"]
    assert isinstance(layers, list)
    assert isinstance(hashes, dict)
    for layer in layers:
        if isinstance(layer, dict) and layer.get("asset") == old:
            layer["asset"] = new
    hashes[new] = hashes.pop(old)


class ThemeV2ValidatorTests(unittest.TestCase):
    def assert_invalid(self, manifest: dict[str, object], expected: str) -> None:
        with self.assertRaisesRegex(ThemeV2ValidationError, expected):
            validate_theme_v2_document(manifest)

    def test_formal_schema_is_draft_2020_12_json_with_resolved_local_refs(self) -> None:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))

        self.assertEqual("https://json-schema.org/draft/2020-12/schema", schema["$schema"])
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(
            ["full-state-frame", "layered-state"],
            schema["properties"]["renderStrategy"]["enum"],
        )
        self.assertEqual(
            ["external-artwork", "procedural", "mixed"],
            schema["properties"]["authoring"]["properties"]["method"]["enum"],
        )
        self.assertEqual("straight", schema["$defs"]["canvas"]["properties"]["alphaMode"]["const"])
        self.assertEqual(
            1,
            schema["allOf"][0]["then"]["properties"]["compatibleLayouts"]["maxItems"],
        )

        def walk(value: object) -> None:
            if isinstance(value, dict):
                reference = value.get("$ref")
                if isinstance(reference, str) and reference.startswith("#/$defs/"):
                    self.assertIn(reference.removeprefix("#/$defs/"), schema["$defs"])
                for nested in value.values():
                    walk(nested)
            elif isinstance(value, list):
                for nested in value:
                    walk(nested)

        walk(schema)

    def test_valid_radial_6_example_passes(self) -> None:
        report = validate_theme_v2_document(_example())

        self.assertEqual("radial-6", report.layout_profile)
        self.assertEqual(7, report.state_count)
        self.assertEqual("full-state-frame", report.render_strategy)

    def test_valid_radial_8_manifest_passes(self) -> None:
        report = validate_theme_v2_document(_radial_8())

        self.assertEqual("radial-8", report.layout_profile)
        self.assertEqual(9, report.state_count)

    def test_layered_state_manifest_passes(self) -> None:
        manifest = _example()
        manifest["renderStrategy"] = "layered-state"
        required = manifest["capabilities"]["required"]  # type: ignore[index]
        required.remove("fullStateFrame")  # type: ignore[union-attr]
        required.append("layeredState")  # type: ignore[union-attr]

        report = validate_theme_v2_document(manifest)

        self.assertEqual("layered-state", report.render_strategy)

    def test_procedural_authoring_is_rejected_as_render_strategy(self) -> None:
        manifest = _example()
        manifest["renderStrategy"] = "procedural-authoring"

        self.assert_invalid(manifest, "unsupported renderStrategy: procedural-authoring")

    def test_authoring_metadata_does_not_change_runtime_capabilities(self) -> None:
        manifest = _example()
        manifest["authoring"] = {"method": "procedural"}

        report = validate_theme_v2_document(
            manifest,
            supported_capabilities={
                "fullStateFrame", "dynamicAnchors", "maskAssets", "instantTransitions"
            },
        )

        self.assertEqual("full-state-frame", report.render_strategy)

    def test_invalid_authoring_method_fails(self) -> None:
        manifest = _example()
        manifest["authoring"] = {"method": "runtime-script"}

        self.assert_invalid(manifest, "unsupported authoring method: runtime-script")

    def test_full_state_frame_rejects_multiple_compatible_layouts(self) -> None:
        manifest = _example()
        manifest["compatibleLayouts"] = ["radial-6", "radial-8"]

        self.assert_invalid(manifest, "full-state-frame requires exactly one compatible layout")

    def test_layered_state_accepts_coherent_multiple_layouts(self) -> None:
        manifest = _radial_8()
        manifest["layoutProfile"] = "radial-6"
        manifest["compatibleLayouts"] = ["radial-6", "radial-8"]
        manifest["renderStrategy"] = "layered-state"
        required = manifest["capabilities"]["required"]  # type: ignore[index]
        required.remove("fullStateFrame")  # type: ignore[union-attr]
        required.append("layeredState")  # type: ignore[union-attr]

        report = validate_theme_v2_document(manifest)

        self.assertEqual(("radial-6", "radial-8"), report.compatible_layouts)

    def test_missing_idle_state_fails(self) -> None:
        manifest = _example()
        del manifest["states"]["idle"]  # type: ignore[index]

        self.assert_invalid(manifest, "missing required state.*idle")

    def test_missing_selected_state_fails(self) -> None:
        manifest = _example()
        del manifest["states"]["selected-4"]  # type: ignore[index]

        self.assert_invalid(manifest, "missing required state.*selected-4")

    def test_duplicate_state_json_key_fails_during_load(self) -> None:
        source = EXAMPLE_PATH.read_text(encoding="utf-8")
        duplicate = source.replace(
            '"idle": {',
            '"idle": {"slotId": null, "assets": {"stateFrame": "states/idle.png"}},\n    "idle": {',
            1,
        )
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(duplicate, encoding="utf-8")

            with self.assertRaisesRegex(ThemeV2ValidationError, "duplicate JSON field: idle"):
                load_theme_v2_manifest(path)

    def test_duplicate_layer_id_fails(self) -> None:
        manifest = _example()
        manifest["layers"].append(copy.deepcopy(manifest["layers"][0]))  # type: ignore[union-attr,index]

        self.assert_invalid(manifest, "duplicate layer id: background")

    def test_duplicate_anchor_id_fails(self) -> None:
        manifest = _example()
        manifest["dynamicAnchors"].append(copy.deepcopy(manifest["dynamicAnchors"][0]))  # type: ignore[union-attr,index]

        self.assert_invalid(manifest, "duplicate anchor id: actionLabel")

    def test_layer_bounds_outside_canvas_fails(self) -> None:
        manifest = _example()
        manifest["layers"][0]["bounds"]["width"] = 1255  # type: ignore[index]

        self.assert_invalid(manifest, r"layers\[0\]\.bounds lies outside referenceCanvas")

    def test_anchor_bounds_outside_canvas_fails(self) -> None:
        manifest = _example()
        manifest["dynamicAnchors"][0]["bounds"]["x"] = -1  # type: ignore[index]

        self.assert_invalid(manifest, r"dynamicAnchors\[0\]\.bounds lies outside referenceCanvas")

    def test_unknown_render_strategy_fails(self) -> None:
        manifest = _example()
        manifest["renderStrategy"] = "magic-css"

        self.assert_invalid(manifest, "unsupported renderStrategy: magic-css")

    def test_alpha_mode_only_accepts_straight_source_assets(self) -> None:
        manifest = _example()
        manifest["referenceCanvas"]["alphaMode"] = "premultiplied"  # type: ignore[index]

        self.assert_invalid(manifest, "requires sRGB with straight alpha")

    def test_package_and_runtime_alpha_contract_is_documented(self) -> None:
        protocol = PROTOCOL_PATH.read_text(encoding="utf-8")

        for required_text in (
            "SOURCE / PACKAGE SPACE",
            "PNG-decoded RGBA",
            "straight alpha",
            "PREMULTIPLIED RESAMPLING SPACE",
            "Format32bppPArgb",
            "directly to the final Physical target size",
            "UpdateLayeredWindow",
            "semi-transparent red edge",
            "transparent colored pixel",
        ):
            self.assertIn(required_text, protocol)

    def test_unknown_surface_fails(self) -> None:
        manifest = _example()
        manifest["surface"] = "settings-page"

        self.assert_invalid(manifest, "unsupported surface: settings-page")

    def test_package_revision_is_not_protocol_version(self) -> None:
        manifest = _example()
        manifest["packageRevision"] = 0

        self.assert_invalid(manifest, "packageRevision must be at least 1")

    def test_unknown_layout_fails(self) -> None:
        manifest = _example()
        manifest["layoutProfile"] = "grid-3x2"

        self.assert_invalid(manifest, "unsupported layoutProfile: grid-3x2")

    def test_missing_hash_record_fails(self) -> None:
        manifest = _example()
        del manifest["assetHashes"]["states/idle.png"]  # type: ignore[index]

        self.assert_invalid(manifest, "missing asset hash record.*states/idle.png")

    def test_orphan_hash_record_fails(self) -> None:
        manifest = _example()
        manifest["assetHashes"]["unused.png"] = "C" * 64  # type: ignore[index]

        self.assert_invalid(manifest, "orphan asset hash record.*unused.png")

    def test_parent_path_traversal_fails(self) -> None:
        manifest = _example()
        _replace_asset_path(manifest, "layers/background.png", "../background.png")

        self.assert_invalid(manifest, "unsafe path segment|package-relative")

    def test_absolute_path_fails(self) -> None:
        manifest = _example()
        _replace_asset_path(manifest, "layers/background.png", "C:/theme/background.png")

        self.assert_invalid(manifest, "package-relative POSIX path")

    def test_ownership_conflict_fails(self) -> None:
        manifest = _example()
        manifest["elementOwnership"][0]["owner"] = "DYNAMIC"  # type: ignore[index]
        manifest["elementOwnership"][0]["contentKey"] = "badDynamicOwner"  # type: ignore[index]

        self.assert_invalid(manifest, "conflicts with layer ownership")

    def test_layer_kind_ownership_mismatch_fails(self) -> None:
        manifest = _example()
        manifest["layers"][1]["ownership"] = "STATIC"  # type: ignore[index]

        self.assert_invalid(manifest, "ownership must be STATE_ASSET for stateAsset")

    def test_duplicate_element_ownership_fails(self) -> None:
        manifest = _example()
        manifest["elementOwnership"].append(copy.deepcopy(manifest["elementOwnership"][0]))  # type: ignore[union-attr,index]

        self.assert_invalid(manifest, "duplicate element ownership: backgroundArt")

    def test_unknown_style_role_fails(self) -> None:
        manifest = _example()
        manifest["dynamicAnchors"][0]["styleRole"] = "missingStyle"  # type: ignore[index]

        self.assert_invalid(manifest, "unknown styleRole: missingStyle")

    def test_unknown_glyph_role_fails(self) -> None:
        manifest = _example()
        manifest["dynamicAnchors"][1]["glyphRole"] = "missingGlyph"  # type: ignore[index]

        self.assert_invalid(manifest, "unknown glyphRole: missingGlyph")

    def test_unknown_style_dependency_fails(self) -> None:
        manifest = _example()
        manifest["styles"]["dynamicRoles"]["actionLabel"]["fontRole"] = "missingFont"  # type: ignore[index]

        self.assert_invalid(manifest, "fontRole references unknown role: missingFont")

    def test_unsupported_required_capability_context_fails(self) -> None:
        manifest = _example()

        with self.assertRaisesRegex(ThemeV2ValidationError, "unsupported required capability.*maskAssets"):
            validate_theme_v2_document(
                manifest,
                supported_capabilities={"fullStateFrame", "dynamicAnchors", "instantTransitions"},
            )

    def test_supported_capability_context_passes(self) -> None:
        manifest = _example()

        validate_theme_v2_document(
            manifest,
            supported_capabilities={
                "fullStateFrame", "dynamicAnchors", "maskAssets", "instantTransitions"
            },
        )

    def test_state_slot_id_must_match_state_name(self) -> None:
        manifest = _example()
        manifest["states"]["selected-2"]["slotId"] = 3  # type: ignore[index]

        self.assert_invalid(manifest, "state/slot mismatch for selected-2")

    def test_state_must_fit_compatible_layout(self) -> None:
        manifest = _example()
        manifest["states"]["selected-7"] = {"slotId": 7, "assets": {"stateFrame": "states/selected-7.png"}}  # type: ignore[index]
        manifest["assetHashes"]["states/selected-7.png"] = "F" * 64  # type: ignore[index]
        manifest["capabilities"]["required"].append("futureStates")  # type: ignore[index]

        self.assert_invalid(manifest, "state selected-7 exceeds compatible layout slot count 6")

    def test_state_binding_to_unknown_layer_fails(self) -> None:
        manifest = _example()
        manifest["states"]["idle"]["assets"]["missingLayer"] = "states/other.png"  # type: ignore[index]
        manifest["assetHashes"]["states/other.png"] = "D" * 64  # type: ignore[index]

        self.assert_invalid(manifest, "references unknown/non-state layer: missingLayer")

    def test_required_state_layer_binding_fails(self) -> None:
        manifest = _example()
        del manifest["states"]["selected-3"]["assets"]["stateFrame"]  # type: ignore[index]
        del manifest["assetHashes"]["states/selected-3.png"]  # type: ignore[index]

        self.assert_invalid(manifest, "missing required layer binding.*stateFrame")

    def test_duplicate_glyph_fallback_source_type_fails(self) -> None:
        manifest = _example()
        sources = manifest["glyphs"]["roles"]["primaryAction"]["keyboard"]["sources"]  # type: ignore[index]
        sources.append({"type": "text", "styleRole": "actionGlyphText"})  # type: ignore[union-attr]

        self.assert_invalid(manifest, "repeats a source type")

    def test_dynamic_anchor_requires_matching_ownership(self) -> None:
        manifest = _example()
        manifest["elementOwnership"][2]["element"] = "renamedActionLabel"  # type: ignore[index]

        self.assert_invalid(manifest, "dynamic anchor actionLabel requires matching DYNAMIC elementOwnership")

    def test_unknown_visibility_selector_fails(self) -> None:
        manifest = _example()
        manifest["layers"][0]["visibleStates"] = ["hover"]  # type: ignore[index]

        self.assert_invalid(manifest, "unknown state selector: hover")

    def test_non_instant_transition_fails(self) -> None:
        manifest = _example()
        manifest["transitions"]["mode"] = "crossfade"  # type: ignore[index]

        self.assert_invalid(manifest, "permits only instant")

    def test_future_state_requires_capability(self) -> None:
        manifest = _example()
        manifest["states"]["confirm"] = {"slotId": None, "assets": {"stateFrame": "states/confirm.png"}}  # type: ignore[index]
        manifest["assetHashes"]["states/confirm.png"] = "E" * 64  # type: ignore[index]

        self.assert_invalid(manifest, "future states require the futureStates capability")

    def test_v1_manifest_is_not_interpreted_as_v2(self) -> None:
        v1 = {
            "id": "v1-fixture",
            "name": "V1 Fixture",
            "version": 1,
            "layoutProfile": "radial-6",
            "slotCount": 6,
            "base": "base.png",
            "selected": "selected.png",
            "layout": "layout.json",
            "selectionAssetMode": "canonical-transform",
        }

        self.assert_invalid(v1, "missing required field.*protocolVersion")

    def test_radial_8_capability_remains_stable_and_integrated(self) -> None:
        contract = PROFILE_CONTRACTS["radial-8"]

        self.assertEqual("stable", contract.status)
        self.assertTrue(contract.runtime_integrated)

    def test_check_assets_accepts_small_synthetic_files(self) -> None:
        manifest = _example()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index, relative_path in enumerate(sorted(manifest["assetHashes"])):  # type: ignore[arg-type]
                asset = root / relative_path
                asset.parent.mkdir(parents=True, exist_ok=True)
                content = f"synthetic-{index}".encode()
                asset.write_bytes(content)
                manifest["assetHashes"][relative_path] = hashlib.sha256(content).hexdigest()  # type: ignore[index]
            path = root / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")

            report = validate_theme_v2(path, check_assets=True)

            self.assertTrue(report.assets_verified)

    def test_check_assets_detects_hash_mismatch(self) -> None:
        manifest = _example()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for relative_path in manifest["assetHashes"]:  # type: ignore[union-attr]
                asset = root / relative_path
                asset.parent.mkdir(parents=True, exist_ok=True)
                asset.write_bytes(b"same-content")
            path = root / "manifest.json"
            path.write_text(json.dumps(manifest), encoding="utf-8")

            with self.assertRaisesRegex(ThemeV2ValidationError, "asset hash mismatch"):
                validate_theme_v2(path, check_assets=True)


if __name__ == "__main__":
    unittest.main()
