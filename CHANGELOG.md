# Changelog

## 1.0.2 - 2026-11-11

- Tested on FM27 Steam build 26385295. The update changed nothing the plugin relies on, so 1.0.1 keeps working there too.
- Numbers no longer go missing for up to five minutes when the player index was built while FM was loading a save. An index that comes out empty, or less than half the size of the one before, is now rebuilt after 20 seconds.
- Two new debug commands for future FM27 updates. `layout` finds where CA and PA sit in the player object, and `raw <id>` logs one player's object.

## 1.0.1 - 2026-11-10

- Players FM keeps as `db::ACTUAL_PLAYER_AND_NON_PLAYER`, mostly older ones, now get their numbers too. The index missed them before.
- A CA/PA column now sorts even when some players in it can't be read. FM waits for a value from every row, so those players get 0 and sort last.
- One-line installers for Linux and Steam Deck (`install.py`) and Windows (`install.ps1`). They find FM27, install BepInEx 6.0.0-be.793 with a checksum check, download the latest plugin and, on Linux, set the Proton winhttp override so no launch option is needed. `--uninstall` removes everything again.

## 1.0.0 - 2026-11-09

First release, tested on FM27 Steam build 26340721 (Unity 6000.3.18f1) with BepInEx 6.0.0-be.793, under Proton on Linux.

- Real CA/PA next to FM's own Current/Potential Ability stars in the profile, hover popup, scout and coach reports and table columns, and also where FM writes "Unknown".
- A dedicated "CA x · PA y" next to the player's name in the profile header and the hover popup, for every player, scouted or not.
- FM's Current/Potential Ability columns sort by the exact number.
- F9 shows or hides the numbers.
- Read-only. Turns itself off after repeated errors and gives FM back its own sort.
