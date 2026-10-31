<#
.SYNOPSIS
Runs the isolated pocket-arc diagnostic while skipping seeds unable to reach a labelled target.

.DESCRIPTION
This wrapper always selects PocketArcProbeOnly. The explicit target-fit
acceptance radius is added to the arc probe's maximum seed-to-fit offset to
derive a conservative seed-to-label reach bound. Seeds outside that bound are
recorded as skipped and do not run the expensive partial-arc fit or classifier.
The full-sweep PocketArc mode remains available through
Run-PocketLipBoundaryAudit.ps1 with its default acceptance radius of zero.

This diagnostic has no effect on detector decisions, readiness or game state.
#>
[CmdletBinding()]
param(
    [string] $SourceRelativePath = 'work/production/evidence/live-detect-pocket-lip-20261030/o08-capture',
    [int[]] $Frames = @(399, 433),
    [Parameter(Mandatory = $true)]
    [ValidateRange(0.01, 5.0)]
    [double] $TargetFitAcceptanceRadiusInBallRadii,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100000)]
    [int] $BlackLabelX,
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 100000)]
    [int] $BlackLabelY,
    [string] $RunPrefix = 'pocketarc-target',
    [string] $Purpose = 'Targeted diagnostic: retain only sparse-black seeds that can reach the labelled fit acceptance radius.',
    [string] $EvidenceRelativePath = 'work/production/evidence/live-detect-pocket-lip-20261030/device-replays',
    [ValidateRange(1, 64)]
    [int] $SparseBlackCandidateLimit = 32,
    [ValidateRange(1, 12)]
    [double] $SparseBlackMinimumPeakRadiusFactor = 8
)

$ErrorActionPreference = 'Stop'
if ($Frames.Count -lt 1 -or $Frames.Count -gt 12) { throw 'Targeted pocket-arc replay accepts 1 to 12 frames.' }
$runner = Join-Path $PSScriptRoot 'Run-PocketLipBoundaryAudit.ps1'
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) { throw "Shared replay runner not found: $runner" }

& $runner `
    -SourceRelativePath $SourceRelativePath `
    -Frames $Frames `
    -PocketArcProbeOnly `
    -RunPrefix $RunPrefix `
    -Purpose $Purpose `
    -EvidenceRelativePath $EvidenceRelativePath `
    -BlackLabelX $BlackLabelX `
    -BlackLabelY $BlackLabelY `
    -SparseBlackCandidateLimit $SparseBlackCandidateLimit `
    -SparseBlackMinimumPeakRadiusFactor $SparseBlackMinimumPeakRadiusFactor `
    -PocketArcTargetFitAcceptanceRadiusInBallRadii $TargetFitAcceptanceRadiusInBallRadii
