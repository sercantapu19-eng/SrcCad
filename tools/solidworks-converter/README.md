# SrcCad SolidWorks conversion adapter

This companion tool converts SolidWorks files to STEP outside the Android APK.
It uses the separately installed open-source `sld2step` project as a conversion
backend. SrcCad Mobile can then open the resulting STEP through its existing
OCCT reader.

## Supported conversion routes

- `.SLDPRT` -> `sld2step.py` -> STEP AP214
- `.SLDASM` -> `sldasm2step.py` -> assembly STEP, including component placements
  and sub-assemblies when the referenced part files are available to the backend.

The adapter checks that output STEP files exist and are non-empty. This is only
a packaging check; geometric validity, component counts, placements, units and
colors still need independent validation against the source assembly.

## Install backend

Follow the upstream installation instructions and licensing terms:
https://github.com/dsandall/sld2step

The upstream project documents an AGPL-3.0 dependency (`sldprt2xt`). Review
license obligations before redistributing a combined service or binary.

## Run

```bash
python3 tools/solidworks-converter/convert.py   "/path/to/part.SLDPRT"   --converter-root "/path/to/sld2step"   --output-dir "/path/to/output"

python3 tools/solidworks-converter/convert.py   "/path/to/top.SLDASM"   --converter-root "/path/to/sld2step"   --output-dir "/path/to/output"   --json-report
```

Or set `SRCCAD_SLD2STEP_ROOT` instead of passing `--converter-root`.

## Important architecture boundary

This is a desktop/server-side conversion companion, not an Android-native
SolidWorks reader and not yet a network service. Do not label SLDPRT/SLDASM as
supported in the APK until a secure upload/job/download API is implemented and
the resulting STEP is validated with real sample files. EASM/EPRT/EDRW are not
supported by this backend.
