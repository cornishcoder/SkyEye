$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskJdk = 'C:\Program Files\Android\openjdk\jdk-21.0.8\bin'
$taskOutput = Join-Path $taskRoot 'work/o02-java-checks'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
& (Join-Path $taskJdk 'javac.exe') -d $taskOutput (Join-Path $taskRoot 'src/SkyEye.Companion/DecodedFrameLedger.java') (Join-Path $taskRoot 'tests/native/DecodedFrameLedgerChecks.java')
if ($LASTEXITCODE -ne 0) { throw 'Native frame ledger compile failed.' }
& (Join-Path $taskJdk 'java.exe') -cp $taskOutput uk.co.skyeye.companion.nativebridge.DecodedFrameLedgerChecks
if ($LASTEXITCODE -ne 0) { throw 'Native frame ledger checks failed.' }
