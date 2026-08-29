from __future__ import annotations

import hashlib
import importlib.util
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

import numpy as np
from PIL import Image


MODULE_PATH = Path(__file__).resolve().parents[1] / "build_selected_emphasis_companion.py"
SPEC = importlib.util.spec_from_file_location("build_selected_emphasis_companion", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
compiler = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = compiler
SPEC.loader.exec_module(compiler)


class SelectedEmphasisCompilerTests(unittest.TestCase):
    @staticmethod
    def sample() -> tuple[np.ndarray, np.ndarray]:
        base = np.array(
            [
                [[0, 0, 0, 0], [10, 20, 30, 255]],
                [[100, 80, 60, 128], [4, 5, 6, 255]],
            ],
            dtype=np.uint8,
        )
        selected = base.copy()
        selected[0, 1] = [200, 100, 50, 255]
        selected[1, 0] = [20, 40, 80, 192]
        return base, selected

    def test_difference_mask_is_exact_binary_pargb_support(self) -> None:
        base, selected = self.sample()
        mask = compiler.difference_mask(base, selected)
        self.assertEqual({0, 255}, set(np.unique(mask).tolist()))
        np.testing.assert_array_equal(mask, np.array([[0, 255], [255, 0]], dtype=np.uint8))

    def test_interpolation_has_raw_exact_endpoints_and_integer_rounding(self) -> None:
        base, selected = self.sample()
        mask = compiler.difference_mask(base, selected)
        zero = compiler.interpolate_pargb(base, selected, mask, 0)
        full = compiler.interpolate_pargb(base, selected, mask, 255)
        half = compiler.interpolate_pargb(base, selected, mask, 128)
        np.testing.assert_array_equal(zero, compiler.to_pargb(base))
        np.testing.assert_array_equal(full, compiler.to_pargb(selected))
        expected = (
            compiler.to_pargb(base).astype(np.uint32) * 127
            + compiler.to_pargb(selected).astype(np.uint32) * 128
            + 127
        ) // 255
        np.testing.assert_array_equal(half[mask == 255], expected.astype(np.uint8)[mask == 255])
        np.testing.assert_array_equal(half[mask == 0], compiler.to_pargb(base)[mask == 0])

    def test_interpolation_preserves_pargb_invariant(self) -> None:
        base, selected = self.sample()
        mask = compiler.difference_mask(base, selected)
        for strength in (0, 1, 64, 128, 192, 254, 255):
            result = compiler.interpolate_pargb(base, selected, mask, strength)
            self.assertTrue(np.all(result[..., :3] <= result[..., 3:4]))
            self.assertTrue(np.all(result[result[..., 3] == 0, :3] == 0))

    def test_component_summary_uses_eight_connected_runs(self) -> None:
        mask = np.zeros((6, 8), dtype=np.uint8)
        mask[0:2, 0:2] = 255
        mask[2, 2] = 255  # diagonal: same 8-connected component
        mask[4:6, 6:8] = 255
        summary = compiler.connected_component_summary(mask)
        self.assertEqual(8, summary["connectivity"])
        self.assertEqual(2, summary["count"])
        self.assertEqual(5, summary["largest"][0]["pixelCount"])

    def test_png_and_json_encoding_are_byte_deterministic(self) -> None:
        image = Image.fromarray(np.array([[[1, 2, 3, 4], [5, 6, 7, 8]]], dtype=np.uint8), "RGBA")
        value = {"z": 1, "a": [3, 2, 1]}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first_png, second_png = root / "first.png", root / "second.png"
            first_json, second_json = root / "first.json", root / "second.json"
            compiler.save_png(first_png, image)
            compiler.save_png(second_png, image)
            compiler.save_json(first_json, value)
            compiler.save_json(second_json, value)
            self.assertEqual(hashlib.sha256(first_png.read_bytes()).digest(), hashlib.sha256(second_png.read_bytes()).digest())
            self.assertEqual(first_json.read_bytes(), second_json.read_bytes())

    def test_pargbz_sidecar_is_deterministic_and_lossless(self) -> None:
        pixels = bytes((0, 0, 0, 0, 10, 20, 30, 255))
        raw = b"PARG" + (2).to_bytes(4, "little") + (1).to_bytes(4, "little") + pixels
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source.pargb"
            first = root / "first.pargbz"
            second = root / "second.pargbz"
            source.write_bytes(raw)
            compiler.save_pargbz(first, source)
            compiler.save_pargbz(second, source)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            encoded = first.read_bytes()
            self.assertEqual(b"PARGZ", encoded[:5])
            self.assertEqual(2, int.from_bytes(encoded[5:9], "little"))
            self.assertEqual(1, int.from_bytes(encoded[9:13], "little"))
            self.assertEqual(pixels, zlib.decompress(encoded[13:]))


if __name__ == "__main__":
    unittest.main()
