param(
    [switch]$AcceptAndroidSdkLicenses,
    [switch]$SkipWorkloadInstall,
    [string]$AndroidSdkDirectory,
    [string]$JavaSdkDirectory = 'C:\Program Files\Android\openjdk\jdk-21.0.8'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskHost = Join-Path $taskRoot 'src\SkyEye.Companion.Hybrid'
if (-not $AndroidSdkDirectory) { $AndroidSdkDirectory = Join-Path $taskRoot 'work\android-sdk-11' }
if (-not $AcceptAndroidSdkLicenses) {
    throw 'Pass -AcceptAndroidSdkLicenses to authorize installing the required Google Android SDK components and accepting their licences.'
}
Push-Location -LiteralPath $taskHost
try {
    $taskPin = Get-Content -LiteralPath 'global.json' -Raw | ConvertFrom-Json
    $taskActual = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $taskActual -ne $taskPin.sdk.version) { throw 'The pinned .NET 11 SDK is required.' }
    if (-not $SkipWorkloadInstall) {
        & dotnet workload install maui-android --skip-manifest-update
        if ($LASTEXITCODE -ne 0) { throw 'MAUI Android workload installation failed.' }
    }
    $taskWorkload = (& dotnet workload --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $taskWorkload -ne $taskPin.sdk.workloadVersion) { throw "Expected workload set $($taskPin.sdk.workloadVersion); selected $taskWorkload." }
    & dotnet build -t:InstallAndroidDependencies -f net11.0-android "-p:AndroidSdkDirectory=$AndroidSdkDirectory" "-p:JavaSdkDirectory=$JavaSdkDirectory" -p:AcceptAndroidSDKLicenses=True
    if ($LASTEXITCODE -ne 0) { throw 'Android dependency installation failed.' }
}
finally { Pop-Location }
