# O09 normal/train Layout02 blind anchor — 2 November 2026

## Result

`O09-NORMAL-TRAIN-LAYOUT02-RAW-RECEIPT` **PASS 7/7**: all seven saved 1920×1080 PNG/receipt pairs have valid file hashes, dimensions, frame/sample and operation bindings, source PTS, timeline, calibration and analyzer identity.

`O09-NORMAL-TRAIN-LAYOUT02-BLIND-REVIEW` **PASS 1/1 selected frame**: before analyzer output was opened, the user approved the raw-only manual centres and provisional apparent source-image rim guides for all 16 visible balls in frame 1085. Chalk and table shadows were excluded. The review set no numeric centre tolerance and makes no physical cloth-plane accuracy claim.

`O09-NORMAL-TRAIN-LAYOUT02-REVIEWED-CENTRE-BASELINE` **PASS as a one-frame Development spatial screen**: current-source `.65` replay matched all 16 reviewed balls with the correct colours (TP 16, FP 0, FN 0; precision and recall 1.0). Scoring used explicit tolerance `0.010` in table-2x1 coordinates. Nearest-rank p95 centre error was `0.005487393425339883` table units. Existing `TableCalibration.Map/Unmap` produced corner round trips within `2.22e-16` table units and a maximum source-pixel round trip error of `4.43e-13 px`; inverse-projected reviewed-to-detected centre error had median `1.2821 px`, p95/max `3.9752 px`. These are descriptive for this image, not an accepted detector or physical accuracy threshold.

The replay and scoring both preserve `uncertain=true`; the observation also reports `layoutUncertain=true` and `obstructed=false`. No Ready/readiness, inventory gate, matched-control, Holdout, D1, D2, or event/clip result follows from this spatial screen. The baseline report records **zero matches with reviewed radii**, so no radius-normalized centre score is available despite approval of the displayed apparent rim guides.

## Capture and review provenance

- Operation: `b8dd4c18-3189-46d7-9b28-d0bc17f94771`; setup group `o09-v3-normal-train-layout-02`; Development only, not Holdout.
- Camera timeline: `39e4f6c1df9f44d185906b7903278514`; calibration: `3795b1fc087645aeac4c7429707741c3`.
- Selected frame: **1085**, the lower-median valid source-PTS image among frames `1040, 1055, 1070, 1085, 1100, 1115, 1131`; source PTS `6542094822 µs`; captured `2026-11-02T12:12:12.2920203Z`.
- Image: 1920×1080, SHA-256 `8220B7046F2C8B77805CA3D0E391CFB152A4492B848B65874944C4657F54EEFD`.
- Raw source receipt: SHA-256 `AF0AE102F91322587CABC1CCDB6B7EA3982FCD7EEAD36CDDE95D32FEA33BD1BE`; `detectionRequested=false`. The physical receipt was retained unchanged; detector results came from subsequent offline full-audit analysis.
- Analyzer identity: `6d4e4f3e2fc28bba1a56c637f6783e1d743e082d7807362324344a102fefa414`; width 800, packed-colour boundary extent `.65`, production extent `.65`, sparse-black extent `1.3`, minimum sparse-black support `.65625`, radius-first sparse-black ranking. Current-source `.65` is not historical captured-settings `.4` reproduction.
- Review record SHA-256 `A291BECB5015A88CA88F2C1E1C783011BBDB3AFE0F2184FCADEB2FEA33309FE8`. It states detector output was not shown, numeric centre tolerance is null, and physical cloth-plane accuracy is not claimed.
- Raw-only review overlay SHA-256 `D8E46D4495DAB6DE339D66968EB93F0BE027611A8AA5A3B9D512382A781DF6AD`; enlarged crops SHA-256 `F015EE8863B6A050F92CF82DE4DDFF170FD2674BB6D4F2935FF43CA88FC32524`.
- Baseline report SHA-256 `3352EEAD9CF270EEDB2F154634ED1328B60A3B98BCBE2E2F535AE0C252C68343`; bound detection derivative SHA-256 `C98E3C35BC93A7565F03D50BDF2061975E2283358AE1E152A2E2223A4D6ABF66`.
- Baseline-only scratch corpus manifest SHA-256 `95185996B2FE491EE83E958E8E733175B7CCC90E7430F2904D3C660F7430EDEA`; Development split lock SHA-256 `EF527D475571FB14EBD3902A701D4D2A065D2AC5805A6C55B495F7C7BA6DCF75`. This scratch corpus is not the frozen 12-layout corpus and was not exported to O08/O09.
- Transform JSON SHA-256 `D86071AB2978CE06F460D11188EA099A60383897E06F56594A34BA723B86BC7F`; projection JSON SHA-256 `163A6B3C5A22A1CDB0A111DE4142BC2F75D3F8F161E07DA869C6E18D688CBA78`.
- Offline replay manifest SHA-256 `F88465E4CC18B4CD95EB346C1ADE1045F9981F42454FC21F7F444FCE4D8B8A43`; replay report SHA-256 `6A4FE3FFC95753B21E35690AF39AF2A49F832E642FDD2A11EF2F6C5AD4C53C4E`. Mode remains `full-audit`; its source is the physical receipt with `detectionRequested=false`. No retained manifest or receipt was relabelled.

