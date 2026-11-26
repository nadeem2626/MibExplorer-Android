# Android device test checklist

Use a spare or test unit for anything that writes. Report: Android version, device model, which steps passed, screenshots.

Read-only first:
1. App installs and launches, Connect screen renders in portrait and landscape.
2. Settings > SSH key: generate a key, public key is shown, still there after restart.
3. Connect by IP: host-key fingerprint dialog appears, compare with the unit's real fingerprint, trust it.
4. Reconnect: no dialog. If the unit's key changes, the connection must be refused.
5. Files: list loads, enter folders, breadcrumb, Up, Search, Sort, Details and text preview.
6. Download a file and a folder.
7. Terminal (Tools): prompt appears, typing, Ctrl+C, arrow keys, rotation, Copy and Paste.
8. Lose Wi-Fi: status shows Disconnected, reconnect works.
9. Theme switch (Night and Day) and side rail highlight in landscape.

Writes (Write mode on, spare unit only): upload, rename, new folder, copy, move, delete empty folder, edit with diff.

Scripts, adaptation changes and reboot each also need their own typed confirmation (`RUN`, `ADAPT`, `REBOOT`) and have never been run on a unit. Coding writes are not available in this app, so there is nothing to test there.
