<#
.SYNOPSIS
    Vitruvian installer for Windows (PowerShell).
.DESCRIPTION
    Interactive setup that writes environment variables to .env.Vitruvian
    Equivalent to scripts/install.sh for Linux/macOS.
#>
[CmdletBinding()]
param(
    [string]$Profile
)

$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$EnvFile = Join-Path $RootDir '.env.Vitruvian'
$DefaultSqliteFileConnection = 'Data Source=appdb/Vitruvian-memory.db'

function Resolve-ProfileName {
    param([string]$Value)
    switch ($Value.ToLowerInvariant()) {
        '1' { return 'dev' }
        'dev' { return 'dev' }
        '2' { return 'personal' }
        'personal' { return 'personal' }
        '3' { return 'team' }
        'team' { return 'team' }
        '4' { return 'prod' }
        'prod' { return 'prod' }
        default { throw "Invalid profile '$Value'. Use dev, personal, team, or prod." }
    }
}

function Get-CachedValue {
    param(
        [string]$Name,
        [string]$Path
    )

    if (-not (Test-Path $Path)) { return $null }

    $escapedName = [regex]::Escape($Name)
    foreach ($line in Get-Content -Path $Path) {
        if ($line -match "^\s*#") { continue }

        $value = $null
        if ($line -match "^\s*export\s+$escapedName=(.*)$") { $value = $Matches[1] }
        elseif ($line -match "^\s*\`$env:$escapedName=(.*)$") { $value = $Matches[1] }
        elseif ($line -match "^\s*$escapedName=(.*)$") { $value = $Matches[1] }
        else { continue }

        $value = $value.Trim()
        if ($value.Length -ge 2) {
            $first = $value.Substring(0, 1)
            $last = $value.Substring($value.Length - 1, 1)
            if (($first -eq "'" -and $last -eq "'") -or ($first -eq '"' -and $last -eq '"')) {
                $value = $value.Substring(1, $value.Length - 2)
            }
        }

        return $value
    }

    return $null
}

function Set-ActiveProfile {
    param([string]$Name)
    @(
        "`$env:VITRUVIAN_PROFILE='$Name'"
    ) | Set-Content -Path $EnvFile -Encoding UTF8
}

if (-not [string]::IsNullOrWhiteSpace($Profile)) {
    $resolvedProfile = Resolve-ProfileName $Profile
    $profileFile = Join-Path $RootDir ".env.Vitruvian.$resolvedProfile"
    if (-not (Test-Path $profileFile)) {
        throw "Profile '$resolvedProfile' does not exist yet. Create it first by running the installer without parameters."
    }

    Set-ActiveProfile $resolvedProfile
    Write-Host "Switched active profile to '$resolvedProfile' in $EnvFile"
    exit 0
}

Write-Host 'Vitruvian installer'
Write-Host 'Select onboarding action:'
Write-Host '  1) Create/update profile configuration'
Write-Host '  2) Switch active profile'
$onboardingAction = Read-Host '>'
Write-Host 'Select profile:'
Write-Host '  1) dev'
Write-Host '  2) personal'
Write-Host '  3) team'
Write-Host '  4) prod'
$profileChoice = Read-Host '>'
$resolvedProfile = Resolve-ProfileName $profileChoice
$ProfileEnvFile = Join-Path $RootDir ".env.Vitruvian.$resolvedProfile"

if ($onboardingAction -eq '2') {
    if (-not (Test-Path $ProfileEnvFile)) {
        throw "Profile '$resolvedProfile' has not been configured yet. Choose create/update first."
    }

    Set-ActiveProfile $resolvedProfile
    Write-Host "Active profile set to '$resolvedProfile'."
    Write-Host "Configuration saved to: $EnvFile"
    exit 0
}

if ($onboardingAction -ne '1') {
    throw 'Invalid onboarding action'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK was not found in PATH. Install .NET 10 SDK before onboarding.'
}

Write-Host 'Select model provider:'
Write-Host '  1) OpenAI'
Write-Host '  2) Anthropic'
Write-Host '  3) Gemini'
$providerChoice = Read-Host '>'

switch ($providerChoice) {
    '1' { $provider = 'openai';    $keyName = 'OPENAI_API_KEY';    $defaultModel = 'gpt-4o-mini' }
    '2' { $provider = 'anthropic'; $keyName = 'ANTHROPIC_API_KEY'; $defaultModel = 'claude-3-5-haiku-latest' }
    '3' { $provider = 'gemini';    $keyName = 'GEMINI_API_KEY';    $defaultModel = 'gemini-2.0-flash' }
    default { Write-Error 'Invalid provider choice'; exit 1 }
}

$apiKey = Read-Host "Enter $keyName"
$apiKey = $apiKey.Trim()
if ([string]::IsNullOrWhiteSpace($apiKey)) { $apiKey = Get-CachedValue -Name $keyName -Path $ProfileEnvFile }
if ([string]::IsNullOrWhiteSpace($apiKey)) { throw "$keyName is required." }
$selectedModel = Read-Host "Enter model name [$defaultModel]"
if ([string]::IsNullOrWhiteSpace($selectedModel)) { $selectedModel = $defaultModel }

