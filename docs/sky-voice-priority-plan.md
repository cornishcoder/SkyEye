# Sky voice assistant — bounded delivery plan

8 October 2026. Delivery and acceptance plan linked from the [roadmap](glow-up-plan.md) and [progress](glow-up-progress.md). The user reported completing API-key setup and requested Sky voice on 7 October, superseding the 4 October voice deferral. The current P0 is O08/O09 detector reliability; voice controls and declared-match tools can proceed independently, while dependable spoken shot-advice parity resumes after the held-out and safety gates.

## Current implementation status — 8 October 2026

The A02.01/A02.02 runtime slices are implemented and build-verified. The A04.02 camera-connect/host/live-Cast path is implemented, installed and user-confirmed on Pixel 11: the user reports the flow worked very well. The explicit host-session action connects the saved camera, claims available host control and starts capture; Cast then uses Android's native receiver picker and waits for playback confirmation. Stop-Cast/capture independence and denial/interruption edges remain unverified. A03.02 match state/declaration/correction tools and A04.01 durable save plus existing phone/TV replay controls are implemented and installed in the latest production Release. Pixel smoke checks verify launch and the refreshed icon-only home; direct voice, match, save/replay and receiver behavior still need acceptance. See the [runtime report](sky-voice-runtime-report.md).

The 8 October A03.01 slice adds voice-started automatic corner review and spoken acceptance of four current reviewed corners, followed by a bounded multi-frame initial table check. The signed Release is installed and reopened on Pixel 11. Spoken calibration and the physical sweep remain to be tried during an active live camera session; no vision robustness claim is made for the user's changing blind-shadow pattern.

## Existing foundations and gaps

Reuse `SkyVoiceSession`, native WebRTC media, protected host credential setup, existing microphone lifecycle and the shared coordinator. A01 Sky branding/identity is delivered. H04 key setup is delivered; a configured key is not evidence of a successful voice session. `SkyVoiceProjectionClient` adapts native events into the existing Agent stream; it does not open a second model connection or execute tool calls itself.

Existing `get_table_state`, `suggest_shots` and `explain_shot` tools provide bounded table advice. Shared touch commands, declared match controls, independent phone/TV scene commands, replay and historical media already exist. Real voice integration must expose supported operations through those same seams and acknowledge actual results. Do not wait for O-series tracking, full rules, statistics, wake-word detection or physics to deliver useful voice control.

## Bounded delivery order

| Chunk | Scope / dependency | Acceptance before widening |
|---|---|---|
| A02.01 | Connect protected host credential and existing session to production Hybrid start/mute/end; depends on H04 and existing voice/media adapters | Actual connection/audio, denied microphone/key/network error, mute/end and activity lifecycle report real state. Camera recording stays independent; no credential enters Razor/JavaScript, snapshots or logs |
| A02.02 | **Implemented:** bridge continuous native voice/tool events to the existing IChatClient/custom AI blocks | Controlled Agent UI checks cover response-before-transcript buffering, typed call/result correlation, duplicate suppression, follow-up responses, interruption and late-event rejection. Real network reconnect/session behavior remains part of live acceptance |
| A03.01 + A05.01 | **Implemented in source:** read current state, request one observation, show choices, select by stable current ID/index, clear; voice-start calibration with reviewed automatic corners and a bounded initial sweep; minimum real connecting/listening/speaking/muted/error UI. Transcript panel keeps a bounded recent in-memory conversation. | Controlled Agent/UI checks pass. Still verify on Pixel that calibration review/approval and the multi-frame result are truthful, alongside "Show my shots", "number two" and "clear that" parity; old/moving/uncertain state yields truthful limits; stale references are rejected; native status and transcript updates render correctly |
| A03.02 | **Implemented in source:** match state reads; explicit match/frame setup, group/turn/winner/foul/ball-presence corrections, latest-decision undo, and declared shot record/replacement | Canonical GameState, app/game version fences and host authority; controlled checks pass. Updated Pixel spoken match parity remains open. Image evidence never becomes a player declaration |
| A04.01 | **Implemented in source:** durable camera clip save; refreshed saved-clip list; existing phone/TV clip playback; 15/30/60-second TV recent replay; confirmed TV pause/resume/seek and return-live | Save uses the durable existing operation journal; TV actions wait for receiver status. Core/Agent checks and Release build pass. Pixel/Chromecast confirmation and unavailable-footage/error handling remain physical acceptance |
| A04.02 | **Camera-connect/host/live-Cast path user-confirmed:** discover/connect, claim/release host, capture controls, native Chromecast picker, live Cast and stop Cast. Download/open/share and typed-input fallback remain | User reports camera/session/Cast flow worked well. Stop Cast leaving capture active, interruption, denials, download/open/share and typed fallback remain |
| A05.02 | Remaining progress/context/references, quiet interaction and interruption polish | Actual app progress, current displayed surface/candidate/session context, no invented listening/success states, no repeated prompts or speech over a shot |

A02.01/A02.02 and A03.01/A05.01 source work is implemented and controlled checks pass; spoken advice/selection/clear parity and the transcript view still need a direct Pixel check. A03.02/A04.01 source work is implemented and the updated Release is installed; match/save/replay behavior has not yet been exercised in conversation. The earlier A04.02 path remains user-confirmed. Each row can split further if compatibility/lifecycle work grows; retain A02-A05 parent IDs. Current shared-command/media and G/R declared slices supply dependencies, not new infrastructure.

## First useful vertical slice

In the normal portrait app, connect Sky, ask for a fresh table observation and choices, select one supported current shot, clear it, mute and end. Touch, Sky and the scene report the same canonical versions. Interrupt a pending read and a pending effect without double dispatch; refuse stale candidate references and guest-only host operations. Show and speak unsupported/uncertain results as such.

The source now includes the declared match correction and bounded save/replay slice. Core/agent checks and a Release build are implementation gates; actual Pixel 11 conversation with a live camera and Chromecast is still the physical parity/media gate. No connection or build alone establishes listening/audio/tool parity, billing finalization, recovery or long-session acceptance. Preserve clips, journals, key storage and capture across deployment.

## Scope and future tools

Keep a compact authoritative state/freshness snapshot and typed tool results. Reuse the existing voice identity and truthful uncertainty policy. Corrections use explicit references and current versions; independent phone/TV control remains available. Wake-word/always-on listening and unsupported proactive referee decisions are separate work.

After their consumers exist, [history missions](analytics-practice-replay-plan.md) extend this catalogue with C03 statistics, SR01/SR02 search/playlists, PR drill setup/attempts, C04 practice planning and PH04 Shot Lab loading. Sky translates natural language to deterministic structured queries/actions; it does not infer performance from conversation memory or analyse whole videos itself. Unavailable evidence/tools return an explicit limitation.

After the first usable voice slice, return to O02.05 real-source acceptance with O01 timed-shot corpus/holdout work, followed by O03-O05. AN01/C03/SR01 declared-data slices can proceed without perfect tracking or physics. Existing T13/T14 physical acceptance remains open and is addressed when required by the selected voice/media slice.
