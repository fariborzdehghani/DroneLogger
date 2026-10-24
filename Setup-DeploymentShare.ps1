[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$deployRoot = 'D:\DroneLoggerDeploy'
$shareName = 'DroneLoggerDeploy'
$firewallRuleName = 'DroneLoggerDeploy-SMB-In'
$projectRoot = $PSScriptRoot

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    Write-Host 'Windows administrator approval is required to create the network share.' -ForegroundColor Yellow
    $arguments = @(
        '-NoProfile'
        '-ExecutionPolicy'
        'Bypass'
        '-File'
        ('"{0}"' -f $PSCommandPath)
    )
    $elevatedProcess = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $elevatedProcess.ExitCode
}

try {
    New-Item -ItemType Directory -Path (Join-Path $deployRoot 'App') -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $projectRoot 'Install-DroneLogger.ps1') -Destination $deployRoot -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Install-DroneLogger.cmd') -Destination $deployRoot -Force

    $activeWifiProfiles = Get-NetConnectionProfile |
        Where-Object {
            $_.InterfaceAlias -like '*Wi-Fi*' -and
            $_.IPv4Connectivity -ne 'Disconnected'
        }

    if (-not $activeWifiProfiles) {
        throw 'No active Wi-Fi network profile was found.'
    }

    foreach ($profile in $activeWifiProfiles) {
        if ($profile.NetworkCategory -ne 'Private') {
            Write-Host "Changing Wi-Fi network '$($profile.Name)' to Private..." -ForegroundColor Cyan
            Set-NetConnectionProfile -InterfaceIndex $profile.InterfaceIndex -NetworkCategory Private
        }
    }

    $serverService = Get-Service -Name 'LanmanServer'
    if ($serverService.Status -ne 'Running') {
        Start-Service -Name 'LanmanServer'
    }

    $existingShare = Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue
    if ($existingShare -and $existingShare.Path -ne $deployRoot) {
        throw "A share named '$shareName' already points to '$($existingShare.Path)'."
    }

    $everyone = ([Security.Principal.SecurityIdentifier]'S-1-1-0').Translate([Security.Principal.NTAccount]).Value
    if (-not $existingShare) {
        Write-Host "Creating read-only share \\$env:COMPUTERNAME\$shareName ..." -ForegroundColor Cyan
        New-SmbShare `
            -Name $shareName `
            -Path $deployRoot `
            -Description 'DroneLogger test deployment files' `
            -ReadAccess $everyone `
            -CachingMode None | Out-Null
    }

    if (-not (Get-NetFirewallRule -Name $firewallRuleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule `
            -Name $firewallRuleName `
            -DisplayName 'DroneLogger deployment share (SMB-In)' `
            -Description 'Allows read-only access to the DroneLogger deployment share from the local private network.' `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort 445 `
            -Profile Private `
            -RemoteAddress LocalSubnet | Out-Null
    }
    else {
        Enable-NetFirewallRule -Name $firewallRuleName
    }

    $ipv4Addresses = Get-NetIPAddress -AddressFamily IPv4 -InterfaceIndex $activeWifiProfiles.InterfaceIndex |
        Where-Object { $_.IPAddress -notlike '169.254.*' } |
        Select-Object -ExpandProperty IPAddress

    Write-Host ''
    Write-Host 'Deployment share is ready.' -ForegroundColor Green
    Write-Host "Folder: $deployRoot"
    Write-Host "Network path: \\$env:COMPUTERNAME\$shareName"
    foreach ($address in $ipv4Addresses) {
        Write-Host "IP fallback: \\$address\$shareName"
    }
    Write-Host 'Remote access may ask for this PC user name and Windows password (a PIN is not a network password).'
}
catch {
    Write-Host ''
    Write-Host "Share setup failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

