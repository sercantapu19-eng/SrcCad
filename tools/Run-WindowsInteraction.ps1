param([Parameter(Mandatory=$true)][string]$AppPath,[string]$ReportPath=(Join-Path $PWD 'windows-interaction.json'))
$ErrorActionPreference='Stop'
if(-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core'){throw 'Interactive Windows desktop required.'}
$AppPath=(Resolve-Path $AppPath).Path
$ReportPath=[IO.Path]::GetFullPath($ReportPath)
if(Test-Path $ReportPath){Remove-Item $ReportPath}
$previous=$env:SRCCAD_UI_REGRESSION_REPORT
try {
    $env:SRCCAD_UI_REGRESSION_REPORT=$ReportPath
    # The interactive foreground launcher grants the child permission to activate.
    # Do not spoof Alt keys or attach to another application's input thread.
    if(-not ('SrcCadAcceptance.Foreground' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace SrcCadAcceptance {
    public static class Foreground {
        [DllImport("user32.dll", SetLastError=true)]
        public static extern bool AllowSetForegroundWindow(uint processId);
    }
}
'@
    }
    $process=Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path $AppPath) -PassThru
    $activationGranted=[SrcCadAcceptance.Foreground]::AllowSetForegroundWindow([uint32]$process.Id)
    Write-Host "UI activation grant: PID=$($process.Id), accepted=$activationGranted"
    if(-not $process.WaitForExit(60000)){
        $process.Kill()
        $last=if(Test-Path $ReportPath){Get-Content -Raw $ReportPath}else{'No startup checkpoint written'}
        throw "UI regression timed out (60s). Last checkpoint: $last"
    }
    if(-not(Test-Path $ReportPath)){throw 'Application produced no UI regression report'}
    $report=Get-Content -Raw $ReportPath | ConvertFrom-Json
    $report | ConvertTo-Json -Depth 5
    if($process.ExitCode -ne 0 -or $report.Failed -ne 0 -or -not $report.Completed -or $report.Passed -lt 108){throw 'Windows interaction regression failed'}
} finally { $env:SRCCAD_UI_REGRESSION_REPORT=$previous }
