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
| P02–P05 | Pending | Workload/package/native media and real-device compatibility must pass before production migration |

No device testing is implied by a build or template listing.
