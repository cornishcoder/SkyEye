# D3 slice 1: settled-change comparer (image side)

11 November 2026, branch `claude/live-detect-d1-d3`. First buildable slice of
[D3](d3-settled-change-design.md): compare two settled table rasters and
report change regions, gates and a change grid. **Not wired into Live
Detect**: no settle state machine, reducer rules (U1 to U5), logging or
setting yet, so nothing on the device changes.

**Target test:** design section 5.3 A, the 33 staged still pairs from T4.
**Before:** no C# implementation; the Python half-scale reference
(`work/evidence-audit/d3/stills-scale.csv`) passes. **Outcome:** the C# port
passes every criterion and matches Python on 31 of 33 pairs.

## Result

| Criterion (design 5.3 A) | Target | C# |
|---|---|---|
| Black-change pairs hit | 9/9 | **9/9** |
| Same-state controls with any ball-sized region | 0/12 | **0/12** |
| Cross-session pairs gated `lighting_drift` | 3/3 | **3/3** (drift 0.112, 0.342, 0.191) |
| Other pairs gated | 0 | **0** (highest 0.067) |
| Cue-change pairs hit (reported, not gated) | 7/11 in Python | **7/11** |

Against Python, pair by pair: drift within 0.0003 on all 33. Two pairs differ:

- `mid_restored-288_present2831-2831` (both frames have the black; different
  sessions): C# reports a ball-sized region at the black, Python does not.
  The region's inscribed radius is 0.61 ball radii, against the 0.60 "thin"
  limit; it is dark before and after, direction unclear. The black was
  likely re-placed between stagings (Python also measures strong change
  there, p95 0.814). A boundary case, not a false region on a control.
- `opp_cueabs-562_restaged-1691`: 7 unexpected regions against 9. Gated
  `lighting_drift` in both, so it would be discarded either way.

Likely sources of small differences: the C# rectification warps with linear
interpolation (as the design specifies for live use), the Python reference
with area interpolation; float residuals are not quantised in either.

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors (the
comparer compiles against the Android binding and the desktop shim), no
warnings in the new files; `SkyEye.Checks` (with the new D3 maths checks) and
`SkyEye.AgentUi.Checks` pass.

Desktop compute, OpenCV 4.13 through the T2 shim: median 52 ms per
comparison including the managed change map (not optimised; the design's
estimate for the change map alone was about 8 ms in numpy). Not measured on
the phone.

## What was built

- `src/SkyEye.Core/Analysis/SettledChangeContracts.cs`: `SettledChangeSettings`
  (the design's half-scale values), `SettledChangeRegion`,
  `SettledChangeComparison`, `ChangeDirection`. Image-free.
- `src/SkyEye.Core/Analysis/SettledChangeMath.cs`: managed parts. Log
  ratio, per-tile median gain field resized as OpenCV `INTER_LINEAR`, 3x3
  Gaussian blur (reflect 101), drift, zones, raster-to-table mapping,
  region rule, direction, cloth model and appearance hints, change grid.
- `src/SkyEye.Vision.Android/SettledChangeComparer.cs`: OpenCV parts.
  `Rectify` (decode and warp to 440x840), `MapToRaster`, `Compare`
  (morphology, connected components, distance transform per region,
  HSV hints). Uses only binding calls the detector already uses; owns no
  frames and releases every Mat before returning.
- `tests/SkyEye.Checks/SettledChangeMathChecks.cs`: design 5.2 managed
  checks (gain, ramp, disc, median, blur, zones including the (1.48, 0.067)
  miss spot, mapping, region rule, direction, drift, grid).
- `tests/SkyEye.Vision.Desktop.Runner settled-change`: runs and scores the
  still pairs. `tools/evidence/d3_stills_pairs.py` writes its input from
  `work/evidence-audit/t4/stills-grey.csv`.

Departures from the design, all deliberate for this slice:

- No byte quantisation of the residual (section 2.1 step 6): float
  throughout, to match the existing Python reference rather than write a
  new one. Revisit with the live Python mirror.
- Regions carry raster coordinates as well as table coordinates, for
  scoring.
- Gates in this slice: `lighting_drift` only. `too_much_change`,
  obstruction, time-gap and window gates need the live context.

## Next slices

1. Settle state machine and reference window ring (design 2.1 steps 2 to
   5), with the live-cadence simulation (5.3 C) on the 29 and 31 October
   clips, scored on desktop.
2. Clip pairs (5.3 B): parity now on the 46 T4 pairs; scoring after T9.
3. Reducer rules in shadow (U1 to U5, proposals only) with the golden
   shadow test, then live wiring and schema 15 logging. Richard's open
   questions in the design (section 6) apply from here.

Regenerate:

    python -I tools/evidence/d3_stills_pairs.py work/d3/stills-pairs.json
    dotnet run --project tests\SkyEye.Vision.Desktop.Runner -- settled-change --pairs work\d3\stills-pairs.json --out work\d3\stills-csharp.csv --regions work\d3\stills-regions.jsonl
