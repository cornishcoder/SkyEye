# Production companion migration (H07)

The portrait host has an explicit production build lane. The camera remains on its existing .NET 10 build and is never installed by the companion migration script.

```powershell
./scripts/Build-SkyEye.ps1 -Target Production -Configuration Release
./scripts/Install-ProductionCompanion.ps1 -CompanionSerial <companion-serial> -SkipBuild
```

Production output is isolated under `work/production/bin/Release/net11.0-android/android-arm64/uk.co.skyeye.companion-Signed.apk`. The lab remains `uk.co.skyeye.companion.lab`; its output/intermediates and storage are separate. Production version code is 2, display version 0.2. The launcher retains `uk.co.skyeye.companion.MainActivity`.

The installer requires an existing authorized ARM64 companion, inspects the package/launcher/FileProvider/voice-service contracts, verifies both APK signatures, refuses mismatched signers and version downgrades, retains a copy of the installed APK, then updates with `install -r`. It compares the app UID and both private-storage inodes before launching. It contains no uninstall, data-clear or camera-install operation. The APK backup is an executable backup, **not a private-data backup**.

On this development laptop, signed Release APKs use the existing local Android Debug certificate (SHA-256 `54A26D27FA0A0BFE68F0C96F9640229E4D9A9B2FA0AD1861BC9CE874CE65A7C1`). Release configuration does not imply a public distribution signing identity. Preserve that identity for updates to these installed packages; distribution signing is a separate release decision.

## Storage and existing features

No export/copy/re-encryption is required: the same package and signer keep the app UID and Android Keystore namespace. Existing native stores are reused:

- `skyeye` preferences: camera, host and the exact existing `calibration-<SHA256(endpoint)>` keys. The host token is a credential, never proof of current authority.
- `secrets/openai`: unchanged AES-GCM envelope and Keystore alias `skyeye-personal-credentials-v1`. Reads cannot create a missing key; an unavailable alias reports failure without replacing it.
- Private `files/clips`: existing video/sidecar layout, including incomplete transfer files.
- Private `files/save-operations`: existing idempotent save ledger.
- `uk.co.skyeye.companion.files`: the same native read-grant FileProvider.

The new screen uses the saved native camera preference before its lab-era preference. Resume rereads the shared lease credential and rebinds a changed camera endpoint before observing it. Fresh service observations and scope/version checks still admit all camera mutations.

Until the dedicated save/candidate/voice adapters land, **More controls** opens the existing native controls inside the same package, with a private activity. It contains the original save workflow, two-second candidate cycle/ten-second refresh, native Cast relay and voice session. Returning to the portrait view ends a conversation in that activity; Home/background behavior and its microphone foreground service remain the existing implementation. The notification opens that native activity. The existing registered service name `crc648b11b8ca3be5d4a4.VoiceService` is explicit so moving assemblies cannot silently change it. Personal key setup returns to the verified protected dialog in the portrait screen. Production hides lab-only framing/test-video controls.

Actual microphone/voice transport re-verification is tracked separately from manifest identity and belongs to A02; no personal-key remote request is made for the H07 storage check.

## Read-only migration evidence

For a controlled device audit, add `-p:SkyEyeMigrationAudit=true` when building either native baseline or production. `SkyEyeMigration` emits only counts, app UID, opaque fingerprints and configured/readable flags at activity creation. It never emits addresses, credential values, encrypted envelopes, filenames or calibration coordinates. This flag is absent from the normal build script. Evidence is recorded in `evidence/production-companion-migration.json`.

## Rollback without erasing data

Do not uninstall or request a lower-version downgrade. Rebuild the retained baseline source with a version code **higher than the installed version**, using the original signing identity. For example, from the repository root with the .NET 10 SDK:

```powershell
dotnet build src/SkyEye.Companion/SkyEye.Companion.csproj -c Release -m:1 -p:ApplicationVersion=3 -p:ApplicationDisplayVersion=0.2-rollback
./scripts/Install-ProductionCompanion.ps1 -CompanionSerial <companion-serial> -SkipBuild -ApkPath src/SkyEye.Companion/bin/Release/net10.0-android/android-arm64/uk.co.skyeye.companion-Signed.apk
```

Choose a larger code if 3 has already been installed. The same installer accepts this baseline package because its stable launcher/provider/service/signing contracts remain intact. The original pre-migration APK is retained locally under `work/h07-before-production.apk`, and each guarded update saves its own `work/production/backups/<timestamp>/before.apk`. Installation of a rollback build has not been exercised on the real production data; UID/storage checks and signer/downgrade refusal have been exercised.
