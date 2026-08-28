"""Build the reference-led Settings Basic production art and audit metadata."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


EXPECTED_SIZE = (1672, 941)
EXPECTED_SHA256 = "BE2ACF99E4DDF5CA2FD8544E3ED3271BDD397CF22728D3D65C0ADB6B8B8BDAB8"

# Measured inclusive-exclusive rectangles containing text or live controls.
DYNAMIC_RECTS = [
    (43, 150, 239, 204), (43, 205, 229, 248),
    (84, 294, 154, 337), (84, 360, 190, 403),
    (84, 431, 156, 476), (84, 504, 157, 549),
    (344, 62, 512, 142),
    # Keep the surrounding sky intact; only remove the reference's baked
    # minimize/close glyphs. The former 1500,0,1672,90 mask produced a
    # visible rectangular interpolation boundary in the WebView2 runtime.
    (1543, 27, 1583, 52), (1603, 19, 1643, 60),
    (1401, 102, 1559, 146),
    (378, 201, 454, 239), (528, 201, 590, 239), (663, 201, 770, 239),
    (411, 303, 572, 349), (411, 384, 600, 432),
    (411, 474, 622, 516), (411, 570, 626, 613), (411, 666, 585, 710),
    (1023, 296, 1208, 340), (1023, 397, 1210, 441),
    (1023, 497, 1220, 541), (1023, 594, 1220, 641),
    (612, 298, 909, 357), (612, 382, 909, 442),
    (408, 507, 901, 547), (408, 603, 901, 643), (408, 699, 901, 739),
    (1022, 333, 1469, 374), (1022, 433, 1469, 475),
    (1022, 533, 1469, 575), (1022, 637, 1469, 678),
    (839, 473, 900, 515), (839, 570, 900, 611), (839, 666, 900, 710),
    (1406, 296, 1464, 340), (1406, 397, 1464, 441),
    (1406, 497, 1464, 541), (1406, 594, 1464, 641),
    (379, 797, 529, 852), (618, 797, 777, 852),
    (882, 797, 1058, 852), (1146, 797, 1295, 852),
]

COORDINATES = [
    ("Navigation rail", 0, 0, 267, 941),
    ("Brand prism", 77, 29, 103, 112),
    ("Brand", 49, 158, 179, 91),
    ("Page title", 352, 68, 153, 74),
    ("Connection status", 1387, 93, 191, 64),
    ("Basic tab", 332, 187, 160, 67),
    ("Advanced tab", 492, 191, 139, 57),
    ("Mappings tab", 631, 191, 180, 57),
    ("Settings surface", 306, 247, 1234, 658),
    ("Settings inner surface", 319, 260, 1207, 502),
    ("Left column", 350, 299, 555, 438),
    ("Right column", 963, 299, 501, 374),
    ("Column divider", 932, 299, 1, 408),
    ("Visual pack row", 350, 300, 555, 55),
    ("Receiver scale row", 350, 385, 555, 55),
    ("Overall size row", 350, 477, 545, 61),
    ("Double tap row", 350, 573, 545, 61),
    ("Dead zone row", 350, 669, 545, 61),
    ("Highlight row", 963, 300, 501, 65),
    ("Petal opacity row", 963, 401, 501, 65),
    ("Border opacity row", 963, 501, 501, 65),
    ("Text opacity row", 963, 601, 501, 68),
    ("Preview button", 351, 786, 202, 82),
    ("Hide preview button", 593, 786, 208, 82),
    ("Apply button", 852, 786, 221, 82),
    ("Restore button", 1119, 786, 201, 82),
    ("Right architecture", 1539, 105, 133, 703),
    ("Bottom-left architecture", 0, 548, 306, 393),
    ("Bottom-right portal", 1295, 715, 243, 226),
    ("Moon", 910, 28, 83, 91),
]


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def interpolated_patch(array: np.ndarray, rect: tuple[int, int, int, int]) -> np.ndarray:
    x0, y0, x1, y1 = rect
    height, width = y1 - y0, x1 - x0
    left = array[y0:y1, max(0, x0 - 5):max(1, x0 - 2)].mean(axis=1)
    right = array[y0:y1, min(array.shape[1] - 1, x1 + 2):min(array.shape[1], x1 + 5)].mean(axis=1)
    top = array[max(0, y0 - 5):max(1, y0 - 2), x0:x1].mean(axis=0)
    bottom = array[min(array.shape[0] - 1, y1 + 2):min(array.shape[0], y1 + 5), x0:x1].mean(axis=0)
    wx = np.linspace(0, 1, width, dtype=np.float32)[None, :, None]
    wy = np.linspace(0, 1, height, dtype=np.float32)[:, None, None]
    horizontal = left[:, None, :] * (1 - wx) + right[:, None, :] * wx
    vertical = top[None, :, :] * (1 - wy) + bottom[None, :, :] * wy
    return (horizontal * .52 + vertical * .48).clip(0, 255)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--review", type=Path, required=True)
    args = parser.parse_args()
    if not args.reference.is_file():
        raise SystemExit(f"Missing reference: {args.reference}")
    if digest(args.reference) != EXPECTED_SHA256:
        raise SystemExit("Reference SHA-256 mismatch")
    with Image.open(args.reference) as opened:
        reference = opened.convert("RGB")
    if reference.size != EXPECTED_SIZE:
        raise SystemExit(f"Reference size mismatch: {reference.size}")

    source = np.asarray(reference, dtype=np.float32)
    rebuilt = source.copy()
    hard_mask = Image.new("L", reference.size, 0)
    draw = ImageDraw.Draw(hard_mask)
    for rect in DYNAMIC_RECTS:
        x0, y0, x1, y1 = rect
        rebuilt[y0:y1, x0:x1] = interpolated_patch(source, rect)
        draw.rectangle((x0, y0, x1 - 1, y1 - 1), fill=255)
    feather = hard_mask.filter(ImageFilter.GaussianBlur(2.0))
    static_art = Image.composite(
        Image.fromarray(rebuilt.astype(np.uint8), "RGB"), reference, feather)

    args.assets.mkdir(parents=True, exist_ok=True)
    args.review.mkdir(parents=True, exist_ok=True)
    static_art.save(args.assets / "static-art.png", optimize=True)
    reference.save(args.review / "reference-settings.png", optimize=True)
    hard_mask.save(args.review / "dynamic-mask-settings.png", optimize=True)
    audit = {
        "reference": {
            "path": str(args.reference), "width": 1672, "height": 941,
            "sha256": EXPECTED_SHA256,
        },
        "elements": [
            {"name": name, "x": x, "y": y, "width": width, "height": height}
            for name, x, y, width, height in COORDINATES
        ],
    }
    (args.review / "coordinates-settings.json").write_text(
        json.dumps(audit, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"dimensions={reference.width}x{reference.height}")
    print(f"sha256={EXPECTED_SHA256}")
    print(f"masked_regions={len(DYNAMIC_RECTS)}")


if __name__ == "__main__":
    main()
