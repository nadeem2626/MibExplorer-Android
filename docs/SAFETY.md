# Safety

## Write mode and typed confirmations
- Write mode is off every time you connect. Turn it on for the current connection in Settings.
- With Write mode off the app only reads: browse, search, sort, details, preview, download, open a folder in the terminal.
- With Write mode on, changes made by the app over SSH, Telnet or FTP are possible: upload, replace, new folder, rename, copy, move, delete (empty folders only), chmod, saving an edited file.
- Some actions also need a typed word: `RUN` (scripts), `CODING`, `ADAPT`, `REBOOT`, `UNINSTALL` (direct SSH uninstall).
- Running scripts, adaptation changes, reboot and the direct SSH uninstall go over SSH only.

## SD update packages
The app can also build SD-card update packages. The unit's own update software installs them, so Write mode and typed confirmations do not apply to that step.

## Other rules
- Recursive delete is disabled. Delete works on empty folders only.
- Batch operations (multi-file transfer, copy, move, delete) are not atomic. A cancelled or failed batch can leave partial results. Telnet and FTP cancels can leave temporary files on the unit.
- The app never disconnects on its own.
- Telnet and FTP send the password unencrypted.
- The private SSH key stays in the device's secure storage and is never shown.
