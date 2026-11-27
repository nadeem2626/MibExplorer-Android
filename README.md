
<p align="center">
  <img src="docs/images/banner.png" alt="MibExplorer for Android" width="100%">
</p>

<p align="center">
  <a href="docs/INSTALL.md">Install</a> ·
  <a href="docs/USER-GUIDE.md">User guide</a> ·
  <a href="docs/BUILDING.md">Build from source</a> ·
  <a href="docs/KNOWN-LIMITATIONS.md">Limitations</a>
</p>

> **Experimental.** Test builds only. Only connecting, listing and entering folders has been used on a real unit, and changes to a head unit can break it. Use at your own risk. See [Known limitations](docs/KNOWN-LIMITATIONS.md) and [Safety](docs/SAFETY.md).

## Download
**Download APK:** [release download link]

Download the APK to your device, open it, and allow the install when Android asks. See [Install](docs/INSTALL.md).

## Features

| Area | What you can do |
|---|---|
| **Connect** | Saved units, SSH / Telnet / FTP, password or key login, key generation and import, trusted host keys |
| **Files** | Browse, search, sort, details and text preview, download files and folders, upload, replace, rename, copy, move, delete empty folders, edit with diff |
| **Terminal** | Shell on the unit over SSH or Telnet with a key row, copy, paste and save log |
| **Scripts** | Run the bundled official scripts or your own `.sh` with live output |
| **SD packages** | Build the zip files that install, re-key or remove SSH on the unit |
| **Coding** | Read the unit's coding, feature list, raw view, history |
| **Adaptations** | Reference catalog and history |
| **Transfers, Reboot, Settings** | Transfer queue with cancel, reboot the unit, Night / Day / Automatic appearance |

Wide landscape screens get a side rail, portrait gets bottom tabs. English only.

## Screenshots

<p align="center">
<img width="49%" alt="Screenshot_2026-11-27-12-28-45-866_com instinct mibexplorer" src="https://github.com/user-attachments/assets/df5406f0-3326-4d3a-bdaf-963c85d800ba" />
<img width="49%" alt="Screenshot_2026-11-27-12-29-14-906_com instinct mibexplorer" src="https://github.com/user-attachments/assets/a6ab19da-9425-44fa-8a79-bf101c2c8e7c" />
<img width="49%" alt="Screenshot_2026-11-27-12-30-03-555_com instinct mibexplorer" src="https://github.com/user-attachments/assets/694501c1-74db-4380-a8e0-236830b0919f" />
<img width="49%" alt="Screenshot_2026-11-27-12-30-07-571_com instinct mibexplorer" src="https://github.com/user-attachments/assets/5f4970b4-61b9-45a7-aa6b-ab0301802ed8" />
<img width="49%" alt="Screenshot_2026-11-27-12-29-42-920_com instinct mibexplorer" src="https://github.com/user-attachments/assets/2751a46e-b745-4ded-bb52-77c5524e8988" />
<img width="49%" alt="Screenshot_2026-11-27-12-29-31-928_com instinct mibexplorer" src="https://github.com/user-attachments/assets/f002cc8e-5876-4e1a-9970-aa458cdd9753" /></p>

*Design previews drawn from the app's layout and colors, with example data. They are not screenshots and will be replaced by real ones.*

## Documentation

- [Install the app and SSH on the unit](docs/INSTALL.md)
- [User guide](docs/USER-GUIDE.md)
- [Safety: write mode and confirmations](docs/SAFETY.md)
- [Known limitations and test status](docs/KNOWN-LIMITATIONS.md)
- [Build from source](docs/BUILDING.md)
- [Test checklist for a device](docs/ANDROID-TEST-CHECKLIST.md)

## License and credits

The Android-specific changes by Nadeem are under the MIT License, see [LICENSE](LICENSE). That license covers only those changes. Inherited upstream code, third-party components and some binary payloads have their own status, and for the upstream project and several included binaries the redistribution terms are unresolved: the upstream repository has no LICENSE file (its README says MIT) and GitHub shows no license for it. See [NOTICE.md](NOTICE.md) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) before redistributing.

This Android version is built and developed by Nadeem ([github.com/nadeem2626](https://github.com/nadeem2626)), based on the original desktop version by djskual.

Built upon the original MibExplorer by djskual: https://github.com/djskual/MibExplorer (base commit `21aaee15af6ed888f91eaf82cfa4b88522fd3b5e`, tag v0.7.0).
