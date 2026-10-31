$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$summarizer = Join-Path $PSScriptRoot '../../scripts/Summarize-VisionReplayBatch.ps1'
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('skyeye-replay-summary-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null

function Invoke-Summary($report) {
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $testDirectory 'o08-replay-proof-test.json') -Encoding utf8
    return @((& $summarizer -RunDirectory $testDirectory -Format Json -WarningAction SilentlyContinue | ConvertFrom-Json))
}
function Assert-Equal($actual, $expected, [string]$context) {
    if ($actual -ne $expected) { throw "$context expected '$expected', got '$actual'" }
}

try {
    $samples = @(
        @{ frameId = 1; detectionRequested = $true },
        @{ frameId = 2; detectionRequested = $false },
        @{ frameId = 3; detectionRequested = $true },
        @{ frameId = 4; detectionRequested = $false },
        @{ frameId = 5; detectionRequested = $true }
    )
    $observations = @(@{ circleCount = 10 }, @{ circleCount = 11 }, @{ circleCount = 12 })
    foreach ($mode in @('screening', 'probe-only')) {
        $rows = Invoke-Summary @{ mode = $mode; samples = $samples; fullAnalysis800 = @{ observations = $observations } }
        $expected = @(10, $null, 11, $null, 12)
        for ($i = 0; $i -lt $samples.Count; $i++) { Assert-Equal $rows[$i].DetectedCount800 $expected[$i] "$mode frame $($i+1)" }
    }

    # Full audit analyzes even frames whose verification detection was disabled.
    $fullObservations = @(@{ circleCount = 1 }, @{ circleCount = 2 }, @{ circleCount = 3 }, @{ circleCount = 4 }, @{ circleCount = 5 })
    $rows = Invoke-Summary @{ mode = 'full-audit'; samples = $samples; fullAnalysis800 = @{ observations = $fullObservations } }
    for ($i = 0; $i -lt $samples.Count; $i++) { Assert-Equal $rows[$i].DetectedCount800 ($i+1) "full-audit frame $($i+1)" }

    # Incomplete observations and unknown mode are ambiguous, so emit no
    # report-level results rather than assigning values to the wrong frames.
    foreach ($report in @(
        @{ mode = 'screening'; samples = $samples; fullAnalysis800 = @{ observations = @(@{ circleCount = 99 }) } },
        @{ mode = 'unknown'; samples = $samples; fullAnalysis800 = @{ observations = $fullObservations } },
        @{ mode = 'screening'; samples = @(@{frameId = 1}, @{frameId = 2}); fullAnalysis800 = @{ observations = @(@{circleCount = 99}) } },
        @{ mode = 'screening'; samples = $samples; fullAnalysis800 = @{ observations = @(@{circleCount = 99}, $null, @{circleCount = 98}) } }
    )) {
        $rows = Invoke-Summary $report
        foreach ($row in $rows) { Assert-Equal $row.DetectedCount800 $null 'ambiguous mapping' }
    }

    # An explicit per-sample analysis is independently attributable even if a
    # report-level observations array cannot be mapped.
    $rows = Invoke-Summary @{ mode = 'unknown'; samples = @(@{frameId = 7; width1000 = @{analysis = @{circleCount = 7}}}); fullAnalysis1000 = @{observations = @(@{circleCount = 99})} }
    Assert-Equal $rows[0].DetectedCount1000 7 'per-sample analysis'
    Write-Output 'Replay summary attribution checks passed.'
} finally {
    $resolved = (Resolve-Path -LiteralPath $testDirectory).Path
    if ($resolved -ne [IO.Path]::GetFullPath($testDirectory)) { throw 'Temporary test directory resolved unexpectedly; refusing cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
