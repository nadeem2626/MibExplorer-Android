# Known limitations and test status

The app is experimental. Safety rules are in [SAFETY.md](SAFETY.md).

Evidence levels: Built = compiles. Linux-tested = core logic run by the test programs in `Tests/` against local servers. Unit-tested = run against a real head unit.

## Not implemented (shown as unavailable in the app; missing work or safety block, not a product decision)
- Live Adaptations read from the unit. The bundled catalog was made for a different vehicle and its mapping to a given unit is not proven.
- Coding feature writes. The unit-side write contract is not available, so writes fail closed.
- Android self-update, and any update check for the Android app itself.
- Side-by-side and intra-line diff (unified diff only).
- Recursive delete (disabled as a safety measure; empty folders only).
- Desktop-only features: window size memory, startup update check.

## Behaviour to know
- Batch operations (multi-file transfer, copy, move, delete) are not atomic. A cancelled or failed batch can leave partial results. Telnet and FTP cancels can leave `.mibx-part` or `.mibx-up-*` temp files on the unit.
- Write mode is off on every new connection. Scripts, Coding, Adaptations, reboot and uninstall also need their own typed confirmation.
- The terminal accepts pasted text only through one guarded path (length cap, confirmation on line breaks).
- Reboot "reachable again" means the port came back, not proof that the unit rebooted.
- The foreground transfer service allows background execution but Android can still stop the app.

## Verification status
- Unit-tested: early connect, list and enter-folder only.
- Never run on a unit: Coding, Adaptations, SD package install and uninstall, reboot, scripts.
- UI: installed on a Xiaomi tablet (Android 15) for several builds; the owner's photos exposed layout defects that were fixed in source. The latest fixes (layout, theme, side rail, header and button styling) have not been confirmed on a device.
- Release signing is not set up. Test builds were signed with a throwaway debug key that is not in this repository.
