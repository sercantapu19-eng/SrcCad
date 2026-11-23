#!/usr/bin/env python3
"""Compile and execute the real managed Core/Commands tests without MSBuild.

Usage: python tools/run-core-tests.py /path/to/dotnet-sdk-root
Useful when MSBuild process introspection is unavailable. Does not test WPF/OCCT.
Uses only the SDK's Roslyn compiler, .NET reference assemblies and .NET runtime.
"""
import json
import pathlib
import subprocess
import sys
import tempfile

root = pathlib.Path(__file__).resolve().parents[1]
sdk_root = pathlib.Path(sys.argv[1]).resolve()
def newest(path):
    return max(path.iterdir(), key=lambda p: tuple(int(v) for v in p.name.split('.')))
sdk = newest(sdk_root / 'sdk')
refs = newest(sdk_root / 'packs/Microsoft.NETCore.App.Ref') / 'ref/net8.0'
runtime = newest(sdk_root / 'shared/Microsoft.NETCore.App').name
dotnet = sdk_root / ('dotnet.exe' if sys.platform == 'win32' else 'dotnet')
with tempfile.TemporaryDirectory(prefix='srccad-tests-') as d:
    out = pathlib.Path(d)
    usings = out/'GlobalUsings.cs'
    usings.write_text('global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\nglobal using System.IO;\n')
    sources = [p for project in ('SrcCad.Core','SrcCad.Commands','SrcCad.Tests')
               for p in (root/'src'/project).rglob('*.cs')
               if 'obj' not in p.parts and 'bin' not in p.parts]
    rsp = out/'compile.rsp'
    rsp.write_text('\n'.join(['-nologo','-target:exe','-nullable:enable',f'-out:"{out / "Tests.dll"}"']+
        [f'-r:"{p}"' for p in sorted(refs.glob('*.dll'))]+
        [f'"{p}"' for p in sources+[usings]]))
    (out/'Tests.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0',
        'framework':{'name':'Microsoft.NETCore.App','version':runtime}}}))
    subprocess.run([str(dotnet),str(sdk/'Roslyn/bincore/csc.dll'),'@'+str(rsp)],check=True)
    result = subprocess.run([str(dotnet),str(out/'Tests.dll')])
    sys.exit(result.returncode)
