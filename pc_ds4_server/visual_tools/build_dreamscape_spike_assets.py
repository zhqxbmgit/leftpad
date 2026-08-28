"""Build deterministic Dreamscape Overview Spike assets from the measured target.

The reference is copied byte-for-byte for the development overlay.  static-art.png
keeps the original architecture while replacing every dynamic text/value region
with a feathered interpolation of its surrounding pixels.  Runtime text is then
rendered by HTML/CSS and populated from the C# state bridge.
"""

from __future__ import annotations

import argparse
import hashlib
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


EXPECTED_SIZE = (1672, 941)
EXPECTED_SHA256 = "4E7D2A19E2FBFEC7E11D7D9E2B396F5EF36644ED2EFF56695261AAAA10265938"

# Inclusive-exclusive rectangles measured in the reference coordinate system.
DYNAMIC_RECTS = [
    # Brand and navigation text.
    (58, 172, 239, 221),
    (57, 220, 234, 263),
    (103, 302, 202, 352),
    (103, 394, 245, 440),
    (103, 474, 205, 519),
    (103, 553, 191, 601),
    # Page title and native window glyphs.
    (347, 65, 677, 151),
    (1538, 20, 1662, 70),
    # Status card titles, values, and runtime dots.
    (416, 278, 562, 382),
    (566, 284, 598, 319),
    (715, 278, 915, 382),
    (876, 284, 909, 319),
    (1022, 278, 1217, 382),
    (1181, 284, 1215, 319),
    (1344, 278, 1542, 389),
    (1501, 284, 1538, 319),
    # Output title, selected value, and button contents.
    (379, 554, 533, 608),
    (397, 617, 602, 668),
    (684, 619, 798, 673),
    # Left mapping labels and selected values.
    (923, 563, 972, 611),
    (997, 562, 1126, 610),
    (923, 618, 972, 666),
    (997, 617, 1126, 665),
    (923, 673, 972, 721),
    (997, 672, 1126, 720),
    (923, 728, 972, 776),
    (997, 727, 1126, 775),
    (923, 783, 972, 831),
    (997, 782, 1126, 830),
    # Right mapping labels and selected values.
    (1273, 563, 1332, 611),
    (1350, 562, 1478, 610),
    (1273, 618, 1332, 666),
    (1350, 617, 1478, 665),
    (1273, 673, 1332, 721),
    (1350, 672, 1478, 720),
    (1273, 728, 1332, 776),
    (1350, 727, 1478, 775),
    (1273, 783, 1332, 831),
    (1350, 782, 1478, 830),
]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def interpolated_patch(array: np.ndarray, rect: tuple[int, int, int, int]) -> np.ndarray:
    x0, y0, x1, y1 = rect
    height = y1 - y0
    width = x1 - x0
    left = array[y0:y1, max(0, x0 - 4):max(1, x0 - 1)].mean(axis=1)
    right = array[y0:y1, min(array.shape[1] - 1, x1 + 1):min(array.shape[1], x1 + 4)].mean(axis=1)
    top = array[max(0, y0 - 4):max(1, y0 - 1), x0:x1].mean(axis=0)
    bottom = array[min(array.shape[0] - 1, y1 + 1):min(array.shape[0], y1 + 4), x0:x1].mean(axis=0)

    horizontal_weight = np.linspace(0.0, 1.0, width, dtype=np.float32)[None, :, None]
    vertical_weight = np.linspace(0.0, 1.0, height, dtype=np.float32)[:, None, None]
    horizontal = left[:, None, :] * (1.0 - horizontal_weight) + right[:, None, :] * horizontal_weight
    vertical = top[None, :, :] * (1.0 - vertical_weight) + bottom[None, :, :] * vertical_weight
    return (horizontal * 0.52 + vertical * 0.48).clip(0, 255)


def build_static_art(reference: Image.Image) -> tuple[Image.Image, Image.Image]:
    source = np.asarray(reference.convert("RGB"), dtype=np.float32)
    reconstructed = source.copy()
    hard_mask = Image.new("L", reference.size, 0)

    from PIL import ImageDraw

    mask_draw = ImageDraw.Draw(hard_mask)
    for rect in DYNAMIC_RECTS:
        x0, y0, x1, y1 = rect
        reconstructed[y0:y1, x0:x1] = interpolated_patch(source, rect)
        mask_draw.rectangle((x0, y0, x1 - 1, y1 - 1), fill=255)

    reconstructed_image = Image.fromarray(reconstructed.astype(np.uint8), "RGB")
    feather = hard_mask.filter(ImageFilter.GaussianBlur(radius=2.0))
    static_art = Image.composite(reconstructed_image, reference.convert("RGB"), feather)
    return static_art, hard_mask


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    if not args.reference.is_file():
        raise SystemExit(f"Reference does not exist: {args.reference}")
    actual_sha = sha256(args.reference)
    if actual_sha != EXPECTED_SHA256:
        raise SystemExit(f"Reference SHA mismatch: {actual_sha}")

    with Image.open(args.reference) as opened:
        reference = opened.convert("RGB")
    if reference.size != EXPECTED_SIZE:
        raise SystemExit(f"Reference size mismatch: {reference.size}")

    args.output.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(args.reference, args.output / "reference-overview.png")
    static_art, dynamic_mask = build_static_art(reference)
    static_art.save(args.output / "static-art.png", optimize=True)
    dynamic_mask.save(args.output / "dynamic-mask.png", optimize=True)
    print(f"reference={args.reference}")
    print(f"dimensions={reference.width}x{reference.height}")
    print(f"sha256={actual_sha}")
    print(f"masked_regions={len(DYNAMIC_RECTS)}")
    print(f"output={args.output}")


if __name__ == "__main__":
    main()
