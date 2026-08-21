#!/usr/bin/env python3
"""Create a standardized human-review sheet for a LeftPad Visual Pack."""

from __future__ import annotations

import argparse
import json
import os
import tempfile
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps, UnidentifiedImageError

from validate_visual_pack import VisualPackValidationError, validate_visual_pack


REVIEW_BACKGROUND = "#080B10"
PANEL_BACKGROUND = "#101722"
PANEL_BORDER = "#283446"
TEXT_PRIMARY = "#EDF5FF"
TEXT_SECONDARY = "#9BAFC6"
ACCENT = "#67D5FF"

CANVAS_WIDTH = 1600
OUTER_MARGIN = 60
SECTION_GAP = 24
HEADER_HEIGHT = 138
SECTION_TITLE_HEIGHT = 56
SECTION_PADDING = 20
REFERENCE_CONTENT_HEIGHT = 380
BASE_CONTENT_HEIGHT = 480
SELECTED_CONTENT_HEIGHT = 480
ALL_SLOTS_CONTENT_HEIGHT = 760


class ComparisonError(ValueError):
    """Raised when a comparison sheet cannot be generated safely."""


@dataclass(frozen=True)
class ComparisonResult:
    output_path: Path
    pack_id: str
    pack_name: str
    slot_count: int
    width: int
    height: int


def _load_font(size: int, bold: bool = False) -> ImageFont.ImageFont:
    candidates = (
        ("DejaVuSans-Bold.ttf", "arialbd.ttf")
        if bold
        else ("DejaVuSans.ttf", "arial.ttf")
    )
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            continue
    try:
        return ImageFont.load_default(size=size)
    except TypeError:  # Pillow versions before scalable default fonts.
        return ImageFont.load_default()


def _load_reference(path: Path) -> Image.Image:
    if not path.is_file():
        raise ComparisonError(f"reference image missing: {path}")
    try:
        with Image.open(path) as source:
            source.verify()
        with Image.open(path) as source:
            source.load()
            if source.width <= 0 or source.height <= 0:
                raise ComparisonError("reference image has invalid dimensions")
            return source.convert("RGBA")
    except ComparisonError:
        raise
    except (OSError, UnidentifiedImageError) as exception:
        raise ComparisonError(f"invalid reference image: {path.name}: {exception}") from exception


def _load_rgba(path: Path) -> Image.Image:
    try:
        with Image.open(path) as source:
            source.load()
            return source.convert("RGBA")
    except (OSError, UnidentifiedImageError) as exception:
        raise ComparisonError(f"unable to open Visual Pack asset {path.name}: {exception}") from exception


def _read_json(path: Path) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exception:
        raise ComparisonError(f"unable to read {path.name}: {exception}") from exception
    if not isinstance(value, dict):
        raise ComparisonError(f"{path.name} root must be an object")
    return value


def _rotate_selected(
    selected: Image.Image,
    angle_degrees_clockwise: float,
    wheel_center: tuple[float, float],
) -> Image.Image:
    return selected.rotate(
        -angle_degrees_clockwise,
        resample=Image.Resampling.BICUBIC,
        expand=False,
        center=wheel_center,
        fillcolor=(0, 0, 0, 0),
    )


def _selected_preview(
    base: Image.Image,
    selected: Image.Image,
    angle_degrees_clockwise: float,
    wheel_center: tuple[float, float],
) -> Image.Image:
    rotated = _rotate_selected(selected, angle_degrees_clockwise, wheel_center)
    try:
        return Image.alpha_composite(base, rotated)
    finally:
        rotated.close()


def _section_height(content_height: int) -> int:
    return SECTION_TITLE_HEIGHT + content_height + SECTION_PADDING * 2


def _paste_contained(
    canvas: Image.Image,
    source: Image.Image,
    box: tuple[int, int, int, int],
) -> None:
    left, top, right, bottom = box
    available = (right - left, bottom - top)
    fitted = ImageOps.contain(source, available, Image.Resampling.LANCZOS)
    try:
        x = left + (available[0] - fitted.width) // 2
        y = top + (available[1] - fitted.height) // 2
        if fitted.mode == "RGBA":
            canvas.paste(fitted, (x, y), fitted)
        else:
            canvas.paste(fitted, (x, y))
    finally:
        fitted.close()


