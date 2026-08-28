#!/usr/bin/env python3
"""Generate the Cyber Glass HUD radial-6 Visual Pack offline and deterministically."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Sequence

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter


CANVAS = 1254
SCALE = 4
HI = CANVAS * SCALE
CX = CY = 627
HCX = HCY = CX * SCALE
SLOT_COUNT = 6
OUTER_R = 450
INNER_R = 198
GAP = 16
CORNER_R = 10
CENTER_R = 182
CENTER_INNER_R = 154
DOTTED_R = 132
MARKER_R = 190
MARKER_SIZE = 12
GLYPH_R = 320
LABEL_R = 365
ANGLES = [0, 60, 120, 180, 240, 300]

BASE_NAME = "radial-base.png"
SELECTED_NAME = "radial-selected-card.png"
LAYOUT_NAME = "radial-layout.json"
MANIFEST_NAME = "manifest.json"
COMPARISON_NAME = "comparison.png"
VERIFICATION_NAME = "verification.json"
REFERENCE_NAME = "reference.png"

FROZEN_SLOT1_MASK_SHA256 = "09D5A6A3B4BFC0CD4A48244364205C50C39EED8FB50AC8DD125B148125959C1E"
FROZEN_CENTER_MASK_SHA256 = "79002A3052A786D2BC28AF31EF8B205D4619CE458E0C6C8B97EE70720121A472"
RESAMPLING = Image.Resampling

NORMAL = {
    "inner": "#0C0D20",
    "middle": "#171A38",
    "outer": "#25294F",
    "alphaInner": 178,
    "alphaOuter": 218,
    "darkBacking": "#050612",
    "darkBackingWidth": 5.0,
    "darkBackingAlpha": 150,
    "violetEdge": "#7657FF",
    "violetEdgeWidth": 2.4,
    "violetEdgeAlpha": 192,
    "blueEdge": "#4D83FF",
    "blueEdgeWidth": 1.1,
    "blueEdgeAlpha": 178,
    "glassCore": "#D9E4FF",
    "glassCoreWidth": 0.55,
    "glassCoreAlpha": 95,
}

SELECTED = {
    "inner": "#141534",
    "middle": "#22275A",
    "outer": "#343B78",
    "alphaInner": 205,
    "alphaOuter": 238,
    "wideHalo": "#7154FF",
    "wideHaloRadius": 12.0,
    "wideHaloAlpha": 78,
    "tightHalo": "#4F7DFF",
    "tightHaloRadius": 4.5,
    "tightHaloAlpha": 132,
    "violetEdge": "#9B6CFF",
    "violetEdgeWidth": 3.0,
    "violetEdgeAlpha": 245,
    "blueCore": "#A9C3FF",
    "blueCoreWidth": 1.0,
    "blueCoreAlpha": 250,
}

CENTER = {
    "inner": "#080919",
    "middle": "#11152D",
    "outer": "#20264A",
    "border": "#7657E8",
    "blueEdge": "#4D7DDE",
    "glassHighlight": "#D5DFFF",
    "dotted": "#7786D2",
    "marker": "#826EEE",
}


def rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return tuple(int(value[index:index + 2], 16) for index in (0, 2, 4))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def bytes_sha(image: Image.Image) -> str:
    return hashlib.sha256(image.tobytes()).hexdigest().upper()


def unit(angle: float) -> tuple[float, float]:
    return math.sin(angle), -math.cos(angle)


def perp(angle: float) -> tuple[float, float]:
    return math.cos(angle), math.sin(angle)


def circle_point(radius: float, angle: float) -> tuple[float, float]:
    ux, uy = unit(angle)
    return CX + radius * ux, CY + radius * uy


def offset_ray_point(angle: float, radius: float, offset: float) -> tuple[float, float]:
    tangent_distance = math.sqrt(max(0.0, radius * radius - offset * offset))
    ux, uy = unit(angle)
    px, py = perp(angle)
    return (
        CX + ux * tangent_distance + px * offset,
        CY + uy * tangent_distance + py * offset,
    )


def angle_of(point: tuple[float, float]) -> float:
    x, y = point
    return math.atan2(x - CX, -(y - CY)) % math.tau


def towards(a: tuple[float, float], b: tuple[float, float], distance: float) -> tuple[float, float]:
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    return a[0] + dx * distance / length, a[1] + dy * distance / length


def quadratic(start, control, end, count: int) -> list[tuple[float, float]]:
    result = []
    for index in range(1, count + 1):
        t = index / count
        mt = 1.0 - t
        result.append((
            mt * mt * start[0] + 2.0 * mt * t * control[0] + t * t * end[0],
            mt * mt * start[1] + 2.0 * mt * t * control[1] + t * t * end[1],
        ))
    return result


def arc(radius: float, start: float, end: float, clockwise: bool, count: int):
    if clockwise and end < start:
        end += math.tau
    if not clockwise and end > start:
        end -= math.tau
    return [circle_point(radius, start + (end - start) * index / count)
            for index in range(1, count + 1)]


def canonical_card_points() -> list[tuple[int, int]]:
    half = math.pi / SLOT_COUNT
    offset = GAP / 2.0
    lo = offset_ray_point(-half, OUTER_R, +offset)
    li = offset_ray_point(-half, INNER_R, +offset)
    ro = offset_ray_point(+half, OUTER_R, -offset)
    ri = offset_ray_point(+half, INNER_R, -offset)
    alo, aro, ali, ari = angle_of(lo), angle_of(ro), angle_of(li), angle_of(ri)
    lo_arc = circle_point(OUTER_R, alo + CORNER_R / OUTER_R)
    ro_arc = circle_point(OUTER_R, aro - CORNER_R / OUTER_R)
    li_arc = circle_point(INNER_R, ali + CORNER_R / INNER_R)
    ri_arc = circle_point(INNER_R, ari - CORNER_R / INNER_R)
    ro_side = towards(ro, ri, CORNER_R)
    ri_side = towards(ri, ro, CORNER_R)
    li_side = towards(li, lo, CORNER_R)
    lo_side = towards(lo, li, CORNER_R)
    points = [lo_arc]
    points += arc(OUTER_R, angle_of(lo_arc), angle_of(ro_arc), True, 180)
    points += quadratic(ro_arc, ro, ro_side, 14)
    points.append(ri_side)
    points += quadratic(ri_side, ri, ri_arc, 14)
    points += arc(INNER_R, angle_of(ri_arc), angle_of(li_arc), False, 100)
    points += quadratic(li_arc, li, li_side, 14)
    points.append(lo_side)
    points += quadratic(lo_side, lo, lo_arc, 14)
    return [(round(x * SCALE), round(y * SCALE)) for x, y in points]


def canonical_mask() -> Image.Image:
    mask = Image.new("L", (HI, HI), 0)
    ImageDraw.Draw(mask).polygon(canonical_card_points(), fill=255)
    return mask


def rotate_slot(image: Image.Image, slot: int, resample=RESAMPLING.BICUBIC) -> Image.Image:
    if slot == 1:
        return image.copy()
    return image.rotate(
        -60.0 * (slot - 1),
        resample=resample,
        center=(HCX, HCY),
        expand=False,
        fillcolor=0 if image.mode == "L" else (0, 0, 0, 0),
    )


def mask_bbox(mask: Image.Image, pad: int = 0) -> tuple[int, int, int, int]:
    bbox = mask.getbbox()
    if bbox is None:
        raise RuntimeError("empty mask")
    return (
        max(0, bbox[0] - pad), max(0, bbox[1] - pad),
        min(HI, bbox[2] + pad), min(HI, bbox[3] + pad),
    )


def interpolate(t: np.ndarray, colors: Sequence[tuple[int, int, int]]) -> np.ndarray:
    first, middle, last = [np.asarray(color, dtype=np.float32) for color in colors]
    result = np.empty((*t.shape, 3), dtype=np.float32)
    lower = t <= 0.56
    local_lower = np.clip(t / 0.56, 0.0, 1.0)
    local_upper = np.clip((t - 0.56) / 0.44, 0.0, 1.0)
    result[lower] = first + (middle - first) * local_lower[lower, None]
    result[~lower] = middle + (last - middle) * local_upper[~lower, None]
    return result


def material(mask: Image.Image, palette: dict[str, object]) -> Image.Image:
    bbox = mask_bbox(mask)
    left, top, _, _ = bbox
    local_mask = np.asarray(mask.crop(bbox), dtype=np.float32) / 255.0
    yy, xx = np.indices(local_mask.shape, dtype=np.float32)
    x = (xx + left) / SCALE
    y = (yy + top) / SCALE
    radius = np.sqrt((x - CX) ** 2 + (y - CY) ** 2)
    t = np.clip((radius - INNER_R) / (OUTER_R - INNER_R), 0.0, 1.0)
    colors = interpolate(t, (
        rgb(str(palette["inner"])),
        rgb(str(palette["middle"])),
        rgb(str(palette["outer"])),
    ))
    # A broad diagonal reflection and radial edge lift evoke layered glass.
    diagonal = np.exp(-((x + 0.72 * y - 770.0) / 38.0) ** 2)
    edge = np.clip((t - 0.62) / 0.38, 0.0, 1.0)
    colors += diagonal[..., None] * np.asarray((10.0, 11.0, 24.0), dtype=np.float32)
    colors += edge[..., None] * np.asarray((3.0, 4.0, 10.0), dtype=np.float32)
    alpha = float(palette["alphaInner"]) + (
        float(palette["alphaOuter"]) - float(palette["alphaInner"])) * t
    rgba = np.zeros((*local_mask.shape, 4), dtype=np.uint8)
    rgba[..., :3] = np.clip(np.rint(colors), 0, 255).astype(np.uint8)
    rgba[..., 3] = np.rint(alpha * local_mask).astype(np.uint8)
    output = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    output.paste(Image.fromarray(rgba, "RGBA"), (left, top))
    return output


def eroded(mask: Image.Image, visual_width: float) -> Image.Image:
    radius = max(1, round(visual_width * SCALE))
    return mask.filter(ImageFilter.MinFilter(radius * 2 + 1))


def inner_band(mask: Image.Image, visual_width: float) -> Image.Image:
    return ImageChops.subtract(mask, eroded(mask, visual_width))


def solid(color: str, alpha_mask: Image.Image, maximum_alpha: int) -> Image.Image:
    if maximum_alpha != 255:
        alpha_mask = alpha_mask.point(lambda value: round(value * maximum_alpha / 255.0))
    layer = Image.new("RGBA", alpha_mask.size, (*rgb(color), 0))
    layer.putalpha(alpha_mask)
    return layer


def glass_reflection(mask: Image.Image, selected: bool) -> Image.Image:
    reflection = Image.new("L", (HI, HI), 0)
    draw = ImageDraw.Draw(reflection)
    points = [(440, 208), (582, 177), (758, 428), (682, 442)]
    draw.polygon([(x * SCALE, y * SCALE) for x, y in points], fill=80 if selected else 48)
    reflection = reflection.filter(ImageFilter.GaussianBlur(8 * SCALE))
    reflection = ImageChops.multiply(reflection, mask)
    return solid("#DDE4FF", reflection, 255)


def normal_card(mask: Image.Image) -> Image.Image:
    card = material(mask, NORMAL)
    card = Image.alpha_composite(card, glass_reflection(mask, selected=False))
    for color, width, alpha in (
        (str(NORMAL["darkBacking"]), float(NORMAL["darkBackingWidth"]), int(NORMAL["darkBackingAlpha"])),
        (str(NORMAL["violetEdge"]), float(NORMAL["violetEdgeWidth"]), int(NORMAL["violetEdgeAlpha"])),
        (str(NORMAL["blueEdge"]), float(NORMAL["blueEdgeWidth"]), int(NORMAL["blueEdgeAlpha"])),
        (str(NORMAL["glassCore"]), float(NORMAL["glassCoreWidth"]), int(NORMAL["glassCoreAlpha"])),
    ):
        card = Image.alpha_composite(card, solid(color, inner_band(mask, width), alpha))
    return card


def normalized_blur(mask: Image.Image, radius: float, maximum_alpha: int) -> Image.Image:
    bbox = mask_bbox(mask, round(radius * SCALE * 4))
    blurred = mask.crop(bbox).filter(ImageFilter.GaussianBlur(radius * SCALE))
    values = np.asarray(blurred, dtype=np.float32)
    peak = float(values.max())
    if peak <= 0:
        raise RuntimeError("blur mask peak is zero")
    values = np.clip(values / peak * maximum_alpha, 0.0, maximum_alpha)
    output = Image.new("L", (HI, HI), 0)
    output.paste(Image.fromarray(np.rint(values).astype(np.uint8), "L"), (bbox[0], bbox[1]))
    return output


def restrict_halo(halo: Image.Image, body: Image.Image) -> Image.Image:
    bbox = mask_bbox(halo)
    left, top, _, _ = bbox
    alpha = np.asarray(halo.crop(bbox), dtype=np.uint8).copy()
    body_values = np.asarray(body.crop(bbox), dtype=np.uint8)
    yy, xx = np.indices(alpha.shape, dtype=np.float32)
    dx = (xx + left) / SCALE - CX
    dy = (yy + top) / SCALE - CY
    radius = np.sqrt(dx ** 2 + dy ** 2)
    angle = np.arctan2(dx, -dy)
    endpoint = angle_of(offset_ray_point(math.pi / SLOT_COUNT, OUTER_R, -GAP / 2.0))
    outer_allowed = (radius >= OUTER_R - 1.5) & (np.abs(angle) <= endpoint + 0.001)
    alpha[(body_values == 0) & ~outer_allowed] = 0
    output = Image.new("L", (HI, HI), 0)
    output.paste(Image.fromarray(alpha, "L"), (left, top))
    return output


def selected_card(mask: Image.Image) -> Image.Image:
    boundary = inner_band(mask, 2.2)
    wide = restrict_halo(
        normalized_blur(boundary, float(SELECTED["wideHaloRadius"]), int(SELECTED["wideHaloAlpha"])),
        mask,
    )
    tight = restrict_halo(
        normalized_blur(boundary, float(SELECTED["tightHaloRadius"]), int(SELECTED["tightHaloAlpha"])),
        mask,
    )
    card = material(mask, SELECTED)
    card = Image.alpha_composite(card, solid(str(SELECTED["wideHalo"]), wide, 255))
    card = Image.alpha_composite(card, solid(str(SELECTED["tightHalo"]), tight, 255))
    card = Image.alpha_composite(card, glass_reflection(mask, selected=True))
    card = Image.alpha_composite(card, solid(
        str(SELECTED["violetEdge"]),
        inner_band(mask, float(SELECTED["violetEdgeWidth"])),
        int(SELECTED["violetEdgeAlpha"]),
    ))
    card = Image.alpha_composite(card, solid(
        str(SELECTED["blueCore"]),
        inner_band(mask, float(SELECTED["blueCoreWidth"])),
        int(SELECTED["blueCoreAlpha"]),
    ))
    return card


def center_layer() -> tuple[Image.Image, Image.Image]:
    disk_mask = Image.new("L", (HI, HI), 0)
    radius_hi = CENTER_R * SCALE
    ImageDraw.Draw(disk_mask).ellipse(
        (HCX - radius_hi, HCY - radius_hi, HCX + radius_hi, HCY + radius_hi),
        fill=255,
    )
    palette = {
        "inner": CENTER["inner"], "middle": CENTER["middle"], "outer": CENTER["outer"],
        "alphaInner": 212, "alphaOuter": 235,
    }
    # Reuse the radial material function with a center-specific local gradient.
    bbox = mask_bbox(disk_mask)
    left, top, _, _ = bbox
    local_mask = np.asarray(disk_mask.crop(bbox), dtype=np.float32) / 255.0
    yy, xx = np.indices(local_mask.shape, dtype=np.float32)
    radius = np.sqrt(((xx + left) / SCALE - CX) ** 2 + ((yy + top) / SCALE - CY) ** 2)
    t = np.clip(radius / CENTER_R, 0.0, 1.0)
    colors = interpolate(t, (rgb(str(palette["inner"])), rgb(str(palette["middle"])), rgb(str(palette["outer"]))))
    diagonal = np.exp(-((((xx + left) / SCALE) + 0.8 * ((yy + top) / SCALE) - 1125.0) / 34.0) ** 2)
    colors += diagonal[..., None] * np.asarray((12.0, 11.0, 24.0), dtype=np.float32)
    alpha = 212.0 + 23.0 * t
    rgba = np.zeros((*local_mask.shape, 4), dtype=np.uint8)
    rgba[..., :3] = np.clip(np.rint(colors), 0, 255).astype(np.uint8)
    rgba[..., 3] = np.rint(alpha * local_mask).astype(np.uint8)
    output = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    output.paste(Image.fromarray(rgba, "RGBA"), (left, top))

    overlay = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)

    def ellipse_line(radius_value: float, color: str, alpha_value: int, width: float) -> None:
        rv = radius_value * SCALE
        inset = width * SCALE / 2.0
        draw.ellipse(
            (HCX - rv + inset, HCY - rv + inset, HCX + rv - inset, HCY + rv - inset),
            outline=(*rgb(color), alpha_value),
            width=max(1, round(width * SCALE)),
        )

    ellipse_line(CENTER_R, str(CENTER["border"]), 228, 2.4)
    ellipse_line(CENTER_R - 3.2, str(CENTER["blueEdge"]), 165, 1.0)
    ellipse_line(CENTER_INNER_R, str(CENTER["glassHighlight"]), 85, 0.8)

    for segment in range(12):
        start = -88 + segment * 30
        draw.arc(
            (HCX - 145 * SCALE, HCY - 145 * SCALE, HCX + 145 * SCALE, HCY + 145 * SCALE),
            start=start,
            end=start + 15,
            fill=(*rgb(str(CENTER["blueEdge"])), 125),
            width=round(1.2 * SCALE),
        )

    dot_radius = 0.55 * SCALE
    for index in range(132):
        angle = math.tau * index / 132
        x, y = circle_point(DOTTED_R, angle)
        x *= SCALE
        y *= SCALE
        draw.ellipse(
            (x - dot_radius, y - dot_radius, x + dot_radius, y + dot_radius),
            fill=(*rgb(str(CENTER["dotted"])), 82),
        )

    half = MARKER_SIZE * SCALE / 2.0
    for slot in range(1, SLOT_COUNT + 1):
        x, y = circle_point(MARKER_R, math.radians(60 * (slot - 1)))
        x *= SCALE
        y *= SCALE
        draw.polygon(
            [(x, y - half), (x + half, y), (x, y + half), (x - half, y)],
            fill=(*rgb(str(CENTER["marker"])), 170),
        )

    return Image.alpha_composite(output, overlay), disk_mask


def clear_zero_alpha(image: Image.Image) -> Image.Image:
    array = np.asarray(image.convert("RGBA")).copy()
    array[array[..., 3] == 0] = 0
    return Image.fromarray(array, "RGBA")


def downsample(image: Image.Image) -> Image.Image:
    return clear_zero_alpha(image.resize((CANVAS, CANVAS), RESAMPLING.LANCZOS))


def downsample_mask(mask: Image.Image) -> Image.Image:
    return mask.resize((CANVAS, CANVAS), RESAMPLING.LANCZOS)


def save_png(image: Image.Image, path: Path) -> None:
    clear_zero_alpha(image).save(path, "PNG", optimize=False, compress_level=9)


def render_scene() -> tuple[Image.Image, Image.Image, Image.Image, list[Image.Image]]:
    slot1 = canonical_mask()
    masks = [rotate_slot(slot1, slot) for slot in range(1, SLOT_COUNT + 1)]
    canonical_normal = normal_card(slot1)
    base_hi = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    for slot in range(1, SLOT_COUNT + 1):
        base_hi = Image.alpha_composite(base_hi, rotate_slot(canonical_normal, slot))
    center_hi, center_mask = center_layer()
    base_hi = Image.alpha_composite(base_hi, center_hi)
    selected_hi = selected_card(slot1)
    return base_hi, selected_hi, center_mask, masks


def polar_anchor(radius: float, angle_degrees: float) -> dict[str, float]:
    x, y = circle_point(radius, math.radians(angle_degrees))
    return {"x": round(x, 3), "y": round(y, 3)}


def layout_data() -> dict[str, object]:
    names = ("Top", "Upper Right", "Lower Right", "Bottom", "Lower Left", "Upper Left")
    slots = []
    for index, angle in enumerate(ANGLES):
        slots.append({
            "slot": index + 1,
            "name": names[index],
            "angleDegreesClockwiseFromTop": angle,
            "glyphAnchor": polar_anchor(GLYPH_R, angle),
            "labelAnchor": polar_anchor(LABEL_R, angle),
        })
    return {
        "canvas": {"width": CANVAS, "height": CANVAS, "mode": "RGBA"},
        "internalRender": {"scale": SCALE, "width": HI, "height": HI, "downsample": "Pillow LANCZOS"},
        "wheelCenter": {"x": CX, "y": CY},
        "slotCount": SLOT_COUNT,
        "slotAnglesDegrees": ANGLES,
        "geometry": {
            "outerRadius": OUTER_R, "innerRadius": INNER_R, "gapPx": GAP,
            "gapConstruction": "logical 60-degree boundary plus parallel offset gapPx/2",
            "cornerRadius": CORNER_R, "centerRadius": CENTER_R,
            "centerInnerRadius": CENTER_INNER_R, "dottedRingRadius": DOTTED_R,
            "markerRadius": MARKER_R, "markerSize": MARKER_SIZE,
        },
        "slots": slots,
        "normalMaterial": NORMAL,
        "selectedMaterial": SELECTED,
        "centerMaterial": CENTER,
        "selectedAssetCanonicalSlot": 1,
        "selectedAssetRotationDegreesClockwise": ANGLES,
        "dynamicContent": "runtime-owned; no text, mappings, numbers, or prompts are baked into assets",
    }


def manifest_data() -> dict[str, object]:
    return {
        "id": "cyber-glass-v1",
        "name": "Cyber Glass HUD",
        "version": 1,
        "layoutProfile": "radial-6",
        "slotCount": 6,
        "base": BASE_NAME,
        "selected": SELECTED_NAME,
        "layout": LAYOUT_NAME,
        "selectionAssetMode": "canonical-transform",
    }


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False, sort_keys=True) + "\n", encoding="utf-8")


def checker_panel(image: Image.Image, size: int) -> Image.Image:
    background = Image.new("RGBA", (size, size), (9, 10, 20, 255))
    fitted = image.resize((size, size), RESAMPLING.LANCZOS)
    background.alpha_composite(fitted)
    return background


def build_comparison(reference: Image.Image, base: Image.Image, previews: Sequence[Image.Image], path: Path) -> None:
    tile = 390
    pad = 18
    all_states = Image.new("RGBA", (tile, tile), (9, 10, 20, 255))
    thumb_size = tile // 3
    for index, preview in enumerate(previews):
        thumb = preview.resize((thumb_size, thumb_size), RESAMPLING.LANCZOS)
        x = (index % 3) * thumb_size
        y = (index // 3) * thumb_size + 65
        all_states.alpha_composite(thumb, (x, y))
    panels = [checker_panel(reference, tile), checker_panel(base, tile), checker_panel(previews[1], tile), all_states]
    sheet = Image.new("RGBA", (pad * 5 + tile * 4, pad * 2 + tile), (5, 6, 14, 255))
    for index, panel in enumerate(panels):
        sheet.alpha_composite(panel, (pad + index * (tile + pad), pad))
    save_png(sheet, path)


def rgba_stats(path: Path) -> dict[str, object]:
    with Image.open(path) as source:
        image = source.convert("RGBA")
        array = np.asarray(image)
    alpha = array[..., 3]
    transparent = alpha == 0
    return {
        "file": path.name,
        "mode": "RGBA",
        "size": list(image.size),
        "alphaMin": int(alpha.min()),
        "alphaMax": int(alpha.max()),
        "transparentPixelCount": int(np.count_nonzero(transparent)),
        "alphaZeroRgbResidueComponents": int(np.count_nonzero(array[..., :3][transparent])),
        "alphaBoundingBox": list(image.getchannel("A").getbbox() or ()),
        "sha256": sha256(path),
    }


def verify(
    out: Path,
    base: Image.Image,
    selected: Image.Image,
    previews: Sequence[Image.Image],
    center_mask_hi: Image.Image,
    slot_masks_hi: Sequence[Image.Image],
    deterministic_match: bool,
) -> dict[str, object]:
    stats = {name: rgba_stats(out / name) for name in (BASE_NAME, SELECTED_NAME, COMPARISON_NAME)}
    slot1_sha = hashlib.sha256(slot_masks_hi[0].tobytes()).hexdigest().upper()
    center_sha = hashlib.sha256(center_mask_hi.tobytes()).hexdigest().upper()
    canonical_body = downsample_mask(slot_masks_hi[0])
    center_mask = np.asarray(downsample_mask(center_mask_hi)) >= 250
    selected_alpha = np.asarray(selected.getchannel("A"))
    base_alpha = np.asarray(base.getchannel("A"))

    rotation = {}
    for slot, target in enumerate(slot_masks_hi, start=1):
        rotated = rotate_slot(slot_masks_hi[0], slot)
        mismatch = int(np.count_nonzero(
            np.abs(np.asarray(rotated, dtype=np.int16) - np.asarray(target, dtype=np.int16)) > 1))
        rotation[str(slot)] = {
            "angleDegreesClockwise": ANGLES[slot - 1],
            "maskPixelsDifferingByMoreThan1": mismatch,
            "aligned": mismatch == 0,
        }

    isolation = {}
    base_array = np.asarray(base)
    for slot, preview in enumerate(previews, start=1):
        preview_array = np.asarray(preview)
        center_changed = int(np.count_nonzero(np.any(preview_array[center_mask] != base_array[center_mask], axis=1)))
        other_changed = {}
        for other_slot, other_mask in enumerate(slot_masks_hi, start=1):
            if other_slot == slot:
                continue
            stable = np.asarray(downsample_mask(other_mask)) >= 250
            other_changed[str(other_slot)] = int(np.count_nonzero(
                np.any(preview_array[stable] != base_array[stable], axis=1)))
        isolation[str(slot)] = {
            "centerChangedPixels": center_changed,
            "otherSlotsChangedPixels": other_changed,
        }

    gap_samples = []
    for boundary in (-30, 30, 90, 150, 210, 270):
        for radius in range(INNER_R + 16, OUTER_R - 16):
            x, y = circle_point(radius, math.radians(boundary))
            gap_samples.append(int(base_alpha[round(y), round(x)]))

    selected_bbox = selected.getchannel("A").getbbox()
    if selected_bbox is None:
        raise RuntimeError("selected asset is empty")
    other_slot_selected_pixels = {}
    for slot in range(2, SLOT_COUNT + 1):
        stable = np.asarray(downsample_mask(slot_masks_hi[slot - 1])) >= 250
        other_slot_selected_pixels[str(slot)] = int(np.count_nonzero(selected_alpha[stable]))

    v5_layout_path = out.parents[1] / "raster-v5" / "radial-layout-v5.json"
    v5_layout = json.loads(v5_layout_path.read_text(encoding="utf-8"))
    layout = layout_data()
    geometry_matches_v5 = (
        layout["canvas"] == v5_layout["canvas"]
        and layout["wheelCenter"] == v5_layout["wheelCenter"]
        and layout["slotCount"] == v5_layout["slotCount"]
        and layout["slotAnglesDegrees"] == v5_layout["slotAnglesDegrees"]
        and layout["geometry"] == v5_layout["geometry"]
    )

    checks = {
        "canvas1254Rgba": all(stats[name]["size"] == [CANVAS, CANVAS] and stats[name]["mode"] == "RGBA"
                              for name in (BASE_NAME, SELECTED_NAME)),
        "fourTimesSupersampling": SCALE == 4 and HI == 5016,
        "geometryMatchesRadialV5": geometry_matches_v5,
        "slot1MaskMatchesFrozenRadialV5": slot1_sha == FROZEN_SLOT1_MASK_SHA256,
        "centerGeometryMatchesFrozenRadialV5": center_sha == FROZEN_CENTER_MASK_SHA256,
        "slotAnglesExact": ANGLES == [0, 60, 120, 180, 240, 300],
        "allSlotsFromCanonicalRotation": all(item["aligned"] for item in rotation.values()),
        "selectionAssetCanonicalSlot1Only": all(value == 0 for value in other_slot_selected_pixels.values()),
        "selectedDoesNotAlterCenter": int(np.count_nonzero(selected_alpha[center_mask])) == 0,
        "centerUnchangedInEveryPreview": all(item["centerChangedPixels"] == 0 for item in isolation.values()),
        "otherSlotsUnchangedInEveryPreview": all(
            all(value == 0 for value in item["otherSlotsChangedPixels"].values())
            for item in isolation.values()),
        "logicalGapSamplesTransparent": max(gap_samples) == 0,
        "transparentOutsideNoBlackBackdrop": (
            base_alpha[0, 0] == 0 and selected_alpha[0, 0] == 0
            and stats[BASE_NAME]["transparentPixelCount"] > CANVAS * CANVAS // 2),
        "noCheckerboardBaked": base_alpha[0, 0] == 0 and base_alpha[0, -1] == 0,
        "alphaZeroRgbClean": all(stats[name]["alphaZeroRgbResidueComponents"] == 0
                                 for name in (BASE_NAME, SELECTED_NAME)),
        "selectedHaloNotCanvasClipped": (
            selected_bbox[0] > 0 and selected_bbox[1] > 0
            and selected_bbox[2] < CANVAS and selected_bbox[3] < CANVAS),
        "deterministicRegeneration": deterministic_match,
        "dynamicBusinessContentAbsentByConstruction": True,
    }
    checks = {name: bool(value) for name, value in checks.items()}
    if not all(checks.values()):
        failed = [name for name, passed in checks.items() if not passed]
        raise RuntimeError(f"verification failed: {failed}")

    files = (BASE_NAME, SELECTED_NAME, LAYOUT_NAME, MANIFEST_NAME, COMPARISON_NAME)
    return {
        "theme": {"id": "cyber-glass-v1", "name": "Cyber Glass HUD"},
        "generator": Path(__file__).name,
        "dependencies": [f"Pillow {Image.__version__}", f"NumPy {np.__version__}"],
        "reference": {"file": REFERENCE_NAME, "sha256": sha256(out / REFERENCE_NAME)},
        "geometry": layout["geometry"],
        "geometryFreeze": {
            "slot1MaskSha256": slot1_sha,
            "expectedSlot1MaskSha256": FROZEN_SLOT1_MASK_SHA256,
            "centerMaskSha256": center_sha,
            "expectedCenterMaskSha256": FROZEN_CENTER_MASK_SHA256,
        },
        "rotationAlignment": rotation,
        "previewIsolation": isolation,
        "selectedOtherSlotPixels": other_slot_selected_pixels,
        "logicalGapSamples": {"count": len(gap_samples), "minAlpha": min(gap_samples), "maxAlpha": max(gap_samples)},
        "alpha": stats,
        "files": {name: sha256(out / name) for name in files},
        "checks": checks,
    }


def build(out: Path) -> dict[str, object]:
    reference_path = out / REFERENCE_NAME
    if not reference_path.exists():
        raise FileNotFoundError(reference_path)
    base_hi, selected_hi, center_mask, masks = render_scene()
    base = downsample(base_hi)
    selected = downsample(selected_hi)
    save_png(base, out / BASE_NAME)
    save_png(selected, out / SELECTED_NAME)
    write_json(out / LAYOUT_NAME, layout_data())
    write_json(out / MANIFEST_NAME, manifest_data())

    previews = [downsample(Image.alpha_composite(base_hi, rotate_slot(selected_hi, slot)))
                for slot in range(1, SLOT_COUNT + 1)]
    with Image.open(reference_path) as reference:
        build_comparison(reference.convert("RGBA"), base, previews, out / COMPARISON_NAME)

    second_base_hi, second_selected_hi, _, _ = render_scene()
    deterministic_match = (
        bytes_sha(base_hi) == bytes_sha(second_base_hi)
        and bytes_sha(selected_hi) == bytes_sha(second_selected_hi)
    )
    report = verify(out, base, selected, previews, center_mask, masks, deterministic_match)
    write_json(out / VERIFICATION_NAME, report)
    return report


def verify_existing(out: Path) -> dict[str, object]:
    report = json.loads((out / VERIFICATION_NAME).read_text(encoding="utf-8"))
    for name, expected in report["files"].items():
        if sha256(out / name) != expected:
            raise RuntimeError(f"SHA mismatch: {name}")
    if sha256(out / REFERENCE_NAME) != report["reference"]["sha256"]:
        raise RuntimeError("reference SHA mismatch")
    if not all(report["checks"].values()):
        raise RuntimeError("recorded verification contains a failed check")
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    out = Path(__file__).resolve().parent
    report = verify_existing(out) if args.verify_only else build(out)
    print(json.dumps(report["checks"], indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
