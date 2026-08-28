#!/usr/bin/env python3
"""Build the Dark Fantasy Radial-8 fidelity-spike raster assets.

This is deliberately an asset-engineering script, not an artwork generator.
Every visible UI pixel originates in the supplied reference image.  The script
adds a hand-tuned alpha matte, neutralizes the reference selection in place,
and transplants the resulting reference-derived light delta locally to each
slot without rotating or resampling the full frame.
"""

from __future__ import annotations

import hashlib
import json
import math
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parents[4]
SPIKE = Path(__file__).resolve().parent
REFERENCE = ROOT / "reference_inputs" / "reference-dark-fantasy-radial8.png"
STATES = SPIKE / "states"
MASKS = SPIKE / "masks"
WORKING = SPIKE / "working"
REVIEW = SPIKE / "review"
PACKAGE = SPIKE / "package"
PACKAGE_STATES = PACKAGE / "states"

EXPECTED_REFERENCE_SHA256 = (
    "4F31DAAE839E3EF13428316A6FC13E5070D0C4C03F7A63E6F426CD76ECABED9F"
)

# Centers are measured directly in the 1254 px reference.  They intentionally
# describe only local treatment registration; the authored artwork is never
# globally rotated or reconstructed from these values.
SLOT_CENTERS_1254 = {
    1: (628.0, 268.0),
    2: (886.0, 334.0),
    3: (998.0, 580.0),
    4: (888.0, 837.0),
    5: (628.0, 937.0),
    6: (368.0, 837.0),
    7: (258.0, 580.0),
    8: (370.0, 334.0),
}

