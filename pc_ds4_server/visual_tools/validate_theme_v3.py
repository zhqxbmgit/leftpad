#!/usr/bin/env python3
"""Validate a LeftPad UI Theme Package V3 manifest.

The V3 validator is a contract-only, offline gate.  It validates the closed
manifest shape and the universal-radial semantic model; it does not discover,
load, decompose, compile, or render a theme.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Any, Iterable, Mapping, Sequence

try:  # The repository validator remains usable without this optional package.
    from jsonschema import Draft202012Validator
except ImportError:  # pragma: no cover - depends on the developer environment.
    Draft202012Validator = None  # type: ignore[assignment,misc]

try:
    from PIL import Image
except ImportError:  # pragma: no cover - reported only by asset-checking calls.
    Image = None  # type: ignore[assignment]


SCHEMA_PATH = Path(__file__).with_name("schemas") / "ui-theme-v3.schema.json"
LAYOUT_SLOT_COUNTS = {"radial-6": 6, "radial-8": 8}
REQUIRED_CAPABILITIES = {
    "universalRadial",
    "semanticLayers",
    "dynamicAnchors",
    "instantTransitions",
}
GLOBAL_ROLES = {"STATIC_RESIDUAL", "HUB"}
SLOT_ROLES = {"PETAL_FILL", "PETAL_BORDER", "PETAL_DETAIL", "SELECTED_EMPHASIS"}
REQUIRED_SLOT_ROLES = {"PETAL_FILL", "PETAL_BORDER", "SELECTED_EMPHASIS"}
DYNAMIC_ROLES = {"DYNAMIC_SLOT_CONTENT", "DYNAMIC_SELECTED_CONTENT"}
IDENTIFIER_RE = re.compile(r"^[A-Za-z][A-Za-z0-9._-]*$")
PATH_SEGMENT_RE = re.compile(r"^[A-Za-z0-9._-]+$")
HASH_RE = re.compile(r"^[A-Fa-f0-9]{64}$")
SELECTED_STATE_RE = re.compile(r"^selected-([1-8])$")
COORDINATE_TOLERANCE = 1e-9
WINDOWS_RESERVED_BASENAMES = {
    "CON", "PRN", "AUX", "NUL",
    *(f"COM{index}" for index in range(1, 10)),
    *(f"LPT{index}" for index in range(1, 10)),
}


class ThemeV3ValidationError(ValueError):
    """Raised when a V3 manifest violates the frozen contract."""


class DuplicateJsonKeyError(ValueError):
    """Raised when a JSON object repeats a field name."""


@dataclass(frozen=True)
class ThemeV3ValidationReport:
    theme_id: str
    layout_profile: str
    slot_count: int
    state_count: int
    semantic_layer_count: int
    dynamic_group_count: int
    mask_count: int
    asset_count: int
    assets_verified: bool


def _unique_json_object(pairs: Iterable[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateJsonKeyError(f"duplicate JSON field: {key}")
        result[key] = value
    return result


def _reject_json_constant(value: str) -> None:
    raise ValueError(f"non-finite JSON number: {value}")


def load_theme_v3_manifest(path: Path | str) -> dict[str, Any]:
    """Load one V3 manifest while rejecting duplicate keys and non-finite JSON."""

    manifest_path = Path(path)
    try:
        text = manifest_path.read_text(encoding="utf-8-sig")
    except OSError as exception:
        raise ThemeV3ValidationError(f"unable to read V3 theme manifest: {exception}") from exception
    try:
        value = json.loads(
            text,
            object_pairs_hook=_unique_json_object,
            parse_constant=_reject_json_constant,
        )
    except (json.JSONDecodeError, DuplicateJsonKeyError, ValueError) as exception:
        raise ThemeV3ValidationError(f"invalid V3 theme manifest: {exception}") from exception
    if not isinstance(value, dict):
        raise ThemeV3ValidationError("invalid V3 theme manifest: root must be an object")
    return value


def _fail(message: str) -> None:
    raise ThemeV3ValidationError(message)


def _object(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        _fail(f"{label} must be an object")
    return value


def _array(value: Any, label: str, *, nonempty: bool = False) -> list[Any]:
    if not isinstance(value, list):
        _fail(f"{label} must be an array")
    if nonempty and not value:
        _fail(f"{label} must not be empty")
    return value


def _string(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value:
        _fail(f"{label} must be a non-empty string")
    return value


def _integer(value: Any, label: str) -> int:
    if type(value) is not int:
        _fail(f"{label} must be an integer")
    return value


def _number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        _fail(f"{label} must be a number")
    result = float(value)
    if not math.isfinite(result):
        _fail(f"{label} must be finite")
    return result


def target_outer_envelope_radius(
    authored_inner_radius: float,
    authored_outer_radius: float,
    authored_envelope_radius: float,
    target_inner_radius: float,
    target_outer_radius: float,
) -> float:
    """Evaluate the frozen V3 outer-padding transform in continuous doubles."""

    values = (
        float(authored_inner_radius),
        float(authored_outer_radius),
        float(authored_envelope_radius),
        float(target_inner_radius),
        float(target_outer_radius),
    )
    if not all(math.isfinite(value) for value in values):
        _fail("outer warp envelope geometry must be finite")
    inner0, outer0, envelope0, inner, outer = values
    if not 0 < inner0 < outer0 < envelope0 <= 140:
        _fail("authored geometry must satisfy 0 < I0 < O0 < R0 <= 140")
    if not 0 < inner < outer:
        _fail("target geometry must satisfy 0 < I < O")
    result = outer + (envelope0 - outer0) * (outer - inner) / (outer0 - inner0)
    if not math.isfinite(result) or result <= outer:
        _fail("target outer warp envelope Rt must be finite and greater than O")
    return result


def _closed(
    value: Mapping[str, Any],
    label: str,
    required: Iterable[str],
    optional: Iterable[str] = (),
) -> None:
    required_set = set(required)
    allowed = required_set | set(optional)
    missing = sorted(required_set - value.keys())
    unknown = sorted(value.keys() - allowed)
    if missing:
        _fail(f"{label} missing required field(s): {', '.join(missing)}")
    if unknown:
        _fail(f"{label} contains unknown field(s): {', '.join(unknown)}")


def _identifier(value: Any, label: str) -> str:
    result = _string(value, label)
    if len(result) > 96 or IDENTIFIER_RE.fullmatch(result) is None:
        _fail(f"{label} is not a valid identifier: {result}")
    return result


def _safe_package_path(value: Any, label: str) -> str:
    result = _string(value, label)
    if len(result) > 240:
        _fail(f"{label} exceeds the 240-character package path limit")
    if PureWindowsPath(result).is_absolute() or PurePosixPath(result).is_absolute():
        _fail(f"{label} must be a package-relative POSIX path")
    if "\\" in result or ":" in result:
        _fail(f"{label} must be a package-relative POSIX path")
    parts = PurePosixPath(result).parts
    if not parts or any(part in {"", ".", ".."} for part in parts):
        _fail(f"{label} contains an unsafe path segment")
    if any(PATH_SEGMENT_RE.fullmatch(part) is None for part in parts):
        _fail(f"{label} contains an unsafe path segment")
    if any(part.endswith(".") for part in parts):
        _fail(f"{label} contains a Windows-ambiguous trailing dot")
    if any(part.split(".", 1)[0].upper() in WINDOWS_RESERVED_BASENAMES for part in parts):
        _fail(f"{label} contains a reserved Windows device name")
    if PurePosixPath(result).suffix.lower() != ".png":
        _fail(f"{label} must reference a PNG asset")
    return result


def _validate_json_schema(document: Mapping[str, Any]) -> None:
    if Draft202012Validator is None:
        return
    try:
        schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        _fail(f"unable to load V3 schema: {exception}")
    errors = sorted(
        Draft202012Validator(schema).iter_errors(document),
        key=lambda error: tuple(str(part) for part in error.absolute_path),
    )
    if errors:
        error = errors[0]
        location = ".".join(str(part) for part in error.absolute_path) or "manifest"
        _fail(f"schema validation failed at {location}: {error.message}")


def _validate_closed_shape(document: dict[str, Any]) -> None:
    required = (
        "protocolVersion", "packageRevision", "id", "name", "surface",
        "renderStrategy", "layoutProfile", "compatibleLayouts", "slotCount",
        "referenceCanvas", "referenceScale", "placement", "radialGeometry",
        "identityStates", "semanticLayers", "masks", "dynamicGroups",
        "fallback", "capabilities", "transitions", "assetHashes",
    )
    _closed(document, "manifest", required, ("description", "exampleOnly", "authoring"))
    if document["protocolVersion"] != 3:
        _fail("manifest.protocolVersion must equal 3")
    if _integer(document["packageRevision"], "manifest.packageRevision") < 1:
        _fail("manifest.packageRevision must be at least 1")
    _identifier(document["id"], "manifest.id")
    _string(document["name"], "manifest.name")
    if "description" in document and not isinstance(document["description"], str):
        _fail("manifest.description must be a string")
    if "exampleOnly" in document and type(document["exampleOnly"]) is not bool:
        _fail("manifest.exampleOnly must be a boolean")
    if document["surface"] != "radial-overlay":
        _fail("manifest.surface must equal radial-overlay")
    if document["renderStrategy"] != "universal-radial":
        _fail("manifest.renderStrategy must equal universal-radial")

    if "authoring" in document:
        authoring = _object(document["authoring"], "manifest.authoring")
        _closed(authoring, "manifest.authoring", ("method",))
        if authoring["method"] not in {"external-artwork", "procedural", "mixed"}:
            _fail("manifest.authoring.method is unsupported")

    layout_profile = _string(document["layoutProfile"], "manifest.layoutProfile")
    if layout_profile not in LAYOUT_SLOT_COUNTS:
        _fail(f"unsupported layoutProfile: {layout_profile}")
    slot_count = _integer(document["slotCount"], "manifest.slotCount")
    if slot_count != LAYOUT_SLOT_COUNTS[layout_profile]:
        _fail(f"slot count mismatch: {layout_profile} requires {LAYOUT_SLOT_COUNTS[layout_profile]}")
    compatible = _array(document["compatibleLayouts"], "manifest.compatibleLayouts", nonempty=True)
    if compatible != [layout_profile]:
        _fail("compatibleLayouts must contain exactly layoutProfile for a slot-bound V3 package")

    canvas = _object(document["referenceCanvas"], "manifest.referenceCanvas")
    _closed(canvas, "manifest.referenceCanvas", ("width", "height", "colorSpace", "alphaMode"))
    width = _integer(canvas["width"], "manifest.referenceCanvas.width")
    height = _integer(canvas["height"], "manifest.referenceCanvas.height")
    if not 1 <= width <= 8192 or not 1 <= height <= 8192:
        _fail("referenceCanvas dimensions must be from 1 through 8192")
    if width != height:
        _fail("V3 referenceCanvas must be square")
    if canvas["colorSpace"] != "sRGB" or canvas["alphaMode"] != "straight":
        _fail("referenceCanvas requires sRGB with straight alpha")

    scale = _object(document["referenceScale"], "manifest.referenceScale")
    _closed(scale, "manifest.referenceScale", ("logicalWidth", "logicalHeight", "fit", "contentOrigin"))
    if _number(scale["logicalWidth"], "manifest.referenceScale.logicalWidth") != 280:
        _fail("manifest.referenceScale.logicalWidth must equal 280 CRU")
    if _number(scale["logicalHeight"], "manifest.referenceScale.logicalHeight") != 280:
        _fail("manifest.referenceScale.logicalHeight must equal 280 CRU")
    if scale["fit"] != "contain":
        _fail("manifest.referenceScale.fit must equal contain")
    origin = _object(scale["contentOrigin"], "manifest.referenceScale.contentOrigin")
    _closed(origin, "manifest.referenceScale.contentOrigin", ("x", "y"))
    if _number(origin["x"], "manifest.referenceScale.contentOrigin.x") != 0 or _number(
        origin["y"], "manifest.referenceScale.contentOrigin.y"
    ) != 0:
        _fail("V3 referenceScale.contentOrigin must be the CRU origin (0, 0)")

    placement = _object(document["placement"], "manifest.placement")
    _closed(placement, "manifest.placement", ("activationAnchor",))
    activation = _object(placement["activationAnchor"], "manifest.placement.activationAnchor")
    _closed(activation, "manifest.placement.activationAnchor", ("x", "y"))
    for axis in ("x", "y"):
        coordinate = _number(activation[axis], f"manifest.placement.activationAnchor.{axis}")
        if not 0 <= coordinate <= 280:
            _fail("activationAnchor must lie within the 280 CRU nominal surface")


def _validate_geometry(document: Mapping[str, Any]) -> None:
    geometry = _object(document["radialGeometry"], "manifest.radialGeometry")
    _closed(
        geometry,
        "manifest.radialGeometry",
        (
            "canonicalUnitWidth", "center", "neutralHubRadius", "neutralInnerRadius",
            "neutralOuterRadius", "neutralGapDegrees", "slotCenters", "outerWarpEnvelope",
        ),
    )
    if _number(geometry["canonicalUnitWidth"], "radialGeometry.canonicalUnitWidth") != 280:
        _fail("radialGeometry.canonicalUnitWidth must equal 280")
    center = _object(geometry["center"], "radialGeometry.center")
    _closed(center, "radialGeometry.center", ("x", "y"))
    if _number(center["x"], "radialGeometry.center.x") != 140 or _number(
        center["y"], "radialGeometry.center.y"
    ) != 140:
        _fail("radialGeometry.center must equal (140, 140) CRU")

    hub = _number(geometry["neutralHubRadius"], "radialGeometry.neutralHubRadius")
    inner = _number(geometry["neutralInnerRadius"], "radialGeometry.neutralInnerRadius")
    outer = _number(geometry["neutralOuterRadius"], "radialGeometry.neutralOuterRadius")
    gap = _number(geometry["neutralGapDegrees"], "radialGeometry.neutralGapDegrees")
    if (hub, inner, outer, gap) != (35, 42, 103, 4):
        _fail("V3 neutral geometry must equal H=35, I=42, O=103, gap=4")
    if not 0 < hub < inner < outer:
        _fail("invalid H/I/O order: contract requires 0 < H < I < O")

    slot_count = int(document["slotCount"])
    pitch = 360.0 / slot_count
    if not 0 <= gap < pitch:
        _fail("neutral gap must satisfy 0 <= gap < slot pitch")
    centers = _array(geometry["slotCenters"], "radialGeometry.slotCenters")
    if len(centers) != slot_count:
        _fail("slot count mismatch in radialGeometry.slotCenters")
    first_angle: float | None = None
    seen_slots: set[int] = set()
    for index, value in enumerate(centers):
        item = _object(value, f"radialGeometry.slotCenters[{index}]")
        _closed(item, f"radialGeometry.slotCenters[{index}]", ("slotId", "angleDegreesClockwiseFromTop"))
        slot_id = _integer(item["slotId"], f"radialGeometry.slotCenters[{index}].slotId")
        angle = _number(item["angleDegreesClockwiseFromTop"], f"radialGeometry.slotCenters[{index}].angleDegreesClockwiseFromTop")
        if slot_id != index + 1 or slot_id in seen_slots:
            _fail("slotCenters must have unique, ordered slot IDs 1..N")
        if not 0 <= angle < 360:
            _fail("slot center angles must be in [0, 360)")
        seen_slots.add(slot_id)
        if first_angle is None:
            first_angle = angle
        expected = (first_angle + index * pitch) % 360
        if not math.isclose(angle, expected, abs_tol=COORDINATE_TOLERANCE):
            _fail("slotCenters must be evenly spaced by slot pitch")

    envelope = _object(geometry["outerWarpEnvelope"], "radialGeometry.outerWarpEnvelope")
    _closed(envelope, "radialGeometry.outerWarpEnvelope", ("authoredRadius", "targetRule", "overflowPolicy"))
    radius = _number(envelope["authoredRadius"], "radialGeometry.outerWarpEnvelope.authoredRadius")
    if not outer < radius <= 140:
        _fail("outerWarpEnvelope.authoredRadius must satisfy O0 < R0 <= 140 CRU")
    if envelope["targetRule"] != "scale-padding-by-petal-thickness":
        _fail("outerWarpEnvelope.targetRule is invalid")
    if envelope["overflowPolicy"] != "expand-transparent":
        _fail("outerWarpEnvelope.overflowPolicy is invalid")
    neutral_target = target_outer_envelope_radius(inner, outer, radius, inner, outer)
    if not math.isclose(neutral_target, radius, abs_tol=COORDINATE_TOLERANCE):
        _fail("neutral outer warp envelope must reconstruct its authored R0")


def _validate_identity_states(document: Mapping[str, Any], referenced_assets: set[str]) -> None:
    states = _object(document["identityStates"], "manifest.identityStates")
    slot_count = int(document["slotCount"])
    required_names = {"idle", *(f"selected-{slot}" for slot in range(1, slot_count + 1))}
    if set(states) != required_names:
        missing = sorted(required_names - states.keys())
        extra = sorted(states.keys() - required_names)
        if missing:
            _fail(f"missing identity state(s): {', '.join(missing)}")
        _fail(f"selected state count mismatch; unexpected state(s): {', '.join(extra)}")
    for name, value in states.items():
        state = _object(value, f"identityStates.{name}")
        _closed(state, f"identityStates.{name}", ("slotId", "asset"))
        slot_id = state["slotId"]
        if name == "idle":
            if slot_id is not None:
                _fail("identityStates.idle.slotId must be null")
        else:
            match = SELECTED_STATE_RE.fullmatch(name)
            expected = int(match.group(1)) if match else -1
            if _integer(slot_id, f"identityStates.{name}.slotId") != expected:
                _fail(f"identity state/slot mismatch for {name}")
        referenced_assets.add(_safe_package_path(state["asset"], f"identityStates.{name}.asset"))


def _validate_masks(
    document: Mapping[str, Any], referenced_assets: set[str]
) -> dict[str, Mapping[str, Any]]:
    masks = _array(document["masks"], "manifest.masks", nonempty=True)
    canvas = document["referenceCanvas"]
    result: dict[str, Mapping[str, Any]] = {}
    for index, value in enumerate(masks):
        mask = _object(value, f"masks[{index}]")
        _closed(mask, f"masks[{index}]", ("id", "asset", "coordinateSpace", "width", "height"))
        mask_id = _identifier(mask["id"], f"masks[{index}].id")
        if mask_id in result:
            _fail(f"duplicate mask id: {mask_id}")
        if mask["coordinateSpace"] != "reference":
            _fail(f"mask {mask_id} must use reference coordinate space")
        if _integer(mask["width"], f"masks[{index}].width") != canvas["width"] or _integer(
            mask["height"], f"masks[{index}].height"
        ) != canvas["height"]:
            _fail(f"mask dimension/metadata violation: {mask_id}")
        referenced_assets.add(_safe_package_path(mask["asset"], f"masks[{index}].asset"))
        result[mask_id] = mask
    return result


def _validate_semantic_layers(
    document: Mapping[str, Any],
    masks: Mapping[str, Mapping[str, Any]],
    referenced_assets: set[str],
) -> None:
    layers = _array(document["semanticLayers"], "manifest.semanticLayers", nonempty=True)
    slot_count = int(document["slotCount"])
    layer_ids: set[str] = set()
    ownership: set[tuple[str, int | None]] = set()
    z_indexes: set[int] = set()
    role_counts: dict[tuple[str, int | None], int] = {}
    for index, value in enumerate(layers):
        layer = _object(value, f"semanticLayers[{index}]")
        _closed(layer, f"semanticLayers[{index}]", ("id", "role", "slotId", "asset", "zIndex"), ("maskId",))
        layer_id = _identifier(layer["id"], f"semanticLayers[{index}].id")
        if layer_id in layer_ids:
            _fail(f"duplicate semantic layer id: {layer_id}")
        layer_ids.add(layer_id)
        role = _string(layer["role"], f"semanticLayers[{index}].role")
        if role not in GLOBAL_ROLES | SLOT_ROLES:
            _fail(f"unknown semantic role: {role}")
        slot_id_value = layer["slotId"]
        slot_id: int | None
        if role in GLOBAL_ROLES:
            if slot_id_value is not None:
                _fail(f"{role} must not own a slot")
            slot_id = None
        else:
            slot_id = _integer(slot_id_value, f"semanticLayers[{index}].slotId")
            if not 1 <= slot_id <= slot_count:
                _fail(f"{role} slotId must be in 1..{slot_count}")
        owner = (role, slot_id)
        if owner in ownership:
            _fail(f"duplicate semantic ownership: {role}({slot_id})")
        ownership.add(owner)
        role_counts[owner] = role_counts.get(owner, 0) + 1
        referenced_assets.add(_safe_package_path(layer["asset"], f"semanticLayers[{index}].asset"))
        z_index = _integer(layer["zIndex"], f"semanticLayers[{index}].zIndex")
        if z_index in z_indexes:
            _fail(f"semantic zIndex must be globally deterministic; duplicate {z_index}")
        z_indexes.add(z_index)
        mask_id = layer.get("maskId")
        if mask_id is not None:
            mask_name = _identifier(mask_id, f"semanticLayers[{index}].maskId")
            if mask_name not in masks:
                _fail(f"semantic layer {layer_id} references unknown mask: {mask_name}")
        if role == "SELECTED_EMPHASIS" and mask_id is None:
            _fail(f"SELECTED_EMPHASIS layer {layer_id} requires an explicit maskId")

    for role in GLOBAL_ROLES:
        if role_counts.get((role, None), 0) != 1:
            _fail(f"missing semantic layer: {role}")
    for slot_id in range(1, slot_count + 1):
        for role in REQUIRED_SLOT_ROLES:
            if role_counts.get((role, slot_id), 0) != 1:
                _fail(f"missing slot layer: {role}({slot_id})")


def _validate_dynamic_groups(document: Mapping[str, Any]) -> None:
    groups = _array(document["dynamicGroups"], "manifest.dynamicGroups", nonempty=True)
    slot_count = int(document["slotCount"])
    geometry = document["radialGeometry"]
    slot_angles = {
        item["slotId"]: float(item["angleDegreesClockwiseFromTop"])
        for item in geometry["slotCenters"]
    }
    group_ids: set[str] = set()
    ownership: set[tuple[str, int | None]] = set()
    z_indexes: set[int] = {int(layer["zIndex"]) for layer in document["semanticLayers"]}
    for index, value in enumerate(groups):
        group = _object(value, f"dynamicGroups[{index}]")
        _closed(
            group,
            f"dynamicGroups[{index}]",
            ("id", "role", "slotId", "zIndex", "anchor", "glyph", "label"),
        )
        group_id = _identifier(group["id"], f"dynamicGroups[{index}].id")
        if group_id in group_ids:
            _fail(f"duplicate dynamic group id: {group_id}")
        group_ids.add(group_id)
        role = _string(group["role"], f"dynamicGroups[{index}].role")
        if role not in DYNAMIC_ROLES:
            _fail(f"unknown dynamic group role: {role}")
        if role == "DYNAMIC_SLOT_CONTENT":
            slot_id = _integer(group["slotId"], f"dynamicGroups[{index}].slotId")
            if not 1 <= slot_id <= slot_count:
                _fail("DYNAMIC_SLOT_CONTENT slotId is outside the layout")
        else:
            if group["slotId"] is not None:
                _fail("DYNAMIC_SELECTED_CONTENT slotId must be null")
            slot_id = None
        owner = (role, slot_id)
        if owner in ownership:
            _fail(f"duplicate semantic ownership: {role}({slot_id})")
        ownership.add(owner)
        z_index = _integer(group["zIndex"], f"dynamicGroups[{index}].zIndex")
        if z_index in z_indexes:
            _fail(f"dynamic zIndex must be globally deterministic; duplicate {z_index}")
        z_indexes.add(z_index)
        anchor = _object(group["anchor"], f"dynamicGroups[{index}].anchor")
        _closed(anchor, f"dynamicGroups[{index}].anchor", ("radius", "angleDegreesClockwiseFromTop"))
        radius = _number(anchor["radius"], f"dynamicGroups[{index}].anchor.radius")
        angle = _number(anchor["angleDegreesClockwiseFromTop"], f"dynamicGroups[{index}].anchor.angleDegreesClockwiseFromTop")
        if role == "DYNAMIC_SLOT_CONTENT":
            if radius != 73 or not math.isclose(angle, slot_angles[slot_id], abs_tol=COORDINATE_TOLERANCE):
                _fail(f"slot dynamic group {slot_id} must use the neutral TextRadius and slot angle")
        elif radius != 0 or angle != 0:
            _fail("selected center dynamic content must use the center anchor")
        _validate_content_box(group["glyph"], f"dynamicGroups[{index}].glyph", is_label=False)
        _validate_content_box(group["label"], f"dynamicGroups[{index}].label", is_label=True)

    required = {
        *(('DYNAMIC_SLOT_CONTENT', slot) for slot in range(1, slot_count + 1)),
        ("DYNAMIC_SELECTED_CONTENT", None),
    }
    missing = required - ownership
    if missing:
        _fail(f"dynamic group coverage is incomplete: {sorted(missing, key=str)}")
    if ownership - required:
        _fail("dynamic group coverage contains unsupported ownership")


def _validate_content_box(value: Any, label: str, *, is_label: bool) -> None:
    required = ("offsetX", "offsetY", "width", "height", "rotationDegrees", "alignment")
    optional = ("authoredFontSize", "maxLines") if is_label else ()
    content = _object(value, label)
    _closed(content, label, required, optional)
    _number(content["offsetX"], f"{label}.offsetX")
    _number(content["offsetY"], f"{label}.offsetY")
    if _number(content["width"], f"{label}.width") <= 0 or _number(
        content["height"], f"{label}.height"
    ) <= 0:
        _fail(f"{label} width and height must be positive")
    _number(content["rotationDegrees"], f"{label}.rotationDegrees")
    if content["alignment"] not in {"center", "near", "far"}:
        _fail(f"{label}.alignment is invalid")
    if is_label:
        if _number(content["authoredFontSize"], f"{label}.authoredFontSize") <= 0:
            _fail(f"{label}.authoredFontSize must be positive")
        if _integer(content["maxLines"], f"{label}.maxLines") < 1:
            _fail(f"{label}.maxLines must be at least one")


def _validate_fallback_capabilities_and_hashes(
    document: Mapping[str, Any], referenced_assets: set[str]
) -> dict[str, str]:
    fallback = _object(document["fallback"], "manifest.fallback")
    _closed(fallback, "manifest.fallback", ("onInvalidCandidate", "onUnsupportedVersion", "startupThemeId"))
    if fallback["onInvalidCandidate"] != "retain-active" or fallback["onUnsupportedVersion"] != "retain-active":
        _fail("invalid fallback policy; V3 requires retain-active")
    _identifier(fallback["startupThemeId"], "manifest.fallback.startupThemeId")

    capabilities = _object(document["capabilities"], "manifest.capabilities")
    _closed(capabilities, "manifest.capabilities", ("required", "optional"))
    required = _array(capabilities["required"], "manifest.capabilities.required")
    optional = _array(capabilities["optional"], "manifest.capabilities.optional")
    if any(not isinstance(value, str) for value in required + optional):
        _fail("capability names must be strings")
    all_capabilities = required + optional
    if len(all_capabilities) != len(set(all_capabilities)):
        _fail("capabilities must be unique across required and optional sets")
    unknown = set(all_capabilities) - REQUIRED_CAPABILITIES
    if unknown:
        _fail(f"unknown capability: {', '.join(sorted(unknown))}")
    if set(required) != REQUIRED_CAPABILITIES or optional:
        _fail("V3 required capabilities must be exactly the frozen minimum set")

    transitions = _object(document["transitions"], "manifest.transitions")
    _closed(transitions, "manifest.transitions", ("mode",))
    if transitions["mode"] != "instant":
        _fail("V3 permits only instant transitions")

    raw_hashes = _object(document["assetHashes"], "manifest.assetHashes")
    hashes: dict[str, str] = {}
    casefold_paths: dict[str, str] = {}
    for path_value, hash_value in raw_hashes.items():
        path = _safe_package_path(path_value, "assetHashes key")
        folded = path.casefold()
        if folded in casefold_paths and casefold_paths[folded] != path:
            _fail(f"asset paths collide on Windows: {casefold_paths[folded]} and {path}")
        casefold_paths[folded] = path
        digest = _string(hash_value, f"assetHashes[{path}]")
        if HASH_RE.fullmatch(digest) is None:
            _fail(f"invalid SHA-256 value for {path}")
        hashes[path] = digest.lower()
    missing = sorted(referenced_assets - hashes.keys())
    orphan = sorted(hashes.keys() - referenced_assets)
    if missing:
        _fail(f"missing asset hash record(s): {', '.join(missing)}")
    if orphan:
        _fail(f"orphan asset hash record(s): {', '.join(orphan)}")
    return hashes


def validate_theme_v3_document(document: dict[str, Any]) -> ThemeV3ValidationReport:
    """Validate schema and cross-field V3 contract invariants."""

    _validate_json_schema(document)
    _validate_closed_shape(document)
    _validate_geometry(document)
    referenced_assets: set[str] = set()
    _validate_identity_states(document, referenced_assets)
    masks = _validate_masks(document, referenced_assets)
    _validate_semantic_layers(document, masks, referenced_assets)
    _validate_dynamic_groups(document)
    hashes = _validate_fallback_capabilities_and_hashes(document, referenced_assets)
    return ThemeV3ValidationReport(
        theme_id=str(document["id"]),
        layout_profile=str(document["layoutProfile"]),
        slot_count=int(document["slotCount"]),
        state_count=len(document["identityStates"]),
        semantic_layer_count=len(document["semanticLayers"]),
        dynamic_group_count=len(document["dynamicGroups"]),
        mask_count=len(document["masks"]),
        asset_count=len(hashes),
        assets_verified=False,
    )


def _resolve_package_asset(root: Path, relative_path: str) -> Path:
    candidate = root.joinpath(*PurePosixPath(relative_path).parts)
    try:
        resolved_root = root.resolve(strict=True)
        resolved_candidate = candidate.resolve(strict=True)
    except (OSError, RuntimeError) as exception:
        _fail(f"missing asset: {relative_path} ({exception})")
    try:
        resolved_candidate.relative_to(resolved_root)
    except ValueError:
        _fail(f"asset resolves outside package root: {relative_path}")
    if not resolved_candidate.is_file():
        _fail(f"missing asset: {relative_path}")
    return resolved_candidate


def _validate_asset_files(document: Mapping[str, Any], root: Path) -> None:
    if Image is None:
        _fail("Pillow is required for V3 asset validation")
    expected_size = (
        int(document["referenceCanvas"]["width"]),
        int(document["referenceCanvas"]["height"]),
    )
    mask_paths = {mask["asset"] for mask in document["masks"]}
    for relative_path, expected_digest in document["assetHashes"].items():
        asset = _resolve_package_asset(root, relative_path)
        actual_digest = hashlib.sha256(asset.read_bytes()).hexdigest()
        if actual_digest.lower() != str(expected_digest).lower():
            _fail(f"asset hash mismatch: {relative_path}")
        try:
            with Image.open(asset) as image:
                image.load()
                if image.format != "PNG":
                    _fail(f"asset must be PNG: {relative_path}")
                if image.size != expected_size:
                    label = "mask dimension/metadata violation" if relative_path in mask_paths else "asset dimension violation"
                    _fail(f"{label}: {relative_path} is {image.size}, expected {expected_size}")
                if image.mode != "RGBA":
                    _fail(f"asset must decode as RGBA: {relative_path}")
        except ThemeV3ValidationError:
            raise
        except (OSError, ValueError) as exception:
            _fail(f"unable to decode PNG asset {relative_path}: {exception}")


def validate_theme_v3(
    manifest_path: Path | str,
    *,
    check_assets: bool = False,
) -> ThemeV3ValidationReport:
    """Validate one V3 manifest and optionally its complete package asset set."""

    path = Path(manifest_path)
    document = load_theme_v3_manifest(path)
    report = validate_theme_v3_document(document)
    if check_assets:
        _validate_asset_files(document, path.parent)
        report = ThemeV3ValidationReport(**{**report.__dict__, "assets_verified": True})
    return report


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Validate a LeftPad UI Theme Package V3 manifest.")
    parser.add_argument("manifest", type=Path, help="Path to the V3 manifest.json file")
    parser.add_argument(
        "--check-assets",
        action="store_true",
        help="require, decode, dimension-check, and SHA-256-check every package asset",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    try:
        report = validate_theme_v3(args.manifest, check_assets=args.check_assets)
    except ThemeV3ValidationError as exception:
        print("V3 THEME INVALID")
        print(f"Reason: {exception}")
        return 1
    print("V3 THEME VALID")
    print(f"ID: {report.theme_id}")
    print(f"Layout: {report.layout_profile} ({report.slot_count} slots)")
    print(f"Assets verified: {'YES' if report.assets_verified else 'NO (manifest-only)'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
