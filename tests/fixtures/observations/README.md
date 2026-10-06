# Observation seed corpus

`v1` is the frozen O01 workbench seed. Run instructions, schema conventions and collection/review gates are in the [corpus guide](../../../docs/observation-corpus-guide.md).

- `pixel-table-still` reuses the original `../poolgame/pixel-table.jpg`, human labels and repo-associated archived C# OpenCV 4.14.0 detections. Source PTS is unavailable, so it remains a tuned still regression.
- `receipt-contracts` contains seven 8×8 Gray8 payloads with embedded source frame IDs. Duplicate, backwards, gap, reset and bad-pair cases are injected contract checks.
- `split-lock.json` freezes exact manifest/index/labels/source/payload identities. Holdout coverage is zero; synthetic fixtures never count as real shots.

Do not edit the frozen corpus to improve a result. Publish a new reviewed version and preserve earlier evidence. JSON and JSONL bytes use LF through the generator and `.gitattributes`; Gray8 payloads remain binary.