Write-Host ''
Write-Host 'Select deployment mode:'
Write-Host '  1) Local console'
Write-Host '  2) Discord channel'
Write-Host '  3) WebSocket host'
$deployChoice = Read-Host '>'
if ($deployChoice -ne '1' -and $deployChoice -ne '2' -and $deployChoice -ne '3') { throw 'Invalid deployment choice' }

Write-Host ''
Write-Host 'Select memory storage:'
Write-Host '  1) Local SQLite (recommended default)'
Write-Host '  2) Third-party connection string'
$storageChoice = Read-Host '>'
switch ($storageChoice) {
    '1' { $memoryConnection = $DefaultSqliteFileConnection }
    ''  { $memoryConnection = $DefaultSqliteFileConnection }
    '2' {
        $memoryConnection = Read-Host 'Enter VITRUVIAN_MEMORY_CONNECTION_STRING'
        if ([string]::IsNullOrWhiteSpace($memoryConnection)) { throw 'A third-party connection string is required for this option.' }
    }
    default { throw 'Invalid storage choice' }
}

$lines = @(
    "`$env:VITRUVIAN_MODEL_PROVIDER='$provider'"
    "`$env:${keyName}='$apiKey'"
    "`$env:VITRUVIAN_MODEL_NAME='$selectedModel'"
    "`$env:VITRUVIAN_MEMORY_CONNECTION_STRING='$memoryConnection'"
)

if ($deployChoice -eq '2') {
    $discordToken   = Read-Host 'Enter DISCORD_BOT_TOKEN'
    $discordChannel = Read-Host 'Enter DISCORD_CHANNEL_ID'
    if ([string]::IsNullOrWhiteSpace($discordToken) -or [string]::IsNullOrWhiteSpace($discordChannel)) {
        throw 'DISCORD_BOT_TOKEN and DISCORD_CHANNEL_ID are required for Discord mode.'
    }
    $lines += "`$env:DISCORD_BOT_TOKEN='$discordToken'"
    $lines += "`$env:DISCORD_CHANNEL_ID='$discordChannel'"
}
elseif ($deployChoice -eq '3') {
    $webSocketUrl = Read-Host 'Enter VITRUVIAN_WEBSOCKET_URL [ws://0.0.0.0:5005/Vitruvian/]'
    if ([string]::IsNullOrWhiteSpace($webSocketUrl)) { $webSocketUrl = 'ws://0.0.0.0:5005/Vitruvian/' }
    $webSocketPublicUrl = Read-Host "Enter VITRUVIAN_WEBSOCKET_PUBLIC_URL [$webSocketUrl]"
    if ([string]::IsNullOrWhiteSpace($webSocketPublicUrl)) { $webSocketPublicUrl = $webSocketUrl }
    $webSocketDomain = Read-Host 'Enter VITRUVIAN_WEBSOCKET_DOMAIN [dev]'
    if ([string]::IsNullOrWhiteSpace($webSocketDomain)) { $webSocketDomain = 'dev' }
    $lines += "`$env:VITRUVIAN_WEBSOCKET_URL='$webSocketUrl'"
    $lines += "`$env:VITRUVIAN_WEBSOCKET_PUBLIC_URL='$webSocketPublicUrl'"
    $lines += "`$env:VITRUVIAN_WEBSOCKET_DOMAIN='$webSocketDomain'"
}

$lines | Set-Content -Path $ProfileEnvFile -Encoding UTF8
Set-ActiveProfile $resolvedProfile

Write-Host ''
Write-Host "Configuration saved to: $EnvFile"
Write-Host "Profile configuration saved to: $ProfileEnvFile"
Write-Host ''
Write-Host 'Next steps:'
$hasSourceLayout = (Test-Path (Join-Path $RootDir 'Vitruviansln')) -and (Test-Path (Join-Path $RootDir 'src\VitruvianCli'))
if ($hasSourceLayout) {
    Write-Host "  1. dotnet build `"$RootDir\Vitruviansln`""
    Write-Host "  2. dotnet run --framework net10.0 --project `"$RootDir\src\VitruvianCli`""
}
else {
    Write-Host '  1. Run: Vitruvian'
    Write-Host '  2. Use /help to view available commands.'
}
Write-Host ''
Write-Host 'The host loads .env.Vitruvian automatically — no need to source the file.'
Write-Host 'To switch profiles quickly, run: .\scripts\install.ps1 -Profile <dev|personal|team|prod>'
Write-Host 'If Discord variables are configured, the host will start in Discord mode automatically.'
Write-Host 'If VITRUVIAN_WEBSOCKET_URL is configured, the host will start in WebSocket mode before Discord mode.'
