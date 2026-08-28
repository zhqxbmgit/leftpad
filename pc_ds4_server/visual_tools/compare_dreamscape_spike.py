"""Normalize a runtime capture to the reference canvas and produce comparisons."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageEnhance


def normalize_canvas(current: Image.Image, reference_size: tuple[int, int]) -> Image.Image:
    current = current.convert("RGB")
    reference_width, reference_height = reference_size
    scale = min(current.width / reference_width, current.height / reference_height)
    crop_width = max(1, round(reference_width * scale))
    crop_height = max(1, round(reference_height * scale))
    left = max(0, (current.width - crop_width) // 2)
    top = max(0, (current.height - crop_height) // 2)
    crop = current.crop((left, top, left + crop_width, top + crop_height))
    return crop.resize(reference_size, Image.Resampling.LANCZOS)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--current", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--suffix", default="")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    with Image.open(args.reference) as opened:
        reference = opened.convert("RGB")
    with Image.open(args.current) as opened:
        current = normalize_canvas(opened, reference.size)

    suffix = f"-{args.suffix}" if args.suffix else ""
    reference.save(args.output / f"reference-overview{suffix}.png", optimize=True)
    current.save(args.output / f"current-overview{suffix}.png", optimize=True)
    Image.blend(reference, current, 0.5).save(
        args.output / f"overlay-50{suffix}.png", optimize=True)
    raw_difference = ImageChops.difference(reference, current)
    ImageEnhance.Contrast(raw_difference).enhance(2.0).save(
        args.output / f"difference{suffix}.png", optimize=True)

    reference_values = np.asarray(reference, dtype=np.float32)
    current_values = np.asarray(current, dtype=np.float32)
    delta = reference_values - current_values
    mae = float(np.mean(np.abs(delta)))
    rmse = float(math.sqrt(np.mean(np.square(delta))))
    ssim = None
    try:
        from skimage.metrics import structural_similarity

        ssim = float(structural_similarity(
            reference_values,
            current_values,
            channel_axis=2,
            data_range=255.0))
    except ImportError:
        pass

    metrics = {
        "referenceWidth": reference.width,
        "referenceHeight": reference.height,
        "sourceCurrentWidth": Image.open(args.current).width,
        "sourceCurrentHeight": Image.open(args.current).height,
        "mae": mae,
        "rmse": rmse,
        "ssim": ssim,
    }
    (args.output / f"metrics{suffix}.json").write_text(
        json.dumps(metrics, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8")
    print(json.dumps(metrics, ensure_ascii=False))


if __name__ == "__main__":
    main()
