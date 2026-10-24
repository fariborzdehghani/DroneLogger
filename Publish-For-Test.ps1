[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'DroneLogger.csproj'
$deployRoot = 'D:\DroneLoggerDeploy'
$deployApp = Join-Path $deployRoot 'App'
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$staging = Join-Path $artifactsRoot ("deploy-staging-{0}" -f [Guid]::NewGuid().ToString('N'))

try {
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
        throw "Project file not found: $projectFile"
    }

    if (-not (Test-Path -LiteralPath $deployRoot -PathType Container)) {
        throw "Deployment folder not found: $deployRoot. Run Setup-DeploymentShare.cmd first."
    }

    New-Item -ItemType Directory -Path $deployApp -Force | Out-Null
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    Write-Host 'Publishing DroneLogger (Release, Windows x64)...' -ForegroundColor Cyan
    & dotnet publish $projectFile `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        --output $staging `
        --nologo `
        -p:DebugType=None `
        -p:DebugSymbols=false

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $commit = 'unavailable'
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $commitOutput = & git -C $projectRoot rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $commitOutput) {
            $commit = $commitOutput.Trim()
            $dirtyOutput = & git -C $projectRoot status --porcelain 2>$null
            if ($dirtyOutput) {
                $commit += '-dirty'
            }
        }
    }

    @(
        "Published: $([DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz'))"
        "Computer: $env:COMPUTERNAME"
        "Git commit: $commit"
        'Runtime: .NET 8 Desktop Runtime (x64) required'
    ) | Set-Content -LiteralPath (Join-Path $staging 'build-info.txt') -Encoding UTF8

    Write-Host "Updating deployment files in $deployApp ..." -ForegroundColor Cyan
    & robocopy.exe $staging $deployApp /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
    $robocopyExitCode = $LASTEXITCODE
    if ($robocopyExitCode -ge 8) {
        throw "robocopy failed with exit code $robocopyExitCode."
    }

    Copy-Item -LiteralPath (Join-Path $projectRoot 'Install-DroneLogger.ps1') -Destination $deployRoot -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Install-DroneLogger.cmd') -Destination $deployRoot -Force

    Write-Host ''
    Write-Host 'Deployment completed successfully.' -ForegroundColor Green
    Write-Host "Shared location: \\$env:COMPUTERNAME\DroneLoggerDeploy"
    Write-Host 'On the test laptop, double-click Install-DroneLogger.cmd in that share.'
}
catch {
    Write-Host ''
    Write-Host "Deployment failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

