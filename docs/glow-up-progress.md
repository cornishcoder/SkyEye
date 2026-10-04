# Glow-up implementation progress

The full implementation plan is in [glow-up-plan.md](glow-up-plan.md). Work starts from `f6ea29c`; production capture and the existing companion remain usable while the new host is proved.

## SDK lanes

- Repository root: SDK **10.0.400** for the existing camera, companion, vision and core checks.
- `src/SkyEye.Companion.Hybrid`: SDK **11.0.100-rc.1.26425.128** for the Android MAUI Blazor compatibility host.
- Run `scripts/Build-SkyEye.ps1 -Target Baseline`, `-Target Hybrid` or `-Target All`. Builds are sequential and check their selected SDK. The Hybrid lane requires the compatibility project introduced in P02/P03.
- The existing installation script continues to install the baseline apps. It does not migrate production to the new host.

## Work log

| Unit | Status | Evidence |
|---|---|---|
| P01 | Complete | Root resolves SDK 10.0.400; Hybrid directory resolves SDK 11.0.100-rc.1.26425.128. Existing core checks pass. Full baseline Release build succeeds: 0 errors, 9 existing generated/native packaging warnings |
| P02 | Complete | SDK 11 workload set 11.0.100-rc.1.26460.1 installed/pinned; MAUI 11.0.0-rc.1.26451.6, Android 37.0.0-rc.1.2257, AI 0.1.0-preview.1.26466.103 restored with a package lock. Android 37 SDK installed in ignored work/android-sdk-11. Debug build: 0 errors/warnings. Separate Lab package launches and renders the real AI assembly on Pixel 11 |
| P03 | Complete | Real UIAgent fixture streams text, source-generated typed tool pending/result and an observable state snapshot on Pixel 11. Repeated Run stream increments state and renders three fixture choices. Desktop checks run the same adapter through the real package and prove cancellation before the effect leaves canonical state unchanged. Debug build: 0 warnings/errors |
| P04 | Complete | Real AgentContext retains predictive state while a registered UIActionBlock waits for a human decision. Pixel 11 shows yellow preview with committed open, rejects without mutation, and refuses an expired accept after a layout change. Same-package checks prove accept once, duplicate invocation, concurrent reject and cancellation. Debug build: 0 warnings/errors |
| P05 | Complete | Trimmed Release launches on Pixel 11; native fixture/live HLS, OpenCV calculation, WebRTC factory, typed tools and predictive decisions pass. Background/foreground restores video/state; native Cast picker opens. Signature v3, ZIP alignment and all 14 ARM64 ELF libraries pass. See evidence/hybrid-release-compatibility.json; full-screen stacking, actual receiver playback and physical 16 KB execution remain separate checks |
| S01 | Complete | Immutable preference slices, typed touch/Sky command envelopes and receipts, serialized writer and disposable subscriptions. Core checks prove stale/duplicate/conflicting request handling, one winner for concurrent versioned commands, observer failure isolation and cancellation. Existing core/HTTP/Python/tracker/carousel checks still pass |
| S02 | Complete | Existing overlay/group controls dispatch typed shared commands. Separate Commands Lab verifies live native candidates, 2-second index rotation, roughly 10-second analysis refresh, uncertainty clearing/recovery and stop/release cleanup. Core checks and both Debug app builds pass. See evidence/shared-candidate-commands.json; production installation remains blocked |
| S03 | Complete | Durable save admission/outcomes, native authority adapter and existing button routing. Core/real HTTP checks and Android builds pass. User-operated Release lab check creates one clip; retry returns the original result and count stays 8. Camera stopped, armed and unowned. See evidence/shared-save-commands.json |
| S04 | Complete | Canonical camera facts, authority adapter and connect/host/capture controls implemented. Core/real HTTP checks and both Release builds pass. User completes transition/background-expiry sequence; read-only inspection confirms released control, hidden capture buttons and stopped/armed camera. See evidence/shared-camera-commands.json |

No device testing is implied by a build or template listing.

## Hybrid setup

Run `scripts/Setup-Hybrid.ps1 -AcceptAndroidSdkLicenses` to install the pinned MAUI Android workload set and Android SDK dependencies. The local SDK defaults to ignored `work/android-sdk-11`; it avoids elevation to write the existing Program Files Android SDK. Java defaults to the existing JDK 21.0.8 and can be overridden. Installing MAUI also updated the already registered .NET 11 preview platform workloads to the coherent RC1 set; the .NET 10 SDK remains pinned for baseline builds.

`scripts/Install-HybridLab.ps1 -CompanionSerial '<ADB address>'` builds and installs only `uk.co.skyeye.companion.lab`. It does not replace production SkyEye, consume its encrypted key or migrate its data. The lab currently verifies the host and actual component assembly, not voice/refereeing functionality.

