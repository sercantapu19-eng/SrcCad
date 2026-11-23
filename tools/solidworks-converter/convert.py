#!/usr/bin/env python3
"""Offline SolidWorks -> STEP adapter for SrcCad.

Runs the external sld2step converter in an isolated per-job directory. This
prevents stale STEP files from a previous conversion being mistaken for output.
The tool is deliberately not bundled into the Android APK.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path


def emit(event: str, payload: dict) -> None:
    print(f"SRCAD_{event} " + json.dumps(payload, ensure_ascii=False), flush=True)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Convert SolidWorks SLDPRT/SLDASM files to STEP for SrcCad"
    )
    parser.add_argument("input", type=Path, help="Source .SLDPRT or .SLDASM file")
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument(
        "--converter-root", type=Path,
        default=Path(os.environ["SRCCAD_SLD2STEP_ROOT"])
        if os.environ.get("SRCCAD_SLD2STEP_ROOT") else None,
        help="Checkout of https://github.com/dsandall/sld2step",
    )
    parser.add_argument(
        "--pack-and-go", type=Path,
        help="Folder containing all referenced files for an assembly Pack-and-Go",
    )
    parser.add_argument("--freecad-cmd", help="Optional freecadcmd executable for STEP re-read validation")
    parser.add_argument("--json-report", action="store_true")
    args = parser.parse_args()

    source = args.input.expanduser().resolve()
    root = args.converter_root.expanduser().resolve() if args.converter_root else None
    output = args.output_dir.expanduser().resolve()
    pack_root = args.pack_and_go.expanduser().resolve() if args.pack_and_go else source.parent

    if not source.is_file():
        parser.error(f"Input file does not exist: {source}")
    if root is None or not root.is_dir():
        parser.error("Set --converter-root or SRCCAD_SLD2STEP_ROOT to the sld2step checkout.")

    suffix = source.suffix.lower()
    entry = {".sldprt": "sld2step.py", ".sldasm": "sldasm2step.py"}.get(suffix)
    if entry is None:
        parser.error("Supported input extensions are .SLDPRT and .SLDASM")
    script = root / entry
    if not script.is_file():
        parser.error(f"Converter entry point not found: {script}")
    if suffix == ".sldasm" and not pack_root.is_dir():
        parser.error(f"Pack-and-Go folder does not exist: {pack_root}")

    output.mkdir(parents=True, exist_ok=True)
    # A clean isolated directory means only this run's output can be returned.
    with tempfile.TemporaryDirectory(prefix="srccad-convert-", dir=output) as job:
        job_dir = Path(job)
        command = [sys.executable, str(script), str(source), "-o", str(job_dir)]
        if args.json_report:
            command.append("--json")
        emit("CONVERSION_START", {
            "source": str(source), "kind": suffix[1:], "job_dir": str(job_dir),
            "pack_and_go": str(pack_root) if suffix == ".sldasm" else None,
        })
        completed = subprocess.run(command, cwd=str(root), check=False)
        if completed.returncode:
            emit("CONVERSION_FAILED", {"exit_code": completed.returncode})
            return completed.returncode

        steps = sorted(job_dir.glob("*.step")) + sorted(job_dir.glob("*.stp"))
        if not steps:
            emit("CONVERSION_FAILED", {"reason": "No STEP output was produced"})
            return 20

        reports = []
        for step in steps:
            size = step.stat().st_size
            if size < 128:
                emit("CONVERSION_FAILED", {"reason": "STEP output is too small", "file": step.name, "bytes": size})
                return 21
            with step.open("rb") as stream:
                header = stream.read(4096).decode("latin-1", errors="ignore")
            if "ISO-10303-21" not in header:
                emit("CONVERSION_FAILED", {"reason": "STEP header not found", "file": step.name})
                return 22
            target = output / step.name
            if target.exists():
                target = output / f"{step.stem}-{os.getpid()}{step.suffix}"
            target.write_bytes(step.read_bytes())
            item = {"path": str(target), "bytes": size, "header_ok": True}

            if args.freecad_cmd:
                check_script = root / "fccheck_one.py"
                if not check_script.is_file():
                    emit("CONVERSION_FAILED", {"reason": "fccheck_one.py missing from converter checkout"})
                    return 23
                env = os.environ.copy()
                env["STEPFILE"] = str(target)
                validation = subprocess.run(
                    [args.freecad_cmd, "-c", f"exec(open({str(check_script)!r}).read())"],
                    cwd=str(root), env=env, text=True, capture_output=True, check=False,
                )
                item["freecad_exit_code"] = validation.returncode
                item["freecad_report"] = validation.stdout.strip()[-4000:]
                if validation.returncode != 0:
                    emit("CONVERSION_FAILED", {"reason": "FreeCAD STEP re-read failed", **item})
                    return 24
            reports.append(item)

        emit("CONVERSION_OK", {"outputs": reports, "validated_by_freecad": bool(args.freecad_cmd)})
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