def _draw_panel(
    canvas: Image.Image,
    draw: ImageDraw.ImageDraw,
    top: int,
    title: str,
    content: Image.Image,
    content_height: int,
    subtitle: str | None = None,
) -> int:
    height = _section_height(content_height)
    left = OUTER_MARGIN
    right = CANVAS_WIDTH - OUTER_MARGIN
    bottom = top + height
    draw.rounded_rectangle(
        (left, top, right, bottom),
        radius=18,
        fill=PANEL_BACKGROUND,
        outline=PANEL_BORDER,
        width=2,
    )
    draw.text(
        (left + 24, top + 13),
        title,
        font=_load_font(28, bold=True),
        fill=TEXT_PRIMARY,
    )
    if subtitle:
        subtitle_font = _load_font(19)
        subtitle_box = draw.textbbox((0, 0), subtitle, font=subtitle_font)
        draw.text(
            (right - 24 - (subtitle_box[2] - subtitle_box[0]), top + 19),
            subtitle,
            font=subtitle_font,
            fill=TEXT_SECONDARY,
        )
    content_top = top + SECTION_TITLE_HEIGHT + SECTION_PADDING
    _paste_contained(
        canvas,
        content,
        (
            left + SECTION_PADDING,
            content_top,
            right - SECTION_PADDING,
            content_top + content_height,
        ),
    )
    return bottom


def _build_all_slots_panel(
    base: Image.Image,
    selected: Image.Image,
    angles: list[float],
    wheel_center: tuple[float, float],
) -> Image.Image:
    panel_width = CANVAS_WIDTH - OUTER_MARGIN * 2 - SECTION_PADDING * 2
    panel = Image.new("RGB", (panel_width, ALL_SLOTS_CONTENT_HEIGHT), PANEL_BACKGROUND)
    draw = ImageDraw.Draw(panel)
    columns = 4 if len(angles) == 8 else 3
    rows = 2
    gap_x = 18
    gap_y = 18
    label_height = 34
    cell_width = (panel_width - gap_x * (columns - 1)) // columns
    cell_height = (ALL_SLOTS_CONTENT_HEIGHT - gap_y * (rows - 1)) // rows
    image_height = cell_height - label_height
    label_font = _load_font(20, bold=True)

    for index, angle in enumerate(angles):
        column = index % columns
        row = index // columns
        x = column * (cell_width + gap_x)
        y = row * (cell_height + gap_y)
        draw.rounded_rectangle(
            (x, y, x + cell_width, y + cell_height),
            radius=12,
            fill=REVIEW_BACKGROUND,
            outline=PANEL_BORDER,
            width=1,
        )
        preview = _selected_preview(base, selected, angle, wheel_center)
        try:
            _paste_contained(
                panel,
                preview,
                (x + 10, y + 8, x + cell_width - 10, y + image_height),
            )
        finally:
            preview.close()
        label = f"Slot {index + 1}  |  {angle:g}°"
        label_box = draw.textbbox((0, 0), label, font=label_font)
        draw.text(
            (
                x + (cell_width - (label_box[2] - label_box[0])) // 2,
                y + image_height + 4,
            ),
            label,
            font=label_font,
            fill=ACCENT if index == 1 else TEXT_SECONDARY,
        )
    return panel


def _build_comparison(
    reference: Image.Image,
    base: Image.Image,
    selected: Image.Image,
    manifest: dict[str, object],
    layout: dict[str, object],
) -> Image.Image:
    wheel_center_value = layout["wheelCenter"]
    if not isinstance(wheel_center_value, dict):
        raise ComparisonError("layout.wheelCenter must be an object")
    wheel_center = (
        float(wheel_center_value["x"]),
        float(wheel_center_value["y"]),
    )
    angles_value = layout["slotAnglesDegrees"]
    if not isinstance(angles_value, list):
        raise ComparisonError("layout.slotAnglesDegrees must be an array")
    angles = [float(value) for value in angles_value]

    slot_two_preview = _selected_preview(base, selected, angles[1], wheel_center)
    all_slots = _build_all_slots_panel(base, selected, angles, wheel_center)
    try:
        total_height = (
            OUTER_MARGIN
            + HEADER_HEIGHT
            + SECTION_GAP
            + _section_height(REFERENCE_CONTENT_HEIGHT)
            + SECTION_GAP
            + _section_height(BASE_CONTENT_HEIGHT)
            + SECTION_GAP
            + _section_height(SELECTED_CONTENT_HEIGHT)
            + SECTION_GAP
            + _section_height(ALL_SLOTS_CONTENT_HEIGHT)
            + OUTER_MARGIN
        )
        canvas = Image.new("RGB", (CANVAS_WIDTH, total_height), REVIEW_BACKGROUND)
        draw = ImageDraw.Draw(canvas)
        title_font = _load_font(42, bold=True)
        metadata_font = _load_font(22)
        draw.text(
            (OUTER_MARGIN, OUTER_MARGIN),
            str(manifest["name"]),
            font=title_font,
            fill=TEXT_PRIMARY,
        )
        metadata = (
            f"ID: {manifest['id']}   |   Profile: {manifest['layoutProfile']}   |   "
            f"Version: {manifest['version']}"
        )
        draw.text(
            (OUTER_MARGIN, OUTER_MARGIN + 62),
            metadata,
            font=metadata_font,
            fill=TEXT_SECONDARY,
        )
        draw.text(
            (CANVAS_WIDTH - OUTER_MARGIN - 260, OUTER_MARGIN + 12),
            "HUMAN REVIEW",
            font=_load_font(20, bold=True),
            fill=ACCENT,
        )

        top = OUTER_MARGIN + HEADER_HEIGHT + SECTION_GAP
        bottom = _draw_panel(
            canvas,
            draw,
            top,
            "Reference",
            reference,
            REFERENCE_CONTENT_HEIGHT,
            "contain fit / no crop",
        )
        bottom = _draw_panel(
            canvas,
            draw,
            bottom + SECTION_GAP,
            "Generated Base",
            base,
            BASE_CONTENT_HEIGHT,
            str(manifest["base"]),
        )
        bottom = _draw_panel(
            canvas,
            draw,
            bottom + SECTION_GAP,
            "Selected Preview",
            slot_two_preview,
            SELECTED_CONTENT_HEIGHT,
            f"Slot 2 / {angles[1]:g}° clockwise",
        )
        _draw_panel(
            canvas,
            draw,
            bottom + SECTION_GAP,
            "All Slots Review",
            all_slots,
            ALL_SLOTS_CONTENT_HEIGHT,
            "rotation / alignment / halo / clipping",
        )
        return canvas
    finally:
        slot_two_preview.close()
        all_slots.close()


