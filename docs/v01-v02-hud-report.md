# V01/V02 shot HUD delivery

7 October 2026. The user chose V01/V02 while the table lighting was unsuitable for tracking work. The bounded implementation is committed and installed in the normal companion. Core, UI, native Pixel renderer and recorded-export checks pass. A prerecorded TV check also runs successfully after the user installs the isolated preview fix. Live real-candidate alignment and moving clear/expiry acceptance remain open.

## Delivered behavior

V01 extends the existing `TableScene`, `ShotAnnotations` and `ShotSceneProjection`: translucent aiming corridors; separate ghost-centre and surface-contact markers; planned rail markers for existing bank/kick routes; white incoming cue, muted teal object route and numbered candidate labels. Phone labels are compact; TV can include candidate number and honest geometric rank. Corridor width is cosmetic, not a measurement confidence interval or calibrated probability.

The candidate controls offer **Just lines**, **White direction** and **Full shot** on Phone, TV or Both. They dispatch the existing version-fenced `ShowAnnotation` command. The original flags retain their numeric meanings; independent surface selection and clear remain intact. Presets operate on the existing candidate catalogue without repeating perception. Touch and programmatic Sky-origin commands use the same coordinator; this does not establish real voice-session/tool acceptance for A02-A05.

V02 adds `geometric-stun-v1`, an explicitly assumed instantaneous post-contact direction. The supported producer removes the incoming velocity component along the contact normal: `v - n * dot(v,n)`, then draws the signed tangent. This uses an ideal elastic equal-mass collision with a stationary target, treating stun at impact as an assumption. The point-mass simplification omits rotational behavior; [OpenStax's two-dimensional collision treatment](https://openstax.org/books/college-physics/pages/8-6-collisions-of-point-masses-in-two-dimensions) documents the underlying momentum components and simplifying assumptions. The code's normalized direction is a geometric model, not a measured velocity.

The dashed amber arrow is labelled **Projected stun line · direction only**, with **Assumes stun at impact · no finish estimate**. Its display length is capped at 0.24 table units and clipped to available rail clearance; that length has no travel-distance meaning. There is no resting-point dot or finish zone. Direct shots and the first collision of an object-bank candidate can supply direction. Cue-cushion/kick or multiple-contact routes, full/near-full contact, mismatched/nonfinite geometry and inadequate rail clearance leave the projection unavailable with a bounded reason. Pace, top/back/side spin, cloth friction, throw, cushion response and pocket jaws are not modelled.

## Shared scene and history

New corridor/contact/cushion/path-role/HUD fields use scene schema 2. Positions remain normalized coordinates of the original source image, projected through the existing calibration. Live, phone/TV composition and recorded export share the native drawing code; text stays upright. Model, candidate identity, assumptions and unsupported reason travel with the scene. Existing table/source/media versions, geometry fingerprints and source-clock journal spans retain ownership of freshness and historical alignment.

Version-one recordings remain readable. Optional recorded extension fields and unspecified path roles are omitted when absent, preserving old scene bytes and fingerprints. Journals promote to schema 2 when they contain extended scenes. Mixed legacy/new history survives durable preservation, rolling trim and restart, including a historical clear. Recorded playback uses the preserved scene and transform at source PTS rather than current advice or calibration.

No periodic detector, separate overlay store, physics engine, observation event or GameState mutation is added. V03 remains gated on validated physics; V04 remains gated on trustworthy O04/O05 observed trajectories and a preserved prediction.

## Verification and evidence

| Check | Observed result and limit |
| --- | --- |
| Core checks | Pass: masks and independent Touch/Sky-origin commands, tangent sign on both cut sides, contact midpoint, bank/kick geometry, unsupported cases, bounds, old serialization, mixed schema history and durable restart. Existing source/expiry/media tests also pass. |
| Agent UI checks | Pass: actual Blazor typed tools/state mapping and existing media/presentation/replay controls. No real voice session is exercised. |
| Pixel 11 native drawing | [Six presets](evidence/v01-hud-native.json): portrait/landscape × full/lines/white direction, using the production native renderer. Overlay ink is counted before compositing the photo. Clear, expiry, delayed same-revision resurrection and unknown-schema refusal pass. |
| Pixel recorded export | [Export proof](evidence/v01-hud-export.json): each six-second file contains 180 corrected frames and 150 annotated frames. Portrait is 1080×1920; landscape is 1920×1080. Source-time HUD spans are 0–2 and 3–6 seconds; 2–3 seconds is clear. Decoded active/clear/reappeared frames were inspected; correction and branding remain during clear. |
| Prerecorded TV playback | [Receiver samples and user reply](evidence/v01-hud-cast.json): connected receiver plays the six-second exported clip and reports completion. The user answers “yes” to the combined controls/playback/readability/clear prompt. This is a controlled prerecorded check, not live moving-table acceptance. |
| Builds and normal delivery | [Delivery/provenance](evidence/v01-hud-delivery.json): sequential Lab/production/retained companion and isolated test Release builds pass. The guarded normal updater retains UID/private storage; installed APK hash matches the checked artifact. |

The visual fixture uses the original human-labelled `pixel-table.jpg`/JSON: declared ball centres are mapped through its calibration, and the existing shot planner selects a supported cut. The video is six seconds of that still, with declared historical geometry. This proves presentation, bounded model behavior and export timing; it is not a new detector run, timed real shot, held-out accuracy measurement or validation that a player achieved stun. MP4s and full PNGs are retained under `work/v01-hud-proof`, without adding videos to Git.

The test replay activity initially collided with the companion's port 8767. It now uses 8769. Automatic approval review rejected direct app force-stop and the combined isolated install/launch command with only “blocked by policy”. The isolated preview was therefore installed/launched by the user through the checked script; the separate guarded normal updater succeeded. Camera capture and personal credentials were not changed.

## Use and continue

With a fresh eligible settled-table candidate, choose the surface and a preset in Candidate shots. Clear restores that surface's live presentation. The existing stale/obstructed/uncertain/source-change gates still apply; low lighting is not permission to fabricate a shot.

For the retained prerecorded TV check:

```powershell
& 'C:\code\SkyEye\scripts\Show-HudReplayCheck.ps1' -CompanionSerial '66181FDDV001HS'
```

It validates the APK's isolated package and the exported MP4 hashes, updates only the test package, opens the portrait clip and supplies Cast/play controls for the landscape clip. This requires the already-built signed test APK and retained `work/v01-hud-proof` exports. The status endpoint is available through owned ADB forwarding on desktop port 18769.

V01/V02 implementation is delivered within these bounds; overall live acceptance remains partial. Next when lighting permits: a requested fresh real direct/bank/kick catalogue, actual touch preset selection on each surface, alignment under live correction, independent density/clear, expiry and source/layout invalidation while video moves. Retain the outstanding T13/T14 interruption/endurance/audio/resource gates. O01 real corpus/holdouts and O02.05 real-source performance keep their evidence dependencies; this presentation delivery does not close them. Sky voice and declared-history consumers can proceed separately without inventing tracking or physics evidence.
