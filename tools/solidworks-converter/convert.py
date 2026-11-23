#!/usr/bin/env python3
"""SrcCad SolidWorks-to-STEP adapter for the open-source sld2step toolchain.

The converter is intentionally an external process: its Python/OCP dependencies
are not embedded in the Android APK. Install sld2step separately and point
SRCCAD_SLD2STEP_ROOT at its checkout.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert SolidWorks part/assembly to STEP for SrcCad")
    parser.add_argument("input", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--converter-root", type=Path,
                        default=Path(os.environ.get("SRCCAD_SLD2STEP_ROOT", "")) if os.environ.get("SRCCAD_SLD2STEP_ROOT") else None)
    parser.add_argument("--json-report", action="store_true")
    args = parser.parse_args()

    source = args.input.expanduser().resolve()
    root = args.converter_root.expanduser().resolve() if args.converter_root else None
    out = args.output_dir.expanduser().resolve()
    if not source.is_file():
        parser.error(f"Input file does not exist: {source}")
    if root is None or not root.is_dir():
        parser.error("Set --converter-root or SRCCAD_SLD2STEP_ROOT to the sld2step checkout.")

    suffix = source.suffix.lower()
    if suffix == ".sldprt":
        script = root / "sld2step.py"
    elif suffix == ".sldasm":
        script = root / "sldasm2step.py"
    else:
        parser.error("Supported input extensions are .SLDPRT and .SLDASM")

    if not script.is_file():
        parser.error(f"Converter entry point not found: {script}")
    out.mkdir(parents=True, exist_ok=True)
    command = [sys.executable, str(script), str(source), "-o", str(out)]
    if args.json_report:
        command.append("--json")
    print("SRCAD_CONVERSION_START " + json.dumps({
        "source": str(source), "kind": suffix[1:], "output": str(out)
    }), flush=True)
    completed = subprocess.run(command, cwd=str(root), check=False)
    if completed.returncode:
        print(f"SRCAD_CONVERSION_FAILED exit={completed.returncode}", file=sys.stderr)
        return completed.returncode

    steps = sorted(out.glob("*.step")) + sorted(out.glob("*.stp"))
    if not steps:
        print("SRCAD_CONVERSION_FAILED no STEP output was produced", file=sys.stderr)
        return 20
    missing = [p for p in steps if p.stat().st_size < 128]
    if missing:
        print("SRCAD_CONVERSION_FAILED empty/invalid STEP candidate(s): " +
              ", ".join(str(p) for p in missing), file=sys.stderr)
        return 21
    print("SRCAD_CONVERSION_OK " + json.dumps([
        {"path": str(p), "bytes": p.stat().st_size} for p in steps
    ]), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
