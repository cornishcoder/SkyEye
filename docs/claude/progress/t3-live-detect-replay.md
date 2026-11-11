# T3 progress: Live Detect reducer replay

Status: **report written** (11 November 2026): [t3-live-detect-replay.md](../t3-live-detect-replay.md).

Done since the overnight pause:

1. Fixed the classification of a ball that is never detected. Black and cue are now judged
   against a count of one, and per-run notes (`EXPECTED` in the script) separate "deliberately off
   the table" from "missed".
2. Rechecked the hold counterfactual. The 4 to 411 s of added loss came from unbounded holds on
   schema 1 and 2 logs, which have no movement field. Adding a positional movement test and a
   legal-inventory cap brings it under 1 s.
3. Read the code62 clip rows: one corner-pocket red dropped out intermittently, the old movement
   signal treated that as movement, and the two-sample confirmation turned it into a pot
   candidate. No ball moved more than 0.011.
4. Quantified the pocket-colour flag, and ran a counterfactual that ignores it where it is the
   only trigger.
5. Added a `--movement recomputed` sensitivity run.

Open, if wanted:

- validate the still-table proxy and the obstruction split against video labels (T9);
- confirm the black and cue presence for runs `dee49d3d`, `23796ede`, `334dcf5f` and `38961617`.

Port caveats and regeneration commands are in the report. No `src/` or `docs/argon/` edits, and
no git writes.
