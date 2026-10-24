[CmdletBinding()]
param(
    [Parameter(DontShow)]
    [string]$ResultFile
)

$ErrorActionPreference = 'Stop'

$shareName = 'DroneLoggerDeploy'
$readerName = 'DroneLoggerReader'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    $temporaryResult = Join-Path $env:TEMP ("DroneLoggerReader-{0}.json" -f [Guid]::NewGuid().ToString('N'))
    try {
        Write-Host 'Windows administrator approval is required to create the read-only deployment account.' -ForegroundColor Yellow
        $arguments = @(
            '-NoProfile'
            '-ExecutionPolicy'
            'Bypass'
            '-File'
            ('"{0}"' -f $PSCommandPath)
            '-ResultFile'
            ('"{0}"' -f $temporaryResult)
        )
        $elevatedProcess = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        if (-not (Test-Path -LiteralPath $temporaryResult -PathType Leaf)) {
            throw 'The account setup did not return credentials.'
        }

        $result = Get-Content -LiteralPath $temporaryResult -Raw | ConvertFrom-Json
        if ($elevatedProcess.ExitCode -ne 0) {
            throw "Elevated account setup failed: $($result.Error)"
        }
        Write-Host ''
        Write-Host 'Read-only deployment account is ready.' -ForegroundColor Green
        Write-Host "User name: $($result.UserName)"
        Write-Host "Password:  $($result.Password)"
        Write-Host "Share:     \\$env:COMPUTERNAME\$shareName"
    }
    finally {
        if (Test-Path -LiteralPath $temporaryResult) {
            Remove-Item -LiteralPath $temporaryResult -Force
        }
    }
    exit 0
}

try {
    if (-not (Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue)) {
        throw "The '$shareName' share does not exist. Run Setup-DeploymentShare.cmd first."
    }

    $randomBytes = New-Object byte[] 12
    $randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomGenerator.GetBytes($randomBytes)
    }
    finally {
        $randomGenerator.Dispose()
    }

    $randomText = [Convert]::ToBase64String($randomBytes).TrimEnd('=').Replace('+', 'K').Replace('/', 'M')
    $plainPassword = "DLr-$randomText!9"
    $securePassword = ConvertTo-SecureString $plainPassword -AsPlainText -Force

    $reader = Get-LocalUser -Name $readerName -ErrorAction SilentlyContinue
    if ($reader) {
        Set-LocalUser -Name $readerName -Password $securePassword -PasswordNeverExpires $true -UserMayChangePassword $false
        Enable-LocalUser -Name $readerName
    }
    else {
        New-LocalUser `
            -Name $readerName `
            -Password $securePassword `
            -Description 'Read-only DroneLogger deployment access' `
            -AccountNeverExpires `
            -PasswordNeverExpires `
            -UserMayNotChangePassword | Out-Null
    }

    $usersGroupSid = [Security.Principal.SecurityIdentifier]'S-1-5-32-545'
    $readerAccount = "$env:COMPUTERNAME\$readerName"
    $isUsersMember = Get-LocalGroupMember -SID $usersGroupSid -ErrorAction SilentlyContinue |
        Where-Object Name -eq $readerAccount
    if (-not $isUsersMember) {
        Add-LocalGroupMember -SID $usersGroupSid -Member $readerName
    }

    $everyone = ([Security.Principal.SecurityIdentifier]'S-1-1-0').Translate([Security.Principal.NTAccount]).Value
    Revoke-SmbShareAccess -Name $shareName -AccountName $everyone -Force -ErrorAction SilentlyContinue | Out-Null
    Grant-SmbShareAccess -Name $shareName -AccountName $readerAccount -AccessRight Read -Force | Out-Null

    if (-not $ResultFile) {
        throw 'No result-file path was supplied by the non-elevated setup process.'
    }

    [pscustomobject]@{
        UserName = $readerAccount
        Password = $plainPassword
    } | ConvertTo-Json | Set-Content -LiteralPath $ResultFile -Encoding UTF8
}
catch {
    if ($ResultFile) {
        [pscustomobject]@{
            Error = $_.Exception.Message
        } | ConvertTo-Json | Set-Content -LiteralPath $ResultFile -Encoding UTF8
    }
    Write-Host "Reader-account setup failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
