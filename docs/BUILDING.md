# Building MibExplorer for Android from source

Everything below was run from a clean extract of this repository's zip on one machine. Other operating systems and versions were not tested.

## Tested environment
- Ubuntu 22.04, x86_64, 2 GB RAM
- .NET SDK 10.0.404 with the `maui-android` workload 10.0.110
- JDK 17 (OpenJDK 17.0.21)
- Android SDK with platform `android-36` and build-tools `36.0.0` (`zipalign`, `apksigner`)

Requirements for any machine: the .NET 10 SDK, the .NET MAUI Android workload, a JDK 17 and the Android SDK (platform 36, build-tools 36.0.0). The commands to install them were not re-run for this document; use the official .NET MAUI and Android SDK installers.

If the build does not find the SDKs on its own, add `-p:AndroidSdkDirectory=<path to Android SDK>` and `-p:JavaSdkDirectory=<path to JDK>`.

## Build the APK
From the repository root:

```bash
cd MibExplorer.Android
dotnet build -f net10.0-android -c Release -r android-arm64 --self-contained -m:1 \
  -p:PublishTrimmed=false -p:RunAOTCompilation=false -p:AndroidLinkMode=None \
  -p:EmbedAssembliesIntoApk=true -p:AndroidPackageFormat=apk
```

- `-m:1` keeps the build to one process. A parallel build ran out of memory on the 2 GB machine.
- A clean build took about 3 minutes and finished with 0 errors (many warnings are normal).
- Output: `MibExplorer.Android/bin/Release/net10.0-android/android-arm64/com.instinct.mibexplorer.apk`. The build also writes a `...-Signed.apk` next to it, signed with a debug key. Do not distribute that one.
- The version is `ApplicationVersion` (build number) and `ApplicationDisplayVersion` in `MibExplorer.Android/MibExplorer.Android.csproj`. Raise the build number for every build you want to install over an older one.

## Sign the APK
Use your own key. This repository contains no keystore and none must be committed (`.gitignore` blocks `*.jks`, `*.keystore`, `*.apk`).

```bash
# one time: create a key (keep the file and passwords safe)
keytool -genkeypair -keystore my-release.jks -alias mibexplorer -keyalg RSA -keysize 2048 -validity 10000

APK=MibExplorer.Android/bin/Release/net10.0-android/android-arm64/com.instinct.mibexplorer.apk
BT=<android sdk>/build-tools/36.0.0
$BT/zipalign -p -f 4 $APK aligned.apk
$BT/apksigner sign --ks my-release.jks --ks-key-alias mibexplorer --out MibExplorer-signed.apk aligned.apk
$BT/apksigner verify --verbose MibExplorer-signed.apk
```

These steps were tested with a temporary key: the result verified with v1, v2 and v3 signatures, and `aapt2 dump badging` showed package `com.instinct.mibexplorer`, targetSdk 36. A new build must be signed with the same key to install over an installed one.

## Install on a device
Copy `MibExplorer-signed.apk` to the device and open it, allowing installs from that source when Android asks. Installing with `adb` was not tested here.

## Run the tests
The core test programs need no Android SDK:

```bash
cd Tests
dotnet run -c Release -- navlayout
```

Run one suite at a time by name; the names are in `Tests/Program.cs`. Results from the clean extract: `navlayout` 11 PASS, `hostkeys` 8 PASS, `lsdate` 8 PASS, `filelist` 11 PASS. The `proto` suite starts local servers with Python scripts that are not in this repository, so it cannot run here. The `uninstall` suite compares the uninstall command with the original desktop source file and prints SKIP when that file is not present.

Passing tests show core logic only. They are not proof of screen behaviour or of behaviour on a head unit.
