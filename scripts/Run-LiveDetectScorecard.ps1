param([string]$OutputDirectory = 'work/live-detect-scorecard', [string]$DetectorReport = '')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDirectory > $null
$results = @()
foreach ($suite in @('SkyEye.GameState.Checks','SkyEye.Observation.Checks','SkyEye.Checks')) {
    $log = Join-Path $OutputDirectory "$suite.log"
    & dotnet run --project "tests/$suite/$suite.csproj" -c Release *> $log
    $code = $LASTEXITCODE
    $results += [pscustomobject]@{id=$suite;kind='software-suite';status=$(if($code -eq 0){'PASS'}else{'FAIL'});exitCode=$code;log=$log}
}
$detectorCases = @()
if ($DetectorReport) {
    $replay = Get-Content -LiteralPath $DetectorReport -Raw | ConvertFrom-Json
    if (-not $replay.completed -or $replay.samples.Count -eq 0) { throw 'Detector report is incomplete or empty.' }
    $expectedHashes = @{
        '1691'='fc1ad7873fa6e35d539b6b28f5fafd1fa7840691221db6cbe7408b29e1e58e3d'
        '1726'='bba5df8d3198218cd06af903e1b1071a999931e6e900463c000e0c788406544d'
        '1764'='a2b0bf0c0a51564b4c8a8e12f50a946bc7671f6b56118bda1404a7bc8085b19e'
    }
    if ($replay.samples.Count -ne 3 -or @($replay.samples.frameId | Sort-Object -Unique).Count -ne 3) { throw 'Expected exactly three distinct positive frames.' }
    foreach ($sample in $replay.samples) {
        if ($sample.pixelSha256 -ne $expectedHashes[[string]$sample.frameId]) { throw 'Reviewed pixel hash mismatch.' }
        $a = $sample.width800.analysis
        if ($null -eq $a) { throw "Missing width800 analysis: $($sample.frameId)" }
        # This contract is restricted to the reviewed 884a3abb positive group.
        if ($sample.frameId -notin @(1691,1726,1764)) { throw 'Frame outside the declared positive inventory contract.' }
        $ok = $a.circleCount -eq 16 -and $a.counts.red -eq 7 -and $a.counts.yellow -eq 7 -and $a.counts.cue -eq 1 -and $a.counts.black -eq 1 -and -not $a.uncertain
        $detectorCases += [pscustomobject]@{id="positive-inventory-$($sample.frameId)";status=$(if($ok){'PASS'}else{'FAIL'});count=$a.circleCount;uncertain=$a.uncertain;scope='inventory availability only; not centre accuracy or physical acceptance'}
    }
}
$report = [ordered]@{schemaVersion=1;createdAt=[DateTime]::UtcNow.ToString('o');commit=(& git rev-parse HEAD);unit='suite (not individual assertions)';passed=@($results|Where-Object status -eq 'PASS').Count;total=$results.Count;results=$results;detectorReport=$DetectorReport;detectorReportSha256=$(if($DetectorReport){(Get-FileHash -LiteralPath $DetectorReport).Hash});detectorCases=$detectorCases;detectorPassed=@($detectorCases|Where-Object status -eq 'PASS').Count;detectorTotal=$detectorCases.Count;physicalAcceptance=@('D1: NOT PROVEN','D2: NOT PROVEN','E: NOT PROVEN')}
$report|ConvertTo-Json -Depth 5|Set-Content (Join-Path $OutputDirectory 'scorecard.json')
$results|Format-Table id,status,exitCode
$detectorCases|Format-Table id,status,count,uncertain
Write-Host "$($report.passed)/$($report.total) software suites passed; physical acceptance remains separate."
if($report.passed -ne $report.total -or $report.detectorPassed -ne $report.detectorTotal){exit 1}
