# Direct1000 existing control screen — 2 November 2026

`O09-DIRECT1000-EXISTING-CONTROL-SCREEN`: two receipt-bound Development controls executed serially; blanket width1000 promotion REJECTED by this screen. Current source and APK unchanged from cc710de benchmark harness. Each arm has one warm-up and four measured trials, alternating order. No Holdout or capture.

|Frame/state|Current800 median ms|Direct1000 median ms|Observed output|
|---|---:|---:|---|
|2083 both present|856.22975|1248.8944|Both 7red+7yellow+cue; black missing; uncertain|
|957 black absent|728.63905|1184.5247|Current7red+8unknown (15); direct7red+7yellow+2unknown (16); both uncertain|

These results differ from the warm rack speedup: unconditional1000 is slower on both controls and changes negative-state proposals. Unknown-circle additions are not yet scored as false positives; use reviewed centres before assigning error types. No candidate production change. Four correlated repeated trials are timing samples, not independent layouts. Exact artifacts and per-trial outputs retained under work/o09/direct1000-existing-controls-20261102; summary.json records arm counts and medians.

Preselection inspected four normal-validation Layout03 receipts. Original detectionRequested is true for2083/957 and false for962/2263. Only the two true inputs were run; the false inputs were not silently rewritten. Thus cue-absent and restored controls remain unmeasured in this timing screen. Existing reviewed full-audit results remain reusable for spatial analysis.

Next target: score frame957's changed proposals against reviewed labels, then decide whether an explicitly gated packed-cluster-only routing experiment merits further measurement. Current width800 and fallback remain production truth. Counts unchanged13/24 captured,4/12 reviewed,lighting0/2,D1/D2/E0/3.

## Reviewed negative proposal association

`O09-DIRECT1000-NEGATIVE-PROPOSAL-SCORE`: diagnostic one-to-one association complete. Reused reviewed table2x1 frame957 labels and first measured output per arm; minimum-cost one-to-one assignment gated at0.010 (existing diagnostic association setting, not reviewed accuracy tolerance). Both arms associate15/15 visible labels with0 misses. Width800 has0 unmatched circles,7 correct colour labels,8unknown. Direct1000 has1 unmatched circle,14 correct colour labels,2unknown overall. Unmatched direct1000 circle is unknown-1 at(0.14474998779296874,0.8443500030517579), radius0.026, boundary support0.8125. Neither arm emits black. Do not describe the additional unknown as a black false positive. This is a classification gain with a surplus proposal and slower runtime, not a complete inventory pass.

The exploratory nearest-label uniqueness assertion failed for direct1000 because two circles competed for one reviewed label; it wrote no completed score. Replaced with one-to-one minimum-cost assignment to enforce label uniqueness; no detector rerun or label edits. Result stored in work/o09/direct1000-existing-controls-20261102/frame957-reviewed-association.json. Next inspect the surplus circle against source pixels and detector duplicate handling, using existing outputs; do not tune thresholds from this one negative frame. Whole-source width1000 remains rejected as a blanket optimization.
