[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'App'
$target = 'D:\DroneLoggerTest'

try {
    if (-not (Test-Path -LiteralPath 'D:\' -PathType Container)) {
        throw 'The target computer does not have a D: drive.'
    }

    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Published application files were not found at $source."
    }

    $runningApp = Get-Process -Name 'DroneLogger' -ErrorAction SilentlyContinue
    if ($runningApp) {
        throw 'DroneLogger is currently running. Close it and run this installer again.'
    }

    $desktopRuntime = $null
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $desktopRuntime = & dotnet --list-runtimes 2>$null |
            Select-String -Pattern '^Microsoft\.WindowsDesktop\.App 8\.'
    }

    if (-not $desktopRuntime) {
        throw 'The .NET 8 Desktop Runtime (x64) is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and try again.'
    }

    New-Item -ItemType Directory -Path $target -Force | Out-Null

    Write-Host "Updating DroneLogger in $target ..." -ForegroundColor Cyan
    & robocopy.exe $source $target /MIR /XF 'session_*.csv' /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
    $robocopyExitCode = $LASTEXITCODE
    if ($robocopyExitCode -ge 8) {
        throw "robocopy failed with exit code $robocopyExitCode."
    }

    $executable = Join-Path $target 'DroneLogger.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "DroneLogger.exe was not found after copying files to $target."
    }

    Write-Host 'DroneLogger is up to date. Starting it now...' -ForegroundColor Green
    Start-Process -FilePath $executable -WorkingDirectory $target
}
catch {
    Write-Host ''
    Write-Host "Install/update failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

