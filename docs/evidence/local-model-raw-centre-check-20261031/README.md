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

**Decision:** the raw model centres are promising proposals for pocket cases, but the false extra red and unverified SkyEye acceptance keep them out of production state. Next, pass externally supplied centres through SkyEye's existing classifier, boundary and uncertainty gates on exact saved frames. Separately, a CUDA-enabled isolated benchmark could measure the 3060's offline throughput without changing the running service. Match positive and absence controls by session and lighting before drawing acceptance conclusions.

## Artifacts

- [`summary.json`](summary.json) — exact input paths and hashes, per-frame metrics, unmatched predictions and candidate details.
- [`summary.matches.csv`](summary.matches.csv) — per-ball same-colour matched centres, offsets and errors.
- [`Compare-LocalModelRawCentres.py`](../../../scripts/Compare-LocalModelRawCentres.py) — reproducible scorer.
