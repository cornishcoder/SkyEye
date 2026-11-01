# O09 reviewed-radius label round-trip — 1 November 2026

## Result

**`O09-RADIUS-LABEL-ROUNDTRIP` — PASS for Development data preparation and export.** The user-approved apparent source-image radius guides were mapped into a separate versioned derivative, validated, frozen, exported to YOLO, and audited against source hashes and normalized labels. This makes the Development export available for a bounded training experiment. It does not pass model accuracy, Holdout, Pixel resource, detector inventory, D1, D2, or E.

## Label transformation

Input: `live-detect-20261101-black-cue-development-v4`, 10 reviewed PNG stills, 10 Development sessions, 3 setup groups, 160 ball/location labels. The original corpus was not rewritten. Derivative: `live-detect-20261101-black-cue-development-v4-o09-reviewed-radii-v1` at `work/o09/radius-reviewed-corpus-v1/`.

For each reviewed centre, the derivative verifies the image hash, label hash, transform mapping, class and visibility, then uses the mapped source pixel centre: `x = sourceXpx / imageWidth`, `y = sourceYpx / imageHeight`. For each of the 155 user-approved visible-ball guides, it records `radiusX = radiusSourcePx / imageWidth` and `radiusY = radiusSourcePx / imageHeight`. Five absent-location labels retain normalized expected points without a radius. These are apparent source-image radii; no physical cloth-plane radius is asserted. Approval response, review date, guide hash, review-pack hash, source manifest hash and transform hash are stored in the derivative. The original 10 images plus 10 label files passed byte-hash immutability checks (**20/20**); derivative source-image copies matched (**10/10**).

## Export verification

The Workbench `validate`, `freeze` and `export-yolo` commands all exited 0. The frozen derivative has three setup groups: two assigned wholly to `train`, one wholly to `validation`. The report declares `sourceSplit: Development`; exported samples correctly use YOLO roles `train` or `validation`. No O01 Holdout sample is included.

`scripts/Verify-O09ReviewedRadiusExport.py` verifies the export against the derivative and manifest:

- Sample coverage: **10/10** sequences, one sample per sequence.
- Source and exported image hashes: **10/10**; PNG encoding, extension and bytes preserved for **10/10** images.
- YOLO boxes: **155/155** visible labels match the image-normalized centres and radii (cue/black/red/yellow/unknown class mapping included).
- Setup groups: **3/3** stay wholly in one train/validation role.
- Normalized label round-trip: **160/160** centres; absent points **5/5** retained without radii.

The first audit attempt failed because the verifier incorrectly expected each exported sample's split field to equal `Development`. The exporter correctly records the corpus source split at report level and the assigned `train`/`validation` role on each sample. After correcting that verifier assertion, the same export passed all four checks above. This was a verifier assumption error, not a corpus, export, or detector failure.

## Targeted tests and current status

Targeted result log for the source tree based on `8af5a24 Add read-only O09 radius review pack`:

- `O09-IMAGE-EDGE-RADIUS-GUIDES`: 155/155 guide rows accounted; 20/20 source files unchanged; user reviewed all displayed cyan circles as “all look correct.”
- `O09-RADIUS-LABEL-ROUNDTRIP`: **10/10** samples, **10/10** image hashes, **155/155** visible boxes, **3/3** groups; all Workbench validation/freeze/export commands exited 0.
- `O09-YOLO-IMAGE-ENCODING`: Observation Checks **2/2 PNG** and **1/1 JPEG** outputs preserve file extensions and exact source bytes.
- `O09-SETUP-GROUP-SPLIT-ISOLATION`: Observation Checks **8/8** focused assertions. The Observation Checks runner exited 0, including corpus/replay contracts. Workbench Release build succeeded with **0 warnings, 0 errors**.

The Observation Checks runner was run after the encoding regression was added. The broader three-suite software score (**3/3**) and focused motion-feed result (**20/20**) are carried forward, not claimed as rerun here. Detector inventory remains **0/3**; physical D1/D2/E remain **0/3 proven**. O09 now has a Development train/validation export, but no model training/comparison, Holdout data, accepted contract, or on-device benchmark has run. Continue with a bounded Development model baseline and keep Holdout unopened until the candidate and scoring protocol are frozen.
