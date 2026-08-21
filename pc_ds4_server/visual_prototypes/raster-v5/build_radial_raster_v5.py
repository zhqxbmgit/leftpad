#!/usr/bin/env python3
"""Build geometry-frozen, supersampled V5 radial HUD PNG assets.

The generator is intentionally offline and deterministic.  It creates one
canonical Slot 1 card at 4x resolution, rotates that asset for Slots 2-6,
then downsamples every formal asset with LANCZOS.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Iterable, Sequence

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

REFERENCE_NAME = "ChatGPT Image 2026年8月19日 12_55_27.png"
BASE_NAME = "radial-base-v5.png"
SELECTED_NAME = "radial-selected-card-v5.png"
NORMAL_PREVIEW_NAME = "radial-normal-preview-v5.png"
SLOT2_PREVIEW_NAME = "radial-selected-slot-2-preview-v5.png"
ALL_REVIEW_NAME = "radial-all-slots-selected-review-v5.png"
COMPARISON_NAME = "radial-v5-comparison.png"
LAYOUT_NAME = "radial-layout-v5.json"
REPORT_NAME = "radial-raster-v5-verification.json"

FROZEN_V4_SLOT1_MASK_SHA256 = "09D5A6A3B4BFC0CD4A48244364205C50C39EED8FB50AC8DD125B148125959C1E"
FROZEN_V4_CENTER_MASK_SHA256 = "79002A3052A786D2BC28AF31EF8B205D4619CE458E0C6C8B97EE70720121A472"

NORMAL = {
    "inner": "#0C0F13",
    "middle": "#14181D",
    "outer": "#1A1E24",
    "middleStop": 0.56,
    "darkBacking": "#05070A",
    "darkBackingWidth": 4.5,
    "darkBackingAlpha": 107,
    "mainBorder": "#59636E",
    "mainBorderWidth": 1.8,
    "mainBorderAlpha": 217,
    "fineHighlight": "#8996A3",
    "fineHighlightWidth": 0.6,
    "fineHighlightAlpha": 46,
}

SELECTED = {
    "inner": "#0D1824",
    "middle": "#163B5A",
    "outer": "#245A7E",
    "middleStop": 0.62,
    "wideHalo": "#59D2FF",
    "wideHaloRadius": 11.0,
    "wideHaloAlpha": 70,
    "tightHalo": "#66D8FF",
    "tightHaloRadius": 4.0,
    "tightHaloAlpha": 120,
    "mainCyan": "#58CCFF",
    "mainCyanWidth": 2.8,
    "mainCyanAlpha": 235,
    "iceCore": "#E7FAFF",
    "iceCoreWidth": 0.9,
    "iceCoreAlpha": 245,
    "wideEdgeStrength": {"outer": 1.0, "side": 0.8, "inner": 0.55},
    "tightEdgeStrength": {"outer": 1.0, "side": 0.82, "inner": 0.6},
    "illuminationWeight": {"radial": 0.75, "directional": 0.25},
}

CENTER = {
    "inner": "#070E17",
    "middle": "#0B131E",
    "outer": "#101923",
    "border": "#505B67",
    "fineHighlight": "#7E8B98",
    "innerRing": "#697687",
    "dotted": "#8695A5",
    "marker": "#8995A2",
    "selectedMarker": "#67D5FF",
}

RESAMPLING = Image.Resampling


def rgb(value: str) -> tuple[int, int, int]:
    value = value.lstrip("#")
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


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


def quadratic(
    start: tuple[float, float],
    control: tuple[float, float],
    end: tuple[float, float],
    count: int,
) -> list[tuple[float, float]]:
    points = []
    for index in range(1, count + 1):
        t = index / count
        mt = 1.0 - t
        points.append(
            (
                mt * mt * start[0] + 2.0 * mt * t * control[0] + t * t * end[0],
                mt * mt * start[1] + 2.0 * mt * t * control[1] + t * t * end[1],
            )
        )
    return points


def arc(radius: float, start: float, end: float, clockwise: bool, count: int) -> list[tuple[float, float]]:
    if clockwise and end < start:
        end += math.tau
    if not clockwise and end > start:
        end -= math.tau
    return [circle_point(radius, start + (end - start) * index / count) for index in range(1, count + 1)]


def canonical_card_points() -> list[tuple[int, int]]:
    """Return Slot 1 using the locked parallel-offset gap construction."""
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

    points: list[tuple[float, float]] = [lo_arc]
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


def rotate_slot(image: Image.Image, slot: int, resample: int = RESAMPLING.BICUBIC) -> Image.Image:
    if slot == 1:
        return image.copy()
    return image.rotate(
        -60.0 * (slot - 1),
        resample=resample,
        center=(HCX, HCY),
        expand=False,
        fillcolor=0 if image.mode == "L" else (0, 0, 0, 0),
    )


def smoothstep(value: np.ndarray) -> np.ndarray:
    value = np.clip(value, 0.0, 1.0)
    return value * value * (3.0 - 2.0 * value)


def interpolate_stops(t: np.ndarray, stops: Sequence[tuple[float, tuple[int, int, int]]]) -> np.ndarray:
    result = np.empty((*t.shape, 3), dtype=np.float32)
    for index in range(len(stops) - 1):
        p0, c0 = stops[index]
        p1, c1 = stops[index + 1]
        region = (t >= p0) & (t <= p1 if index == len(stops) - 2 else t < p1)
        local = np.clip((t - p0) / max(p1 - p0, 1e-6), 0.0, 1.0)
        c0a = np.asarray(c0, dtype=np.float32)
        c1a = np.asarray(c1, dtype=np.float32)
        result[region] = c0a + (c1a - c0a) * local[region, None]
    result[t < stops[0][0]] = stops[0][1]
    result[t > stops[-1][0]] = stops[-1][1]
    return result


def mask_bbox(mask: Image.Image, pad: int = 0) -> tuple[int, int, int, int]:
    bbox = mask.getbbox()
    if bbox is None:
        raise RuntimeError("empty mask")
    return (
        max(0, bbox[0] - pad),
        max(0, bbox[1] - pad),
        min(HI, bbox[2] + pad),
        min(HI, bbox[3] + pad),
    )


def material_layer(mask: Image.Image, selected: bool) -> Image.Image:
    bbox = mask_bbox(mask)
    left, top, right, bottom = bbox
    local_mask = np.asarray(mask.crop(bbox), dtype=np.uint8)
    yy, xx = np.indices(local_mask.shape, dtype=np.float32)
    x = (xx + left) / SCALE
    y = (yy + top) / SCALE
    radius = np.sqrt((x - CX) ** 2 + (y - CY) ** 2)
    t = np.clip((radius - INNER_R) / (OUTER_R - INNER_R), 0.0, 1.0)

    palette = SELECTED if selected else NORMAL
    middle = float(palette["middleStop"])
    colors = interpolate_stops(
        t,
        (
            (0.0, rgb(str(palette["inner"]))),
            (middle, rgb(str(palette["middle"]))),
            (1.0, rgb(str(palette["outer"]))),
        ),
    )

    if selected:
        # V5 directional illumination: 75% radial energy plus a continuous
        # 25% bias toward Slot 1's right-facing outer region.  The factor is
        # baked into the canonical asset and rotates with it for Slots 2-6.
        radial_factor = smoothstep((t - 0.18) / 0.82)
        directional_factor = smoothstep((x - (CX - 145.0)) / 290.0)
        light = 0.75 * radial_factor + 0.25 * radial_factor * directional_factor
        colors -= (1.0 - light)[..., None] * np.asarray((1.5, 5.0, 8.0), dtype=np.float32)
        colors += light[..., None] * np.asarray((0.0, 1.0, 2.0), dtype=np.float32)
        # The requested V5 stop values are treated as the material source
        # palette.  A continuous baked tone-down keeps the actual V5 output
        # darker than the accepted V4 pixels, especially near the outer arc.
        colors -= (2.5 + 4.5 * t)[..., None]
    else:
        # Smoked-glass hierarchy: low-frequency vignette, weak cool edge lift,
        # and restrained darkening at the hub-side edge.  No texture/noise.
        axial = np.exp(-((x - CX) / 205.0) ** 2).astype(np.float32)
        vignette = (1.0 - axial) * 1.4 + smoothstep((t - 0.82) / 0.18) * 0.8
        cool_lift = smoothstep((t - 0.62) / 0.38)[..., None] * np.asarray((0.2, 0.7, 1.3))
        inner_shade = ((1.0 - smoothstep(t / 0.16)) * 1.5)[..., None]
        colors = colors - vignette[..., None] - inner_shade + cool_lift

    rgba = np.zeros((*local_mask.shape, 4), dtype=np.uint8)
    rgba[..., :3] = np.clip(np.rint(colors), 0, 255).astype(np.uint8)
    rgba[..., 3] = local_mask
    output = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    output.paste(Image.fromarray(rgba, "RGBA"), (left, top))
    return output


def eroded(mask: Image.Image, visual_width: float) -> Image.Image:
    radius = max(1, round(visual_width * SCALE))
    return mask.filter(ImageFilter.MinFilter(radius * 2 + 1))


def inner_band(mask: Image.Image, visual_width: float) -> Image.Image:
    return ImageChops.subtract(mask, eroded(mask, visual_width))


def solid_layer(color: tuple[int, int, int], alpha_mask: Image.Image, maximum_alpha: int) -> Image.Image:
    if maximum_alpha != 255:
        alpha_mask = alpha_mask.point(lambda value: round(value * maximum_alpha / 255.0))
    layer = Image.new("RGBA", alpha_mask.size, (*color, 0))
    layer.putalpha(alpha_mask)
    return layer


def normal_card(mask: Image.Image) -> Image.Image:
    card = material_layer(mask, selected=False)
    layers = (
        (NORMAL["darkBacking"], NORMAL["darkBackingWidth"], NORMAL["darkBackingAlpha"]),
        (NORMAL["mainBorder"], NORMAL["mainBorderWidth"], NORMAL["mainBorderAlpha"]),
        (NORMAL["fineHighlight"], NORMAL["fineHighlightWidth"], NORMAL["fineHighlightAlpha"]),
    )
    for color, width, alpha in layers:
        card = Image.alpha_composite(card, solid_layer(rgb(str(color)), inner_band(mask, float(width)), int(alpha)))
    return card


def weighted_boundary(
    mask: Image.Image,
    *,
    outer_strength: float,
    side_strength: float,
    inner_strength: float,
    width: float = 2.0,
) -> Image.Image:
    band = inner_band(mask, width)
    bbox = mask_bbox(band)
    left, top, _, _ = bbox
    values = np.asarray(band.crop(bbox), dtype=np.float32) / 255.0
    yy, xx = np.indices(values.shape, dtype=np.float32)
    radius = np.sqrt(((xx + left) / SCALE - CX) ** 2 + ((yy + top) / SCALE - CY) ** 2)
    outer_p = 1.0 - smoothstep((np.abs(radius - OUTER_R) - 1.0) / 14.0)
    inner_p = 1.0 - smoothstep((np.abs(radius - INNER_R) - 1.0) / 12.0)
    strength = side_strength + (outer_strength - side_strength) * outer_p
    strength += (inner_strength - side_strength) * inner_p
    values = np.clip(values * strength, 0.0, 1.0)
    local = Image.fromarray(np.rint(values * 255.0).astype(np.uint8), "L")
    output = Image.new("L", (HI, HI), 0)
    output.paste(local, (left, top))
    return output


def normalized_blur(mask: Image.Image, radius: float, maximum_alpha: int) -> Image.Image:
    pad = round(radius * SCALE * 4)
    bbox = mask_bbox(mask, pad)
    blurred = mask.crop(bbox).filter(ImageFilter.GaussianBlur(radius * SCALE))
    values = np.asarray(blurred, dtype=np.float32)
    peak = float(values.max())
    if peak <= 0:
        raise RuntimeError("blur mask peak is zero")
    values = np.clip(values / peak * maximum_alpha, 0.0, maximum_alpha)
    local = Image.fromarray(np.rint(values).astype(np.uint8), "L")
    output = Image.new("L", (HI, HI), 0)
    output.paste(local, (bbox[0], bbox[1]))
    return output


def restrict_halo(halo: Image.Image, body: Image.Image) -> Image.Image:
    """Keep card/card and card/hub gaps clear while retaining outer bloom.

    Side and hub-side halos bloom inward.  The strongest outer-arc halo may
    extend beyond the wheel, matching the reference without filling gaps.
    """
    bbox = mask_bbox(halo)
    left, top, _, _ = bbox
    alpha = np.asarray(halo.crop(bbox), dtype=np.uint8).copy()
    body_values = np.asarray(body.crop(bbox), dtype=np.uint8)
    yy, xx = np.indices(alpha.shape, dtype=np.float32)
    dx = (xx + left) / SCALE - CX
    dy = (yy + top) / SCALE - CY
    radius = np.sqrt(dx**2 + dy**2)
    angle = np.arctan2(dx, -dy)
    outer_endpoint = angle_of(offset_ray_point(math.pi / SLOT_COUNT, OUTER_R, -GAP / 2.0))
    outer_allowed = (radius >= OUTER_R - 1.5) & (np.abs(angle) <= outer_endpoint + 0.001)
    allowed = (body_values > 0) | outer_allowed
    alpha[~allowed] = 0
    local = Image.fromarray(alpha, "L")
    output = Image.new("L", (HI, HI), 0)
    output.paste(local, (left, top))
    return output


def selected_card(mask: Image.Image) -> Image.Image:
    wide_strength = SELECTED["wideEdgeStrength"]
    tight_strength = SELECTED["tightEdgeStrength"]
    wide_boundary = weighted_boundary(
        mask,
        outer_strength=float(wide_strength["outer"]),
        side_strength=float(wide_strength["side"]),
        inner_strength=float(wide_strength["inner"]),
    )
    tight_boundary = weighted_boundary(
        mask,
        outer_strength=float(tight_strength["outer"]),
        side_strength=float(tight_strength["side"]),
        inner_strength=float(tight_strength["inner"]),
    )
    wide = restrict_halo(
        normalized_blur(wide_boundary, float(SELECTED["wideHaloRadius"]), int(SELECTED["wideHaloAlpha"])),
        mask,
    )
    tight = restrict_halo(
        normalized_blur(tight_boundary, float(SELECTED["tightHaloRadius"]), int(SELECTED["tightHaloAlpha"])),
        mask,
    )
    # Paint the body first so the inward portion of both Gaussian blooms stays
    # visible over the dark steel-blue material.  Only the outer-arc portion
    # is allowed beyond the body by restrict_halo().
    card = material_layer(mask, selected=True)
    card = Image.alpha_composite(card, solid_layer(rgb(str(SELECTED["wideHalo"])), wide, 255))
    card = Image.alpha_composite(card, solid_layer(rgb(str(SELECTED["tightHalo"])), tight, 255))
    card = Image.alpha_composite(
        card,
        solid_layer(
            rgb(str(SELECTED["mainCyan"])),
            inner_band(mask, float(SELECTED["mainCyanWidth"])),
            int(SELECTED["mainCyanAlpha"]),
        ),
    )
    card = Image.alpha_composite(
        card,
        solid_layer(
            rgb(str(SELECTED["iceCore"])),
            inner_band(mask, float(SELECTED["iceCoreWidth"])),
            int(SELECTED["iceCoreAlpha"]),
        ),
    )
    return card


def center_layer() -> tuple[Image.Image, Image.Image, Image.Image]:
    output = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    disk_mask = Image.new("L", (HI, HI), 0)
    md = ImageDraw.Draw(disk_mask)
    r = CENTER_R * SCALE
    md.ellipse((HCX - r, HCY - r, HCX + r, HCY + r), fill=255)

    bbox = mask_bbox(disk_mask)
    left, top, _, _ = bbox
    local_mask = np.asarray(disk_mask.crop(bbox), dtype=np.uint8)
    yy, xx = np.indices(local_mask.shape, dtype=np.float32)
    radius = np.sqrt(((xx + left) / SCALE - CX) ** 2 + ((yy + top) / SCALE - CY) ** 2)
    t = np.clip(radius / CENTER_R, 0.0, 1.0)
    colors = interpolate_stops(
        t,
        (
            (0.0, rgb(CENTER["inner"])),
            (0.58, rgb(CENTER["middle"])),
            (1.0, rgb(CENTER["outer"])),
        ),
    )
    colors -= (1.0 - t)[..., None] * np.asarray((0.3, 0.5, 0.7), dtype=np.float32)
    rgba = np.zeros((*local_mask.shape, 4), dtype=np.uint8)
    rgba[..., :3] = np.clip(np.rint(colors), 0, 255).astype(np.uint8)
    rgba[..., 3] = local_mask
    local = Image.fromarray(rgba, "RGBA")
    output.paste(local, (left, top), local)

    overlay = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    def ellipse_line(radius_value: float, color: str, alpha: int, width: float) -> None:
        rv = radius_value * SCALE
        inset = width * SCALE / 2.0
        draw.ellipse(
            (HCX - rv + inset, HCY - rv + inset, HCX + rv - inset, HCY + rv - inset),
            outline=(*rgb(color), alpha),
            width=max(1, round(width * SCALE)),
        )

    ellipse_line(CENTER_R, CENTER["border"], 220, 2.0)
    ellipse_line(CENTER_R - 2.2, CENTER["fineHighlight"], 41, 0.8)
    ellipse_line(CENTER_INNER_R, CENTER["innerRing"], 105, 1.0)

    dot_color = (*rgb(CENTER["dotted"]), 92)
    dot_radius = 0.55 * SCALE
    for index in range(132):
        angle = math.tau * index / 132
        x, y = circle_point(DOTTED_R, angle)
        x *= SCALE
        y *= SCALE
        draw.ellipse((x - dot_radius, y - dot_radius, x + dot_radius, y + dot_radius), fill=dot_color)

    output = Image.alpha_composite(output, overlay)

    marker_layer = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    marker_mask = Image.new("L", (HI, HI), 0)
    marker_draw = ImageDraw.Draw(marker_layer)
    marker_mask_draw = ImageDraw.Draw(marker_mask)
    half = MARKER_SIZE * SCALE / 2.0
    for slot in range(1, SLOT_COUNT + 1):
        x, y = circle_point(MARKER_R, math.radians(60 * (slot - 1)))
        x *= SCALE
        y *= SCALE
        points = [(x, y - half), (x + half, y), (x, y + half), (x - half, y)]
        marker_draw.polygon(points, fill=(*rgb(CENTER["marker"]), 151))
        marker_mask_draw.polygon(points, fill=255)
    output = Image.alpha_composite(output, marker_layer)
    return output, disk_mask, marker_mask


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


def alpha_composite_safe(base: Image.Image, overlay: Image.Image) -> Image.Image:
    return Image.alpha_composite(base.convert("RGBA"), overlay.convert("RGBA"))


def build_review(previews: Sequence[Image.Image], path: Path) -> None:
    tile = 390
    pad = 18
    width = pad * 4 + tile * 3
    height = pad * 3 + tile * 2
    review = Image.new("RGBA", (width, height), (8, 11, 16, 255))
    for index, preview in enumerate(previews):
        thumb = preview.resize((tile, tile), RESAMPLING.LANCZOS)
        x = pad + (index % 3) * (tile + pad)
        y = pad + (index // 3) * (tile + pad)
        review.alpha_composite(thumb, (x, y))
    save_png(review, path)


def build_comparison(reference_path: Path, normal: Image.Image, selected: Image.Image, path: Path) -> None:
    panel = 600
    pad = 20
    comparison = Image.new("RGBA", (panel * 3 + pad * 4, panel + pad * 2), (8, 11, 16, 255))
    with Image.open(reference_path) as source:
        images = [source.convert("RGBA"), normal, selected]
        for index, image in enumerate(images):
            fitted = image.resize((panel, panel), RESAMPLING.LANCZOS)
            comparison.alpha_composite(fitted, (pad + index * (panel + pad), pad))
    save_png(comparison, path)


def polar_anchor(radius: float, angle: float) -> dict[str, float]:
    x, y = circle_point(radius, math.radians(angle))
    return {"x": round(x, 3), "y": round(y, 3)}


def layout_data() -> dict[str, object]:
    slots = []
    for slot in range(1, SLOT_COUNT + 1):
        angle = 60.0 * (slot - 1)
        slots.append(
            {
                "slot": slot,
                "name": ("Top", "Upper Right", "Lower Right", "Bottom", "Lower Left", "Upper Left")[slot - 1],
                "angleDegreesClockwiseFromTop": angle,
                "glyphAnchor": polar_anchor(GLYPH_R, angle),
                "labelAnchor": polar_anchor(LABEL_R, angle),
            }
        )
    return {
        "canvas": {"width": CANVAS, "height": CANVAS, "mode": "RGBA"},
        "internalRender": {"scale": SCALE, "width": HI, "height": HI, "downsample": "Pillow LANCZOS"},
        "wheelCenter": {"x": CX, "y": CY},
        "slotCount": SLOT_COUNT,
        "slotAnglesDegrees": [60.0 * index for index in range(SLOT_COUNT)],
        "geometry": {
            "outerRadius": OUTER_R,
            "innerRadius": INNER_R,
            "gapPx": GAP,
            "gapConstruction": "logical 60-degree boundary plus parallel offset gapPx/2",
            "cornerRadius": CORNER_R,
            "centerRadius": CENTER_R,
            "centerInnerRadius": CENTER_INNER_R,
            "dottedRingRadius": DOTTED_R,
            "markerRadius": MARKER_R,
            "markerSize": MARKER_SIZE,
        },
        "slots": slots,
        "normalMaterial": NORMAL,
        "selectedMaterial": SELECTED,
        "centerMaterial": CENTER,
        "selectedAssetCanonicalSlot": 1,
        "selectedAssetRotationDegreesClockwise": [0, 60, 120, 180, 240, 300],
        "reference": f"../{REFERENCE_NAME}",
    }


def rgba_stats(path: Path) -> dict[str, object]:
    with Image.open(path) as image:
        array = np.asarray(image.convert("RGBA"))
    alpha = array[..., 3]
    nonzero = alpha > 0
    return {
        "file": path.name,
        "mode": "RGBA",
        "size": list(image.size),
        "alphaMin": int(alpha.min()),
        "alphaMax": int(alpha.max()),
        "nonzeroAlphaRange": [int(alpha[nonzero].min()), int(alpha[nonzero].max())] if np.any(nonzero) else [0, 0],
        "transparentPixelCount": int(np.count_nonzero(~nonzero)),
        "alphaZeroRgbResidueComponents": int(np.count_nonzero(array[..., :3][~nonzero])),
        "sha256": sha256(path),
    }


def bbox_list(mask: Image.Image) -> list[int]:
    bbox = mask.getbbox()
    if bbox is None:
        return []
    return list(bbox)


def verify(
    out: Path,
    slot_masks_hi: Sequence[Image.Image],
    selected_hi: Image.Image,
    base: Image.Image,
    previews: Sequence[Image.Image],
    center_mask_hi: Image.Image,
) -> dict[str, object]:
    asset_names = (
        BASE_NAME,
        SELECTED_NAME,
        NORMAL_PREVIEW_NAME,
        SLOT2_PREVIEW_NAME,
        ALL_REVIEW_NAME,
        COMPARISON_NAME,
    )
    stats = {name: rgba_stats(out / name) for name in asset_names}
    formal = (BASE_NAME, SELECTED_NAME, NORMAL_PREVIEW_NAME, SLOT2_PREVIEW_NAME)
    if not all(stats[name]["mode"] == "RGBA" for name in formal):
        raise RuntimeError("formal PNG mode verification failed")
    if not all(stats[name]["size"] == [CANVAS, CANVAS] for name in formal):
        raise RuntimeError("formal PNG canvas verification failed")
    if not all(stats[name]["alphaZeroRgbResidueComponents"] == 0 for name in formal):
        raise RuntimeError("alpha=0 RGB residue verification failed")

    canonical_body = downsample_mask(slot_masks_hi[0])
    slot1_mask_sha = hashlib.sha256(slot_masks_hi[0].tobytes()).hexdigest().upper()
    center_mask_sha = hashlib.sha256(center_mask_hi.tobytes()).hexdigest().upper()
    selected_final = Image.open(out / SELECTED_NAME).convert("RGBA")
    selected_alpha = selected_final.getchannel("A")
    halo_only = ImageChops.subtract(selected_alpha, canonical_body)
    selected_bbox = selected_alpha.getbbox()
    if selected_bbox is None:
        raise RuntimeError("selected asset is empty")

    rotation = {}
    for slot, target_hi in enumerate(slot_masks_hi, start=1):
        rotated = rotate_slot(slot_masks_hi[0], slot)
        a = np.asarray(rotated, dtype=np.int16)
        b = np.asarray(target_hi, dtype=np.int16)
        mismatch = int(np.count_nonzero(np.abs(a - b) > 1))
        rotation[str(slot)] = {
            "angleDegreesClockwise": 60 * (slot - 1),
            "maskPixelsDifferingByMoreThan1": mismatch,
            "aligned": mismatch == 0,
        }

    base_array = np.asarray(base)
    center_mask = np.asarray(downsample_mask(center_mask_hi)) >= 250
    preview_checks: dict[str, object] = {}
    for slot, preview in enumerate(previews, start=1):
        preview_array = np.asarray(preview)
        center_changed = int(np.count_nonzero(np.any(preview_array[center_mask] != base_array[center_mask], axis=1)))
        other = {}
        for other_slot, other_hi in enumerate(slot_masks_hi, start=1):
            if other_slot == slot:
                continue
            stable = np.asarray(downsample_mask(other_hi)) >= 250
            other[str(other_slot)] = int(np.count_nonzero(np.any(preview_array[stable] != base_array[stable], axis=1)))
        preview_checks[str(slot)] = {"centerChangedPixels": center_changed, "otherSlotsChangedPixels": other}

    base_alpha = np.asarray(base.getchannel("A"))
    gap_samples = []
    for boundary in (-30, 30, 90, 150, 210, 270):
        for radius in range(INNER_R + 16, OUTER_R - 16):
            x, y = circle_point(radius, math.radians(boundary))
            gap_samples.append(int(base_alpha[round(y), round(x)]))

    checks = {
        "canvas1254Rgba": all(stats[name]["size"] == [CANVAS, CANVAS] and stats[name]["mode"] == "RGBA" for name in formal),
        "fourTimesSupersampling": SCALE == 4 and HI == 5016,
        "slotMaskIdenticalToV4": slot1_mask_sha == FROZEN_V4_SLOT1_MASK_SHA256,
        "centerGeometryIdenticalToV4": center_mask_sha == FROZEN_V4_CENTER_MASK_SHA256,
        "parallelGapTarget16": GAP == 16,
        "zeroAlphaRgbClean": all(stats[name]["alphaZeroRgbResidueComponents"] == 0 for name in formal),
        "baseLogicalGapSamplesTransparent": max(gap_samples) == 0,
        "allSlotsFromCanonicalRotation": all(item["aligned"] for item in rotation.values()),
        "centerUnchangedInEverySelectedPreview": all(item["centerChangedPixels"] == 0 for item in preview_checks.values()),
        "otherSlotsUnchangedInEverySelectedPreview": all(
            all(value == 0 for value in item["otherSlotsChangedPixels"].values()) for item in preview_checks.values()
        ),
        "normalPreviewMatchesBase": sha256(out / NORMAL_PREVIEW_NAME) == sha256(out / BASE_NAME),
        "selectedHaloNotCanvasClipped": (
            selected_bbox[0] > 0
            and selected_bbox[1] > 0
            and selected_bbox[2] < CANVAS
            and selected_bbox[3] < CANVAS
        ),
    }
    if not all(checks.values()):
        raise RuntimeError(f"verification failed: {json.dumps(checks, sort_keys=True)}")

    return {
        "generator": Path(__file__).name,
        "dependencies": ["Python standard library", f"Pillow {Image.__version__}", f"NumPy {np.__version__}"],
        "canvas": {"width": CANVAS, "height": CANVAS, "mode": "RGBA"},
        "internalRender": {"width": HI, "height": HI, "scale": SCALE, "downsample": "LANCZOS"},
        "bodyGeometry": layout_data()["geometry"],
        "geometryFreeze": {
            "slot1MaskSha256": slot1_mask_sha,
            "expectedV4Slot1MaskSha256": FROZEN_V4_SLOT1_MASK_SHA256,
            "centerMaskSha256": center_mask_sha,
            "expectedV4CenterMaskSha256": FROZEN_V4_CENTER_MASK_SHA256,
        },
        "selectedAssetBBox": bbox_list(selected_alpha),
        "selectedBodyBBox": bbox_list(canonical_body),
        "selectedHaloBBox": bbox_list(halo_only),
        "selectedHalo": {
            "wideRadiusPx": SELECTED["wideHaloRadius"],
            "tightRadiusPx": SELECTED["tightHaloRadius"],
            "wideOuterSideInnerStrength": SELECTED["wideEdgeStrength"],
            "tightOuterSideInnerStrength": SELECTED["tightEdgeStrength"],
            "gapPolicy": "side and inner halo inward; outer arc may bloom outward",
        },
        "alpha": stats,
        "logicalGapSamples": {"count": len(gap_samples), "minAlpha": min(gap_samples), "maxAlpha": max(gap_samples)},
        "rotationAlignment": rotation,
        "previewIsolation": preview_checks,
        "checks": checks,
    }


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False, sort_keys=True) + "\n", encoding="utf-8")


def build(out: Path, reference_path: Path) -> dict[str, object]:
    out.mkdir(parents=True, exist_ok=True)
    slot1_mask = canonical_mask()
    slot_masks = [rotate_slot(slot1_mask, slot) for slot in range(1, SLOT_COUNT + 1)]

    canonical_normal = normal_card(slot1_mask)
    base_hi = Image.new("RGBA", (HI, HI), (0, 0, 0, 0))
    for slot in range(1, SLOT_COUNT + 1):
        base_hi = alpha_composite_safe(base_hi, rotate_slot(canonical_normal, slot))
    center_hi, center_mask_hi, _ = center_layer()
    base_hi = alpha_composite_safe(base_hi, center_hi)

    selected_hi = selected_card(slot1_mask)
    base = downsample(base_hi)
    selected = downsample(selected_hi)
    save_png(base, out / BASE_NAME)
    save_png(selected, out / SELECTED_NAME)
    save_png(base, out / NORMAL_PREVIEW_NAME)

    previews: list[Image.Image] = []
    for slot in range(1, SLOT_COUNT + 1):
        preview_hi = alpha_composite_safe(base_hi, rotate_slot(selected_hi, slot))
        previews.append(downsample(preview_hi))
    save_png(previews[1], out / SLOT2_PREVIEW_NAME)
    build_review(previews, out / ALL_REVIEW_NAME)
    build_comparison(reference_path, base, previews[1], out / COMPARISON_NAME)
    write_json(out / LAYOUT_NAME, layout_data())
    report = verify(out, slot_masks, selected_hi, base, previews, center_mask_hi)
    write_json(out / REPORT_NAME, report)
    return report


def verify_existing(out: Path) -> dict[str, object]:
    required = [
        BASE_NAME,
        SELECTED_NAME,
        NORMAL_PREVIEW_NAME,
        SLOT2_PREVIEW_NAME,
        ALL_REVIEW_NAME,
        COMPARISON_NAME,
        LAYOUT_NAME,
        REPORT_NAME,
    ]
    missing = [name for name in required if not (out / name).exists()]
    if missing:
        raise RuntimeError(f"missing generated assets: {missing}")
    report = json.loads((out / REPORT_NAME).read_text(encoding="utf-8"))
    for name, recorded in report["alpha"].items():
        current = rgba_stats(out / name)
        if current["sha256"] != recorded["sha256"]:
            raise RuntimeError(f"SHA mismatch for {name}")
    return report


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    out = Path(__file__).resolve().parent
    reference_path = out.parent / REFERENCE_NAME
    report = verify_existing(out) if args.verify_only else build(out, reference_path)
    print(json.dumps(report["checks"], indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
