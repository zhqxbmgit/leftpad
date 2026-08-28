from __future__ import annotations

import hashlib
import json
import math
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image, ImageDraw

TOOLS_DIRECTORY = Path(__file__).resolve().parents[1]
if str(TOOLS_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIRECTORY))

from validate_visual_pack import (  # noqa: E402
    VisualPackValidationError,
    format_valid_report,
    validate_visual_pack,
)


PROFILE_ANGLES = {
    "radial-6": [0, 60, 120, 180, 240, 300],
    "radial-8": [0, 45, 90, 135, 180, 225, 270, 315],
}


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def _write_rgba_png(path: Path) -> None:
    image = Image.new("RGBA", (1254, 1254), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(
        (410, 174, 844, 451),
        radius=10,
        fill=(18, 30, 44, 180),
        outline=(88, 204, 255, 235),
        width=3,
    )
    image.save(path, format="PNG")


def _manifest(
    profile: str = "radial-6",
    slot_count: int | None = None,
) -> dict[str, object]:
    expected_count = len(PROFILE_ANGLES[profile])
    return {
        "id": "fixture-pack",
        "name": "Fixture Pack",
        "version": 1,
        "layoutProfile": profile,
        "slotCount": expected_count if slot_count is None else slot_count,
        "base": "base.png",
        "selected": "selected.png",
        "layout": "layout.json",
        "selectionAssetMode": "canonical-transform",
    }


def _layout(profile: str = "radial-6") -> dict[str, object]:
    angles = PROFILE_ANGLES[profile]
    slots = []
    for index, angle in enumerate(angles, start=1):
        radians = math.radians(angle)
        glyph_anchor = (
            round(627 + 320 * math.sin(radians), 3),
            round(627 - 320 * math.cos(radians), 3),
        )
        label_anchor = (
            round(627 + 365 * math.sin(radians), 3),
            round(627 - 365 * math.cos(radians), 3),
        )
        slots.append(
            {
                "slot": index,
                "angleDegreesClockwiseFromTop": angle,
                "glyphAnchor": {"x": glyph_anchor[0], "y": glyph_anchor[1]},
                "labelAnchor": {"x": label_anchor[0], "y": label_anchor[1]},
            }
        )
    geometry = {
        "outerRadius": 450,
        "innerRadius": 198,
        "gapPx": 16,
        "cornerRadius": 10,
    }
    if profile == "radial-8":
        geometry.update({"centerRadius": 182, "centerInnerRadius": 154})
    return {
        "canvas": {"width": 1254, "height": 1254, "mode": "RGBA"},
        "geometry": geometry,
        "slotAnglesDegrees": angles,
        "slotCount": len(angles),
        "slots": slots,
        "wheelCenter": {"x": 627, "y": 627},
    }


def _create_pack(
    directory: Path,
    profile: str = "radial-6",
    manifest_slot_count: int | None = None,
) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "manifest.json").write_text(
        json.dumps(_manifest(profile, manifest_slot_count), indent=2), encoding="utf-8"
    )
    (directory / "layout.json").write_text(
        json.dumps(_layout(profile), indent=2), encoding="utf-8"
    )
    _write_rgba_png(directory / "base.png")
    _write_rgba_png(directory / "selected.png")


class VisualPackValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.pack = Path(self.temporary_directory.name) / "pack"

    def assert_invalid(self, expected: str) -> None:
        with self.assertRaisesRegex(VisualPackValidationError, expected):
            validate_visual_pack(self.pack)

    def test_valid_radial_6_pack_passes(self) -> None:
        _create_pack(self.pack)

        report = validate_visual_pack(self.pack)

        self.assertEqual("fixture-pack", report.pack_id)
        self.assertEqual("radial-6", report.layout_profile)
        self.assertEqual(6, report.slot_count)
        self.assertIn("VISUAL PACK VALID", format_valid_report(report))
        self.assertIn("verification.json not found", report.warnings[0])

    def test_valid_radial_8_pack_passes_as_runtime_integrated(self) -> None:
        _create_pack(self.pack, "radial-8")

        report = validate_visual_pack(self.pack)

        self.assertEqual("radial-8", report.layout_profile)
        self.assertEqual(8, report.slot_count)
        self.assertEqual("stable", report.profile_status)
        self.assertTrue(report.runtime_compatible)
        formatted = format_valid_report(report)
        self.assertIn("Runtime Compatible:\nYES", formatted)
        self.assertIn("Runtime Capability Gap:\nNONE", formatted)

    def test_radial_8_with_slot_count_6_fails(self) -> None:
        _create_pack(self.pack, "radial-8", manifest_slot_count=6)

        self.assert_invalid("radial-8 requires slotCount 8: found 6")

    def test_radial_8_missing_anchor_fails(self) -> None:
        _create_pack(self.pack, "radial-8")
        layout = _layout("radial-8")
        del layout["slots"][4]["glyphAnchor"]  # type: ignore[index]
        (self.pack / "layout.json").write_text(json.dumps(layout), encoding="utf-8")

        self.assert_invalid(r"layout\.slots\[4\]\.glyphAnchor must be an object")

    def test_radial_8_invalid_slot_ordering_fails(self) -> None:
        _create_pack(self.pack, "radial-8")
        layout = _layout("radial-8")
        layout["slots"][2]["slot"] = 4  # type: ignore[index]
        (self.pack / "layout.json").write_text(json.dumps(layout), encoding="utf-8")

        self.assert_invalid("invalid transform mapping for slot 3")

    def test_missing_manifest_fails(self) -> None:
        self.pack.mkdir()

        self.assert_invalid("manifest missing")

    def test_missing_selected_asset_fails(self) -> None:
        _create_pack(self.pack)
        (self.pack / "selected.png").unlink()

        self.assert_invalid("selected asset missing")

    def test_unsupported_layout_profile_fails(self) -> None:
        _create_pack(self.pack)
        manifest = _manifest()
        manifest["layoutProfile"] = "grid-3x2"
        (self.pack / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

        self.assert_invalid("unsupported layoutProfile: grid-3x2")

    def test_wrong_slot_count_fails(self) -> None:
        _create_pack(self.pack)
        manifest = _manifest()
        manifest["slotCount"] = 8
        (self.pack / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

        self.assert_invalid("radial-6 requires slotCount 6: found 8")

    def test_duplicate_json_field_fails(self) -> None:
        _create_pack(self.pack)
        manifest_text = (self.pack / "manifest.json").read_text(encoding="utf-8")
        duplicate = manifest_text.replace(
            '"id": "fixture-pack",',
            '"id": "fixture-pack",\n  "id": "duplicate",',
        )
        (self.pack / "manifest.json").write_text(duplicate, encoding="utf-8")

        self.assert_invalid("duplicate JSON field: id")

    def test_invalid_manifest_field_type_fails(self) -> None:
        _create_pack(self.pack)
        manifest = _manifest()
        manifest["version"] = "1"
        (self.pack / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")

        self.assert_invalid("manifest.version must be an integer")

    def test_invalid_png_fails(self) -> None:
        _create_pack(self.pack)
        (self.pack / "selected.png").write_bytes(b"not a png")

        self.assert_invalid("invalid selected PNG")

    def test_alpha_zero_rgb_residue_fails(self) -> None:
        _create_pack(self.pack)
        with Image.open(self.pack / "base.png") as source:
            image = source.copy()
        image.putpixel((10, 10), (12, 34, 56, 0))
        image.save(self.pack / "base.png", format="PNG")

        self.assert_invalid("base asset has RGB residue in alpha=0 pixels")

    def test_baked_black_background_fails(self) -> None:
        _create_pack(self.pack)
        Image.new("RGBA", (1254, 1254), (0, 0, 0, 255)).save(
            self.pack / "base.png", format="PNG"
        )

        self.assert_invalid("no transparent background")

    def test_matching_verification_hashes_pass(self) -> None:
        _create_pack(self.pack)
        verification = {
            "assets": {
                filename: {"file": filename, "sha256": _sha256(self.pack / filename)}
                for filename in ("base.png", "selected.png", "layout.json")
            }
        }
        (self.pack / "verification.json").write_text(
            json.dumps(verification), encoding="utf-8"
        )

        report = validate_visual_pack(self.pack)

        self.assertFalse(report.warnings)
        self.assertTrue(all(asset.hash_verified for asset in report.assets))

    def test_hash_mismatch_fails(self) -> None:
        _create_pack(self.pack)
        verification = {
            "assets": {
                "base.png": {"file": "base.png", "sha256": "0" * 64}
            }
        }
        (self.pack / "verification.json").write_text(
            json.dumps(verification), encoding="utf-8"
        )

        self.assert_invalid("SHA-256 mismatch for base.png")

    def test_current_radial_v5_passes(self) -> None:
        radial_v5 = (
            TOOLS_DIRECTORY.parent
            / "PcDs4Server"
            / "Assets"
            / "UIVisualPacks"
            / "radial-v5"
        )

        report = validate_visual_pack(radial_v5)

        self.assertEqual("radial-v5", report.pack_id)
        self.assertEqual("Tactical HUD V5", report.name)
        self.assertEqual("canonical-transform", report.selection_mode)


if __name__ == "__main__":
    unittest.main()
