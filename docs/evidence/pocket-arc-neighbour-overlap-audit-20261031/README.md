# Pocket-arc candidate neighbour/overlap audit — 31 October 2026

This offline audit asks whether a candidate black-ball arc can be rejected when its centre is too close to a ball the ordinary width-1000 detector already accepted. It reads the existing `seed75fullarc` positive and black-absent negative probe CSVs, joins each candidate to its saved replay proof, and hash-checks every source PNG and receipt before computing distances. It does not run the detector or change production gates.

The audit measures (a) nearest accepted-ball centre distance, (b) circle clearance using the probe's fitted arc radius and the accepted circle radius, and (c) hypothetical class-agnostic centre thresholds of 1.5R, 1.75R, 1.85R and 2R, where R is 27 canonical pixels. A positive target candidate is a saved black-classified arc candidate that passes the existing diagnostic arc checks and lies within 27px of the draft interior reference point. Every black-classified passing candidate in the black-absent control counts as a false candidate.

## Results

- Positive set: 6 repeated frames from one fixed black/cue/red layout. Five frames had at least one target candidate before the hypothetical vetoes (6 candidates total; frame 1425 had no near-label passing candidate even before filtering).
- At 1.5R, 1.75R and 1.85R, all 6 target candidates across those 5 frames survived. At 2R, 4/6 candidates across 4/5 baseline-success frames survived.
- Negative set: 6 repeated frames from a different, black-absent layout. The 1.75R rule rejected 3 of 53 passing false candidates; all 6 frames still had at least one false candidate. The circle-overlap veto also rejected only 3/53 and left false candidates in all 6 frames.
- The frame 1179 example is a real geometric overlap: candidate rank 26 at `(935.4,996.4)`, radius 20.25px, is 44.4px from an accepted yellow at `(959,1034)`, radius 28px, giving -3.9px clearance. It is 46.6px from an accepted red at `(962.4,958.4)`. The hypothetical 1.75R rule and overlap rule both reject it, but neither clears any negative frame.
- Overlap veto retained only 3/6 target candidates across 3/5 baseline-success positive frames. It would suppress valid black-plus-cue positive evidence in this sample and is not suitable as a production rule.

**Conclusion:** these hypotheses do not provide a useful or safe detector gate. Do not change production or promote the result into the plan as an acceptance finding. The positive and negative captures are from different source timelines and lighting conditions; each set contains repeated views of a fixed layout, not independent trials. Positive labels are draft interior references rather than reviewed ball centres, and the replay analyzer configuration differs from the capture configuration. The fitted radius for a partial arc may also underestimate the true ball radius. Collect a same-session, same-light positive/negative pair and repeat the measurement before considering a geometric veto.

## Reproduction and artifacts

From the repository root:

```powershell
python scripts/Audit-PocketArcNeighbourOverlap.py
```

`candidate-neighbour-audit.csv` contains each passing black-classified candidate and its nearest-ball/clearance measurements. `summary.json` records the aggregate counts and exact SHA-256 values for both source CSVs, replay proof JSON files, source PNGs and frame receipts. The source PNG hashes match the hashes in their replay CSV and proof records.

Input CSV SHA-256:

- Positive `positive-probes.csv`: `abc6ea3e6e87245a03e0f330ef168549786186a85a0fa4643721ec6462009f8e`
- Negative `negative-probes.csv`: `ac16e5553feecdebc2651e73f803d670e66194210f7827932d62ed4054feb5fa`

Replay proof JSON SHA-256:

- Positive `o08-replay-proof-seed75fullarcpos-133220-pocketarc.json`: `03e2f9f41c3103138b80f92cf47a746637ea4e951dc5e0dc91961f88aafd369d`
- Negative `o08-replay-proof-seed75fullarcneg-133415-pocketarc.json`: `f8bf4dfb23750a355fea2c77f07ca9ccd0336df3aeaa81ea62e7a3451bcebad1`

