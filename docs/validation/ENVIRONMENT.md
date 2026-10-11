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

## Windows deployment follow-up

User reported Windows core tests 60/60 PASS followed by FileNotFoundException for SrcCad.Kernel.Occt.dll before native acceptance could run. No native acceptance PASS has been reported.

The original script copied only OCCT/bin DLLs explicitly and guessed the bridge location from the application output. The fixed build exports actual TargetPath + ijwhost.dll paths from the C++/CLI project. Native tests use a separate native output/intermediate directory so BASLAT and core-only builds cannot accidentally run an older native test executable. The test project sets Copy Local and explicitly deploys the bridge, ijwhost and all OCCT runtime DLLs to its own output. Test TargetPath is likewise read from an MSBuild manifest.

Preflight checks x64 PE headers and compares source/destination SHA256 hashes for the bridge, ijwhost and linked OCCT libraries. Test execution uses its output directory and a temporary process PATH; PATH is restored afterward. stdout/stderr are captured separately so Windows PowerShell 5.1 cannot cut off the CLR exception as NativeCommandError. The script requires all SM-061..072 PASS lines before reporting native success.

Validation here: 8/8 PowerShell deployment filesystem-fixture tests passed, all managed core tests still pass, C# and PowerShell syntax checks pass, project XML parses. These fixture DLLs are NEVER loaded or used as native geometry substitutes. Actual Windows C++/CLI/OCCT execution remains pending.

Re-run tools/Run-SolidAcceptance.ps1 on Windows. If it fails, logs/solid-deployment.log, solid-native-paths.txt, solid-test-path.txt, solid-stdout.log and solid-stderr.log identify exactly which files and outputs were used.

Microsoft reference for the ijwhost requirement: https://learn.microsoft.com/en-us/dotnet/core/porting/cpp-cli
