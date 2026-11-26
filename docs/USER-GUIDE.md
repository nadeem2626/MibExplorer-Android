# User guide

This guide describes what the Android app offers today. Only the early connect, list and enter-folder functions have been used on a real unit; everything else is described from the app's screens and code and may behave differently on your unit. See [KNOWN-LIMITATIONS.md](KNOWN-LIMITATIONS.md).

## Navigation
Main sections: Connect, Files, Coding, Adaptations, Tools. In portrait they are tabs at the bottom. On a wide landscape screen a side rail replaces the tabs. Settings, Transfers, Terminal and Scripts open from the Tools screen.

## First connection
1. Join the head unit's Wi-Fi in the device's Android settings.
2. Open **Connect**. Under Network, tap **Use Wi-Fi network** so the app talks over that Wi-Fi, then **Detect unit IP** or type the IP yourself.
3. In **Unit details** set the profile name, protocol (SSH, Telnet or FTP), IP, port, user and password. Presets only fill a name and IP, so check the protocol, port and user against your unit.
4. SSH accepts two kinds of login. Type the unit's password to log in with the password. Or leave the password empty to log in with the app's saved SSH key. The key pair is made in Settings > SSH key > Generate new, or you can import your own private key file there. A key login only works if the unit already accepts that key; if it does not, use the password. If the unit has no SSH at all, see [Install](INSTALL.md).
5. Tap **Connect**. On the first SSH connection the app shows the unit's host-key fingerprint. Compare it with the unit's real fingerprint before you trust it. If the key later changes, the app refuses the connection. Trusted hosts are listed under Settings > Trusted hosts.
6. **Save profile** keeps the unit for next time. "Remember password on this device" stores the password encrypted on the device.

Telnet and FTP send the password unencrypted.

## Write mode
Write mode is off every time you connect. Turn it on for the current connection in Settings (Write mode, this connection). Without it the app only reads from the unit. See [SAFETY.md](SAFETY.md). Even with it on, scripts, reboot, adaptations, coding and SSH uninstall each ask you to type a word (`RUN`, `REBOOT`, `ADAPT`, `CODING`, `UNINSTALL`).

## Files
Browse the unit's file system, with a breadcrumb, Search (by name in the open folder) and Sort (name, modified, size). Select items for actions. Open an item for details and a text preview.
- Read-only actions (Write mode off): browse, search, sort, details and preview, download files and folders (a folder is saved as a zip), open a folder in the Terminal.
- Actions that change the unit (Write mode on): upload, replace, new folder, rename, copy, move, delete (empty folders only), chmod, edit a text file with a diff before saving.
- Some areas of the unit are treated as system areas, and the app shows a banner when nothing can be changed there.

## Terminal
Tools > Terminal opens a shell on the unit. It needs an SSH or Telnet connection (FTP has no shell). It has a key row for keys a device keyboard lacks (Esc, Tab, Ctrl and arrows), Copy, Paste, Clear and Save log. Pasting text with a line break asks for confirmation.

## Transfers
Tools > Transfers lists downloads, uploads and replaces of this session with their results. Queued transfers can be cancelled. Cancelling over Telnet or FTP can leave a partial temporary file on the unit (`.mibx-part` or `.mibx-up-*`).

## Scripts
Tools > Scripts lists the bundled official scripts and scripts you import with **Import .sh**. Update (internet) refreshes the official set and needs internet, not a unit. Running a script needs an SSH connection, Write mode and typing `RUN`. The output appears live; you can cancel a running script and copy or clear the log.

## Coding
Read from unit loads the unit's coding. Tabs: Features, Raw, Changes, History. Writing coding is not available in this app.

## Adaptations
Read the unit identity to load the matching reference catalog, search groups and adaptations, and review History. Live adaptation values are not read from the unit. Applying changes is built behind SSH, Write mode and typing `ADAPT`, and has never been run on a unit.

## Reboot and uninstall
Tools has Reboot unit (SSH, Write mode, typing `REBOOT`) and Uninstall SSH (SSH, Write mode, typing `UNINSTALL`). "Reachable again" after a reboot only means the port answered, not proof of a reboot.

## Settings
Write mode, appearance (Automatic follows the device setting, Day or Night; Night is the default), SSH key (show the public key, copy it, generate a new key, import a private key file with its passphrase), trusted hosts, a startup log under Diagnostics, Licenses and About. The private key stays in the device's secure storage and is never shown. Generating a new key keeps the old one as "previous" until the unit accepts the new public key.
