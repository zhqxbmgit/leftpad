from __future__ import annotations

import copy
import hashlib
import json
import sys
import tempfile
import unittest
from fractions import Fraction
from pathlib import Path

from PIL import Image

TOOLS_DIRECTORY = Path(__file__).resolve().parents[1]
if str(TOOLS_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIRECTORY))

from validate_theme_v3 import (  # noqa: E402
    REQUIRED_CAPABILITIES,
    SCHEMA_PATH,
    ThemeV3ValidationError,
    target_outer_envelope_radius,
    validate_theme_v3,
    validate_theme_v3_document,
)
from universal_radial_settings_migration import (  # noqa: E402
    LEGACY_SETTINGS_SEMANTIC_REVISION,
    UNIVERSAL_SETTINGS_SEMANTIC_REVISION,
    legacy_surface_scale,
    legacy_surface_scale_ratio,
    migrate_legacy_dead_alpha,
    migrate_legacy_text_alpha,
)


EXAMPLE_PATH = TOOLS_DIRECTORY / "examples" / "reference-theme-v3.example.json"
PROTOCOL_PATH = TOOLS_DIRECTORY / "UNIVERSAL_RADIAL_PROTOCOL_V3.md"


def _content_box(*, label: bool) -> dict[str, object]:
    result: dict[str, object] = {
        "offsetX": 0,
        "offsetY": 0,
        "width": 4,
        "height": 4,
        "rotationDegrees": 0,
        "alignment": "center",
    }
    if label:
        result.update({"authoredFontSize": 15, "maxLines": 1})
    return result


def _manifest(slot_count: int) -> dict[str, object]:
    if slot_count not in (6, 8):
        raise ValueError("test fixture supports radial-6 and radial-8")
    profile = f"radial-{slot_count}"
    pitch = 360 / slot_count
    states: dict[str, object] = {
        "idle": {"slotId": None, "asset": "states/idle.png"}
    }
    semantic_layers: list[dict[str, object]] = [
        {
            "id": "static-residual",
            "role": "STATIC_RESIDUAL",
            "slotId": None,
            "asset": "semantic/static-residual.png",
            "zIndex": 0,
        },
        {
            "id": "hub",
            "role": "HUB",
            "slotId": None,
            "asset": "semantic/hub.png",
            "zIndex": 10,
        },
    ]
    masks: list[dict[str, object]] = []
    groups: list[dict[str, object]] = []
    for slot in range(1, slot_count + 1):
        angle = (slot - 1) * pitch
        states[f"selected-{slot}"] = {
            "slotId": slot,
            "asset": f"states/selected-{slot}.png",
        }
        masks.append(
            {
                "id": f"slot-{slot}-selection-mask",
                "asset": f"masks/slot-{slot}-selection.png",
                "coordinateSpace": "reference",
                "width": 8,
                "height": 8,
            }
        )
        semantic_layers.extend(
            [
                {
                    "id": f"slot-{slot}-fill",
                    "role": "PETAL_FILL",
                    "slotId": slot,
                    "asset": f"semantic/slot-{slot}-fill.png",
                    "zIndex": 20 + slot,
                },
                {
                    "id": f"slot-{slot}-border",
                    "role": "PETAL_BORDER",
                    "slotId": slot,
                    "asset": f"semantic/slot-{slot}-border.png",
                    "zIndex": 30 + slot,
                },
                {
                    "id": f"slot-{slot}-selected",
                    "role": "SELECTED_EMPHASIS",
                    "slotId": slot,
                    "asset": f"semantic/slot-{slot}-selected.png",
                    "maskId": f"slot-{slot}-selection-mask",
                    "zIndex": 40 + slot,
                },
            ]
        )
        groups.append(
            {
                "id": f"slot-{slot}-content",
                "role": "DYNAMIC_SLOT_CONTENT",
                "slotId": slot,
                "zIndex": 60 + slot,
                "anchor": {
                    "radius": 73,
                    "angleDegreesClockwiseFromTop": angle,
                },
                "glyph": _content_box(label=False),
                "label": _content_box(label=True),
            }
        )
    groups.append(
        {
            "id": "selected-content",
            "role": "DYNAMIC_SELECTED_CONTENT",
            "slotId": None,
            "zIndex": 70,
            "anchor": {"radius": 0, "angleDegreesClockwiseFromTop": 0},
            "glyph": _content_box(label=False),
            "label": _content_box(label=True),
        }
    )
    manifest: dict[str, object] = {
        "protocolVersion": 3,
        "packageRevision": 1,
        "id": f"test-universal-radial{slot_count}-v3",
        "name": f"Test Universal Radial {slot_count}",
        "surface": "radial-overlay",
        "renderStrategy": "universal-radial",
        "layoutProfile": profile,
        "compatibleLayouts": [profile],
        "slotCount": slot_count,
        "referenceCanvas": {
            "width": 8,
            "height": 8,
            "colorSpace": "sRGB",
            "alphaMode": "straight",
        },
        "referenceScale": {
            "logicalWidth": 280,
            "logicalHeight": 280,
            "fit": "contain",
            "contentOrigin": {"x": 0, "y": 0},
        },
        "placement": {"activationAnchor": {"x": 140, "y": 140}},
        "radialGeometry": {
            "canonicalUnitWidth": 280,
            "center": {"x": 140, "y": 140},
            "neutralHubRadius": 35,
            "neutralInnerRadius": 42,
            "neutralOuterRadius": 103,
            "neutralGapDegrees": 4,
            "slotCenters": [
                {
                    "slotId": slot,
                    "angleDegreesClockwiseFromTop": (slot - 1) * pitch,
                }
                for slot in range(1, slot_count + 1)
            ],
            "outerWarpEnvelope": {
                "authoredRadius": 140,
                "targetRule": "scale-padding-by-petal-thickness",
                "overflowPolicy": "expand-transparent",
            },
        },
        "identityStates": states,
        "semanticLayers": semantic_layers,
        "masks": masks,
        "dynamicGroups": groups,
        "fallback": {
            "onInvalidCandidate": "retain-active",
            "onUnsupportedVersion": "retain-active",
            "startupThemeId": "radial-v5",
        },
        "capabilities": {
            "required": [
                "universalRadial",
                "semanticLayers",
                "dynamicAnchors",
                "instantTransitions",
            ],
            "optional": [],
        },
        "transitions": {"mode": "instant"},
        "assetHashes": {},
    }
    referenced = {
        state["asset"] for state in states.values() if isinstance(state, dict)
    }
    referenced.update(
        layer["asset"] for layer in semantic_layers
    )
    referenced.update(mask["asset"] for mask in masks)
    manifest["assetHashes"] = {path: "0" * 64 for path in sorted(referenced)}
    return manifest


