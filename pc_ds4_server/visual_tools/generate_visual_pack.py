#!/usr/bin/env python3
"""Package designer-exported assets into a standard LeftPad Visual Pack."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import tempfile
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

from compare_visual_pack import ComparisonError, compare_visual_pack
from validate_visual_pack import (
    SUPPORTED_SELECTION_MODE,
    SUPPORTED_VERSION,
    VisualPackValidationError,
    get_profile_contract,
    validate_visual_pack,
)


REQUIRED_CONFIG_FIELDS = (
    "id",
    "name",
    "version",
    "layoutProfile",
    "slotCount",
    "selectionAssetMode",
    "source",
    "assets",
)
REQUIRED_ASSET_FIELDS = ("base", "selected", "layout")


class GeneratorError(ValueError):
    """Raised when a Visual Pack cannot be packaged safely."""


class DuplicateConfigKeyError(ValueError):
    """Raised when theme-config.json contains a duplicate field."""


@dataclass(frozen=True)
class GenerationResult:
    output_directory: Path
    pack_id: str
    pack_name: str
    hashes: dict[str, str]
    validated: bool
    comparison_generated: bool
    warnings: tuple[str, ...]


def _unique_json_object(pairs: Iterable[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise DuplicateConfigKeyError(f"duplicate JSON field: {key}")
        result[key] = value
    return result


def _load_config(path: Path) -> dict[str, Any]:
    if not path.is_file():
        raise GeneratorError(f"theme config missing: {path}")
    try:
        text = path.read_text(encoding="utf-8-sig")
        value = json.loads(text, object_pairs_hook=_unique_json_object)
    except OSError as exception:
        raise GeneratorError(f"unable to read theme config: {exception}") from exception
    except (json.JSONDecodeError, DuplicateConfigKeyError) as exception:
        raise GeneratorError(f"invalid theme config: {exception}") from exception
    if not isinstance(value, dict):
        raise GeneratorError("invalid theme config: root must be an object")
    return value


def _require_string(container: dict[str, Any], field: str, owner: str) -> str:
    value = container.get(field)
    if not isinstance(value, str) or not value.strip():
        raise GeneratorError(f"{owner}.{field} must be a non-empty string")
    return value


def _require_integer(container: dict[str, Any], field: str, owner: str) -> int:
    value = container.get(field)
    if type(value) is not int:
        raise GeneratorError(f"{owner}.{field} must be an integer")
    return value


def _require_object(container: dict[str, Any], field: str, owner: str) -> dict[str, Any]:
    value = container.get(field)
    if not isinstance(value, dict):
        raise GeneratorError(f"{owner}.{field} must be an object")
    return value


def _require_local_filename(filename: str, owner: str) -> None:
    if (
        filename in {".", ".."}
        or "/" in filename
        or "\\" in filename
        or Path(filename).name != filename
    ):
        raise GeneratorError(f"{owner} must be a local file name: {filename}")


def _validate_config(config: dict[str, Any]) -> None:
    missing = [field for field in REQUIRED_CONFIG_FIELDS if field not in config]
    if missing:
        raise GeneratorError(
            "theme config missing required field(s): " + ", ".join(missing)
        )

    _require_string(config, "id", "config")
    _require_string(config, "name", "config")
    version = _require_integer(config, "version", "config")
    layout_profile = _require_string(config, "layoutProfile", "config")
    slot_count = _require_integer(config, "slotCount", "config")
    selection_mode = _require_string(config, "selectionAssetMode", "config")
    source = _require_object(config, "source", "config")
    assets = _require_object(config, "assets", "config")

    if version != SUPPORTED_VERSION:
        raise GeneratorError(f"unsupported config version: {version}")
    try:
        profile = get_profile_contract(layout_profile)
    except VisualPackValidationError as exception:
        raise GeneratorError(str(exception)) from exception
    if slot_count != profile.slot_count:
        raise GeneratorError(
            f"{layout_profile} requires slotCount {profile.slot_count}: found {slot_count}"
        )
    if selection_mode != SUPPORTED_SELECTION_MODE:
        raise GeneratorError(f"unsupported selectionAssetMode: {selection_mode}")

    missing_assets = [field for field in REQUIRED_ASSET_FIELDS if field not in assets]
    if missing_assets:
        raise GeneratorError(
            "config.assets missing required field(s): " + ", ".join(missing_assets)
        )
    filenames: list[str] = []
    for field in REQUIRED_ASSET_FIELDS:
        filename = _require_string(assets, field, "config.assets")
        _require_local_filename(filename, f"config.assets.{field}")
        filenames.append(filename)
    if len(set(filenames)) != len(filenames):
        raise GeneratorError("config asset output filenames must be distinct")

    reference = source.get("reference")
    if reference is not None:
        if not isinstance(reference, str) or not reference.strip():
            raise GeneratorError("config.source.reference must be a non-empty string")
        _require_local_filename(reference, "config.source.reference")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def _resolve_inputs(
    config_path: Path,
    config: dict[str, Any],
) -> tuple[dict[str, Path], Path | None, list[str]]:
    assets_directory = config_path.parent / "assets"
    assets = config["assets"]
    assert isinstance(assets, dict)
    source_paths: dict[str, Path] = {}
    for role in REQUIRED_ASSET_FIELDS:
        filename = str(assets[role])
        source_path = (assets_directory / filename).resolve()
        if not source_path.is_file():
            raise GeneratorError(f"missing {role} asset: {source_path}")
        source_paths[role] = source_path

    warnings: list[str] = []
    source = config["source"]
    assert isinstance(source, dict)
    reference_name = source.get("reference")
    reference_path: Path | None = None
    if isinstance(reference_name, str):
        candidate = (config_path.parent / reference_name).resolve()
        if candidate.is_file():
            reference_path = candidate
        else:
            warnings.append(
                f"reference image not found; comparison skipped: {candidate}"
            )
    else:
        warnings.append("reference image not configured; comparison skipped")
    return source_paths, reference_path, warnings


def _resolve_output(config_path: Path, output: str | Path | None) -> Path:
    if output is None:
        output_path = (config_path.parent / "output").resolve()
    else:
        output_path = Path(output).expanduser().resolve()

    config_directory = config_path.parent.resolve()
    assets_directory = (config_directory / "assets").resolve()
    if output_path in {config_directory, assets_directory}:
        raise GeneratorError("output directory must not replace the config or assets directory")
    if config_path.is_relative_to(output_path):
        raise GeneratorError("output directory must not contain the source workspace")
    return output_path


def _write_json(path: Path, value: dict[str, Any]) -> None:
    path.write_text(
        json.dumps(value, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def _copy_assets(
    source_paths: dict[str, Path],
    output_names: dict[str, str],
    staging_directory: Path,
) -> dict[str, str]:
    hashes: dict[str, str] = {}
    for role in REQUIRED_ASSET_FIELDS:
        source_path = source_paths[role]
        destination = staging_directory / output_names[role]
        source_hash = _sha256(source_path)
        shutil.copyfile(source_path, destination)
        destination_hash = _sha256(destination)
        if source_hash != destination_hash:
            raise GeneratorError(f"byte-identical copy verification failed: {source_path.name}")
        hashes[destination.name] = destination_hash
    return hashes


def _build_manifest(config: dict[str, Any]) -> dict[str, Any]:
    assets = config["assets"]
    assert isinstance(assets, dict)
    return {
        "id": config["id"],
        "name": config["name"],
        "version": config["version"],
        "layoutProfile": config["layoutProfile"],
        "slotCount": config["slotCount"],
        "base": assets["base"],
        "selected": assets["selected"],
        "layout": assets["layout"],
        "selectionAssetMode": config["selectionAssetMode"],
    }


def _build_verification(
    config_path: Path,
    config: dict[str, Any],
    source_paths: dict[str, Path],
    hashes: dict[str, str],
) -> dict[str, Any]:
    assets = config["assets"]
    assert isinstance(assets, dict)
    records: dict[str, Any] = {}
    for role in REQUIRED_ASSET_FIELDS:
        filename = str(assets[role])
        records[filename] = {
            "role": role,
            "file": filename,
            "sourceFile": source_paths[role].name,
            "sha256": hashes[filename],
            "sourceSha256": hashes[filename],
            "byteIdentical": True,
        }
    return {
        "generator": "generate_visual_pack.py",
        "generatorVersion": 1,
        "themeConfig": config_path.name,
        "packId": config["id"],
        "assets": records,
        "checks": {"sourceAssetsByteIdentical": True},
    }


def _remove_backup(path: Path) -> None:
    if not path.exists():
        return
    if path.is_dir():
        shutil.rmtree(path)
    else:
        path.unlink()


def _publish_staging(staging: Path, output: Path, force: bool) -> None:
    if output.exists() and not force:
        raise GeneratorError(f"output already exists; use --force: {output}")

    backup: Path | None = None
    if output.exists():
        backup = output.parent / f".{output.name}-backup-{uuid.uuid4().hex}"
        output.replace(backup)
    try:
        staging.replace(output)
    except OSError as exception:
        if backup is not None and backup.exists() and not output.exists():
            backup.replace(output)
        raise GeneratorError(f"unable to publish output directory: {exception}") from exception
    else:
        if backup is not None:
            _remove_backup(backup)


def generate_visual_pack(
    theme_config: str | Path,
    output: str | Path | None = None,
    *,
    force: bool = False,
    skip_comparison: bool = False,
    skip_validation: bool = False,
) -> GenerationResult:
    config_path = Path(theme_config).expanduser().resolve()
    config = _load_config(config_path)
    _validate_config(config)
    source_paths, reference_path, warnings = _resolve_inputs(config_path, config)
    output_path = _resolve_output(config_path, output)

    if output_path.exists() and not force:
        raise GeneratorError(f"output already exists; use --force: {output_path}")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(
        tempfile.mkdtemp(
            prefix=f".{output_path.name}-staging-",
            dir=output_path.parent,
        )
    )
    published = False
    try:
        manifest = _build_manifest(config)
        assets = config["assets"]
        assert isinstance(assets, dict)
        output_names = {role: str(assets[role]) for role in REQUIRED_ASSET_FIELDS}
        hashes = _copy_assets(source_paths, output_names, staging)
        _write_json(staging / "manifest.json", manifest)
        verification = _build_verification(
            config_path,
            config,
            source_paths,
            hashes,
        )
        _write_json(staging / "verification.json", verification)

        validated = False
        if skip_validation:
            warnings.append("explicit Visual Pack validation skipped by --skip-validation")
        else:
            try:
                validate_visual_pack(staging)
            except VisualPackValidationError as exception:
                raise GeneratorError(f"generated Visual Pack invalid: {exception}") from exception
            validated = True

        comparison_generated = False
        if skip_comparison:
            warnings.append("comparison skipped by --skip-comparison")
        elif reference_path is not None:
            try:
                compare_visual_pack(
                    reference_path,
                    staging,
                    staging / "comparison.png",
                )
            except ComparisonError as exception:
                raise GeneratorError(f"comparison generation failed: {exception}") from exception
            comparison_generated = True

        _publish_staging(staging, output_path, force)
        published = True
        return GenerationResult(
            output_directory=output_path,
            pack_id=str(config["id"]),
            pack_name=str(config["name"]),
            hashes=hashes,
            validated=validated,
            comparison_generated=comparison_generated,
            warnings=tuple(warnings),
        )
    finally:
        if not published and staging.exists():
            shutil.rmtree(staging)


def format_success(result: GenerationResult) -> str:
    lines = [
        "================================",
        "VISUAL PACK GENERATED",
        "================================",
        "",
        "ID:",
        result.pack_id,
        "",
        "Name:",
        result.pack_name,
        "",
        "Output:",
        str(result.output_directory),
        "",
        "Validation:",
        "PASS" if result.validated else "SKIPPED",
        "",
        "Comparison:",
        "CREATED" if result.comparison_generated else "SKIPPED",
        "",
        "Asset SHA-256:",
    ]
    lines.extend(f"{filename}: {digest}" for filename, digest in result.hashes.items())
    if result.warnings:
        lines.extend(["", "Warnings:"])
        lines.extend(f"- {warning}" for warning in result.warnings)
    lines.extend(["", "Human Approval:", "REQUIRED"])
    return "\n".join(lines)


def format_failure(reason: str) -> str:
    return "\n".join(
        [
            "================================",
            "VISUAL PACK GENERATION FAILED",
            "================================",
            "",
            "Reason:",
            reason,
        ]
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Package exported visual assets into a standard Visual Pack."
    )
    parser.add_argument("theme_config", help="theme-config.json path")
    parser.add_argument("--output", help="Output directory (default: config directory/output)")
    parser.add_argument("--force", action="store_true", help="Replace an existing output")
    parser.add_argument(
        "--skip-comparison",
        action="store_true",
        help="Do not generate comparison.png",
    )
    parser.add_argument(
        "--skip-validation",
        action="store_true",
        help="Explicitly skip the default Validator gate",
    )
    arguments = parser.parse_args(argv)
    try:
        result = generate_visual_pack(
            arguments.theme_config,
            arguments.output,
            force=arguments.force,
            skip_comparison=arguments.skip_comparison,
            skip_validation=arguments.skip_validation,
        )
    except GeneratorError as exception:
        print(format_failure(str(exception)))
        return 1
    print(format_success(result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
