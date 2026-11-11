# D3 on-device shadow check: plan for approval

11 November 2026. Plan only: nothing here has been run. It follows
`AGENTS.md` and `docs/device-control-guide.md` (Home actions, keeping capture
short, stop verification, stale-coordinate rules).

**Target test:** `D3-SLICE4-LIVE-WIRING` on the Pixel. **Current result:**
built and checked offline only (`d3-slice4-live-wiring.md`); per-pass cost,
memory, the mode line and real logs are unverified. **Intended outcome:**
one bounded Live Detect session in shadow that measures the cost, shows the
evidence is produced and logged, and shows Live Detect behaves exactly as
before.

State read before planning (read-only): Pixel 11 Pro XL `66181FDDV001HS`
authorised over ADB; camera `/status` `Ready`, armed, not recording, battery
100 %, 139 clips. Installed companion: 0.8.62 / code 70.

## Phase A: build and install (no capture)

1. Bump the companion to 0.8.63 / code 71 in
   `src/SkyEye.Companion.Hybrid/SkyEye.Companion.Hybrid.csproj`; commit.
2. Back up the current log before the update:
   `adb pull /sdcard/Android/data/uk.co.skyeye.companion/files/live-detect/`
   into `work/production/evidence/d3-shadow-check-20261111/before/`.
3. Build and install with the guarded installer:
   `scripts\Install-ProductionCompanion.ps1 -CompanionSerial 66181FDDV001HS`
   (Production Release). It backs up the installed APK, refuses a different
   signing certificate or a downgrade, installs in place and checks UID and
   private storage are unchanged. Record the APK SHA-256 and backup path.
4. Read back `versionCode=71`, `versionName=0.8.63`. The camera package is
   not touched. Capture stays stopped throughout this phase.

## Phase B: bounded live session (about 3 minutes of capture)

Operated from fresh screenshots each time, never stale coordinates; no Cast
or TV; no match; no new ball staging.

1. Fresh screenshot: Home shows **Start live session**; camera `/status` is
   `Ready` or `Stopped`. Tap **Start live session** once; confirm the live
   view and camera `Active`.
2. Tap **Start live detect** once; wait for the first baseline (Home shows
   the Live Detect readout).
3. **Still table, about 30 s.** Expected: settled-change state reaches
   `Settled`, one `NoReference` evidence record (first segment), no
   comparison.
4. **Control: you pass a hand over the table without touching a ball**,
   then step away for about 10 s. Expected: one comparison with **no
   ball-sized region** (any region it does find is rail- or size-rejected),
   and no proposal other than possibly `unexplained_change`.
5. **Optional, only if you are playing anyway: one ordinary shot**, then
   wait for the table to settle. Expected: one comparison with ball-sized
   regions where balls left and arrived; shadow proposals logged, none
   applied; Live Detect's own events exactly as they would be without D3.
6. Open **Live Detect log** from Home and read **Current status**: the line
   should read "Settled-change evidence: shadow (not used) · N
   comparison(s) this run". Screenshot. **Back to home.**
7. Read memory once while running (read-only):
   `adb shell dumpsys meminfo uk.co.skyeye.companion`.
8. **Stop**: **Stop live detect**, then **End recording session** once from a
   fresh screenshot. Verify camera `/status` reports `Stopped` and Home shows
   **Start live session**. If a stop tap leaves it `Active`, follow the
   guide's host-recovery steps and ask you to press the named action rather
   than tapping again.

Abort and stop capture at once if the app crashes, Live Detect stops by
itself, the cost guard trips (it logs `settled_change_disabled`), or a tap
lands on the wrong control.

## Phase C: read back (offline, capture stopped)

Pull `live-detect/live-detect.jsonl` and `live-detect/settled-change/` and
check, for this run only:

| Check | Pass condition |
|---|---|
| Install | code 71 installed in place; storage identity unchanged |
| Evidence produced | every frame has `settledChange`; states move through Moving, Settling, Settled |
| Cost | per-pass `computeMs` median 40 ms or less (guard not tripped); report p50, p95 and the comparison passes separately |
| Memory | report the `dumpsys meminfo` total; expect roughly 15 MB more than before |
| Control comparison | the hand-pass comparison has no ball-sized region, or each one is explained |
| Shot comparison (if done) | ball-sized regions at the balls that moved; proposals present, `applied` false |
| Sidecars | pairs saved only for comparisons that qualify, about 70 KB each, within caps |
| Behaviour unchanged | `--golden-shadow` on the pulled log passes (reducer output identical without the evidence) |
| Readout | the activity-screen line matches the run; Home layout unchanged |
| Stop | `/status` `Stopped`, Home **Start live session** |

Then update the plan log, the Live Detect plan and the ledger with the
newly executed physical result, kept separate from the software and
detector metrics.

## Rollback

- The release build cannot have its preference changed from outside (not
  debuggable), so there is no remote "off" switch. The run is bounded by the
  cost guard and the observer catches its own errors.
- If the build misbehaves, reinstall the previous behaviour as a new
  version (rebuild commit `a5a5718` or earlier with code 72); the installer
  refuses downgrades by design. The backup APK from step A3 is kept for
  reference.

## Decisions for you

1. Approve phase A (install 0.8.63 / code 71 in place)?
2. Approve phase B (about 3 minutes of capture), and will you do the hand
   pass in step 4 and, if you like, one shot in step 5? I can drive the
   taps over ADB from screenshots, or you can tap the named actions.
