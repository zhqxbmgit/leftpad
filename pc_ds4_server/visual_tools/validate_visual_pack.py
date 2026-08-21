#!/usr/bin/env python3
"""Validate a LeftPad UI Visual Pack against supported toolchain profiles."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

try:
    from PIL import Image, ImageChops, UnidentifiedImageError
except ImportError as exception:  # pragma: no cover - exercised only without Pillow.
    Image = None  # type: ignore[assignment]
    ImageChops = None  # type: ignore[assignment]
    UnidentifiedImageError = OSError  # type: ignore[assignment,misc]
    PILLOW_IMPORT_ERROR: ImportError | None = exception
else:
    PILLOW_IMPORT_ERROR = None


SUPPORTED_VERSION = 1
SUPPORTED_SELECTION_MODE = "canonical-transform"
MASTER_SIZE = 1254
EXPECTED_CENTER = 627.0
ANGLE_TOLERANCE = 0.0001


@dataclass(frozen=True)
class LayoutProfileContract:
    profile_id: str
    slot_count: int
    angles: tuple[float, ...]
    status: str
    runtime_integrated: bool
    required_geometry_fields: tuple[str, ...]


PROFILE_CONTRACTS = {
    "radial-6": LayoutProfileContract(
        profile_id="radial-6",
        slot_count=6,
        angles=(0.0, 60.0, 120.0, 180.0, 240.0, 300.0),
        status="stable",
        runtime_integrated=True,
        required_geometry_fields=("outerRadius", "innerRadius", "gapPx", "cornerRadius"),
    ),
    "radial-8": LayoutProfileContract(
        profile_id="radial-8",
        slot_count=8,
        angles=(0.0, 45.0, 90.0, 135.0, 180.0, 225.0, 270.0, 315.0),
        status="experimental",
        runtime_integrated=False,
        required_geometry_fields=(
            "outerRadius",
            "innerRadius",
            "gapPx",
            "cornerRadius",
            "centerRadius",
            "centerInnerRadius",
        ),
    ),
}
SUPPORTED_LAYOUT_PROFILES = tuple(PROFILE_CONTRACTS)

# Backward-compatible aliases for callers that still describe the Runtime-only
# radial-6 contract. New toolchain code should use get_profile_contract().
SUPPORTED_LAYOUT_PROFILE = "radial-6"
SUPPORTED_SLOT_COUNT = 6

MANIFEST_FIELDS = (
    "id",
    "name",
    "version",
    "layoutProfile",
    "slotCount",
    "base",
    "selected",
    "layout",
    "selectionAssetMode",
)


class VisualPackValidationError(ValueError):
    """Raised when a Visual Pack violates the supported contract."""


class DuplicateJsonKeyError(ValueError):
    """Raised when a JSON object repeats a field name."""


@dataclass(frozen=True)
class AssetValidation:
    role: str
    filename: str
    width: int
    height: int
    mode: str
    transparent_pixels: int
    rgb_residue_pixels: int
    rgb_residue_components: int
    hash_verified: bool


@dataclass(frozen=True)
class ValidationReport:
    pack_id: str
    name: str
    layout_profile: str
    slot_count: int
    selection_mode: str
    profile_status: str
    runtime_compatible: bool
    assets: tuple[AssetValidation, ...]
    warnings: tuple[str, ...]


def get_profile_contract(profile_id: str) -> LayoutProfileContract:
    contract = PROFILE_CONTRACTS.get(profile_id)
    if contract is None:
        raise VisualPackValidationError(f"unsupported layoutProfile: {profile_id}")
    return contract


def _unique_json_object(pairs: Iterable[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKeyError(f"duplicate JSON field: {key}")
        result[key] = value
    return result


def _load_json(path: Path, label: str) -> dict[str, Any]:
    try:
        text = path.read_text(encoding="utf-8-sig")
    except OSError as exception:
        raise VisualPackValidationError(f"unable to read {label}: {exception}") from exception

    try:
        value = json.loads(text, object_pairs_hook=_unique_json_object)
    except (json.JSONDecodeError, DuplicateJsonKeyError) as exception:
        raise VisualPackValidationError(f"invalid {label}: {exception}") from exception

    if not isinstance(value, dict):
        raise VisualPackValidationError(f"invalid {label}: root must be an object")
    return value


def _require_object(container: dict[str, Any], field: str, owner: str) -> dict[str, Any]:
    value = container.get(field)
    if not isinstance(value, dict):
        raise VisualPackValidationError(f"{owner}.{field} must be an object")
    return value


def _require_list(container: dict[str, Any], field: str, owner: str) -> list[Any]:
    value = container.get(field)
    if not isinstance(value, list):
        raise VisualPackValidationError(f"{owner}.{field} must be an array")
    return value


def _require_string(container: dict[str, Any], field: str, owner: str) -> str:
    value = container.get(field)
    if not isinstance(value, str) or not value.strip():
        raise VisualPackValidationError(f"{owner}.{field} must be a non-empty string")
    return value


def _require_integer(container: dict[str, Any], field: str, owner: str) -> int:
    value = container.get(field)
    if type(value) is not int:
        raise VisualPackValidationError(f"{owner}.{field} must be an integer")
    return value


def _require_number(container: dict[str, Any], field: str, owner: str) -> float:
    value = container.get(field)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise VisualPackValidationError(f"{owner}.{field} must be a number")
    number = float(value)
    if not math.isfinite(number):
        raise VisualPackValidationError(f"{owner}.{field} must be finite")
    return number


def _nearly_equal(left: float, right: float) -> bool:
    return abs(left - right) < ANGLE_TOLERANCE


def _validate_local_filename(filename: str, field: str) -> None:
    if (
        filename in {".", ".."}
        or "/" in filename
        or "\\" in filename
        or Path(filename).name != filename
    ):
        raise VisualPackValidationError(
            f"manifest.{field} must be a local file name: {filename}"
        )


def _validate_manifest(manifest: dict[str, Any]) -> LayoutProfileContract:
    missing = [field for field in MANIFEST_FIELDS if field not in manifest]
    if missing:
        raise VisualPackValidationError(
            "manifest missing required field(s): " + ", ".join(missing)
        )

    _require_string(manifest, "id", "manifest")
    _require_string(manifest, "name", "manifest")
    version = _require_integer(manifest, "version", "manifest")
    layout_profile = _require_string(manifest, "layoutProfile", "manifest")
    slot_count = _require_integer(manifest, "slotCount", "manifest")
    selection_mode = _require_string(manifest, "selectionAssetMode", "manifest")

    for field in ("base", "selected", "layout"):
        filename = _require_string(manifest, field, "manifest")
        _validate_local_filename(filename, field)

    if version != SUPPORTED_VERSION:
        raise VisualPackValidationError(f"unsupported manifest version: {version}")
    profile = get_profile_contract(layout_profile)
    if slot_count != profile.slot_count:
        raise VisualPackValidationError(
            f"{layout_profile} requires slotCount {profile.slot_count}: found {slot_count}"
        )
    if selection_mode != SUPPORTED_SELECTION_MODE:
        raise VisualPackValidationError(
            f"unsupported selectionAssetMode: {selection_mode}"
        )
    return profile


def _validate_anchor(
    point: Any,
    owner: str,
    width: int,
    height: int,
) -> None:
    if not isinstance(point, dict):
        raise VisualPackValidationError(f"{owner} must be an object")
    x = _require_number(point, "x", owner)
    y = _require_number(point, "y", owner)
    if not (0.0 <= x <= width and 0.0 <= y <= height):
        raise VisualPackValidationError(f"{owner} is outside the master canvas")


def _validate_layout(
    layout: dict[str, Any],
    manifest_slot_count: int,
    profile: LayoutProfileContract,
) -> None:
    canvas = _require_object(layout, "canvas", "layout")
    width = _require_integer(canvas, "width", "layout.canvas")
    height = _require_integer(canvas, "height", "layout.canvas")
    mode = _require_string(canvas, "mode", "layout.canvas")
    if (width, height, mode) != (MASTER_SIZE, MASTER_SIZE, "RGBA"):
        raise VisualPackValidationError(
            f"layout.canvas must be {MASTER_SIZE}x{MASTER_SIZE} RGBA"
        )

    layout_slot_count = _require_integer(layout, "slotCount", "layout")
    if layout_slot_count != profile.slot_count or layout_slot_count != manifest_slot_count:
        raise VisualPackValidationError(
            f"layout.slotCount must be {profile.slot_count} for {profile.profile_id} "
            "and match manifest"
        )

    wheel_center = _require_object(layout, "wheelCenter", "layout")
    center_x = _require_number(wheel_center, "x", "layout.wheelCenter")
    center_y = _require_number(wheel_center, "y", "layout.wheelCenter")
    if not (_nearly_equal(center_x, EXPECTED_CENTER) and _nearly_equal(center_y, EXPECTED_CENTER)):
        raise VisualPackValidationError("layout.wheelCenter must be 627,627")

    geometry = _require_object(layout, "geometry", "layout")
    geometry_values = {
        field: _require_number(geometry, field, "layout.geometry")
        for field in profile.required_geometry_fields
    }
    outer_radius = geometry_values["outerRadius"]
    inner_radius = geometry_values["innerRadius"]
    gap_px = geometry_values["gapPx"]
    corner_radius = geometry_values["cornerRadius"]
    if outer_radius <= 0 or inner_radius <= 0 or outer_radius <= inner_radius:
        raise VisualPackValidationError(
            "layout.geometry radii must satisfy outerRadius > innerRadius > 0"
        )
    if gap_px < 0 or corner_radius < 0:
        raise VisualPackValidationError(
            "layout.geometry gapPx and cornerRadius must be non-negative"
        )
    if profile.profile_id == "radial-8":
        center_radius = geometry_values["centerRadius"]
        center_inner_radius = geometry_values["centerInnerRadius"]
        if not (
            inner_radius > center_radius > center_inner_radius > 0
        ):
            raise VisualPackValidationError(
                "radial-8 geometry must satisfy innerRadius > centerRadius > "
                "centerInnerRadius > 0"
            )

    angles = _require_list(layout, "slotAnglesDegrees", "layout")
    if len(angles) != profile.slot_count:
        raise VisualPackValidationError(
            f"layout.slotAnglesDegrees must contain {profile.slot_count} values "
            f"for {profile.profile_id}"
        )
    for index, expected in enumerate(profile.angles):
        value = angles[index]
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            raise VisualPackValidationError(
                f"layout.slotAnglesDegrees[{index}] must be a number"
            )
        if not math.isfinite(float(value)) or not _nearly_equal(float(value), expected):
            raise VisualPackValidationError(
                f"invalid slot angle {index + 1}: expected {expected:g}"
            )

    slots = _require_list(layout, "slots", "layout")
    if len(slots) != profile.slot_count:
        raise VisualPackValidationError(
            f"layout.slots must contain {profile.slot_count} entries for "
            f"{profile.profile_id}"
        )
    for index, expected_angle in enumerate(profile.angles):
        slot = slots[index]
        owner = f"layout.slots[{index}]"
        if not isinstance(slot, dict):
            raise VisualPackValidationError(f"{owner} must be an object")
        slot_number = _require_integer(slot, "slot", owner)
        angle = _require_number(slot, "angleDegreesClockwiseFromTop", owner)
        if slot_number != index + 1 or not _nearly_equal(angle, expected_angle):
            raise VisualPackValidationError(
                f"invalid transform mapping for slot {index + 1}"
            )
        _validate_anchor(slot.get("glyphAnchor"), f"{owner}.glyphAnchor", width, height)
        _validate_anchor(slot.get("labelAnchor"), f"{owner}.labelAnchor", width, height)

    center_text_anchor = layout.get("centerTextAnchor")
    if center_text_anchor is not None:
        _validate_anchor(center_text_anchor, "layout.centerTextAnchor", width, height)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def _validate_png(path: Path, role: str) -> AssetValidation:
    if PILLOW_IMPORT_ERROR is not None or Image is None or ImageChops is None:
        raise VisualPackValidationError(
            "Pillow is required for PNG validation; install Pillow in the Python environment"
        )
    if not path.is_file():
        raise VisualPackValidationError(f"{role.lower()} asset missing: {path.name}")
    if path.suffix.lower() != ".png":
        raise VisualPackValidationError(f"{role.lower()} asset must use a .png extension")

    try:
        with Image.open(path) as source:
            source.verify()
        with Image.open(path) as source:
            source.load()
            if source.format != "PNG":
                raise VisualPackValidationError(f"{role.lower()} asset is not a PNG")
            if source.mode != "RGBA":
                raise VisualPackValidationError(
                    f"{role.lower()} asset must be RGBA: found {source.mode}"
                )
            if source.size != (MASTER_SIZE, MASTER_SIZE):
                raise VisualPackValidationError(
                    f"{role.lower()} asset must be {MASTER_SIZE}x{MASTER_SIZE}: "
                    f"found {source.width}x{source.height}"
                )

            alpha = source.getchannel("A")
            alpha_histogram = alpha.histogram()
            transparent_pixels = alpha_histogram[0]
            nontransparent_pixels = source.width * source.height - transparent_pixels
            if transparent_pixels == 0:
                raise VisualPackValidationError(
                    f"{role.lower()} asset has no transparent background; "
                    "black/checkerboard backgrounds must not be baked"
                )
            if nontransparent_pixels == 0:
                raise VisualPackValidationError(f"{role.lower()} asset contains no visible pixels")

            alpha_bbox = alpha.getbbox()
            if alpha_bbox is None:
                raise VisualPackValidationError(f"{role.lower()} asset contains no visible pixels")
            left, top, right, bottom = alpha_bbox
            if left <= 0 or top <= 0 or right >= source.width or bottom >= source.height:
                raise VisualPackValidationError(
                    f"{role.lower()} asset background reaches the canvas border; "
                    "remove black/checkerboard backing and preserve transparency"
                )

            transparent_mask = alpha.point(lambda value: 255 if value == 0 else 0)
            red, green, blue, _ = source.split()
            residue_channels = [
                ImageChops.multiply(channel, transparent_mask)
                for channel in (red, green, blue)
            ]
            residue_components = sum(
                sum(channel.histogram()[1:]) for channel in residue_channels
            )
            residue_union = ImageChops.lighter(
                residue_channels[0],
                ImageChops.lighter(residue_channels[1], residue_channels[2]),
            )
            residue_pixels = sum(residue_union.histogram()[1:])
            if residue_components:
                raise VisualPackValidationError(
                    f"{role.lower()} asset has RGB residue in alpha=0 pixels: "
                    f"{residue_pixels} pixels, {residue_components} components"
                )

            return AssetValidation(
                role=role,
                filename=path.name,
                width=source.width,
                height=source.height,
                mode=source.mode,
                transparent_pixels=transparent_pixels,
                rgb_residue_pixels=residue_pixels,
                rgb_residue_components=residue_components,
                hash_verified=False,
            )
    except VisualPackValidationError:
        raise
    except (OSError, UnidentifiedImageError) as exception:
        raise VisualPackValidationError(
            f"invalid {role.lower()} PNG: {path.name}: {exception}"
        ) from exception


def _collect_hash_records(
    value: Any,
    records: dict[str, set[str]],
    parent_key: str | None = None,
) -> None:
    if isinstance(value, dict):
        sha_value = value.get("sha256")
        if sha_value is not None:
            if not isinstance(sha_value, str) or len(sha_value) != 64:
                raise VisualPackValidationError("verification.json contains an invalid SHA-256")
            try:
                int(sha_value, 16)
            except ValueError as exception:
                raise VisualPackValidationError(
                    "verification.json contains an invalid SHA-256"
                ) from exception
            file_value = value.get("file")
            if isinstance(file_value, str) and file_value:
                filename = Path(file_value).name
            elif parent_key and "." in parent_key:
                filename = Path(parent_key).name
            else:
                filename = None
            if filename:
                records.setdefault(filename, set()).add(sha_value.upper())
        for key, child in value.items():
            _collect_hash_records(child, records, key)
    elif isinstance(value, list):
        for child in value:
            _collect_hash_records(child, records, parent_key)


def _verify_optional_hashes(
    pack_directory: Path,
    paths: tuple[Path, ...],
) -> tuple[set[str], list[str]]:
    verification_path = pack_directory / "verification.json"
    if not verification_path.is_file():
        return set(), ["verification.json not found; SHA-256 verification skipped"]

    verification = _load_json(verification_path, "verification.json")
    records: dict[str, set[str]] = {}
    _collect_hash_records(verification, records)
    verified: set[str] = set()
    warnings: list[str] = []
    for path in paths:
        hashes = records.get(path.name)
        if not hashes:
            warnings.append(f"verification.json has no SHA-256 for {path.name}")
            continue
        if len(hashes) != 1:
            raise VisualPackValidationError(
                f"verification.json has conflicting SHA-256 values for {path.name}"
            )
        expected = next(iter(hashes))
        actual = _sha256(path)
        if actual != expected:
            raise VisualPackValidationError(
                f"SHA-256 mismatch for {path.name}: expected {expected}, found {actual}"
            )
        verified.add(path.name)
    return verified, warnings


def validate_visual_pack(directory: str | Path) -> ValidationReport:
    pack_directory = Path(directory).expanduser().resolve()
    if not pack_directory.is_dir():
        raise VisualPackValidationError(
            f"Visual Pack directory not found: {pack_directory}"
        )

    manifest_path = pack_directory / "manifest.json"
    if not manifest_path.is_file():
        raise VisualPackValidationError("manifest missing: manifest.json")
    manifest = _load_json(manifest_path, "manifest.json")
    profile = _validate_manifest(manifest)

    base_path = pack_directory / manifest["base"]
    selected_path = pack_directory / manifest["selected"]
    layout_path = pack_directory / manifest["layout"]
    if not layout_path.is_file():
        raise VisualPackValidationError(f"layout asset missing: {layout_path.name}")

    layout = _load_json(layout_path, layout_path.name)
    _validate_layout(layout, manifest["slotCount"], profile)

    base_report = _validate_png(base_path, "Base")
    selected_report = _validate_png(selected_path, "Selected")
    verified, warnings = _verify_optional_hashes(
        pack_directory,
        (base_path, selected_path, layout_path),
    )

    assets = tuple(
        AssetValidation(
            role=asset.role,
            filename=asset.filename,
            width=asset.width,
            height=asset.height,
            mode=asset.mode,
            transparent_pixels=asset.transparent_pixels,
            rgb_residue_pixels=asset.rgb_residue_pixels,
            rgb_residue_components=asset.rgb_residue_components,
            hash_verified=asset.filename in verified,
        )
        for asset in (base_report, selected_report)
    )

    return ValidationReport(
        pack_id=manifest["id"],
        name=manifest["name"],
        layout_profile=manifest["layoutProfile"],
        slot_count=manifest["slotCount"],
        selection_mode=manifest["selectionAssetMode"],
        profile_status=profile.status,
        runtime_compatible=profile.runtime_integrated,
        assets=assets,
        warnings=tuple(warnings),
    )


def format_valid_report(report: ValidationReport) -> str:
    lines = [
        "================================",
        "VISUAL PACK VALID",
        "================================",
        "",
        "ID:",
        report.pack_id,
        "",
        "Name:",
        report.name,
        "",
        "Profile:",
        report.layout_profile,
        "",
        "Slot Count:",
        str(report.slot_count),
        "",
        "Selection Mode:",
        report.selection_mode,
        "",
        "Profile Status:",
        report.profile_status,
        "",
        "Assets:",
    ]
    for asset in report.assets:
        hash_text = "verified" if asset.hash_verified else "not recorded"
        lines.append(
            f"{asset.role}: {asset.filename} "
            f"(PNG, {asset.mode}, {asset.width}x{asset.height}, SHA-256 {hash_text})"
        )
    lines.extend(["", "Alpha:", "PASS"])
    for asset in report.assets:
        lines.append(
            f"{asset.role.lower()} transparent pixels: {asset.transparent_pixels}"
        )
        lines.append(
            f"{asset.role.lower()} RGB residue: {asset.rgb_residue_components}"
        )
    if report.warnings:
        lines.extend(["", "Warnings:"])
        lines.extend(f"- {warning}" for warning in report.warnings)
    lines.extend(
        [
            "",
            "Toolchain Valid:",
            "YES",
            "",
            "Runtime Compatible:",
            "YES" if report.runtime_compatible else "NO",
            "",
            "Runtime Capability Gap:",
            "NONE" if report.runtime_compatible else "EXISTS",
            "",
            "--------------------------------",
            "Pack:",
            report.name,
            "",
            "Toolchain Compatible:",
            "YES",
            "--------------------------------",
        ]
    )
    return "\n".join(lines)


def format_invalid_report(reason: str) -> str:
    return "\n".join(
        [
            "================================",
            "VISUAL PACK INVALID",
            "================================",
            "",
            "Reason:",
            reason,
            "",
            "Runtime Compatible:",
            "NO",
        ]
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Validate a LeftPad Visual Pack against the Runtime contract."
    )
    parser.add_argument("visual_pack_directory", help="Visual Pack directory to validate")
    arguments = parser.parse_args(argv)
    try:
        report = validate_visual_pack(arguments.visual_pack_directory)
    except VisualPackValidationError as exception:
        print(format_invalid_report(str(exception)))
        return 1
    print(format_valid_report(report))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