Pixel 11 checks: Android API 37; enabled WebView `com.google.android.webview` 153.0.8010.36. Accessibility hierarchy and screenshot show the Razor page and `Microsoft.AspNetCore.Components.AI` loaded. Tool streaming and predictive decisions pass; native media and trimmed Release remain separate gates.

Predictive-state finding: AgentContext rejects unconfirmed predictive state at turn completion. The fixture therefore emits a registered UI action and keeps the turn pending until accept/reject/cancel. Ending a simple text stream is insufficient to test a real interactive preview. All decisions resynchronize from canonical versioned state, so rollback cannot restore an obsolete layout. The lab installer stops its previous process and disables incremental installation to avoid testing stale assemblies.

## Native host gate (P05)

The lab links the existing PlayerBridge, CandidateOverlay, CastBridge and Cast options source. A MAUI handler owns the player/listeners and releases them when detached. Video and the native Cast button occupy a native region above contextual Razor controls; a full-screen transparent stack is still H01 work. Activity pause/resume stops/reopens the selected media, without mutating camera capture.

Debug device evidence on Pixel 11:

- Bundled silent H.264 fixture plays through ExoPlayer; native callback reports actual playing.
- OpenCV 4.14.0 loads and sums a 2-by-2 matrix of sevens to 28 through JNI.
- The existing WebRTC AAR creates/disposes a native PeerConnectionFactory without a microphone/session/model.
- Real Pixel 10 HLS plays while a real typed agent tool completes; app background/foreground restores video and retains the agent snapshot.
- Native Cast picker opens and lists Chromecast/speaker routes; it is dismissed without loading a receiver.
- APK contains CoreCLR/JIT and all 14 ARM64 native libraries pass the ELF 16 KB alignment inspection. Physical Pixel 11 page size is 4 KB; this does not establish execution on a 16 KB device.

