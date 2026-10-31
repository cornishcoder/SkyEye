# Local raw-model centre comparison — 31 October 2026

This exact-frame diagnostic compares PoolTableModeller's saved local-model centres against SkyEye's provisional manual centres in raw 1920×1080 pixels. Both the model output and labels were hash-checked against their source image. Matching is greedy, one-to-one and same-colour, within one nominal raw ball radius (27px). It measures proposal geometry only; it does not run SkyEye classification, boundary fitting, acceptance, readiness or table-state logic. The manual centres are still draft.

## Results

| Frame | Model count | Same-colour draft labels matched | Median / max centre error | Other result |
| --- | ---: | ---: | ---: | --- |
| 1365, daylight; black present | 16 (7 red, 7 yellow, black, cue) | 15/15 non-black labels | 3.35px / 10.01px | Black centre `(99,897)` is 6.71px from approximate interior point `(102,903)`; this is not a reviewed centre label. One physics correction adjusted two balls. |
| 978, dim pocket; black absent | 16 (8 red, 7 yellow, cue) | 15/15 labels | 1.49px / 2.72px | Extra red `chromatic-peak` at `(283,987)`; expected inventory is 15. |

The black proposal on frame 1365 came from the hand-written `pocket-black-specular` recovery, not either YOLO model. Frame 978 shows why the model output cannot be used directly as table truth: it returns all labelled balls at close centres, but adds a red at an empty-pocket control.

The saved comparison reports 4,572ms and 4,671ms end-to-end. The local service process that handled the comparison runs Python 3.13 with `torch 2.15.0+cpu`; `torch.cuda.is_available()` is false, despite an RTX 3060 Laptop GPU being present. Its `/health` response says `CPU`, but that field is hard-coded. The model call omits a device argument; this CPU-only PyTorch build means that process cannot use CUDA. The comparison manifest does not itself record the execution device, so the CPU attribution is based on the still-running service process and its interpreter.

The service listens on `127.0.0.1:5001`; a phone cannot directly reach that loopback endpoint. Any remote inference design would need an explicit network transport and latency/privacy review. Do not treat this laptop timing as a Pixel 11 benchmark.

**SkyEye handoff result:** the model centres have now been replayed through SkyEye's existing candidate path on six repeated daylight positive frames and six repeated dim empty-pocket controls. SkyEye correctly classifies the cue and black proposals in daylight, but rejects both because their partial pocket-side boundaries score below 24/32 support (the black has no fit in two frames). On the empty-pocket controls, the extra `chromatic-peak` red is rejected at the table margin; an exact-frame classifier probe also labels it `unknown`. These are separate capture timelines, not matched lighting controls, and do not justify changing production gates. Full method, per-frame outcomes and hashes are in [the proposal gate replay](../local-raw-proposal-gates-20261031/README.md).

**Decision:** keep local model output diagnostic-only. The most useful next experiment is to use the mapped centres to seed a pocket-aware partial-arc fit, while retaining the current global support gate and checking against a same-light empty-pocket control. The 4.57s/4.67s measurements came from CPU-only PyTorch even though the laptop has an RTX 3060; they are not GPU timings. A separate isolated CUDA benchmark can quantify the 3060 later without modifying the running service.

## Artifacts

- [`summary.json`](summary.json) — exact input paths and hashes, per-frame metrics, unmatched predictions and candidate details.
- [`summary.matches.csv`](summary.matches.csv) — per-ball same-colour matched centres, offsets and errors.
- [`Compare-LocalModelRawCentres.py`](../../../scripts/Compare-LocalModelRawCentres.py) — reproducible scorer.
