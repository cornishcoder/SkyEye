# Recorded geometry device gate

This is an offline decoder/OpenCV check, not a camera capture command. Run against an available, authorised Pixel 11. Production recording can continue. The separate test package does not access a personal API key.

Generate `work/t08-geometry-fixture.mp4` and its manifest with the existing OpenCV environment:

```powershell
./work/python-reference/Scripts/python.exe tests/native/make_recorded_geometry_fixture.py
dotnet build tests/SkyEye.Vision.Device/SkyEye.Vision.Device.csproj -c Release -m:1
```

Install the signed test APK in place, copy the generated fixture to the test package's external files directory as `geometry-fixture.mp4`, then run:

```text
adb -s <Pixel 11 serial> shell am instrument -w -e mode fixture uk.co.skyeye.visionchecks/uk.co.skyeye.visionchecks.RecordedGeometryInstrumentation
```

Require `passed: true` in the result. Pull `/sdcard/Android/data/uk.co.skyeye.visionchecks/files/recorded-geometry-checks.json` for the full safe journal. Instrumentation exit status alone is insufficient: Android can report a crashed test process with a zero shell exit code.

The fixture uses a real labelled dim-rack still with controlled movement, dimming and obstruction. It does not establish general detector accuracy. It checks accepted original/moved/recovered geometry, uncertain transition/covered/tail intervals, checksum rejection and identical journal recovery through a new service instance.

For the real recording-clock gate, obtain an acknowledged temporary replay from an active owned camera, verify its checksum, and copy that MP4 as `geometry-fixture.mp4`. Copy only the safe `clip` acknowledgement (including `recordingOrigin`, excluding viewing URLs/tokens) as `geometry-source.json`. Run the same instrumentation with `-e mode real`. Require accepted geometry and the exact acknowledged timeline/origin. Keep capture running and confirm that no permanent clip was added.