The source-pixel transform uses the existing table calibration corners, top-left origin, x-right/y-down, and table-2x1 axes. Its four mapped corners match the declared table boundary. This verifies the coordinate conversion, not camera-pose continuity or ball-centre position on the cloth plane.

## Verifier and timing result

The bounded physical verifier **FAIL**ed with `Unsettled (TimedOut)` after `4001.775 ms`. It saved seven verifier samples with maximum source gap `533.378 ms`; all observed adjacent gaps were below the unchanged `800 ms` ceiling, but the overall timeout is still a failure. Do not call this stillness, readiness, soak, or a successful verifier window.

Per-frame evidence-capture callbacks took `2.778–10.4456 ms`; analyzer processing took `62.0961–423.3699 ms`; source age was `652.8495–694.1638 ms`. These timings describe this physical operation only. They do not establish a before/after causal improvement or the Live Detect request-to-ready target. The selected frame 1085 measured `64.7935 ms` processing, `4.0079 ms` evidence capture, and `691.2925 ms` source age.

The verifier timing artifact is [`layout02-verifier-timing-20261102.json`](../../work/o09/device-state/layout02-verifier-timing-20261102.json), SHA-256 `5AE0520B5A556666A85CE4ED91B4BE17C77C9E17DA8BC442D31F9C1A1CB6228F`. Raw receipt audit and image review artifacts are under `work/o09/`; ignored work artifacts are intentionally referenced here rather than copied into the source commit.

## Safe boundary and queue

Fresh readback at `2026-11-02T12:34:21.6746526Z`: mounted-camera `/status` was `Stopped`, revision 9, timeline unchanged, 9,334 completed frames, zero viewers, and clip count 139. Pixel 11 Home showed **Camera ready** and enabled **Start live session**; capture and verifier actions were disabled. Installed companion was 0.8.61/code69, APK SHA-256 `9F3465F4D4A5BD3F5F848BA0D8DE299BA4D9E50AE05F7808DA8F7F033F3A0DCA`. No clip was added; Cast/TV and camera package were untouched.

Token-free safe-state artifact: [`layout02-final-safe-state-20261102.json`](../../work/o09/device-state/layout02-final-safe-state-20261102.json), SHA-256 `D8E6DCC60CD88796570448D8A036C7E7BC8C0510EB1EC4E355CBD22F2E02BBFE`.

At this checkpoint: **1/12** Development anchors independently reviewed; **2/24** planned state acquisitions attempted; **0/12** additional matched-control states acquired; criteria-complete lighting strata **0/2**; detector inventory acceptance **0/3**; Holdout unopened; D1/D2/E **0/3** proven. Layout01 still needs a replacement independent anchor because its response was proposal-conditioned. There are **23 planned state acquisitions remaining**, including that replacement. The next target is `O09-NORMAL-TRAIN-LAYOUT03-BLIND-ANCHOR-CONTROL`, using one setup group for a predeclared black/cue present/absent sequence. Capture stays stopped between each physical table change.
