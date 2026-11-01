
## INVENTORY-POSITIVE: partial-arc paired control

The existing evaluation-only probe was run on the reviewed same-setup lower-left pair: frame678 with black present and frame844 with black absent. Exact receipt-bound frame hashes are preserved in each manifest. The present-point source pixel (105,908) was derived from frame678's reviewed table point using that receipt's calibration. Frame844 used the same expected empty location as `unreviewed-negative-control`.

| Frame | Expected | Candidate centre, width800 | Visible support | Longest run | Arc span | Probe checks |
| --- | --- | --- | ---: | ---: | ---: | --- |
| 678 | Black present | (51.4,59.4) | 22/32 | 9 | 90 degrees | Pass |
| 844 | Black absent | (63.4,36.9) | 20/32 | 12 | 123.75 degrees | Pass |

This is a **discrimination failure**: both positive and empty control pass the diagnostic arc checks. The empty-location fit's distance from expected point is13.12 analysis px; the present fit is18.75px. Neither is production output. Do not promote or lower gates. The captured image conditions are within one reviewed physical setup group, but retain independent image hashes and no physical accuracy claim. The prior arbitrary frame1691 opposite-rail positive plus frame562 cue-absent partial-arc support is a different geometry/setup and remains unmatched.

Positive report SHA256 `8925d529bbaf2942b7be03ef987e39b3061c27844a458a554accaf64b57cbec9`; manifest `1194944bf678615d514d7e6c933ba243ac604b4cb242e38e51d9a14c90772196`. Negative report `28698bbe62302c13ce7e40144848eeda8c79aeb3eb9c4bf2308f4ed4bc7f91c6`; manifest `b8108fd42f453d63bdc732aa7c63cb1b3d5dcb6dcdee220f38f101fa53bee889`. Artifacts: `work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/arc-discrimination-20261101`.

Current scorecard: software suites3/3 PASS after the motion-feed handoff fix; focused motion-gap20/20 PASS (do not repeat unchanged); inventory positive0/3 PASS; D1/D2/E0/3 proven. Full current inventory remains a failing detector gate. Next test should study false arc acceptance on reviewed empty controls and use additional already-reviewed absence frames only if their location/setup labels support a predeclared comparison. No physical shot sequence until D1 and D2 pass.
