# Notice

## Origin
This repository is an Android port derived from https://github.com/djskual/MibExplorer (base commit `21aaee15af6ed888f91eaf82cfa4b88522fd3b5e`, tag v0.7.0). The official scripts (`Scripts/Official/`), the data and payloads in `MibExplorer.Core/`, and much of the shared service code come from djskual's desktop project, which is not included in this repository.

The Android version is built and developed by Nadeem based on the original desktop version by djskual. Author profile: https://github.com/nadeem2626

## Upstream license status (checked 26 Nov 2026)
- The upstream README at the base commit says: "This project is licensed under the MIT License."
- The upstream repository root at the base commit has no LICENSE file .
- The GitHub repository API reports `license: null` for djskual/MibExplorer. There is no copyright line anywhere in the upstream tree.
- Source: https://api.github.com/repos/djskual/MibExplorer and the base-commit tree.

What this means: the upstream author states MIT in the README, but without a LICENSE file or a copyright holder line the grant is informal. No license text is reproduced here, because none was supplied. Whether the stated MIT line is enough to redistribute and modify is a legal question this repository does not answer.

## This repository
The `LICENSE` file (MIT, Copyright (c) 2026 Nadeem) covers only the Android-specific changes written by Nadeem. It does not license, and is not a statement about, anything else in this repository:
- code and files that come from djskual's MibExplorer (the desktop project, the shared service code, `Scripts/`, catalogs and payloads), whose status is described above;
- third-party components listed in `THIRD-PARTY-NOTICES.md`, which keep their own licenses;
- the binaries and other items listed below, whose origin and terms are unknown.
The files are not separately marked as "Nadeem's" versus inherited, so where a file mixes both, the inherited part keeps whatever rights it already has. A sensible step before wide publication is to ask djskual to add a LICENSE file with a copyright line upstream.

## Items with unresolved redistribution rights
- `MibExplorer.Core/Payload/`: `CodingCenter/*/pc`, `CodingCenter/*/dumb_persistence_reader`, `AdaptationsCenter/*/pc` are QNX binaries carried over from upstream. Their embedded strings refer to a Volkswagen MIB2 firmware build tree (5.49.12.SR3 MIB2MAIN). Their origin and redistribution terms are not documented, and they may be vendor firmware code.
- `Payload/sshd.zip` carries OpenSSH 5.2 (QNX Secure Shell build) and OpenSSL 1.0.2j libraries. Those projects have their own license notices that must accompany binary redistribution; no notices are included.
- `Scripts/Official/` is a pinned copy of the upstream official scripts, also embedded in the Android app so the Scripts list works offline. Its terms follow the upstream status above.
- `MibExplorer.Core/Data/` catalogs (coding, adaptations, ODX JSON) come from upstream or from data of other vehicles; their source terms are not documented.
