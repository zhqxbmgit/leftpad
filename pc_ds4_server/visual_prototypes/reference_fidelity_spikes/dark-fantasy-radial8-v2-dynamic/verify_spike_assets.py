#!/usr/bin/env python3
"""Deterministic structural, alpha, runtime-capture, and review checks for Spike 02."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


SPIKE = Path(__file__).resolve().parent
PACKAGE = SPIKE / "package"
REVIEW = SPIKE / "review"
WORKING = SPIKE / "working"
SPIKE01 = SPIKE.parent / "dark-fantasy-radial8-v1"
REFERENCE = SPIKE.parents[3] / "reference_inputs" / "reference-dark-fantasy-radial8.png"
REFERENCE_REVIEW = SPIKE01 / "review" / "reference-vs-selected-1.png"
STATE_NAMES = ["idle", *[f"selected-{slot}" for slot in range(1, 9)]]
RUNTIME_NAMES = ["idle", "selected-1", "selected-2", "selected-3", "selected-5", "selected-8"]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for path in (Path(r"C:\Windows\Fonts\segoeui.ttf"), Path(r"C:\Windows\Fonts\arial.ttf")):
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def fit(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    result = image.copy()
    result.thumbnail(size, Image.Resampling.LANCZOS)
    return result


def panel(image: Image.Image, title: str, size: tuple[int, int] = (410, 440)) -> Image.Image:
    result = Image.new("RGB", size, (19, 20, 23))
    draw = ImageDraw.Draw(result)
    draw.text((14, 10), title, font=font(21), fill=(239, 224, 188))
    preview = fit(image.convert("RGBA"), (size[0] - 24, size[1] - 54))
    checker = Image.new("RGB", preview.size, (58, 58, 61))
    tile = 18
    checker_draw = ImageDraw.Draw(checker)
    for y in range(0, preview.height, tile):
        for x in range(0, preview.width, tile):
            if (x // tile + y // tile) % 2:
                checker_draw.rectangle((x, y, x + tile - 1, y + tile - 1), fill=(88, 88, 92))
    checker.paste(preview, (0, 0), preview)
    result.paste(checker, ((size[0] - preview.width) // 2, 45))
    return result


def sheet(items: list[tuple[str, Image.Image]], destination: Path, columns: int = 4) -> None:
    cards = [panel(image, title) for title, image in items]
    rows = (len(cards) + columns - 1) // columns
    canvas = Image.new("RGB", (columns * 410, rows * 440), (10, 11, 13))
    for index, card in enumerate(cards):
        canvas.paste(card, ((index % columns) * 410, (index // columns) * 440))
    canvas.save(destination, optimize=True)


def anchor_bounds(manifest: dict[str, object], anchor_id: str) -> list[int]:
    anchor = next(item for item in manifest["dynamicAnchors"] if item["id"] == anchor_id)
    bounds = anchor["bounds"]
    return [bounds["x"], bounds["y"], bounds["width"], bounds["height"]]


def union_bounds(*values: list[int]) -> list[int]:
    left = min(value[0] for value in values)
    top = min(value[1] for value in values)
    right = max(value[0] + value[2] for value in values)
    bottom = max(value[1] + value[3] for value in values)
    return [left, top, right - left, bottom - top]


def runtime_crop(image: Image.Image, bounds: list[int], padding: int = 24) -> Image.Image:
    scale = image.width / 1254.0
    x, y, width, height = bounds
    left = max(0, round((x - padding) * scale))
    top = max(0, round((y - padding) * scale))
    right = min(image.width, round((x + width + padding) * scale))
    bottom = min(image.height, round((y + height + padding) * scale))
    return image.crop((left, top, right, bottom))


def detail_sheet(items: list[tuple[str, Image.Image]], destination: Path) -> None:
    columns, card_width, card_height = 3, 520, 330
    rows = (len(items) + columns - 1) // columns
    canvas = Image.new("RGB", (columns * card_width, rows * card_height), (10, 11, 13))
    title_font = font(22)
    for index, (title, source) in enumerate(items):
        card = Image.new("RGB", (card_width, card_height), (19, 20, 23))
        draw = ImageDraw.Draw(card)
        draw.text((14, 10), title, font=title_font, fill=(239, 224, 188))
        preview = source.convert("RGBA")
        target_width, target_height = card_width - 28, card_height - 54
        factor = min(target_width / preview.width, target_height / preview.height)
        preview = preview.resize(
            (max(1, round(preview.width * factor)), max(1, round(preview.height * factor))),
            Image.Resampling.LANCZOS,
        )
        backdrop = Image.new("RGB", preview.size, (68, 68, 72))
        backdrop.paste(preview, (0, 0), preview)
        card.paste(backdrop, ((card_width - preview.width) // 2, 46))
        canvas.paste(card, ((index % columns) * card_width, (index // columns) * card_height))
    canvas.save(destination, optimize=True)


def composite_variants(image: Image.Image) -> dict[str, Image.Image]:
    rgba = image.convert("RGBA")
    variants: dict[str, Image.Image] = {}
    for name, rgb in (("white", (255, 255, 255)), ("black", (0, 0, 0)), ("mid-gray", (127, 127, 127))):
        base = Image.new("RGBA", rgba.size, (*rgb, 255))
        base.alpha_composite(rgba)
        variants[name] = base.convert("RGB")
    checker = Image.new("RGB", rgba.size, (70, 70, 73))
    draw = ImageDraw.Draw(checker)
    tile = 24
    for y in range(0, rgba.height, tile):
        for x in range(0, rgba.width, tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle((x, y, x + tile - 1, y + tile - 1), fill=(104, 104, 108))
    checker.paste(rgba, (0, 0), rgba)
    variants["checkerboard"] = checker
    return variants


def reference_image() -> Image.Image:
    if REFERENCE.exists():
        return Image.open(REFERENCE).convert("RGB")
    # The original source is deliberately external to the repository; Spike 01's tracked
    # comparison preserves its exact 1254x1254 reference half for offline review.
    comparison = Image.open(REFERENCE_REVIEW).convert("RGB")
    assert comparison.size == (2508, 1304)
    return comparison.crop((0, 50, 1254, 1304))


def main() -> None:
    manifest = json.loads((PACKAGE / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["protocolVersion"] == 2
    assert manifest["id"] == "reference-dark-fantasy-radial8-dynamic-spike"
    assert manifest["name"] == "Dark Fantasy Radial 8 Dynamic Spike"
    assert manifest["renderStrategy"] == "full-state-frame"
    assert manifest["authoring"]["method"] == "mixed"
    assert manifest["layoutProfile"] == "radial-8"
    assert manifest["compatibleLayouts"] == ["radial-8"]
    assert manifest["referenceCanvas"]["width"] == 1254
    assert manifest["referenceCanvas"]["height"] == 1254
    assert manifest["referenceCanvas"]["colorSpace"] == "sRGB"
    assert manifest["referenceCanvas"]["alphaMode"] == "straight"
    assert manifest["referenceScale"]["logicalWidth"] == 420
    assert manifest["referenceScale"]["logicalHeight"] == 420
    assert manifest["placement"]["activationAnchor"] == {"x": 210, "y": 210}
    assert manifest["capabilities"]["required"] == [
        "fullStateFrame", "dynamicAnchors", "instantTransitions"
    ]

    assert list(manifest["states"]) == STATE_NAMES
    assert len(manifest["layers"]) == 19
    assert len(manifest["dynamicAnchors"]) == 18
    assert sum(layer["kind"] == "stateAsset" for layer in manifest["layers"]) == 1
    assert sum(layer["kind"] == "dynamicGlyph" for layer in manifest["layers"]) == 9
    assert sum(layer["kind"] == "dynamicText" for layer in manifest["layers"]) == 9

    anchors = {anchor["id"]: anchor for anchor in manifest["dynamicAnchors"]}
    layers = {layer["id"]: layer for layer in manifest["layers"]}
    ownership = {item["layerId"]: item for item in manifest["elementOwnership"]}
    assert len(ownership) == 19
    for slot in range(1, 9):
        for role, suffix in (("glyph", "Glyph"), ("text", "Label")):
            anchor_id = f"slot{slot}{suffix}Anchor"
            layer_id = f"slot{slot}{suffix}Layer"
            key = f"slot{slot}Action{suffix}"
            assert anchors[anchor_id]["layerId"] == layer_id
            assert anchors[anchor_id]["role"] == role
            assert anchors[anchor_id]["visibleStates"] == ["all"]
            assert ownership[layer_id]["element"] == anchor_id
            assert ownership[layer_id]["owner"] == "DYNAMIC"
            assert ownership[layer_id]["contentKey"] == key
            assert layers[layer_id]["visibleStates"] == ["all"]
    for role, suffix in (("glyph", "Glyph"), ("text", "Label")):
        anchor_id = f"selected{suffix}Anchor"
        layer_id = f"selected{suffix}Layer"
        assert anchors[anchor_id]["layerId"] == layer_id
        assert anchors[anchor_id]["role"] == role
        assert anchors[anchor_id]["visibleStates"] == ["selected"]
        assert ownership[layer_id]["element"] == anchor_id
        assert ownership[layer_id]["contentKey"] == f"selectedAction{suffix}"

    role = manifest["glyphs"]["roles"]["darkFantasyAction"]
    assert [x["type"] for x in role["keyboard"]["sources"]] == ["runtimeSymbol"]
    assert [x["type"] for x in role["keyboardShortcut"]["sources"]] == ["runtimeSymbol"]
    assert role["keyboard"]["sources"][0]["symbolSet"] == "leftpad-ds4"
    assert role["keyboardShortcut"]["sources"][0]["symbolSet"] == "leftpad-ds4"
    assert [x["type"] for x in role["ds4"]["sources"]] == ["runtimeSymbol", "text"]
    assert [x["type"] for x in role["genericAction"]["sources"]] == ["runtimeSymbol", "text"]
    assert role["ds4"]["sources"][0]["symbolSet"] == "leftpad-ds4"
    assert role["genericAction"]["sources"][0]["symbolSet"] == "leftpad-ds4"

    states: dict[str, object] = {}
    for name in STATE_NAMES:
        path = PACKAGE / "states" / f"{name}.png"
        with Image.open(path) as source:
            source.verify()
        image = Image.open(path).convert("RGBA")
        assert image.size == (1254, 1254)
        array = np.asarray(image, dtype=np.uint8)
        transparent = array[..., 3] == 0
        assert np.all(array[..., :3][transparent] == 0)
        expected = manifest["assetHashes"][f"states/{name}.png"]
        assert sha256(path) == expected
        states[name] = {
            "sha256": expected,
            "contentBounds": list(image.getbbox() or ()),
            "transparentPixels": int(transparent.sum()),
            "partialAlphaPixels": int(((array[..., 3] > 0) & (array[..., 3] < 255)).sum()),
        }

    runtime: dict[str, object] = {}
    runtime_images: dict[str, Image.Image] = {}
    for name in RUNTIME_NAMES:
        path = REVIEW / f"runtime-200pct-set-a-{name}.png"
        image = Image.open(path).convert("RGBA")
        assert image.size == (840, 840)
        array = np.asarray(image, dtype=np.uint8)
        assert [int(array[0, 0, 3]), int(array[-1, -1, 3])] == [0, 0]
        runtime[name] = {"sha256": sha256(path), "size": [840, 840]}
        runtime_images[name] = image

    report_path = WORKING / "runtime-report.json"
    runtime_report = json.loads(report_path.read_text(encoding="utf-8"))
    assert runtime_report["layerCount"] == 19 and runtime_report["anchorCount"] == 18
    metrics = runtime_report["runtime"]
    assert metrics["decodedAssetsBeforeAfter"][0] == metrics["decodedAssetsBeforeAfter"][1]
    assert metrics["authoredLayersBeforeAfter"][0] == metrics["authoredLayersBeforeAfter"][1]
    assert metrics["dynamicBuildsBeforeAfter"][1] == metrics["dynamicBuildsBeforeAfter"][0] + 1
    assert metrics["mappingRebuildsBeforeAfter"][1] == metrics["mappingRebuildsBeforeAfter"][0] + 1
    assert not metrics["idleDynamicLayersDraw"]["slot5Glyph"]
    assert not metrics["idleDynamicLayersDraw"]["slot5Label"]
    assert not metrics["selectedDynamicLayersDraw"]["selected-5Glyph"]
    assert not metrics["selectedDynamicLayersDraw"]["selected-5Label"]
    for key in ("slot1Glyph", "slot2Glyph", "slot4Glyph", "slot7Glyph"):
        assert not metrics["idleDynamicLayersDraw"][key]
    for key in ("slot3Glyph", "slot6Glyph", "slot8Glyph"):
        assert metrics["idleDynamicLayersDraw"][key]
    for key in ("selected-1Glyph", "selected-2Glyph"):
        assert not metrics["selectedDynamicLayersDraw"][key]
    for key in ("selected-3Glyph", "selected-8Glyph"):
        assert metrics["selectedDynamicLayersDraw"][key]

    set_b_f2 = Image.open(REVIEW / "runtime-200pct-set-b-selected-1.png").convert("RGBA")
    items = [
        ("Spike 01 selected-1", Image.open(SPIKE01 / "states" / "selected-1.png")),
        ("Spike 02 idle / Set A", runtime_images["idle"]),
        ("Spike 02 selected-1 / E", runtime_images["selected-1"]),
        ("Spike 02 selected-2 / Ctrl+Shift+K", runtime_images["selected-2"]),
        ("Spike 02 selected-3 / CROSS", runtime_images["selected-3"]),
        ("Spike 02 selected-5 / empty", runtime_images["selected-5"]),
        ("Spike 02 selected-8 / D-Pad Down", runtime_images["selected-8"]),
        ("Spike 02 Set B selected-1 / F2", set_b_f2),
    ]
    sheet(items, REVIEW / "spike02-review-sheet.png")

    details_dir = REVIEW / "details"
    details_dir.mkdir(parents=True, exist_ok=True)
    detail_specs = [
        ("Slot 1 / E", "slot1-e", runtime_images["idle"], union_bounds(
            anchor_bounds(manifest, "slot1GlyphAnchor"), anchor_bounds(manifest, "slot1LabelAnchor"))),
        ("Slot 2 / Ctrl+Shift+K", "slot2-shortcut", runtime_images["idle"], union_bounds(
            anchor_bounds(manifest, "slot2GlyphAnchor"), anchor_bounds(manifest, "slot2LabelAnchor"))),
        ("Slot 3 / CROSS", "slot3-cross", runtime_images["idle"], union_bounds(
            anchor_bounds(manifest, "slot3GlyphAnchor"), anchor_bounds(manifest, "slot3LabelAnchor"))),
        ("Slot 7 / F1", "slot7-f1", runtime_images["idle"], union_bounds(
            anchor_bounds(manifest, "slot7GlyphAnchor"), anchor_bounds(manifest, "slot7LabelAnchor"))),
        ("Slot 4 / Tab", "slot4-tab", runtime_images["idle"], union_bounds(
            anchor_bounds(manifest, "slot4GlyphAnchor"), anchor_bounds(manifest, "slot4LabelAnchor"))),
        ("Central / E", "central-e", runtime_images["selected-1"], union_bounds(
            anchor_bounds(manifest, "selectedGlyphAnchor"), anchor_bounds(manifest, "selectedLabelAnchor"))),
        ("Central / F2 (Set B)", "central-f2-set-b", set_b_f2, union_bounds(
            anchor_bounds(manifest, "selectedGlyphAnchor"), anchor_bounds(manifest, "selectedLabelAnchor"))),
        ("Central / Ctrl+Shift+K", "central-shortcut", runtime_images["selected-2"], union_bounds(
            anchor_bounds(manifest, "selectedGlyphAnchor"), anchor_bounds(manifest, "selectedLabelAnchor"))),
        ("Central / CROSS", "central-cross", runtime_images["selected-3"], union_bounds(
            anchor_bounds(manifest, "selectedGlyphAnchor"), anchor_bounds(manifest, "selectedLabelAnchor"))),
        ("Central / D-Pad Down", "central-dpad-down", runtime_images["selected-8"], union_bounds(
            anchor_bounds(manifest, "selectedGlyphAnchor"), anchor_bounds(manifest, "selectedLabelAnchor"))),
    ]
    detail_items: list[tuple[str, Image.Image]] = []
    for title, slug, image, bounds in detail_specs:
        crop = runtime_crop(image, bounds)
        crop.save(details_dir / f"{slug}-1x.png", optimize=True)
        detail_items.append((title, crop))
    detail_sheet(detail_items, REVIEW / "dynamic-content-detail-review.png")

    variants = composite_variants(runtime_images["selected-1"])
    for name, image in variants.items():
        image.save(REVIEW / f"runtime-selected-1-on-{name}.png", optimize=True)
    sheet([(name, image) for name, image in variants.items()], REVIEW / "checkerboard-review.png")

    report = {
        "result": "PASS",
        "states": states,
        "layerCount": len(manifest["layers"]),
        "anchorCount": len(manifest["dynamicAnchors"]),
        "assetHashClosure": "exact",
        "alphaZeroRgbResidue": 0,
        "runtime200Percent": runtime,
        "mappingRebuild": "PASS",
        "authoredDecoderReuse": "PASS",
        "noneSlot5": "NO DRAW",
        "keyboardGlyphOwnership": "LABEL ONLY; GLYPH SOURCE INCAPABLE/NO DRAW",
        "ds4GlyphOwnership": "RUNTIME SYMBOL + LABEL",
        "reviewSheet": "review/spike02-review-sheet.png",
        "dynamicContentDetailReview": "review/dynamic-content-detail-review.png",
        "oneToOneDetailCrops": len(detail_specs),
        "checkerboardReview": "review/checkerboard-review.png",
    }
    (WORKING / "verification-report.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8", newline="\n"
    )
    print(json.dumps({
        "result": "SPIKE02 VERIFICATION PASS",
        "states": 9,
        "layers": 19,
        "anchors": 18,
        "runtimeCaptures": len(runtime),
        "alphaZeroRgbResidue": 0,
    }, indent=2))


if __name__ == "__main__":
    main()
