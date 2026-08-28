"""Normalize the Settings runtime capture and produce review artifacts."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageEnhance


def normalize_canvas(current: Image.Image, size: tuple[int, int]) -> Image.Image:
    current = current.convert("RGB")
    width, height = size
    scale = min(current.width / width, current.height / height)
    crop_width = max(1, round(width * scale))
    crop_height = max(1, round(height * scale))
    left = max(0, (current.width - crop_width) // 2)
    top = max(0, (current.height - crop_height) // 2)
    return current.crop((left, top, left + crop_width, top + crop_height)).resize(
        size, Image.Resampling.LANCZOS)


def error_metrics(
    reference: np.ndarray,
    current: np.ndarray,
    include_ssim: bool = False,
) -> dict[str, float | None]:
    delta = reference.astype(np.float32) - current.astype(np.float32)
    metrics: dict[str, float | None] = {
        "mae": float(np.mean(np.abs(delta))),
        "rmse": float(math.sqrt(np.mean(np.square(delta)))),
        "ssim": None,
    }
    if include_ssim:
        try:
            from skimage.metrics import structural_similarity

            metrics["ssim"] = float(structural_similarity(
                reference, current, channel_axis=2, data_range=255.0))
        except ImportError:
            pass
    return metrics


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--current", type=Path, required=True)
    parser.add_argument("--mask", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    with Image.open(args.reference) as opened:
        reference = opened.convert("RGB")
    with Image.open(args.current) as opened:
        source_size = opened.size
        current = normalize_canvas(opened, reference.size)
    with Image.open(args.mask) as opened:
        dynamic_mask = np.asarray(opened.convert("L").resize(reference.size)) > 0

    reference.save(args.output / "reference-settings.png", optimize=True)
    current.save(args.output / "current-settings.png", optimize=True)
    Image.blend(reference, current, 0.5).save(
        args.output / "overlay-50-settings.png", optimize=True)
    raw_difference = ImageChops.difference(reference, current)
    ImageEnhance.Contrast(raw_difference).enhance(2.0).save(
        args.output / "difference-settings.png", optimize=True)

    reference_values = np.asarray(reference)
    current_values = np.asarray(current)
    static_pixels = ~dynamic_mask
    metrics = {
        "referenceWidth": reference.width,
        "referenceHeight": reference.height,
        "sourceCurrentWidth": source_size[0],
        "sourceCurrentHeight": source_size[1],
        "raw": error_metrics(reference_values, current_values, include_ssim=True),
        "staticOnly": error_metrics(
            reference_values[static_pixels], current_values[static_pixels]),
        "dynamicMaskPixelCount": int(dynamic_mask.sum()),
    }
    (args.output / "metrics-settings.json").write_text(
        json.dumps(metrics, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8")
    print(json.dumps(metrics, ensure_ascii=False))


if __name__ == "__main__":
    main()
