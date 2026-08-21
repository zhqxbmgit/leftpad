from __future__ import annotations

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

from compare_visual_pack import ComparisonError, compare_visual_pack  # noqa: E402


PROFILE_ANGLES = {
    "radial-6": [0, 60, 120, 180, 240, 300],
    "radial-8": [0, 45, 90, 135, 180, 225, 270, 315],
}


def _write_asset(path: Path, selected: bool = False) -> None:
    image = Image.new("RGBA", (1254, 1254), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    if selected:
        draw.rounded_rectangle(
            (410, 174, 844, 451),
            radius=10,
            fill=(22, 46, 72, 210),
            outline=(103, 213, 255, 245),
            width=4,
        )
    else:
        draw.ellipse(
            (177, 177, 1077, 1077),
            fill=(12, 20, 30, 180),
            outline=(70, 90, 112, 220),
            width=3,
        )
    image.save(path, format="PNG")


def _create_valid_pack(directory: Path, profile: str = "radial-6") -> None:
    directory.mkdir(parents=True)
    angles = PROFILE_ANGLES[profile]
    manifest = {
        "id": "fixture-pack",
        "name": "Fixture Pack",
        "version": 1,
        "layoutProfile": profile,
        "slotCount": len(angles),
        "base": "base.png",
        "selected": "selected.png",
        "layout": "layout.json",
        "selectionAssetMode": "canonical-transform",
    }
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
    layout = {
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
    (directory / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    (directory / "layout.json").write_text(json.dumps(layout), encoding="utf-8")
    _write_asset(directory / "base.png")
    _write_asset(directory / "selected.png", selected=True)


def _write_reference(path: Path) -> None:
    image = Image.new("RGB", (900, 540), (25, 31, 48))
    draw = ImageDraw.Draw(image)
    draw.rectangle((90, 70, 810, 470), fill=(48, 72, 112), outline=(122, 92, 220), width=8)
    image.save(path, format="PNG")


class CompareVisualPackTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        self.pack = self.root / "pack"
        self.reference = self.root / "reference.png"
        _create_valid_pack(self.pack)
        _write_reference(self.reference)

    def test_valid_pack_and_reference_pass(self) -> None:
        output = self.root / "review.png"

        result = compare_visual_pack(self.reference, self.pack, output)

        self.assertEqual("fixture-pack", result.pack_id)
        self.assertEqual("Fixture Pack", result.pack_name)
        self.assertEqual(output, result.output_path)

    def test_missing_reference_fails_without_output(self) -> None:
        output = self.root / "review.png"

        with self.assertRaisesRegex(ComparisonError, "reference image missing"):
            compare_visual_pack(self.root / "missing.png", self.pack, output)

        self.assertFalse(output.exists())

    def test_invalid_pack_fails_without_output(self) -> None:
        output = self.root / "review.png"
        (self.pack / "manifest.json").unlink()

        with self.assertRaisesRegex(ComparisonError, "Visual Pack invalid"):
            compare_visual_pack(self.reference, self.pack, output)

        self.assertFalse(output.exists())

    def test_missing_selected_asset_fails_without_output(self) -> None:
        output = self.root / "review.png"
        (self.pack / "selected.png").unlink()

        with self.assertRaisesRegex(ComparisonError, "selected asset missing"):
            compare_visual_pack(self.reference, self.pack, output)

        self.assertFalse(output.exists())

    def test_default_output_file_is_created(self) -> None:
        result = compare_visual_pack(self.reference, self.pack)

        self.assertEqual(self.pack / "comparison.png", result.output_path)
        self.assertTrue(result.output_path.is_file())

    def test_generated_image_is_readable_and_opaque(self) -> None:
        output = self.root / "review.png"
        result = compare_visual_pack(self.reference, self.pack, output)

        with Image.open(result.output_path) as image:
            image.load()
            self.assertEqual("PNG", image.format)
            self.assertEqual("RGB", image.mode)
            self.assertEqual(1600, image.width)
            self.assertGreater(image.height, 2200)
            self.assertEqual((8, 11, 16), image.getpixel((0, 0)))

    def test_radial_8_comparison_contains_eight_slot_review(self) -> None:
        radial_8_pack = self.root / "radial-8-pack"
        _create_valid_pack(radial_8_pack, "radial-8")
        output = self.root / "radial-8-review.png"

        result = compare_visual_pack(self.reference, radial_8_pack, output)

        self.assertEqual(8, result.slot_count)
        self.assertTrue(output.is_file())
        with Image.open(output) as image:
            image.load()
            self.assertEqual("RGB", image.mode)
            self.assertEqual(1600, image.width)


if __name__ == "__main__":
    unittest.main()
