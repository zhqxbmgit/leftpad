#!/usr/bin/env python3
"""Build Dark Fantasy Spike 02 from tracked Spike 01 artwork.

The imagegen cleanup is used only as a texture donor inside fixed semantic
cleanup masks.  The silhouette, alpha, non-semantic pixels, and selected-state
lighting deltas all come from the tracked Spike 01 state artwork.
"""

from __future__ import annotations

import hashlib
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont


SPIKE = Path(__file__).resolve().parent
SPIKE01 = SPIKE.parent / "dark-fantasy-radial8-v1"
DONOR = SPIKE / "source" / "imagegen-clean-donor.png"
STATES = SPIKE / "states"
PACKAGE = SPIKE / "package"
PACKAGE_STATES = PACKAGE / "states"
MASKS = SPIKE / "masks"
WORKING = SPIKE / "working"
REVIEW = SPIKE / "review"

STATE_NAMES = ["idle", *[f"selected-{slot}" for slot in range(1, 9)]]
CANVAS = (1254, 1254)

# Measured directly against the tracked 1254x1254 artwork.  Bounds are kept
# per-slot because the forged bezels and available tangential label surfaces
# are not identical.
GEOMETRY: dict[str, object] = {
    "slots": {
        "1": {
            "direction": "top",
            "center": [628, 268],
            "glyph": [570, 207, 116, 116],
            "label": [548, 320, 160, 82],
            "labelRotation": 0,
            "horizontalAlignment": "center",
            "verticalAlignment": "center",
        },
        "2": {
            "direction": "upper-right",
            "center": [886, 334],
            "glyph": [833, 282, 106, 106],
            "label": [776, 360, 220, 130],
            "labelRotation": 36,
            "horizontalAlignment": "center",
            "verticalAlignment": "center",
        },
        "3": {
            "direction": "right",
            "center": [998, 580],
            "glyph": [945, 527, 106, 106],
            "label": [900, 500, 92, 160],
            "labelRotation": 82,
            "horizontalAlignment": "center",
            "verticalAlignment": "center",
        },
        "4": {
            "direction": "lower-right",
            "center": [888, 837],
            "glyph": [835, 784, 106, 106],
            "label": [788, 836, 196, 116],
            "labelRotation": -36,
            "horizontalAlignment": "center",
            "verticalAlignment": "center",
        },
        "5": {
            "direction": "bottom",
            "center": [628, 937],
            "glyph": [575, 884, 106, 106],
            "label": [548, 970, 160, 76],
            "labelRotation": 0,
            "horizontalAlignment": "center",
            "verticalAlignment": "center",
        },
        "6": {
            "direction": "lower-left",
            "center": [368, 837],
            "glyph": [315, 784, 106, 106],
            "label": [272, 836, 196, 116],
            "labelRotation": 36,
            "horizontalAlignment": "center",
            "verticalAlignment": "center",
        },
        "7": {
            "direction": "left",
            "center": [258, 580],
            "glyph": [205, 527, 106, 106],
            "label": [264, 500, 92, 160],
            "labelRotation": -82,
            "horizontalAlignment": "center",
            "verticalAlignment": "center",
        },
        "8": {
            "direction": "upper-left",
            "center": [370, 334],
            "glyph": [317, 282, 106, 106],
            "label": [260, 360, 220, 130],
            "labelRotation": -36,
            "horizontalAlignment": "center",
            "verticalAlignment": "center",
        },
    },
    "central": {
        "glyph": [508, 380, 240, 195],
        "label": [405, 500, 446, 150],
        "horizontalAlignment": "center",
        "verticalAlignment": "center",
    },
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def ensure_dirs() -> None:
    for directory in (STATES, PACKAGE_STATES, MASKS, WORKING, REVIEW):
        directory.mkdir(parents=True, exist_ok=True)


def rounded_rect_mask(bounds: list[int], radius: int = 14) -> Image.Image:
    mask = Image.new("L", CANVAS, 0)
    ImageDraw.Draw(mask).rounded_rectangle(tuple(bounds), radius=radius, fill=255)
    return mask


def ellipse_mask(bounds: tuple[int, int, int, int]) -> Image.Image:
    mask = Image.new("L", CANVAS, 0)
    ImageDraw.Draw(mask).ellipse(bounds, fill=255)
    return mask


def build_cleanup_masks() -> tuple[Image.Image, Image.Image, dict[str, Image.Image]]:
    regions: dict[str, Image.Image] = {}
    hard = Image.new("L", CANVAS, 0)
    hard_draw = ImageDraw.Draw(hard)

    for slot, raw in GEOMETRY["slots"].items():
        data = dict(raw)
        cx, cy = data["center"]
        radius = 72 if slot == "1" else 68
        glyph = ellipse_mask((cx - radius, cy - radius, cx + radius, cy + radius))
        label_bounds = list(data["label"])
        # Cleanup targets the original horizontal labels, independent of the
        # tangential dynamic anchor rotation used by the new package.
        cleanup_label_bounds = {
            "1": [540, 350, 716, 402],
            "2": [805, 405, 970, 458],
            "3": [920, 658, 1080, 716],
            "4": [811, 914, 974, 972],
            "5": [540, 1008, 718, 1068],
            "6": [282, 914, 450, 972],
            "7": [170, 658, 342, 716],
            "8": [286, 405, 450, 458],
        }[slot]
        label = rounded_rect_mask(cleanup_label_bounds, 12)
        regions[f"slot{slot}-glyph"] = glyph
        regions[f"slot{slot}-label"] = label
        hard_draw.bitmap((0, 0), glyph, fill=255)
        hard_draw.bitmap((0, 0), label, fill=255)

    # Replacing the complete inner central plate is safer than trying to mask
    # individual letters: it guarantees no Sword/Balanced/description ghosts
    # while retaining the donor's matching runes, cracks, and local lighting.
    central = ellipse_mask((444, 404, 812, 792))
    regions["central-semantics"] = central
    hard_draw.bitmap((0, 0), central, fill=255)

    feather = hard.filter(ImageFilter.GaussianBlur(9.0))
    hard.save(MASKS / "semantic-cleanup-hard.png")
    feather.save(MASKS / "semantic-cleanup-mask.png")
    for name, mask in regions.items():
        mask.save(MASKS / f"{name}.png")
    return hard, feather, regions


def canonical_rgba(array: np.ndarray) -> Image.Image:
    output = array.astype(np.uint8, copy=True)
    output[..., :3][output[..., 3] == 0] = 0
    return Image.fromarray(output, mode="RGBA")


def blur_signed_delta(delta: np.ndarray, radius: float) -> np.ndarray:
    channels = []
    for channel in range(3):
        encoded = np.clip((delta[..., channel] + 255.0) * 0.5, 0.0, 255.0).astype(np.uint8)
        image = Image.fromarray(encoded, mode="L")
        blurred = np.asarray(image.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)
        channels.append(blurred * 2.0 - 255.0)
    return np.stack(channels, axis=-1)


def build_states(feather: Image.Image) -> dict[str, Image.Image]:
    idle = np.asarray(Image.open(SPIKE01 / "states" / "idle.png").convert("RGBA"), dtype=np.uint8)
    donor = np.asarray(Image.open(DONOR).convert("RGB"), dtype=np.uint8)
    if idle.shape[:2] != (1254, 1254) or donor.shape[:2] != (1254, 1254):
        raise SystemExit("Spike01 and donor must both be 1254x1254")

    blend = np.asarray(feather, dtype=np.float32)[..., None] / 255.0
    body = idle[..., 3:4].astype(np.float32) / 255.0
    blend *= body
    clean_rgb = idle[..., :3].astype(np.float32) * (1.0 - blend) + donor.astype(np.float32) * blend
    clean_idle = np.dstack((np.clip(clean_rgb, 0, 255).astype(np.uint8), idle[..., 3]))
    frames: dict[str, Image.Image] = {"idle": canonical_rgba(clean_idle)}

    semantic = blend
    idle_rgb = idle[..., :3].astype(np.float32)
    for slot in range(1, 9):
        name = f"selected-{slot}"
        source = np.asarray(Image.open(SPIKE01 / "states" / f"{name}.png").convert("RGBA"), dtype=np.uint8)
        delta = source[..., :3].astype(np.float32) - idle_rgb
        low_frequency = blur_signed_delta(delta, 22.0)
        # Exact selected lighting is retained outside semantic cleanup zones.
        # Inside them, only the low-frequency illumination field is retained,
        # preventing icon-shaped high-frequency ghosts.
        selected_rgb = clean_rgb + delta * (1.0 - semantic) + low_frequency * semantic
        selected = np.dstack((np.clip(selected_rgb, 0, 255).astype(np.uint8), source[..., 3]))
        frames[name] = canonical_rgba(selected)

    for name, frame in frames.items():
        path = STATES / f"{name}.png"
        frame.save(path, optimize=True)
        shutil.copy2(path, PACKAGE_STATES / path.name)
    return frames


def layer(layer_id: str, kind: str, z: int, bounds: list[int], visible: list[str], owner: str) -> dict[str, object]:
    x, y, width, height = bounds
    return {
        "id": layer_id,
        "kind": kind,
        "zIndex": z,
        "bounds": {"x": x, "y": y, "width": width, "height": height},
        "visibleStates": visible,
        "ownership": owner,
        "required": True,
    }


def anchor(anchor_id: str, layer_id: str, role: str, bounds: list[int], visible: list[str],
           style: str, rotation: float, overflow: str, minimum: float, glyph_role: str | None = None) -> dict[str, object]:
    x, y, width, height = bounds
    value: dict[str, object] = {
        "id": anchor_id,
        "layerId": layer_id,
        "role": role,
        "bounds": {"x": x, "y": y, "width": width, "height": height},
        "horizontalAlignment": "center",
        "verticalAlignment": "center",
        "overflowPolicy": overflow,
        "minimumScale": minimum,
        "rotation": rotation,
        "maxLines": 1,
        "visibleStates": visible,
        "styleRole": style,
    }
    if glyph_role is not None:
        value["glyphRole"] = glyph_role
    return value


def write_manifest() -> dict[str, object]:
    states: dict[str, object] = {}
    for name in STATE_NAMES:
        states[name] = {
            "slotId": None if name == "idle" else int(name.split("-")[1]),
            "assets": {"stateFrame": f"states/{name}.png"},
        }

    layers: list[dict[str, object]] = [
        layer("stateFrame", "stateAsset", 0, [0, 0, 1254, 1254], ["all"], "STATE_ASSET")
    ]
    anchors: list[dict[str, object]] = []
    ownership: list[dict[str, object]] = [
        {"element": "stateArt", "owner": "STATE_ASSET", "layerId": "stateFrame"}
    ]

    for slot, raw in GEOMETRY["slots"].items():
        data = dict(raw)
        glyph_bounds = list(data["glyph"])
        label_bounds = list(data["label"])
        glyph_layer = f"slot{slot}GlyphLayer"
        label_layer = f"slot{slot}LabelLayer"
        layers.append(layer(glyph_layer, "dynamicGlyph", 10, glyph_bounds, ["all"], "DYNAMIC"))
        layers.append(layer(label_layer, "dynamicText", 11, label_bounds, ["all"], "DYNAMIC"))
        anchors.append(anchor(f"slot{slot}GlyphAnchor", glyph_layer, "glyph", glyph_bounds, ["all"],
                              "darkFantasySlotGlyph", 0, "shrink", 0.48, "darkFantasyAction"))
        anchors.append(anchor(f"slot{slot}LabelAnchor", label_layer, "text", label_bounds, ["all"],
                              "darkFantasySlotLabel", float(data["labelRotation"]), "ellipsis", 0.52))
        ownership.append({"element": f"slot{slot}GlyphAnchor", "owner": "DYNAMIC", "layerId": glyph_layer,
                          "contentKey": f"slot{slot}ActionGlyph"})
        ownership.append({"element": f"slot{slot}LabelAnchor", "owner": "DYNAMIC", "layerId": label_layer,
                          "contentKey": f"slot{slot}ActionLabel"})

    central = dict(GEOMETRY["central"])
    layers.append(layer("selectedGlyphLayer", "dynamicGlyph", 12, list(central["glyph"]), ["selected"], "DYNAMIC"))
    layers.append(layer("selectedLabelLayer", "dynamicText", 13, list(central["label"]), ["selected"], "DYNAMIC"))
    anchors.append(anchor("selectedGlyphAnchor", "selectedGlyphLayer", "glyph", list(central["glyph"]),
                          ["selected"], "darkFantasySelectedGlyph", 0, "shrink", 0.42, "darkFantasyAction"))
    anchors.append(anchor("selectedLabelAnchor", "selectedLabelLayer", "text", list(central["label"]),
                          ["selected"], "darkFantasySelectedLabel", 0, "shrink", 0.30))
    ownership.append({"element": "selectedGlyphAnchor", "owner": "DYNAMIC", "layerId": "selectedGlyphLayer",
                      "contentKey": "selectedActionGlyph"})
    ownership.append({"element": "selectedLabelAnchor", "owner": "DYNAMIC", "layerId": "selectedLabelLayer",
                      "contentKey": "selectedActionLabel"})

    text_source = {"type": "text", "styleRole": "darkFantasySlotGlyph"}
    runtime_ds4 = {"type": "runtimeSymbol", "symbolSet": "leftpad-ds4"}
    manifest: dict[str, object] = {
        "protocolVersion": 2,
        "packageRevision": 1,
        "id": "reference-dark-fantasy-radial8-dynamic-spike",
        "name": "Dark Fantasy Radial 8 Dynamic Spike",
        "description": "Reference-fidelity Spike 01 artwork with action semantics owned by Phase 3 dynamic mappings.",
        "exampleOnly": False,
        "surface": "radial-overlay",
        "renderStrategy": "full-state-frame",
        "authoring": {"method": "mixed"},
        "layoutProfile": "radial-8",
        "compatibleLayouts": ["radial-8"],
        "referenceCanvas": {"width": 1254, "height": 1254, "colorSpace": "sRGB", "alphaMode": "straight"},
        "referenceScale": {"logicalWidth": 420, "logicalHeight": 420, "fit": "contain",
                           "contentOrigin": {"x": 0, "y": 0}},
        "placement": {"activationAnchor": {"x": 210, "y": 210}},
        "states": states,
        "layers": layers,
        "dynamicAnchors": anchors,
        "styles": {
            "fontRoles": {"fantasyDisplay": "display", "fantasySymbols": "symbol"},
            "colorRoles": {
                "warmIvory": "#E8D8B0FF",
                "paleGold": "#F1D292FF",
                "selectedGold": "#FFD178FF",
                "darkEdge": "#0B0805F0",
                "deepShadow": "#0000008C",
            },
            "outlineRoles": {
                "thinDark": {"colorRole": "darkEdge", "width": 1.35},
                "glyphDark": {"colorRole": "darkEdge", "width": 2.6},
                "centralDark": {"colorRole": "darkEdge", "width": 2.25},
            },
            "shadowRoles": {
                "restrained": {"colorRole": "deepShadow", "offsetX": 1.2, "offsetY": 1.8, "blur": 1.25}
            },
            "dynamicRoles": {
                "darkFantasySlotLabel": {"fontRole": "fantasyDisplay", "colorRole": "warmIvory",
                                         "outlineRole": "thinDark", "shadowRole": "restrained", "size": 28},
                "darkFantasySlotGlyph": {"fontRole": "fantasySymbols", "colorRole": "paleGold",
                                         "outlineRole": "glyphDark", "shadowRole": "restrained", "size": 62},
                "darkFantasySelectedGlyph": {"fontRole": "fantasySymbols", "colorRole": "selectedGold",
                                             "outlineRole": "centralDark", "shadowRole": "restrained", "size": 102},
                "darkFantasySelectedLabel": {"fontRole": "fantasyDisplay", "colorRole": "warmIvory",
                                             "outlineRole": "centralDark", "shadowRole": "restrained", "size": 40},
            },
        },
        "glyphs": {"roles": {"darkFantasyAction": {
            # Phase 3 deliberately treats an incapable runtime-symbol-only family as no-draw.
            # Keyboard identity is therefore owned by the label layer exactly once, while DS4
            # retains the symbol + semantic-name relationship.
            "keyboard": {"sources": [dict(runtime_ds4)]},
            "keyboardShortcut": {"sources": [dict(runtime_ds4)]},
            "ds4": {"sources": [dict(runtime_ds4), dict(text_source)]},
            "genericAction": {"sources": [dict(runtime_ds4), dict(text_source)]},
        }}},
        "elementOwnership": ownership,
        "masks": [],
        "visualRegions": [],
        "fallback": {"onInvalidCandidate": "retain-active", "onUnsupportedVersion": "retain-active",
                     "startupThemeId": "radial-v5"},
        "capabilities": {"required": ["fullStateFrame", "dynamicAnchors", "instantTransitions"], "optional": []},
        "transitions": {"mode": "instant"},
        "assetHashes": {f"states/{name}.png": sha256(PACKAGE_STATES / f"{name}.png") for name in STATE_NAMES},
    }
    (PACKAGE / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    return manifest


def checkerboard(size: tuple[int, int], cell: int = 24) -> Image.Image:
    yy, xx = np.mgrid[0:size[1], 0:size[0]]
    cells = ((xx // cell + yy // cell) & 1)[..., None]
    rgb = np.where(cells == 0, 220, 168).astype(np.uint8)
    return Image.fromarray(np.repeat(rgb, 3, axis=2), mode="RGB")


def on_background(image: Image.Image, background: tuple[int, int, int] | None = None) -> Image.Image:
    base = checkerboard(image.size) if background is None else Image.new("RGB", image.size, background)
    base = base.convert("RGBA")
    base.alpha_composite(image.convert("RGBA"))
    return base.convert("RGB")


def thumbnail(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    copy = image.copy()
    copy.thumbnail(size, Image.Resampling.LANCZOS)
    return copy


def draw_rect(draw: ImageDraw.ImageDraw, bounds: list[int], color: tuple[int, int, int, int], width: int = 4) -> None:
    x, y, w, h = bounds
    draw.rectangle((x, y, x + w, y + h), outline=color, width=width)


def build_reviews(frames: dict[str, Image.Image], hard: Image.Image, manifest: dict[str, object]) -> None:
    idle = frames["idle"]
    original = Image.open(SPIKE01 / "states" / "idle.png").convert("RGBA")

    clean_sheet = Image.new("RGB", (2508, 1304), (16, 17, 18))
    clean_sheet.paste(on_background(original), (0, 50))
    clean_sheet.paste(on_background(idle), (1254, 50))
    draw = ImageDraw.Draw(clean_sheet)
    font = ImageFont.load_default(size=24)
    draw.text((20, 12), "SPIKE01 BAKED SEMANTICS", fill=(236, 215, 170), font=font)
    draw.text((1274, 12), "SPIKE02 CLEAN DYNAMIC-SAFE ART", fill=(236, 215, 170), font=font)
    clean_sheet.save(REVIEW / "clean-art-review.png", quality=96)

    overlay = on_background(idle).convert("RGBA")
    draw = ImageDraw.Draw(overlay, "RGBA")
    for slot, raw in GEOMETRY["slots"].items():
        data = dict(raw)
        draw_rect(draw, list(data["glyph"]), (242, 190, 72, 235))
        draw_rect(draw, list(data["label"]), (102, 210, 255, 235))
        x, y = data["center"]
        draw.text((x - 10, y - 10), slot, fill=(255, 255, 255, 255), font=font)
    central = dict(GEOMETRY["central"])
    draw_rect(draw, list(central["glyph"]), (242, 190, 72, 235))
    draw_rect(draw, list(central["label"]), (102, 210, 255, 235))
    overlay.save(REVIEW / "anchor-overlay-review.png")

    ownership = on_background(idle).convert("RGBA")
    tint = Image.new("RGBA", CANVAS, (30, 120, 220, 0))
    tint.putalpha(hard.point(lambda value: int(value * 0.52)))
    ownership.alpha_composite(tint)
    draw = ImageDraw.Draw(ownership, "RGBA")
    draw.rectangle((20, 20, 470, 120), fill=(7, 8, 10, 220))
    draw.text((36, 34), "STATE_ASSET = untouched dark artwork", fill=(220, 220, 220, 255), font=font)
    draw.text((36, 66), "DYNAMIC_GLYPH = gold boxes / blue cleanup", fill=(242, 190, 72, 255), font=font)
    draw.text((36, 94), "DYNAMIC_TEXT = cyan boxes / blue cleanup", fill=(102, 210, 255, 255), font=font)
    ownership.save(REVIEW / "ownership-review.png")

    tile = 410
    items = [
        ("Spike01 selected-1", Image.open(SPIKE01 / "states" / "selected-1.png").convert("RGBA")),
        ("Spike02 idle", frames["idle"]),
        ("Spike02 selected-1", frames["selected-1"]),
        ("Spike02 selected-3", frames["selected-3"]),
        ("Spike02 selected-5", frames["selected-5"]),
        ("Spike02 selected-8", frames["selected-8"]),
    ]
    sheet = Image.new("RGB", (tile * 3, tile * 2), (16, 17, 18))
    for index, (label, image) in enumerate(items):
        canvas = on_background(image)
        canvas.thumbnail((tile - 20, tile - 48), Image.Resampling.LANCZOS)
        x = (index % 3) * tile + (tile - canvas.width) // 2
        y = (index // 3) * tile + 40
        sheet.paste(canvas, (x, y))
        ImageDraw.Draw(sheet).text(((index % 3) * tile + 12, (index // 3) * tile + 10),
                                   label, fill=(236, 215, 170), font=font)
    sheet.save(REVIEW / "spike02-authoring-review-sheet.png", quality=96)

    (WORKING / "geometry.json").write_text(json.dumps(GEOMETRY, indent=2) + "\n", encoding="utf-8", newline="\n")
    report = {
        "result": "BUILT",
        "imagegenDonorSha256": sha256(DONOR),
        "spike01IdleSha256": sha256(SPIKE01 / "states" / "idle.png"),
        "stateCount": len(frames),
        "layerCount": len(manifest["layers"]),
        "dynamicAnchorCount": len(manifest["dynamicAnchors"]),
        "states": {name: {"sha256": sha256(STATES / f"{name}.png")} for name in STATE_NAMES},
    }
    (WORKING / "asset-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8", newline="\n")


def main() -> None:
    ensure_dirs()
    if not DONOR.is_file():
        raise SystemExit("imagegen cleanup donor missing")
    for name in STATE_NAMES:
        if not (SPIKE01 / "states" / f"{name}.png").is_file():
            raise SystemExit(f"Spike01 state missing: {name}")
    hard, feather, _ = build_cleanup_masks()
    frames = build_states(feather)
    manifest = write_manifest()
    build_reviews(frames, hard, manifest)
    print(json.dumps({
        "result": "SPIKE02 AUTHORING BUILD COMPLETE",
        "states": len(frames),
        "layers": len(manifest["layers"]),
        "anchors": len(manifest["dynamicAnchors"]),
        "package": str(PACKAGE),
    }, indent=2))


if __name__ == "__main__":
    main()
