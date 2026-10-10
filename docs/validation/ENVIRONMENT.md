# Validation evidence — 2026-10-10

- Reviewed upstream head `98646e12529e1d58fe3c60cdf69ff1a5bd3e9388`, PR #2's six changed source files and commit list. Preserved subsequently added `7ebba8c` requirements document.
- Found three compile blockers: duplicate `DistancePointToSegment`, capture of `out outerLoop` inside a lambda (CS1628), missing feature history fields.
- Linux environment: .NET SDK 8.0.425 / runtime 8.0.31 downloaded. CLI and MSBuild cannot read process metadata in this environment; both fail before the project build.
- Real Roslyn C# compiler invoked directly with SDK reference assemblies. Actual Core, Commands and Tests sources compiled; no production implementation substituted. `tools/run-core-tests.py` reproduces this method.
- Original smoke tests pass. New 60-case baseline: 36 pass, 24 fail. After fixes: 60 pass, 0 fail. Raw outputs retained.
- SM-021 was strengthened to cover a fixed center-rectangle corner, failed dimension rollback and fixed-point dragging.
- All C# source syntax checked with Roslyn, including the native-test conditional branch. Project/XAML XML parsed successfully. These checks do NOT constitute a Windows application build or UI test.
- Windows, MSVC C++/CLI and OCCT native runtime are unavailable here. Native bridge additions, keyboard/focus behavior, preview/commit UI behavior and Windows test script have NOT been executed in their target environment.
- Twelve native tests call the same production prism builder and measure real B-Rep volume. They are prepared, not passed.
- `Run-SolidAcceptance.ps1` builds the solution, builds tests with the real native DLL, runs 72 automated cases and records logs. Manual acceptance remains required even if those pass.
- No claim of complete solver, full feature history, arbitrary nested contours, persistence or complete Solid Model is made. No later module has been developed or enabled.
