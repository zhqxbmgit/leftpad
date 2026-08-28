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

from generate_visual_pack import GeneratorError, generate_visual_pack  # noqa: E402
from validate_visual_pack import validate_visual_pack  # noqa: E402


PROFILE_ANGLES = {
    "radial-6": [0, 60, 120, 180, 240, 300],
    "radial-8": [0, 45, 90, 135, 180, 225, 270, 315],
}


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def _write_asset(path: Path, selected: bool = False) -> None:
    image = Image.new("RGBA", (1254, 1254), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    if selected:
        draw.rounded_rectangle(
            (410, 174, 844, 451),
            radius=10,
            fill=(18, 52, 82, 210),
            outline=(91, 209, 255, 245),
            width=4,
        )
    else:
        draw.ellipse(
            (177, 177, 1077, 1077),
            fill=(13, 21, 32, 185),
            outline=(76, 92, 112, 225),
            width=3,
        )
    image.save(path, format="PNG")


def _layout(profile: str = "radial-6") -> dict[str, object]:
    angles = PROFILE_ANGLES[profile]
    anchors = [
        (
            round(627 + 320 * math.sin(math.radians(angle)), 3),
            round(627 - 320 * math.cos(math.radians(angle)), 3),
        )
        for angle in angles
    ]
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
        "slots": [
            {
                "slot": index,
                "angleDegreesClockwiseFromTop": angle,
                "glyphAnchor": {"x": anchor[0], "y": anchor[1]},
                "labelAnchor": {"x": anchor[0], "y": anchor[1]},
            }
            for index, (angle, anchor) in enumerate(zip(angles, anchors), start=1)
        ],
        "wheelCenter": {"x": 627, "y": 627},
    }


def _config(profile: str = "radial-6") -> dict[str, object]:
    slot_count = len(PROFILE_ANGLES[profile])
    return {
        "id": "generated-fixture",
        "name": "Generated Fixture",
        "version": 1,
        "layoutProfile": profile,
        "slotCount": slot_count,
        "selectionAssetMode": "canonical-transform",
        "source": {"reference": "reference.png"},
        "assets": {
            "base": "radial-base.png",
            "selected": "radial-selected-card.png",
            "layout": "radial-layout.json",
        },
    }


def _create_workspace(
    root: Path,
    include_reference: bool = True,
    profile: str = "radial-6",
) -> Path:
    assets = root / "assets"
    assets.mkdir(parents=True)
    _write_asset(assets / "radial-base.png")
    _write_asset(assets / "radial-selected-card.png", selected=True)
    (assets / "radial-layout.json").write_text(
        json.dumps(_layout(profile)), encoding="utf-8"
    )
    if include_reference:
        reference = Image.new("RGB", (840, 520), (24, 31, 48))
        ImageDraw.Draw(reference).rectangle(
            (80, 60, 760, 460), fill=(51, 76, 118), outline=(122, 95, 222), width=8
        )
        reference.save(root / "reference.png", format="PNG")
    config_path = root / "theme-config.json"
    config_path.write_text(json.dumps(_config(profile)), encoding="utf-8")
    return config_path


class GenerateVisualPackTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.workspace = Path(self.temporary_directory.name) / "workspace"
        self.config_path = _create_workspace(self.workspace)
        self.output = self.workspace / "output"

    def test_valid_config_generates_standard_output_and_identical_assets(self) -> None:
        result = generate_visual_pack(self.config_path)

        self.assertEqual(self.output, result.output_directory)
        self.assertTrue(result.validated)
        for filename in (
            "manifest.json",
            "radial-base.png",
            "radial-selected-card.png",
            "radial-layout.json",
            "verification.json",
            "comparison.png",
        ):
            self.assertTrue((self.output / filename).is_file(), filename)
        for filename in ("radial-base.png", "radial-selected-card.png", "radial-layout.json"):
            self.assertEqual(
                _sha256(self.workspace / "assets" / filename),
                _sha256(self.output / filename),
            )

    def test_invalid_config_fails_without_output(self) -> None:
        config = _config()
        config["layoutProfile"] = "grid-3x2"
        self.config_path.write_text(json.dumps(config), encoding="utf-8")

        with self.assertRaisesRegex(GeneratorError, "unsupported layoutProfile"):
            generate_visual_pack(self.config_path)

        self.assertFalse(self.output.exists())

    def test_missing_asset_fails_without_output(self) -> None:
        (self.workspace / "assets" / "radial-selected-card.png").unlink()

        with self.assertRaisesRegex(GeneratorError, "missing selected asset"):
            generate_visual_pack(self.config_path)

        self.assertFalse(self.output.exists())

    def test_existing_output_without_force_fails(self) -> None:
        self.output.mkdir()
        sentinel = self.output / "keep.txt"
        sentinel.write_text("keep", encoding="utf-8")

        with self.assertRaisesRegex(GeneratorError, "output already exists; use --force"):
            generate_visual_pack(self.config_path)

        self.assertEqual("keep", sentinel.read_text(encoding="utf-8"))

    def test_force_replaces_existing_output(self) -> None:
        self.output.mkdir()
        sentinel = self.output / "old.txt"
        sentinel.write_text("old", encoding="utf-8")

        result = generate_visual_pack(self.config_path, force=True)

        self.assertEqual(self.output, result.output_directory)
        self.assertFalse(sentinel.exists())
        self.assertTrue((self.output / "manifest.json").is_file())

    def test_generated_pack_passes_validator(self) -> None:
        generate_visual_pack(self.config_path)

        report = validate_visual_pack(self.output)

        self.assertEqual("generated-fixture", report.pack_id)
        self.assertFalse(report.warnings)

    def test_comparison_generated_when_reference_exists(self) -> None:
        result = generate_visual_pack(self.config_path)

        self.assertTrue(result.comparison_generated)
        with Image.open(self.output / "comparison.png") as image:
            image.load()
            self.assertEqual("PNG", image.format)
            self.assertEqual("RGB", image.mode)

    def test_missing_reference_warns_without_failing(self) -> None:
        (self.workspace / "reference.png").unlink()

        result = generate_visual_pack(self.config_path)

        self.assertTrue(result.validated)
        self.assertFalse(result.comparison_generated)
        self.assertFalse((self.output / "comparison.png").exists())
        self.assertIn("reference image not found", result.warnings[0])

    def test_generator_packages_runtime_integrated_radial_8(self) -> None:
        radial_8_workspace = Path(self.temporary_directory.name) / "radial-8-workspace"
        radial_8_config = _create_workspace(radial_8_workspace, profile="radial-8")

        result = generate_visual_pack(radial_8_config)
        report = validate_visual_pack(result.output_directory)

        self.assertTrue(result.validated)
        self.assertTrue(result.comparison_generated)
        self.assertEqual("radial-8", report.layout_profile)
        self.assertEqual(8, report.slot_count)
        self.assertTrue(report.runtime_compatible)


if __name__ == "__main__":
    unittest.main()