def _write_package(root: Path, manifest: dict[str, object]) -> Path:
    hashes = manifest["assetHashes"]
    assert isinstance(hashes, dict)
    for index, relative_path in enumerate(sorted(hashes)):
        asset = root / relative_path
        asset.parent.mkdir(parents=True, exist_ok=True)
        Image.new("RGBA", (8, 8), (index % 255, 20, 30, 255)).save(asset, format="PNG")
        hashes[relative_path] = hashlib.sha256(asset.read_bytes()).hexdigest()
    manifest_path = root / "manifest.json"
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    return manifest_path


def _remove_hash(manifest: dict[str, object], relative_path: str) -> None:
    hashes = manifest["assetHashes"]
    assert isinstance(hashes, dict)
    hashes.pop(relative_path)


class ThemeV3ValidatorTests(unittest.TestCase):
    def assert_invalid(self, manifest: dict[str, object]) -> None:
        with self.assertRaises(ThemeV3ValidationError):
            validate_theme_v3_document(manifest)

    def test_formal_schema_is_closed_draft_2020_12(self) -> None:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))

        self.assertEqual("https://json-schema.org/draft/2020-12/schema", schema["$schema"])
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(3, schema["properties"]["protocolVersion"]["const"])
        self.assertEqual(
            "universal-radial",
            schema["properties"]["renderStrategy"]["const"],
        )
        self.assertEqual(
            REQUIRED_CAPABILITIES,
            set(schema["$defs"]["capability"]["enum"]),
        )
        envelope = schema["$defs"]["radialGeometry"]["properties"]["outerWarpEnvelope"]
        authored_radius = envelope["properties"]["authoredRadius"]
        self.assertEqual(103, authored_radius["exclusiveMinimum"])
        self.assertEqual(140, authored_radius["maximum"])
        self.assertEqual(
            "scale-padding-by-petal-thickness",
            envelope["properties"]["targetRule"]["const"],
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

    def test_nonproduction_example_is_manifest_valid(self) -> None:
        example = json.loads(EXAMPLE_PATH.read_text(encoding="utf-8"))

        report = validate_theme_v3_document(example)

        self.assertEqual("example-universal-radial8-v3", report.theme_id)
        self.assertTrue(example["exampleOnly"])
        self.assertEqual(8, report.slot_count)
        self.assertIn("other valid themes may declare a smaller R0", example["description"])

    def test_valid_radial_6_package_with_real_assets(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            manifest_path = _write_package(Path(directory), _manifest(6))

            report = validate_theme_v3(manifest_path, check_assets=True)

        self.assertEqual("radial-6", report.layout_profile)
        self.assertEqual(6, report.slot_count)
        self.assertTrue(report.assets_verified)

    def test_valid_radial_8_package_with_real_assets(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            manifest_path = _write_package(Path(directory), _manifest(8))

            report = validate_theme_v3(manifest_path, check_assets=True)

        self.assertEqual("radial-8", report.layout_profile)
        self.assertEqual(8, report.slot_count)
        self.assertTrue(report.assets_verified)

    def test_missing_global_semantic_layer_is_rejected(self) -> None:
        manifest = _manifest(6)
        layers = manifest["semanticLayers"]
        assert isinstance(layers, list)
        removed = layers.pop(0)
        assert isinstance(removed, dict)
        _remove_hash(manifest, str(removed["asset"]))

        self.assert_invalid(manifest)

    def test_missing_slot_layer_is_rejected(self) -> None:
        manifest = _manifest(6)
        layers = manifest["semanticLayers"]
        assert isinstance(layers, list)
        removed = next(
            layer
            for layer in layers
            if layer["role"] == "PETAL_BORDER" and layer["slotId"] == 3
        )
        layers.remove(removed)
        _remove_hash(manifest, str(removed["asset"]))

        self.assert_invalid(manifest)

    def test_invalid_h_i_o_order_is_rejected(self) -> None:
        manifest = _manifest(6)
        geometry = manifest["radialGeometry"]
        assert isinstance(geometry, dict)
        geometry["neutralHubRadius"] = 50

        self.assert_invalid(manifest)

    def test_gap_greater_than_or_equal_to_pitch_is_rejected(self) -> None:
        manifest = _manifest(6)
        geometry = manifest["radialGeometry"]
        assert isinstance(geometry, dict)
        geometry["neutralGapDegrees"] = 60

        self.assert_invalid(manifest)

    def test_theme_authored_outer_envelope_below_140_is_valid(self) -> None:
        manifest = _manifest(6)
        geometry = manifest["radialGeometry"]
        assert isinstance(geometry, dict)
        envelope = geometry["outerWarpEnvelope"]
        assert isinstance(envelope, dict)
        envelope["authoredRadius"] = 132

        report = validate_theme_v3_document(manifest)

        self.assertEqual(6, report.slot_count)

    def test_outer_envelope_equal_to_authored_outer_radius_is_rejected(self) -> None:
        manifest = _manifest(6)
        geometry = manifest["radialGeometry"]
        assert isinstance(geometry, dict)
        envelope = geometry["outerWarpEnvelope"]
        assert isinstance(envelope, dict)
        envelope["authoredRadius"] = geometry["neutralOuterRadius"]

        self.assert_invalid(manifest)

    def test_outer_envelope_below_authored_outer_radius_is_rejected(self) -> None:
        manifest = _manifest(6)
        geometry = manifest["radialGeometry"]
        assert isinstance(geometry, dict)
        envelope = geometry["outerWarpEnvelope"]
        assert isinstance(envelope, dict)
        envelope["authoredRadius"] = 102

        self.assert_invalid(manifest)

    def test_outer_envelope_above_canonical_nominal_radius_is_rejected(self) -> None:
        manifest = _manifest(6)
        geometry = manifest["radialGeometry"]
        assert isinstance(geometry, dict)
        envelope = geometry["outerWarpEnvelope"]
        assert isinstance(envelope, dict)
        envelope["authoredRadius"] = 140.0001

        self.assert_invalid(manifest)

    def test_approved_target_outer_envelope_formula_is_deterministic(self) -> None:
        result = target_outer_envelope_radius(42, 103, 140, 50, 120)

        self.assertAlmostEqual(120 + 37 * 70 / 61, result, places=12)

    def test_target_outer_envelope_does_not_regress_to_translation_formula(self) -> None:
        result = target_outer_envelope_radius(42, 103, 140, 50, 120)
        old_incorrect_result = 140 + (120 - 103)

        self.assertNotAlmostEqual(old_incorrect_result, result, places=12)

    def test_slot_count_mismatch_is_rejected(self) -> None:
        manifest = _manifest(6)
        manifest["slotCount"] = 8

        self.assert_invalid(manifest)

    def test_unsafe_path_is_rejected(self) -> None:
        manifest = _manifest(6)
        layers = manifest["semanticLayers"]
        hashes = manifest["assetHashes"]
        assert isinstance(layers, list)
        assert isinstance(hashes, dict)
        old = layers[0]["asset"]
        layers[0]["asset"] = "../outside.png"
        hashes["../outside.png"] = hashes.pop(old)

        self.assert_invalid(manifest)

    def test_reserved_windows_asset_path_is_rejected(self) -> None:
        manifest = _manifest(6)
        layers = manifest["semanticLayers"]
        hashes = manifest["assetHashes"]
        assert isinstance(layers, list)
        assert isinstance(hashes, dict)
        old = layers[0]["asset"]
        layers[0]["asset"] = "semantic/CON.png"
        hashes["semantic/CON.png"] = hashes.pop(old)

        self.assert_invalid(manifest)

    def test_missing_asset_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest_path = _write_package(root, _manifest(6))
            (root / "states" / "idle.png").unlink()

            with self.assertRaisesRegex(ThemeV3ValidationError, "missing asset"):
                validate_theme_v3(manifest_path, check_assets=True)

    def test_sha_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = _manifest(6)
            manifest_path = _write_package(root, manifest)
            hashes = manifest["assetHashes"]
            assert isinstance(hashes, dict)
            hashes["states/idle.png"] = "F" * 64
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

            with self.assertRaisesRegex(ThemeV3ValidationError, "hash mismatch"):
                validate_theme_v3(manifest_path, check_assets=True)

    def test_missing_identity_state_is_rejected(self) -> None:
        manifest = _manifest(6)
        states = manifest["identityStates"]
        assert isinstance(states, dict)
        removed = states.pop("selected-2")
        assert isinstance(removed, dict)
        _remove_hash(manifest, str(removed["asset"]))

        self.assert_invalid(manifest)

    def test_selected_state_count_mismatch_is_rejected(self) -> None:
        manifest = _manifest(6)
        states = manifest["identityStates"]
        hashes = manifest["assetHashes"]
        assert isinstance(states, dict)
        assert isinstance(hashes, dict)
        states["selected-7"] = {"slotId": 7, "asset": "states/selected-7.png"}
        hashes["states/selected-7.png"] = "7" * 64

        self.assert_invalid(manifest)

    def test_mask_dimension_metadata_violation_is_rejected(self) -> None:
        manifest = _manifest(6)
        masks = manifest["masks"]
        assert isinstance(masks, list)
        masks[0]["width"] = 7

        self.assert_invalid(manifest)

    def test_decoded_mask_dimension_violation_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = _manifest(6)
            manifest_path = _write_package(root, manifest)
            relative_path = "masks/slot-1-selection.png"
            mask_path = root / relative_path
            Image.new("RGBA", (7, 8), (255, 255, 255, 255)).save(mask_path, format="PNG")
            hashes = manifest["assetHashes"]
            assert isinstance(hashes, dict)
            hashes[relative_path] = hashlib.sha256(mask_path.read_bytes()).hexdigest()
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")

            with self.assertRaisesRegex(ThemeV3ValidationError, "mask dimension"):
                validate_theme_v3(manifest_path, check_assets=True)

    def test_unknown_capability_is_rejected(self) -> None:
        manifest = _manifest(6)
        capabilities = manifest["capabilities"]
        assert isinstance(capabilities, dict)
        required = capabilities["required"]
        assert isinstance(required, list)
        required.append("visualRegions")

        self.assert_invalid(manifest)

    def test_invalid_fallback_is_rejected(self) -> None:
        manifest = _manifest(6)
        fallback = manifest["fallback"]
        assert isinstance(fallback, dict)
        fallback["onInvalidCandidate"] = "replace-active"

        self.assert_invalid(manifest)

    def test_extra_property_is_rejected(self) -> None:
        manifest = _manifest(6)
        manifest["runtimeMagic"] = True

        self.assert_invalid(manifest)

    def test_duplicate_semantic_ownership_is_rejected(self) -> None:
        manifest = _manifest(6)
        layers = manifest["semanticLayers"]
        assert isinstance(layers, list)
        duplicate = copy.deepcopy(layers[2])
        duplicate["id"] = "slot-1-fill-duplicate"
        duplicate["zIndex"] = 100
        layers.append(duplicate)

        self.assert_invalid(manifest)

    def test_orphan_hash_is_rejected(self) -> None:
        manifest = _manifest(6)
        hashes = manifest["assetHashes"]
        assert isinstance(hashes, dict)
        hashes["unused.png"] = "A" * 64

        self.assert_invalid(manifest)

    def test_missing_hash_is_rejected(self) -> None:
        manifest = _manifest(6)
        _remove_hash(manifest, "states/idle.png")

        self.assert_invalid(manifest)

    def test_selected_emphasis_without_mask_is_rejected(self) -> None:
        manifest = _manifest(6)
        layers = manifest["semanticLayers"]
        assert isinstance(layers, list)
        selected = next(layer for layer in layers if layer["role"] == "SELECTED_EMPHASIS")
        selected.pop("maskId")

        self.assert_invalid(manifest)

    def test_dynamic_group_coverage_is_rejected(self) -> None:
        manifest = _manifest(6)
        groups = manifest["dynamicGroups"]
        assert isinstance(groups, list)
        groups.pop(0)

        self.assert_invalid(manifest)

    def test_protocol_freezes_runtime_and_compiler_boundaries(self) -> None:
        protocol = PROTOCOL_PATH.read_text(encoding="utf-8")
        normalized_protocol = " ".join(protocol.split())

        for phrase in (
            "Every authored visible pixel MUST have one definite semantic owner",
            "Runtime MUST NOT infer ownership from RGB",
            "semantic reconstruction at the complete neutral vector",
            "raw decoded RGBA pixel equality",
            "V1 is DEPRECATED FOR NEW AUTHORING",
            "settingsSemanticRevision",
            "Rt = 120 + 37 * 70 / 61",
            "ReceiverUiScalePercent is not a Universal radial visual parameter",
            "STATIC_RESIDUAL is not petal support",
        ):
            self.assertIn(phrase, normalized_protocol)


class UniversalSettingsMigrationContractTests(unittest.TestCase):
    def test_settings_semantic_revisions_are_distinct_and_ordered(self) -> None:
        self.assertEqual(1, LEGACY_SETTINGS_SEMANTIC_REVISION)
        self.assertEqual(2, UNIVERSAL_SETTINGS_SEMANTIC_REVISION)

    def test_base_scale_examples_use_exact_ratios(self) -> None:
        self.assertEqual(Fraction(1, 1), legacy_surface_scale_ratio(280, 100))
        self.assertEqual(Fraction(6, 5), legacy_surface_scale_ratio(280, 120))
        self.assertEqual(Fraction(6, 5), legacy_surface_scale_ratio(336, 100))
        self.assertEqual(Fraction(10, 7), legacy_surface_scale_ratio(320, 125))

    def test_base_scale_double_is_not_integer_rounded(self) -> None:
        self.assertAlmostEqual(10 / 7, legacy_surface_scale(320, 125))
        self.assertNotEqual(1, legacy_surface_scale(320, 125))

    def test_legacy_fill_default_maps_to_full_strength(self) -> None:
        self.assertEqual(255, migrate_legacy_dead_alpha(218, 218))

    def test_legacy_border_default_maps_to_full_strength(self) -> None:
        self.assertEqual(255, migrate_legacy_dead_alpha(100, 100))

    def test_legacy_highlight_default_maps_to_full_strength(self) -> None:
        self.assertEqual(255, migrate_legacy_dead_alpha(80, 80))

    def test_legacy_dead_alpha_zero_and_above_default(self) -> None:
        self.assertEqual(0, migrate_legacy_dead_alpha(0, 218))
        self.assertEqual(255, migrate_legacy_dead_alpha(219, 218))

    def test_legacy_dead_alpha_midpoint_rounds_away_from_zero(self) -> None:
        self.assertEqual(128, migrate_legacy_dead_alpha(50, 100))

    def test_legacy_text_240_and_255_map_to_full_strength(self) -> None:
        self.assertEqual(255, migrate_legacy_text_alpha(240))
        self.assertEqual(255, migrate_legacy_text_alpha(255))

    def test_legacy_text_zero_remains_zero(self) -> None:
        self.assertEqual(0, migrate_legacy_text_alpha(0))

    def test_invalid_migration_inputs_are_rejected(self) -> None:
        with self.assertRaises(ValueError):
            legacy_surface_scale_ratio(0, 100)
        with self.assertRaises(ValueError):
            migrate_legacy_dead_alpha(256, 218)
        with self.assertRaises(ValueError):
            migrate_legacy_text_alpha(-1)


if __name__ == "__main__":
    unittest.main()