def _save_png_atomically(image: Image.Image, output_path: Path) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{output_path.stem}-",
        suffix=".png",
        dir=output_path.parent,
    )
    os.close(descriptor)
    temporary_path = Path(temporary_name)
    try:
        image.save(temporary_path, format="PNG", optimize=True)
        temporary_path.replace(output_path)
    finally:
        temporary_path.unlink(missing_ok=True)


def compare_visual_pack(
    reference_image: str | Path,
    visual_pack_directory: str | Path,
    output: str | Path | None = None,
) -> ComparisonResult:
    pack_directory = Path(visual_pack_directory).expanduser().resolve()
    try:
        validation = validate_visual_pack(pack_directory)
    except VisualPackValidationError as exception:
        raise ComparisonError(f"Visual Pack invalid: {exception}") from exception

    reference_path = Path(reference_image).expanduser().resolve()
    reference = _load_reference(reference_path)
    manifest_path = pack_directory / "manifest.json"
    manifest = _read_json(manifest_path)
    layout_path = pack_directory / str(manifest["layout"])
    layout = _read_json(layout_path)
    base_path = pack_directory / str(manifest["base"])
    selected_path = pack_directory / str(manifest["selected"])

    if output is None:
        output_path = pack_directory / "comparison.png"
    else:
        output_path = Path(output).expanduser().resolve()
    if output_path.suffix.lower() != ".png":
        reference.close()
        raise ComparisonError("output file must use a .png extension")
    protected_paths = {
        reference_path,
        manifest_path.resolve(),
        layout_path.resolve(),
        base_path.resolve(),
        selected_path.resolve(),
    }
    if output_path in protected_paths:
        reference.close()
        raise ComparisonError("output path must not overwrite an input or Visual Pack asset")

    base = _load_rgba(base_path)
    selected = _load_rgba(selected_path)
    try:
        comparison = _build_comparison(reference, base, selected, manifest, layout)
        try:
            _save_png_atomically(comparison, output_path)
            return ComparisonResult(
                output_path=output_path,
                pack_id=validation.pack_id,
                pack_name=validation.name,
                slot_count=validation.slot_count,
                width=comparison.width,
                height=comparison.height,
            )
        finally:
            comparison.close()
    finally:
        reference.close()
        base.close()
        selected.close()


def format_success(result: ComparisonResult) -> str:
    return "\n".join(
        [
            "================================",
            "VISUAL PACK COMPARISON CREATED",
            "================================",
            "",
            "Pack:",
            result.pack_name,
            "",
            "ID:",
            result.pack_id,
            "",
            "Slots:",
            str(result.slot_count),
            "",
            "Output:",
            str(result.output_path),
            "",
            "Size:",
            f"{result.width}x{result.height}",
            "",
            "Human Approval:",
            "REQUIRED",
        ]
    )


def format_failure(reason: str) -> str:
    return "\n".join(
        [
            "================================",
            "VISUAL PACK COMPARISON FAILED",
            "================================",
            "",
            "Reason:",
            reason,
        ]
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Create a standardized human-review image for a Visual Pack."
    )
    parser.add_argument("reference_image", help="Reference image path")
    parser.add_argument("visual_pack_directory", help="Visual Pack directory")
    parser.add_argument(
        "--output",
        help="Output PNG path (default: <VisualPackDirectory>/comparison.png)",
    )
    arguments = parser.parse_args(argv)
    try:
        result = compare_visual_pack(
            arguments.reference_image,
            arguments.visual_pack_directory,
            arguments.output,
        )
    except ComparisonError as exception:
        print(format_failure(str(exception)))
        return 1
    print(format_success(result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
