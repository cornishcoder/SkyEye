# T14 background corrected delivery and resource acceptance

The canonical requirement is the T14 section of `glow-up-plan.md`. T13 performance gates remain independently open. Preserve its requested-only perception and stored phone GPU correction.

Required stored-rendering acceptance: with full table perception idle, Pixel 11 continuously displays a smooth perspective-corrected live table using the stored calibration transform. No periodic `VisionAnalyzer` or pocket detection is required to maintain that view. Phone rendering and requested composed TV delivery have independent owners.

1. Add a bounded, source-scoped delivery lease and regression checks for foreground/background/lock/local replay, clear, source replacement and receiver-loss grace.
2. Replace production TV texture readback with an independent service-owned Media3 decoder, external GLES texture, perspective/annotation compositor and hardware encoder. No activity, window or UI pixels enter this path. Start it only for an explicit connected-TV correction/annotation request. Plain live keeps this pipeline idle.
3. Keep source-clock journalling and HTTP media delivery alive in the foreground media-playback service. Fence reconnection/source/geometry changes, expire advice, retain stable stored rendering and bounded segment storage. Test actual corrected TV moving pixels, background, lock and local playback separately.
4. Preserve verified source files across interrupted permanent preparation and bound recovery storage. Retry must not publish incomplete derivatives or invent annotation history. Check recovery/cancellation/source changes with deterministic file-store checks and physical interruptions where practical.
5. Run physical quality/resource acceptance: 1080p geometry and retained pockets, watermark/advice expiry, silent live TV, recorded room audio/PTS, receiver/network recovery, bounded memory/cache/queues, representative temperature and latency. Record sample counts and observed versus untested behavior; do not mark T14 complete from a build alone.

Implement and commit each verified chunk. Serialize Android build lanes. Companion updates preserve signing/UID/storage; the camera remains recording unless a necessary recording break is made concrete and explicitly authorized.
