# Changelog

## Unreleased

- One-line installers for Linux and Steam Deck (`install.py`) and Windows (`install.ps1`). They find FM27, install BepInEx 6.0.0-be.793 with a checksum check, download the latest plugin and, on Linux, set the Proton winhttp override so no launch option is needed. `--uninstall` removes everything again.

## 1.0.0 - 2026-11-09

First release, tested on FM27 Steam build 26340721 (Unity 6000.3.18f1) with BepInEx 6.0.0-be.793, under Proton on Linux.

- Real CA/PA next to FM's own Current/Potential Ability stars in the profile, hover popup, scout and coach reports and table columns, and also where FM writes "Unknown".
- A dedicated "CA x · PA y" next to the player's name in the profile header and the hover popup, for every player, scouted or not.
- FM's Current/Potential Ability columns sort by the exact number.
- F9 shows or hides the numbers.
- Read-only. Turns itself off after repeated errors and gives FM back its own sort.
