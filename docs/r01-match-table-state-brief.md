# SkyEye — Match & Table State Epic

Create a new **Match & Table State** epic and move the existing **R01** out of the Replay epic into this new epic as its first mission.

Do not treat this as replay functionality. Replay, coaching, scoring, rules, shot reconstruction and future 3D simulation should all become downstream consumers of the state/event model established here.

Preserve the intent of the existing roadmap where possible and renumber/reorganise remaining Replay work only where necessary to keep the plan coherent.

## R01 — Establish the Match & Table State Core

Build the first vertical slice of SkyEye's persistent understanding of a game in progress.

The central idea is:

> SkyEye should not repeatedly look at isolated frames and ask "what is on the table now?". It should maintain an authoritative understanding of how the table and match have changed over time.

Create a new pure C# domain library, preferably:

```text
SkyEye.GameState
```

Use another name only if the existing solution structure strongly suggests a better domain term.

The library must have no dependency on:

- Android
- CameraX / Media3
- OpenCV
- Chromecast
- GPT / Sky
- UI frameworks
- networking

Those systems provide evidence to this library; they must not define its state model.

## Inputs

Design a small typed input model capable of accepting timestamped evidence such as:

```text
TableObservation
MotionStarted
MotionStopped
TableSettled

BallObserved
BallMoved
BallMissing
BallAppeared

PossibleCollision
PossibleCushionContact
PossiblePocketEvent

ShotStarted
ShotEnded

PlayerCorrection
SkyCorrection
```

Do not attempt to implement every future detector during R01.

Use the observations/signals that SkyEye already has where practical and create clean domain seams for evidence that later missions will provide.

## Canonical state

The library should maintain a deterministic canonical state representing at least the concepts required to understand a frame/match, for example:

```text
MatchState
  players
  frame score / target
  current frame

FrameState
  current player
  current visit
  open table / assigned colours
  reds remaining
  yellows remaining
  black state
  foul/visit state

TableState
  known balls
  positions where available
  moving/settled
  last settled observation
```

Do not force this exact object hierarchy if a simpler domain model emerges naturally.

Optimise for clarity and principle of least surprise.

## Events, not mutable guesses

Prefer an event-driven model.

Evidence should result in meaningful domain events such as:

```text
ShotStarted
ShotEnded
TableSettled

BallPotted
CueBallPotted

ColourAssigned
TurnChanged
FoulRecorded

FrameWon
MatchWon

StateCorrected
```

Maintain an ordered history of meaningful events so state can be reproduced/reduced deterministically.

This does **not** need enterprise event-sourcing infrastructure, brokers or generic frameworks.

A simple append-only ordered event history plus a deterministic reducer is preferred.

## Observation versus interpretation

Keep these concepts separate.

Example:

```text
Observation:
yellow ball Y3 existed before the shot
yellow ball Y3 is absent after the shot
its tracked trajectory approached bottom-right pocket

Interpretation:
BallPotted(Y3, BottomRight)
```

Likewise:

```text
Observation:
balls began moving

Interpretation:
ShotStarted
```

The state library should not contain CV implementation details.

## Confidence and provenance

Facts derived from imperfect perception must retain enough metadata to distinguish:

```text
Observed
Inferred
PlayerConfirmed
PlayerCorrected
```

Include confidence where useful.

Player corrections should outrank machine inference.

Do not pretend uncertain evidence is certain.

For example, SkyEye should be capable of representing:

```text
PlayerColour = Yellow
Source = Inferred
Confidence = 0.78
```

until the player says:

> "I'm yellows."

At which point that fact becomes player-confirmed.

## Corrections are first-class

A player must be able to conversationally repair SkyEye state later through Sky.

Examples:

> "No, that wasn't a foul."

> "I'm yellows."

> "His turn."

> "That red stayed up."

> "That's my frame."

R01 does not need GPT integration, but the domain API should make these corrections straightforward.

Avoid directly mutating arbitrary fields from outside the domain.

Represent corrections as commands/events so state remains explainable and reproducible.

## Continuous observation compatibility

Design the API for the architecture established in T13/T14:

```text
live decoded frames
      ↓
bounded latest-frame observation worker
      ↓
timestamped observations/events
      ↓
SkyEye.GameState
      ↓
canonical state
```

The state library must not care how often observations arrive.

It should work equally well with:

- low-rate idle observations;
- high-rate shot tracking;
- a heavyweight observation after the table settles;
- explicit observations requested by Sky.

No timers should exist in this domain library.

## First vertical slice

Do not attempt to solve the complete rules of pool in R01.

Prove the architecture with one realistic sequence.

For example:

1. start a two-player frame;
2. establish a settled table observation;
3. receive `ShotStarted`;
4. receive observations/events showing a yellow disappeared into a known pocket;
5. receive `ShotEnded` / `TableSettled`;
6. derive/record the appropriate pot event;
7. update canonical table/frame state;
8. retain the event history;
9. apply a player correction;
10. rebuild/update the state deterministically.

Tests should tell this story clearly.

## Testing

The new library should be heavily unit-testable without Android or recorded video.

Use readable scenario-style tests such as:

```text
Given an open table
And Rich is the current player
When a yellow is potted
And the table settles
Then the relevant table state is updated
And the event history records the pot
```

Also test:

- stale/out-of-order observations where relevant;
- duplicate evidence;
- uncertain inference;
- player correction overriding inference;
- deterministic reconstruction from event history.

Keep abstractions minimal.

## Integration

Integrate the library into SkyEye only far enough to prove the vertical slice.

Do not rewrite unrelated application architecture.

Existing perception/application code should translate its evidence into the new domain inputs.

Expose the resulting canonical state so existing/future consumers can obtain it.

Future consumers will include:

```text
Sky voice context
shot selection
rules
automatic scoring
session statistics
replay reconstruction
3D replay
actual-vs-predicted analysis
coaching
```

None of those need to be implemented in R01.

## Deliverable

At completion report:

1. the new project/library and its responsibility;
2. core commands/observations/events/state types;
3. the reducer/state-transition design;
4. how confidence/provenance and corrections work;
5. the vertical scenario demonstrated;
6. integration points added to the current application;
7. what remains intentionally deferred.

Keep the code small, explicit and human-readable.

The purpose of R01 is:

> **Give SkyEye a durable understanding of a game unfolding over time, rather than a collection of unrelated snapshots.**