# Local bezel size compensation measured against the neutral slot rings.
SLOT_SCALES = {1: 1.0, 2: 0.92, 3: 0.92, 4: 0.92, 5: 0.91, 6: 0.92, 7: 0.92, 8: 0.92}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def smoothstep(edge0: float, edge1: float, value: np.ndarray) -> np.ndarray:
    t = np.clip((value - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def build_base_matte(size: tuple[int, int]) -> Image.Image:
    width, height = size
    sx = width / 1254.0
    sy = height / 1254.0

    # Hand-traced UI silhouette.  This retains the irregular forged-metal rim,
    # the cardinal point ornaments, all slot bezels, and the WHISTLE/label tail.
    outline_1254 = [
        (627, 109), (666, 132), (706, 148), (760, 161), (812, 190),
        (848, 211), (891, 210), (946, 228), (982, 257), (1008, 299),
        (1037, 354), (1060, 415), (1077, 484), (1100, 527), (1122, 578),
        (1102, 630), (1077, 677), (1060, 745), (1037, 807), (1008, 865),
        (974, 920), (932, 951), (883, 976), (820, 1000), (754, 1017),
        (694, 1029), (661, 1061), (627, 1071), (591, 1061), (558, 1029),
        (499, 1017), (433, 1001), (372, 978), (321, 953), (278, 921),
        (245, 866), (216, 808), (194, 748), (177, 681), (153, 633),
        (132, 579), (153, 526), (177, 480), (194, 414), (216, 354),
        (246, 299), (278, 257), (319, 229), (365, 211), (408, 211),
        (445, 190), (497, 162), (550, 149), (589, 132),
    ]
    outline = [(round(x * sx), round(y * sy)) for x, y in outline_1254]

    hard = Image.new("L", size, 0)
    draw = ImageDraw.Draw(hard)
    draw.polygon(outline, fill=255)

    # Bezel unions keep the irregular protruding slot rims present even where
    # the coarse hand trace passes slightly inside a dark metal point.
    for slot, (cx, cy) in SLOT_CENTERS_1254.items():
        radius = (126 if slot == 1 else 115) * min(sx, sy)
        x = cx * sx
        y = cy * sy
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=255)

    # Inward-only feathering prevents forest RGB from contaminating the edge.
    blurred = hard.filter(ImageFilter.GaussianBlur(radius=max(1.0, 2.2 * min(sx, sy))))
    matte = Image.fromarray(
        np.minimum(np.asarray(hard, dtype=np.uint8), np.asarray(blurred, dtype=np.uint8)),
        mode="L",
    )
    return matte


def top_light_fields(rgb: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    height, width, _ = rgb.shape
    scale = width / 1254.0
    yy, xx = np.mgrid[0:height, 0:width]
    cx, cy = SLOT_CENTERS_1254[1]
    cx *= scale
    cy *= scale
    dx = xx - cx
    dy = yy - cy
    radius = np.sqrt(dx * dx + dy * dy)

    source = rgb.astype(np.float32)
    red, green, blue = source[..., 0], source[..., 1], source[..., 2]
    luminance = 0.2126 * red + 0.7152 * green + 0.0722 * blue
    warm = np.clip((red - blue - 8.0) / 105.0, 0.0, 1.0)
    warm *= np.clip((green - blue + 5.0) / 75.0, 0.0, 1.0)
    warm *= smoothstep(24.0, 135.0, luminance)

    ring = smoothstep(58.0 * scale, 82.0 * scale, radius) * (
        1.0 - smoothstep(120.0 * scale, 150.0 * scale, radius)
    )
    inner = (1.0 - smoothstep(52.0 * scale, 76.0 * scale, radius)) * 0.30
    halo = smoothstep(105.0 * scale, 121.0 * scale, radius) * (
        1.0 - smoothstep(146.0 * scale, 172.0 * scale, radius)
    )
    connector = (
        smoothstep(72.0 * scale, 100.0 * scale, dy)
        * (1.0 - smoothstep(150.0 * scale, 180.0 * scale, dy))
        * (1.0 - smoothstep(18.0 * scale, 58.0 * scale, np.abs(dx)))
    )
    treatment = np.clip(warm * np.maximum.reduce((ring, inner, halo * 0.75, connector * 0.85)), 0.0, 1.0)

    # Keep selected treatment texture: only the emissive component is reduced.
    neutral_luma = np.minimum(luminance, 42.0 + luminance * 0.28)
    neutral = np.stack(
        (neutral_luma * 1.10, neutral_luma * 0.96, neutral_luma * 0.73), axis=-1
    )
    neutral = np.clip(neutral, 0.0, 255.0)
    blend = treatment[..., None]
    idle_rgb = source * (1.0 - blend) + neutral * blend

    # Actual reference-derived selected delta.  This includes ring texture,
    # asymmetric hot spots, runes, cracks, and connector response.
    delta = np.maximum(source - idle_rgb, 0.0)
    delta *= np.clip((ring + halo * 0.82 + connector * 0.92 + inner * 0.25), 0.0, 1.0)[..., None]

    # Alpha for just the free-standing amber halo outside the hard UI body.
    halo_alpha = np.clip(255.0 * warm * (halo * 0.72 + connector * 0.30), 0.0, 190.0)
    return np.clip(idle_rgb, 0, 255).astype(np.uint8), delta.astype(np.float32), halo_alpha.astype(np.uint8)


def transform_local_field(
    field: np.ndarray,
    source_center: tuple[float, float],
    target_center: tuple[float, float],
    clockwise_degrees: float,
    scale: float,
) -> np.ndarray:
    """Bilinearly map a local source field onto one target slot."""

    height, width = field.shape[:2]
    channels = 1 if field.ndim == 2 else field.shape[2]
    output = np.zeros_like(field, dtype=np.float32)
    radius = int(math.ceil(178.0 * width / 1254.0 * scale))
    tx, ty = target_center
    x0 = max(0, int(math.floor(tx - radius)))
    x1 = min(width, int(math.ceil(tx + radius + 1)))
    y0 = max(0, int(math.floor(ty - radius)))
    y1 = min(height, int(math.ceil(ty + radius + 1)))
    yy, xx = np.mgrid[y0:y1, x0:x1]

    theta = math.radians(clockwise_degrees)
    cos_t = math.cos(theta)
    sin_t = math.sin(theta)
    local_x = (xx - tx) / scale
    local_y = (yy - ty) / scale
    # Inverse of the clockwise image-coordinate rotation.
    source_dx = cos_t * local_x + sin_t * local_y
    source_dy = -sin_t * local_x + cos_t * local_y
    sx = source_center[0] + source_dx
    sy = source_center[1] + source_dy

    sx0 = np.floor(sx).astype(np.int32)
    sy0 = np.floor(sy).astype(np.int32)
    valid = (sx0 >= 0) & (sy0 >= 0) & (sx0 + 1 < width) & (sy0 + 1 < height)
    fx = (sx - sx0).astype(np.float32)
    fy = (sy - sy0).astype(np.float32)
    sx0c = np.clip(sx0, 0, width - 2)
    sy0c = np.clip(sy0, 0, height - 2)

    source = field[..., None] if channels == 1 else field
    a = source[sy0c, sx0c]
    b = source[sy0c, sx0c + 1]
    c = source[sy0c + 1, sx0c]
    d = source[sy0c + 1, sx0c + 1]
    sample = (
        a * ((1.0 - fx) * (1.0 - fy))[..., None]
        + b * (fx * (1.0 - fy))[..., None]
        + c * ((1.0 - fx) * fy)[..., None]
        + d * (fx * fy)[..., None]
    )
    sample *= valid[..., None]
    if channels == 1:
        output[y0:y1, x0:x1] = sample[..., 0]
    else:
        output[y0:y1, x0:x1] = sample
    return output


def decontaminated_halo_rgb(reference_rgb: np.ndarray, halo_alpha: np.ndarray) -> np.ndarray:
    source = reference_rgb.astype(np.float32)
    luma = 0.2126 * source[..., 0] + 0.7152 * source[..., 1] + 0.0722 * source[..., 2]
    strength = np.clip(halo_alpha.astype(np.float32) / 190.0, 0.0, 1.0)
    brightness = np.clip(58.0 + luma * 1.35 + strength * 72.0, 0.0, 255.0)
    return np.stack(
        (brightness, brightness * 0.58, brightness * 0.14), axis=-1
    ).clip(0, 255).astype(np.uint8)


def rgba_with_alpha(rgb: np.ndarray, alpha: np.ndarray) -> Image.Image:
    clean_rgb = rgb.copy()
    # Remove even invisible world-scene payload: fully transparent pixels are
    # canonical transparent black, not forest RGB hidden behind alpha zero.
    clean_rgb[alpha == 0] = 0
    rgba = np.dstack((clean_rgb, alpha.astype(np.uint8)))
    return Image.fromarray(rgba.astype(np.uint8), mode="RGBA")


def checkerboard(size: tuple[int, int], cell: int = 24) -> Image.Image:
    width, height = size
    yy, xx = np.mgrid[0:height, 0:width]
    cells = ((xx // cell + yy // cell) & 1).astype(np.uint8)
    base = np.where(cells[..., None] == 0, 218, 168).astype(np.uint8)
    rgb = np.repeat(base, 3, axis=2)
    return Image.fromarray(rgb, mode="RGB")


def composite(image: Image.Image, background: Image.Image) -> Image.Image:
    bg = background.convert("RGBA")
    bg.alpha_composite(image)
    return bg.convert("RGB")


def labeled_tile(image: Image.Image, label: str, tile_size: int, transparent_bg: bool) -> Image.Image:
    thumb = image.copy()
    thumb.thumbnail((tile_size - 24, tile_size - 54), Image.Resampling.LANCZOS)
    if transparent_bg:
        canvas = checkerboard((tile_size, tile_size), cell=18).convert("RGBA")
    else:
        canvas = Image.new("RGBA", (tile_size, tile_size), (19, 21, 22, 255))
    x = (tile_size - thumb.width) // 2
    y = 40 + (tile_size - 40 - thumb.height) // 2
    canvas.alpha_composite(thumb.convert("RGBA"), (x, y))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default(size=18)
    draw.rectangle((0, 0, tile_size, 34), fill=(8, 9, 10, 235))
    draw.text((12, 8), label, fill=(239, 220, 177, 255), font=font)
    return canvas


def build_review(reference: Image.Image, frames: dict[str, Image.Image]) -> None:
    tile_size = 420
    layout = [
        ("Reference", reference.convert("RGBA")), ("Idle", frames["idle"]),
        ("Selected-1", frames["selected-1"]), None,
        ("Selected-2", frames["selected-2"]), ("Selected-3", frames["selected-3"]),
        ("Selected-4", frames["selected-4"]), None,
        ("Selected-5", frames["selected-5"]), ("Selected-6", frames["selected-6"]),
        ("Selected-7", frames["selected-7"]), ("Selected-8", frames["selected-8"]),
    ]
    for transparent_bg, filename in ((False, "review-sheet.png"), (True, "checkerboard-contact-sheet.png")):
        sheet = Image.new("RGBA", (tile_size * 4, tile_size * 3), (13, 14, 15, 255))
        for index, entry in enumerate(layout):
            if entry is None:
                continue
            label, image = entry
            tile = labeled_tile(image, label, tile_size, transparent_bg and label != "Reference")
            sheet.alpha_composite(tile, ((index % 4) * tile_size, (index // 4) * tile_size))
        sheet.convert("RGB").save(REVIEW / filename, quality=96)

    comparison = Image.new("RGB", (1254 * 2, 1304), (25, 26, 27))
    comparison.paste(reference.convert("RGB"), (0, 50))
    selected_on_checker = composite(frames["selected-1"], checkerboard(reference.size, 24))
    comparison.paste(selected_on_checker, (1254, 50))
    draw = ImageDraw.Draw(comparison)
    font = ImageFont.load_default(size=24)
    draw.text((20, 13), "REFERENCE (world background retained only for comparison)", fill=(245, 224, 180), font=font)
    draw.text((1274, 13), "SELECTED-1 (transparent UI on checkerboard)", fill=(245, 224, 180), font=font)
    comparison.save(REVIEW / "reference-vs-selected-1.png", quality=96)

    for name, color in (
        ("white", (255, 255, 255)),
        ("black", (0, 0, 0)),
        ("mid-gray", (128, 128, 128)),
    ):
        canvas = Image.new("RGB", reference.size, color)
        composite(frames["selected-1"], canvas).save(REVIEW / f"selected-1-on-{name}.png")
    composite(frames["selected-1"], checkerboard(reference.size, 24)).save(
        REVIEW / "selected-1-on-checkerboard.png"
    )


def write_manifest(size: tuple[int, int]) -> None:
    width, height = size
    state_names = ["idle", *[f"selected-{slot}" for slot in range(1, 9)]]
    states = {}
    for name in state_names:
        states[name] = {
            "slotId": None if name == "idle" else int(name.split("-")[1]),
            "assets": {"stateFrame": f"states/{name}.png"},
        }

    manifest = {
        "protocolVersion": 2,
        "packageRevision": 1,
        "id": "reference-dark-fantasy-radial8-spike",
        "name": "Dark Fantasy Radial 8 Spike",
        "description": "Untracked reference-fidelity spike using isolated and locally edited reference pixels.",
        "exampleOnly": False,
        "surface": "radial-overlay",
        "renderStrategy": "full-state-frame",
        "authoring": {"method": "mixed"},
        "layoutProfile": "radial-8",
        "compatibleLayouts": ["radial-8"],
        "referenceCanvas": {
            "width": width, "height": height, "colorSpace": "sRGB", "alphaMode": "straight"
        },
        "referenceScale": {
            "logicalWidth": 420, "logicalHeight": 420, "fit": "contain",
            "contentOrigin": {"x": 0, "y": 0},
        },
        "placement": {"activationAnchor": {"x": 210, "y": 210}},
        "states": states,
        "layers": [{
            "id": "stateFrame", "kind": "stateAsset", "zIndex": 0,
            "bounds": {"x": 0, "y": 0, "width": width, "height": height},
            "visibleStates": ["all"], "ownership": "STATE_ASSET", "required": True,
        }],
        "dynamicAnchors": [],
        # Required protocol metadata remains deliberately unreferenced: this
        # spike has no dynamic layer and requests no glyph/style capability.
        "styles": {
            "fontRoles": {"interface": "ui"},
            "colorRoles": {"primaryText": "#FFFFFFFF"},
            "outlineRoles": {}, "shadowRoles": {},
            "dynamicRoles": {"unusedText": {"fontRole": "interface", "colorRole": "primaryText", "size": 12}},
        },
        "glyphs": {"roles": {"unusedAction": {
            family: {"sources": [{"type": "text", "styleRole": "unusedText"}]}
            for family in ("keyboard", "keyboardShortcut", "ds4", "genericAction")
        }}},
        "elementOwnership": [{"element": "stateArt", "owner": "STATE_ASSET", "layerId": "stateFrame"}],
        "masks": [],
        "visualRegions": [],
        "fallback": {
            "onInvalidCandidate": "retain-active", "onUnsupportedVersion": "retain-active",
            "startupThemeId": "radial-v5",
        },
        "capabilities": {"required": ["fullStateFrame", "instantTransitions"], "optional": []},
        "transitions": {"mode": "instant"},
        "assetHashes": {f"states/{name}.png": sha256(PACKAGE_STATES / f"{name}.png") for name in state_names},
    }
    (PACKAGE / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    if not REFERENCE.is_file():
        raise SystemExit("REFERENCE INPUT MISSING")
    actual_hash = sha256(REFERENCE)
    if actual_hash != EXPECTED_REFERENCE_SHA256:
        raise SystemExit(f"reference hash changed: {actual_hash}")

    for directory in (STATES, MASKS, WORKING, REVIEW, PACKAGE_STATES):
        directory.mkdir(parents=True, exist_ok=True)

    reference = Image.open(REFERENCE).convert("RGB")
    width, height = reference.size
    if width != height:
        raise SystemExit(f"spike requires square reference, got {width}x{height}")
    rgb = np.asarray(reference, dtype=np.uint8)
    base_matte_image = build_base_matte(reference.size)
    base_alpha = np.asarray(base_matte_image, dtype=np.uint8)
    idle_rgb, top_delta, top_halo = top_light_fields(rgb)

    # Any halo outside the body uses decontaminated amber RGB so forest pixels
    # cannot reappear under low alpha.
    top_halo = np.where(base_alpha < 250, top_halo, 0).astype(np.uint8)
    selected1_alpha = np.maximum(base_alpha, top_halo)
    selected1_rgb = rgb.copy()
    halo_rgb = decontaminated_halo_rgb(rgb, top_halo)
    outside = (top_halo > base_alpha) & (top_halo > 0)
    selected1_rgb[outside] = halo_rgb[outside]

    frames: dict[str, Image.Image] = {}
    frames["idle"] = rgba_with_alpha(idle_rgb, base_alpha)
    frames["selected-1"] = rgba_with_alpha(selected1_rgb, selected1_alpha)

    source_center = tuple(v * width / 1254.0 for v in SLOT_CENTERS_1254[1])
    for slot in range(2, 9):
        target_center = tuple(v * width / 1254.0 for v in SLOT_CENTERS_1254[slot])
        angle = (slot - 1) * 45.0
        scale = SLOT_SCALES[slot]
        delta = transform_local_field(top_delta, source_center, target_center, angle, scale)
        halo = transform_local_field(top_halo.astype(np.float32), source_center, target_center, angle, scale)
        result = np.clip(idle_rgb.astype(np.float32) + delta * 0.96, 0.0, 255.0).astype(np.uint8)
        alpha = np.maximum(base_alpha, np.clip(halo, 0, 190).astype(np.uint8))

        # Reconstructed only outside the isolated body; inside is a pure
        # additive transplant over the target's original texture and icon.
        outside = halo > base_alpha
        if np.any(outside):
            source_halo_rgb = decontaminated_halo_rgb(rgb, np.clip(halo, 0, 190).astype(np.uint8))
            result[outside] = source_halo_rgb[outside]
        frames[f"selected-{slot}"] = rgba_with_alpha(result, alpha)

    base_matte_image.save(MASKS / "ui-alpha-matte.png")
    Image.fromarray(np.clip(top_delta * 2.0, 0, 255).astype(np.uint8), mode="RGB").save(
        WORKING / "reference-selected-light-delta.png"
    )
    Image.fromarray(top_halo, mode="L").save(WORKING / "reference-selected-halo-alpha.png")

    for name, frame in frames.items():
        state_path = STATES / f"{name}.png"
        frame.save(state_path, optimize=True)
        shutil.copy2(state_path, PACKAGE_STATES / state_path.name)

    build_review(reference, frames)
    write_manifest(reference.size)

    report = {
        "reference": str(REFERENCE),
        "referenceSha256": actual_hash,
        "canvas": {"width": width, "height": height},
        "states": {
            name: {
                "sha256": sha256(STATES / f"{name}.png"),
                "alphaMin": int(np.asarray(frame.getchannel("A")).min()),
                "alphaMax": int(np.asarray(frame.getchannel("A")).max()),
                "transparentPixels": int(np.count_nonzero(np.asarray(frame.getchannel("A")) == 0)),
            }
            for name, frame in frames.items()
        },
    }
    (WORKING / "asset-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
