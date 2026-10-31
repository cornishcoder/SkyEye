# O08 evaluation-reference correction

Direct inspection of the exact raw frame 1365 shows that the regular O08 report's configured source reference `(1282,536)` lands on empty cloth. It does not identify the black ball in these saved frames. Its warped location `(494.9,1540.2)` is likewise not the lower-left pocket ball cluster.

Treat the regular reference as invalid for black localization. All label-distance and accuracy claims made using that point are void. This correction changes no candidate selection, ranked seed, regular fit, arc fit, or sector data.

The pocket-arc report's reference `(102,903)` lies in the lower-left pocket ball cluster, but it is an approximate interior point rather than a measured centre label. It supports no centre-error or accuracy claim. Both references remain evaluation-only and were not used by the 23-pair sector exporter or the original four-case contact sheet.

The source-pixel export is diagnostic evidence about the pixels sampled by the existing fits. It makes no accuracy claim.

## Requested target-near review example

The full sweep contains frame 1365 rank 19, seed `(74,57.5)`. Its pocket-arc fit is centred at `(66.4,38.9)`, has 12 supported-visible sectors, and passes the diagnostic checks. The regular fit is centred at `(51.9,86.4)` with 18 supported sectors. Their separations from the approximate pocket point `(57.1,36.6)` are 9.6 px and 50.1 px respectively. These are point separations to an approximate interior reference, not localization errors or accuracy measurements. The approximate point was used only to select this separate review illustration at the user's request; candidate selection, saved fits and detector behavior were not changed.

See `target-near-frame1365-rank19-review.png` and its JSON sidecar for raw and warped overlays, seed, fit circles, and sampled supported sectors.