Android 17/API 37 requires `ACCESS_LOCAL_NETWORK` at runtime for this manually addressed LAN camera and existing Cast integration. The lab declares it and requests nearby-device permission when viewing the camera; grant and live playback are verified. See [Android's local-network permission guidance](https://developer.android.com/privacy-and-security/local-network-permission?hl=en). The lab uses cleartext HTTP because the existing camera protocol is local HTTP. Camera ownership and read tokens remain native and never enter the agent projection.

Native Debug and trimmed Release builds pass. Release has 0 errors and 10 existing generated OpenCV/native-input warnings. SDK 11 builds referenced Android libraries for both their original target and the CoreCLR-compatible target; the packaged APK contains one copy per native library. Duplicate OpenCV inputs are byte-identical; inspection and device calculations verify the selected binaries. The Release APK is 44,417,389 bytes, verifies with APK Signature Scheme v3, passes ZIP 16 KB alignment and all 14 ARM64 ELF alignment checks.

Release device checks repeat fixture/live playback, OpenCV/WebRTC loading, typed stream and predictive preview/reject/accept/stale accept. Layout version 3 survives an expired proposal and activity background/foreground while playback resumes. The native Cast picker also opens in Release. No inspected fatal, unhandled or missing-native-library errors. The temporary test recording is stopped and its own host lease released; camera remains armed. Production APKs/data were not migrated or reinstalled. Signing here is the lab development identity, not proof of H07 production signing/storage migration.

`Resources/Raw/probe.mp4` is synthetic test data, generated with FFmpeg's `testsrc2` at 640x360/24 fps for 8 seconds, H.264/yuv420p and faststart; it contains no camera footage.

## Shared commands (S01)

`SkyEye.Core/Application` starts the canonical store with target-group and candidate-display preferences, the first state needed for S02. Only typed supported commands can change it. Touch and Sky carry operation IDs and expected versions through one serialized writer. Receipts describe their original decision; callers render the current store snapshot, never replay an old receipt as state. Unchanged preferences do not increment a version. Notifications are ordered, disposable and isolated from observer failures; observers post UI/further command work and return promptly.

The 256-operation in-memory cache protects preference retries in this bounded window. It is not a durable at-most-once guarantee for camera saves; S03 must persist effect outcomes before those operations use this path. Connection/table/game slices are introduced with their verified adapters rather than presenting unimplemented state as fact. S01 does not yet route production buttons or voice tools through the coordinator.

## Existing screen on shared commands (S02)

The candidate toggle and target-group spinner now dispatch `ShowShotChoices` and `SetTargetGroup` through `AppCoordinator`. The native phone overlay and encoded TV scene read the same canonical preferences. Queued UI notifications render the latest store snapshot, so an older notification cannot restore a previous group or toggle value. Host, calibration, freshness and settled-layout checks remain in the existing pipeline; S05 extracts that analysis separately.

`dotnet build src/SkyEye.Companion/SkyEye.Companion.csproj -c Debug -p:SkyEyeCommandLab=true` builds a separate **SkyEye Commands Lab** from the existing companion source. Its package is `uk.co.skyeye.companion.commands.lab`, with separate private storage and credentials. File-provider authority is derived from the application ID; the production authority remains `uk.co.skyeye.companion.files`. This lab intentionally retains the current screen. H01 introduces the portrait Hybrid shell.

Both production and command-lab Debug builds succeed with 0 errors and 11 existing generated/native warnings. Core checks pass. Pixel 11 touch testing records overlay/group changes in a sanitized latest-state file, then verifies three actual live candidates visibly on the native player. The encoded scene reports indices 0/1/2 on the existing 2-second schedule, and new candidate presentations arrive approximately every 10 seconds. The test creates no clips, stops its recording and releases its host; the camera remains armed with no error.

The standard-lighting check exposes a remaining vision limitation: the black ball's bright number patch can be classified as a second cue ball, and touching balls can fail the geometric overlap check. After the user separates the central balls, automatic detections recover and candidates appear. A manual colour-correction attempt is correctly rejected when its source layout changes. The feed briefly clears candidates when subsequent detections become unsettled/uncertain, then recovers. This is recorded rather than hidden by weakening eligibility checks. Actual receiver playback remains unverified.

Automatic approval review blocks installing the updated production companion and launching the lab remotely, returning only `blocked by policy` without a specific reason. The separate lab installation is allowed and the user opens it manually. Production storage/APK is not replaced; the source change and built production APK are ready, with the separate lab supplying on-device verification.

## Durable save foundation (S03, first commit)

`SaveClip` carries a bounded replay duration and a non-secret camera/session/host/revision scope. The coordinator admits it at the current preference version, then awaits the camera effect outside the preference writer. A save cannot freeze overlay/group commands or restore the state version attached to an old result.

The private operation journal writes and flushes a reservation before dispatch, then replaces it atomically with an acknowledged clip or a failed/uncertain receipt. A pending record after restart is uncertain and never sent again. Only known pre-save refusals are failed; cancellation/transport loss after reservation, unreadable records and malformed acknowledgements retain uncertainty. This provides at-most-one dispatch per retained operation, not recovery of a remote result that was never received. Replay protection does not depend on the camera's bounded in-memory cache. The journal has a 10,000-operation limit and refuses new saves at capacity rather than forgetting old operations. One OS file lock prevents competing processes from sending saves concurrently. Flushed files and atomic replacement are tested for application restart recovery; arbitrary hardware/storage failure is not claimed.

Core checks cover concurrent duplicates, durable success/failure/stale receipts, changed duration/host/command conflicts, interrupted and corrupt records, cancellation before/after reservation, malformed acknowledgement, capacity and another writer. A held save also proves that preference commands still commit. Native adapter and existing save-button routing remain the next part of S03.

### Native adapter and button routing

The existing Save last control now sends a typed `SaveClip`. The adapter freezes the native endpoint/credential for that operation, obtains fresh status, checks camera/session/host-generation/capture-revision scope, then renews host authority before sending the original operation ID to the camera. A guest or stale scope cannot create a clip. Pre-save refusals have generic safe messages; server/transport failures that might follow a remote commit remain uncertain. The journal contains a hash of camera identity and acknowledged clip metadata, without host/viewing tokens, API keys or camera URLs.

Real local HTTP integration tests exercise the actual `CameraClient` and adapter against camera authority and HTTP framing. They prove original IDs/duration, no network traffic for durable retries, guest/invalid-token/lease-expiry rejection, revision mismatch, known no-history failure, uncertain server failure, and that reconnecting during preflight cannot redirect the admitted save or change its credential. An additional Windows check blocks terminal journal replacement after a successful effect and verifies that the retained reservation still prevents another dispatch.

Commands Lab adds an explicit Retry last save control to submit the same persisted request for device verification, including after reopening. This control is compiled only into the lab. Reopening never sends a request automatically. The ordinary production UI has no additional diagnostic control. Device verification is still pending at this commit.


Build verification: production Release and Commands Lab Debug/Release succeed with 0 errors. The final incremental production Release/Debug lab builds each report 7 existing generated/native warnings; the fresh Release lab build reports 11. The Release Commands Lab installs successfully on Pixel 11. Manual opening is requested because automatic approval review previously blocked remote launching. Production installation is not retried.


The Release lab is manually opened by the user. The next remote UI script containing Connect, Become host and Start is rejected by automatic approval review with only `blocked by policy` and no specific reason. None of that script executes. The user is asked to perform the bounded save/retry check and Stop/Release cleanup manually; read-only inspection remains available. This is an approval-review rejection, not an Android package conflict, and uninstalling existing apps is unnecessary. S03 remains in progress until that device result is observed. See evidence/shared-save-commands.json for the verified automated checks and explicit pending device gate.

### Release device gate completed

The user performs Connect/Become host/Start, waits at least 30 seconds, then selects the 30-second save and retries that same operation. Read-only verification observes the camera clip count rise from 7 to 8, and the Release lab displays **Original result: Saved 31.4 seconds on camera** after retry. The count remains 8. Metadata identifies one new 31.4-second test clip; the requested replay is rounded at the existing segment boundary, within the validated two-second allowance. Camera is stopped, armed, unowned and without error after the user's Stop/Release cleanup. No clips are deleted.

S03 is complete. App-restart replay, interruption/transport loss and terminal persistence failure are proved in core/real HTTP fixtures; those failures are not induced on the physical phone. The physical device proves the Release journal's actual serialization and duplicate read path. The next planned unit is S04, connection/host/capture effects using existing camera authority. The visible portrait shell remains H01.
## Camera command foundation (S04, first commit)

Canonical connection facts now include reachability, optional armed/capture state, camera/session identity, host generation, observed ownership and camera revision. A new connection identity is issued for every connect, including the same endpoint. Connecting immediately invalidates old control proof; completions and queued mutations from an older connection cannot restore it. Unknown transport results retain unknown armed/capture/ownership instead of claiming recording stopped. Frequent native battery/history/viewing data is not used to invalidate otherwise-current control commands.

Typed connection, native refresh, host claim/release and capture controls are admitted under the canonical writer. Their network queue runs separately so preferences remain responsive. Queue entry rechecks connection and capture scope after earlier effects. Native refresh is restricted to the system adapter. All pending effect IDs, including saves, are checked across command types, preventing an in-flight save/camera request from being reinterpreted as another operation. Durable save protection remains separate; camera controls retain bounded in-process receipts and never automatically replay on restart. Their connection/session/host/revision scope is revalidated before dispatch.

Core checks prove guest rejection, valid/invalid transitions, pending/completed duplicate handling, cross-type ID conflicts, queued stale-scope rejection, responsive preferences, same-address reconnect and late replies, observed host expiry, invalid native snapshots and unknown outcomes. Native authority adapter and existing controls are not wired at this first commit.

### Native camera authority and existing controls

The native adapter obtains fresh camera status and proves a stored host credential with a successful heartbeat, followed by status from the same camera generation. A displayed host name or a credential alone never sets canonical ownership. Invalid/expired credentials are removed from native storage. Changing endpoints clears the old credential; reconnecting to the same endpoint can retain it only after fresh proof, under a new connection identity. Even an apparent Start/Stop no-op verifies authority before reporting unchanged.

Connect, Become host, Release control and Start/Pause/Resume/Stop now dispatch typed commands from the existing Android screen. Polling updates the same canonical camera facts. Camera battery/history and viewing access remain native, and only a native view bound to the current canonical connection can update the display. Telemetry-only observations do not increment the command version. Host/capture/connection changes invalidate advice, end voice on lost ownership and gate capture/save/candidate controls on verified control. Unreachable status clears control proof while continuing recovery observations; it does not claim capture has stopped. Foregrounding requests an immediate observation.

Real HTTP integration checks use the actual CameraClient, adapter and camera authority. They cover original capture operation IDs, pause/resume, duplicate/stale requests without another mutation, same-endpoint reconnect, 31-second lease expiry with capture remaining active, invalid saved credentials, separately observed active state after a lost start acknowledgement, release cleanup and the native credential/viewing boundary. These supplement the core queued-race and cancellation checks.

Production and separate Commands Lab Release builds each succeed with 0 errors and 7 existing generated OpenCV/native packaging warnings. The updated Release lab installs on Pixel 11; no production installation or remote launch/control workaround is attempted. The user-operated phone gate requests Connect/Claim/Start/Pause/Resume, 40 seconds in the background, verification of Active with Host available, then Claim/Stop/Release. The pre-check camera is armed, stopped, unowned and has 8 clips. See evidence/shared-camera-commands.json; the physical gate remains pending at this commit.

### Release camera-control gate completed

The user reports completing the requested transition and background-expiry sequence with no error reported. Read-only inspection of the updated Release lab directly observes **Host control released.**, **Stopped ... Host available**, and hidden capture/release buttons. Camera status independently confirms stopped, armed, unowned, no error and 8 clips. The installed package update time matches this build. Intermediate capture and background-expiry screen states are user-reported rather than captured by the assistant; exact authority, expiry and race behavior are additionally verified in the real HTTP/core fixtures.

S04 is complete and committed locally. S05 next extracts the remaining clip and analysis effects from the Activity while preserving cancellation, calibration, corrections and freshness. The portrait Hybrid shell remains H01 after S05.
