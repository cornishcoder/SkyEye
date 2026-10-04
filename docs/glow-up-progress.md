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
| P04–P05 | Pending | Proposals/native media and Release runtime checks must pass before production migration |

No device testing is implied by a build or template listing.

## Hybrid setup

Run `scripts/Setup-Hybrid.ps1 -AcceptAndroidSdkLicenses` to install the pinned MAUI Android workload set and Android SDK dependencies. The local SDK defaults to ignored `work/android-sdk-11`; it avoids elevation to write the existing Program Files Android SDK. Java defaults to the existing JDK 21.0.8 and can be overridden. Installing MAUI also updated the already registered .NET 11 preview platform workloads to the coherent RC1 set; the .NET 10 SDK remains pinned for baseline builds.

`scripts/Install-HybridLab.ps1 -CompanionSerial '<ADB address>'` builds and installs only `uk.co.skyeye.companion.lab`. It does not replace production SkyEye, consume its encrypted key or migrate its data. The lab currently verifies the host and actual component assembly, not voice/refereeing functionality.

Pixel 11 checks: Android API 37; enabled WebView `com.google.android.webview` 153.0.8010.36. Accessibility hierarchy and screenshot show the Razor page and `Microsoft.AspNetCore.Components.AI` loaded. No inspected fatal/unhandled errors. Native video, tool streaming and trimmed Release behavior remain separate gates.
