# V01/V02 shot HUD delivery

7 October 2026. The bounded V01/V02 implementation is committed and installed in the normal companion. Core, UI, native Pixel renderer and recorded-export checks pass. The latest live run verifies a real direct-shot HUD, independent phone/TV presets, clear and expiry while source time advances. The user confirms the TV markers are **aligned and readable**. [Live acceptance evidence](evidence/v01-hud-live-acceptance.json) records the actual checks and final normal Release; broader detector, physical bank/kick and T13/T14 acceptance remain open.

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

V01/V02 implementation is delivered within these bounds. Direct-shot live alignment, independent preset density/clear and timed expiry now pass as recorded below. Broader physical acceptance remains partial: bank/kick arrangements, individual Phone/TV target-selector touch coverage, and real movement/source/layout invalidation correlation remain. Retain the outstanding T13/T14 interruption/endurance/audio/resource gates. O01 real corpus/holdouts and O02.05 real-source performance keep their evidence dependencies; this presentation delivery does not close them. Sky voice and declared-history consumers can proceed separately without inventing tracking or physics evidence.

## Earlier live follow-up and shiny-only TV policy

The first live attempt reports no candidates. Two requested observations 2.8 seconds apart establish settlement and produce three choices. Correction from the O08 planning audit: the live session uses a fifteen-second maximum observation gap; the tracker default is six seconds, and the UI advises repeating within six. The successful live pair was 2.8 seconds apart, so it did not measure either maximum. The earlier six-second maximum claim was incorrect. The UI now shows the table-check status alongside candidates and explains the repeat timing. One sample reports seven reds and six yellows while the visible table has seven of each; subsequent counts change. This is a presentation/flow check, not detector accuracy acceptance.

Inspection finds a live-loop race: a requested catalogue could be cleared before the subsequent show command because both surfaces were still in live mode. The loop now retains fresh requested catalogues independently of display mode; existing expiry/source/layout checks still clear stale ones. Core checks verify that clearing both surfaces retains a fresh selectable catalogue. Render refusals also log their bounded validation reason.

The user reports missing TV correction/watermark and then explicitly requires the shiny view only. The normal live-TV policy now always requests corrected, branded composition. The original-view toggle is removed, and central Touch/Sky commands cannot disable TV correction. Phone framing remains independently selectable and persisted. Transient shots/catalogues are not restored after restart. Missing valid calibration is disclosed; no guessed rectangle is substituted.

The shiny-only Release is installed through the guarded updater. The user confirms **both** TV watermark and corrected perspective are back. A subsequent opt-in diagnostics launch/API check is rejected by automatic approval review with only “blocked by policy”; no part of that combined command executes. The attempted local diagnostics connection had also returned a prematurely ended response, so its live admission/preset checks are not counted as passed. The temporary installer diagnostic-launch switch is removed, and normal Release is restored. [Live follow-up evidence](evidence/v01-hud-live-followup.json) distinguishes these results from the earlier native/export fixtures. Real moving HUD alignment, physical preset parity and live clear/expiry remain open.

Live correction currently restores compatible saved camera calibration. Connecting does not detect or validate corners again. **Calibrate table** reads a fresh frame, runs the existing corner detector and opens its proposed corners for review. Camera identity/resolution/configuration fences refuse incompatible saved data, but an unchanged stream configuration does not establish that the physical mount stayed still. A fresh-on-connect corner-validation policy is separate future work, not implemented by this HUD slice.

## Latest live acceptance — 7 October 2026

This run exercises the real camera and physical table through the existing native decode, requested detector, typed coordinator and shared scene. Two matching requested observations produce sixteen detections and three candidates. A supported direct cut supplies the actual live HUD; the phone captures and decoded corrected TV frame are visually inspected. The user first confirms TV visibility, then answers **“Aligned and readable”** to the marker alignment and label question. [Live acceptance evidence](evidence/v01-hud-live-acceptance.json) contains compact samples, artifact hashes, exact-frame fixture results and final installation identity.

| Live check | Result and boundary |
| --- | --- |
| Independent presets | Phone and TV each render lines (2 paths/3 markers), white direction (2/1), full shot (3/4) for the supported cut. TV changes leave the phone's white-direction mask unchanged. Actual UI taps separately verify all three Both presets without increasing the full-analysis count; that full-ball candidate correctly omits an unsupported stun arrow. |
| Independent clear | Touch-origin phone clear removes phone advice while full TV advice remains. Sky-origin TV clear removes TV advice and keeps correction/watermark. Both can be shown again from the same still-valid catalogue. |
| Live expiry | After the Pixel's fifteen-second catalogue deadline, both native scenes and the catalogue clear while TV source PTS advances. Correction and branding remain. The deadline is measured against the device's own clock. |
| TV reader restart | TV scene-cache identity now includes its decoder generation. A diagnostics-only TV reader restart exercises the existing reconnect path; TV generation 3→4 produces a newly encoded frame with scene 2→3 and advancing source PTS, the same table/candidate and unchanged phone generation 7. Full HUD recovers about 0.57 seconds after the before sample. This is controlled decoder recovery, not network/endurance acceptance. |
| Exact decoded-frame binding | Isolated native barcode fixture: ten raw-pixel/source-PTS matches and cancellation refusal pass. Transient snapshot retries do not relax atomic pairing, scope or freshness. Requested-observation refusal is visible rather than silent. This fixture does not measure real detector accuracy. |
| Final delivery | Sequential opt-in diagnostics (12 warnings), retained companion (14) and normal production Release (10) builds have zero errors. Guarded normal update retains UID 10644 and private-storage identity; installed APK hash matches. Normal live phone/Cast are restored, loopback diagnostics are absent and owned ADB forwards removed. Camera capture stays active without capture-control changes. |

The successful opt-in diagnostics checks supersede the earlier blocked/failed diagnostics attempt for these exercised gates; its historical record is retained. Programmatic Touch/Sky origins test the real coordinator, while the separate UI taps establish Both-preset wiring. No real voice session is exercised.

Intermittent circle count/colour variation still exists. Physical balls being still does not establish matching detector layouts, and a reset is not evidence of observed ball motion. The two-second settlement span is unchanged. The live session actually allows a fifteen-second observation gap; the tracker default and confirmation/correction freshness use six seconds. O08 will reconcile the distinct policies and UI/docs; it will not obtain reliability by silently accepting stale or interrupted evidence. Compatible stored calibration is reused; no fresh-on-connect corner policy is implemented.

Remaining acceptance is physical bank/kick arrangements, individual Phone/TV target-selector touch coverage, real movement/source/layout invalidation correlation, and the broader T13/T14 network recovery/endurance/room-audio/resource gates. O01 real timed corpus/holdouts and O02.05 sustained real-source tracking performance remain open. The bounded geometric-stun assumptions remain in force: no measured spin/pace or finish estimate. O08 mounted-camera observation reliability is now the next P0 mission before A02.01 voice delivery; see the [detailed plan](o08-mounted-table-reliability-plan.md).
