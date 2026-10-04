param(
    [ValidateSet('Baseline', 'Hybrid', 'All')][string]$Target = 'Baseline',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot

function Invoke-SkyEyeBuild([string]$Directory, [string]$Project) {
    Push-Location -LiteralPath $Directory
    try {
        $taskExpected = (Get-Content -LiteralPath 'global.json' -Raw | ConvertFrom-Json).sdk.version
        $taskActual = (& dotnet --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $taskActual -ne $taskExpected) {
            throw "Expected SDK $taskExpected in $Directory; selected $taskActual. Install the pinned SDK before building."
        }
        if (-not (Test-Path -LiteralPath $Project -PathType Leaf)) {
            throw "Project not present yet: $Project. The Hybrid compatibility host must be created before selecting this build lane."
        }
        Write-Host "Building $Project with SDK $taskActual ($Configuration)"
        & dotnet build $Project -c $Configuration -m:1
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $Project" }
    }
    finally { Pop-Location }
}

# SDK resolution follows working directory. Never build the .NET 11 host from
# the .NET 10 solution directory or run the Android builds concurrently.
if ($Target -in @('Baseline', 'All')) { Invoke-SkyEyeBuild $taskRoot 'SkyEye.sln' }
if ($Target -in @('Hybrid', 'All')) {
    Invoke-SkyEyeBuild (Join-Path $taskRoot 'src\SkyEye.Companion.Hybrid') 'SkyEye.Companion.Hybrid.csproj'
}
