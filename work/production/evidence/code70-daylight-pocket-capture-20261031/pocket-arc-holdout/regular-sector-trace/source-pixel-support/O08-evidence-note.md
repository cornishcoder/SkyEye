# O08 evaluation-reference correction

Direct inspection of the exact raw frame 1365 shows that the regular O08 report's configured source reference `(1282,536)` lands on empty cloth. It does not identify the black ball in these saved frames. Its warped location `(494.9,1540.2)` is likewise not the lower-left pocket ball cluster.

Treat the regular reference as invalid for black localization. All label-distance and accuracy claims made using that point are void. This correction changes no candidate selection, ranked seed, regular fit, arc fit, or sector data.

The pocket-arc report's reference `(102,903)` lies in the lower-left pocket ball cluster, but it is an approximate interior point rather than a measured centre label. It supports no centre-error or accuracy claim. Both references remain evaluation-only and were not used by the sector exporter or its contact-sheet selection.

The source-pixel export is diagnostic evidence about the pixels sampled by the existing fits. It makes no accuracy claim.
