# O02 atomic decoded-frame pairing

6 October 2026. The first O02 slice replaces the separate requested-analysis texture copy and heuristic timestamp read with one owned pixel/receipt acquisition. Controlled Pixel 11 proofs cover local H.264 MP4, the live TS transport, restart and the production C# frame adapter. O02's enabled active/latest tracking feed, ROI motion and real-source performance gates remain open.

## Result and ownership

The native ledger retains at most 64 metadata stamps and matches the actual surface timestamp exactly to a release entry. Missing or colliding timestamps remain unresolved. Every play, stop and release resets decoder scope; callbacks from the old generation cannot populate the new ledger. Acquisition verifies the same surface/timestamp/generation before and after the bitmap copy. Changed copies are recycled, and no nearest/latest timestamp is substituted.

The copy buffer is allocated explicitly and recycled if TextureView copying throws or fails; an untouched transparent buffer is refused. The returned receipt owns its raw bitmap until `PlayerTableFrameSource` takes it. Closing an untaken receipt recycles it. The adapter checks current camera authority/live scope, cancellation and exact timestamp equality; errors dispose acquired pixels. It retains source PTS, decoder generation, receipt ordinal and dimensions through the observation result. The command consumer also rejects a native generation that disagrees with the requested player generation. Ordinary requested analysis and explicit review-image behavior remain in place; no production tracking timer or automatic rule decision is added.

For live TS, the exact release entry selects the mapped source timestamp using the existing extractor offset and recording-status wrap epoch. This preserves the camera-clock domain while avoiding selection by wall-clock proximity. The metadata ordinal identifies a release receipt within that decoder generation; it is not a physical camera frame counter. Existing annotation/TV journalling and their acceptance gates are separate from this requested-observation change.

## Physical verification

The controlled fixture is 240 encoded frames at 640×360/30 Hz with two H.264 B frames. Each frame carries a 12-bit pixel barcode. The test independently compares that barcode with presentation/source PTS, rather than using the ledger ordinal as its expected image identity. MP4/TS hashes, actual TS origin, barcode convention and test APK hash are retained in the report. Loopback TS uses the same `/live/stream.ts` player/extractor branch as immediate viewing, with a simulated recording-clock reference and no real camera control.

| Proof | Observed result |
| --- | --- |
| [Native pixel pairing](evidence/o02-frame-pairing.json) | 125 distinct pixel-ID/time matches: 25 local MP4, 50 after stop/restart, 50 live TS; zero mismatches |
| Changed-copy rejection | Final native run refuses twelve copies whose surface changed during capture; fourteen attempts had no playing texture during preparation/restart |
| Stop/generation | Stopped acquisition unavailable; restart yields a new generation, with old-generation copies refused |
| [C# adapter](evidence/o02-frame-binding.json) | Ten raw-bitmap/source-PTS matches through the actual `PlayerTableFrameSource` and JNI bindings; identity/dimensions retained, no analysis JPEG, cancelled acquisition refused |
| [Native source clock](evidence/o02-source-clock.json) | Four TS epoch cases, exact release lookup, unrelated generation/unknown release refusal, duplicate/frozen/future journal checks |
| Host Java ledger checks | Exact match, no nearest/newest fallback, collisions, old callbacks, bounded retention and scope reset pass |
| Pure/Core checks | Observation identity survives native image disposal; a mismatched native generation cannot escape a current command; existing regression suite passes |

The first physical gate exposed an overly strict release-time check: TextureView can latch actual pixels before Media3's scheduled render deadline. Rejecting every release timestamp ahead of `now` admitted too few valid copies. The delivered gate retains exact pixel/surface identity and permits up to 100 ms early scheduling or 250 ms lateness; source age remains independently bounded. The revised barcode proof passes. Copy rejection counts are from this controlled run, not a hardware drop rate or sustained cadence measurement. [Contract and reproduction steps](o02-frame-pairing-plan.md).

## Delivery and remaining gates

Signed Release test, production, Lab and retained companion build/installation provenance is recorded in [delivery evidence](evidence/o02-delivery.json). The normal production update preserves signer, UID and private storage through the guarded installer. Test operations install only the separate vision-check package and restore the production activity; camera capture/control and Cast are not exercised by this slice. Production launch proves installation/initialization, not real-table observation accuracy.

O02.01-O02.03 foundations are delivered. Next is O02.04's explicitly enabled one-active/one-latest frame feed, scoped duplicate/gap handling and native ROI/flow measurements; then O02.05's actual mounted 1080p acquisition, 15/30/60 Hz tradeoffs, worker/acquisition/source-age timings, resources and interruption tests. The controlled 640×360 fixture does not establish those performance gates, rotated/HDR/unusual-codec coverage or complete real-source recovery. O01 still has zero real timed-shot/held-out sequences. O03 shot boundaries, O04 durable association and O05 reviewed GameState proposals follow trustworthy feed/corpus evidence. No automatic pot/foul/score accuracy is claimed.

7 October update: [O02.04 feed/ROI foundations](o02-motion-feed-report.md) are implemented with controlled Pixel proof and diagnostic-only enablement. O02.05/real-source companion acceptance is the next gate; this does not close the held-out, cadence, TV or automatic outcome limits above.
