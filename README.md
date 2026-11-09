<p align="center">
  <img src="docs/images/banner.png" alt="FM27 Ability Numbers: the real Current and Potential Ability inside Football Manager 27's own screens" width="100%">
</p>

<p align="center">
  <a href="https://github.com/da3gal/fm27-ability-numbers/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/da3gal/fm27-ability-numbers?style=flat-square&color=ff3ea5"></a>
  <a href="https://github.com/da3gal/fm27-ability-numbers/actions/workflows/ci.yml"><img alt="Tests" src="https://img.shields.io/github/actions/workflow/status/da3gal/fm27-ability-numbers/ci.yml?branch=main&style=flat-square&label=tests"></a>
  <img alt="Football Manager 27" src="https://img.shields.io/badge/Football%20Manager-27-1f2a6b?style=flat-square">
  <img alt="BepInEx 6 BE build 793 or newer" src="https://img.shields.io/badge/BepInEx-6%20BE%20%E2%89%A5%20793-5b3fd6?style=flat-square">
  <img alt="Windows, Linux (Proton), Steam Deck" src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20(Proton)%20%7C%20Steam%20Deck-2b2f55?style=flat-square">
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/github/license/da3gal/fm27-ability-numbers?style=flat-square&color=3fb950"></a>
</p>

<p align="center">
  <b>Every player's real CA and PA, right inside Football Manager 27's own screens.</b><br>
  <a href="#install">Install</a> · <a href="#use">Use</a> · <a href="#faq">FAQ</a> · <a href="#how-it-works">How it works</a>
</p>

---

FM27 hides a player's Current Ability (CA) and Potential Ability (PA) behind star ratings, and often behind "Unknown". **FM27 Ability Numbers** is a small [BepInEx](https://github.com/BepInEx/BepInEx) plugin that writes the real numbers into FM's own UI, in FM's own font and layout. There is no extra window and no tool to alt-tab to. It only reads data and never changes your save.

## Features

- **Every player header.** A dedicated `CA 194 · PA 195` next to the name in the profile and the hover popup, for every player, even while FM is still scouting him.
- **Next to the stars.** The number sits beside FM's Current/Potential Ability stars wherever they appear, and beside "Unknown" too.
- **Real sorting.** FM's *Ability* and *Potential* columns in squad view, shortlists and player search sort by the exact number instead of half-star buckets.
- **F9.** Show or hide the numbers at any time. The plugin remembers your choice.
- **Safe by design.** Read-only, fails safe (it switches itself off and gives FM back its own sort), and survives most game updates.

## In game

<p align="center">
  <img src="docs/images/profile-header.png" alt="Profile header of a player still being scouted: FM shows no stars, but the header reads CA 194 · PA 195" width="100%"><br>
  <sub><b>Profile header.</b> FM is still scouting this player and shows no stars at all, yet the header still says <code>CA 194 · PA 195</code>.</sub>
</p>

<p align="center">
  <img src="docs/images/squad.png" alt="Squad view with Ability and Potential columns sorted by the real Potential Ability, a number beside every star rating" width="100%"><br>
  <sub><b>Squad view.</b> FM's own <i>Ability</i> and <i>Potential</i> columns, sorted by the real PA (177, 176, 175…).</sub>
</p>

<p align="center">
  <img src="docs/images/shortlist.png" alt="Shortlist sorted by real Potential Ability, with numbers next to stars and next to Unknown" width="100%"><br>
  <sub><b>Shortlist.</b> Sorted by real PA. Unscouted players get their number next to "Unknown".</sub>
</p>

<p align="center">
  <img src="docs/images/popup.png" alt="Player hover popup with CA 159 · PA 165 next to the name and numbers by the coach report stars" width="70%"><br>
  <sub><b>Hover popup.</b> <code>CA · PA</code> next to the name, numbers by the coach-report stars.</sub>
</p>

## Compatibility

| Football Manager 27 | BepInEx | Platform | Status |
|---|---|---|---|
| Steam, build 26340721 (Unity 6000.3.18f1) | 6.0.0-be.793 | Linux (Proton Experimental) | Tested |
| Steam, same build | 6.0.0-be.793 | Windows | Expected to work, untested |
| Game Pass / Epic | | | Untested |

## Install

You need FM27 for PC and [BepInEx 6 Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be), Unity **IL2CPP win-x64**, build **793 or newer**. BepInEx 5 and 6.0.0-pre releases do not work with FM27.

1. Extract the BepInEx zip into the FM27 folder (the one with `fm.exe`). You now have `winhttp.dll`, `doorstop_config.ini`, `BepInEx/` and `dotnet/` next to `fm.exe`.
2. Download `FM27AbilityNumbers.dll` from [Releases](https://github.com/da3gal/fm27-ability-numbers/releases/latest) and put it in `BepInEx/plugins/FM27AbilityNumbers/`.
3. On Linux (Proton) or Steam Deck, open FM27 → *Properties* → *Launch Options* in Steam and set the line below.
   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
   Without it, Proton ignores BepInEx's `winhttp.dll` and nothing loads. If you already use `WINEDLLOVERRIDES`, add `winhttp=n,b` to it with a `;` in between.
4. Start FM27. The first start after installing BepInEx, or after a game update, takes a few extra minutes while BepInEx prepares itself.

**Did it work?** `BepInEx/LogOutput.log` should contain `Loading [FM27 Ability Numbers`, and `players indexed` once a save is loaded.

## Use

| Where | What you get |
|---|---|
| Profile header, hover popup | `CA x · PA y` next to the player's name |
| CA/PA stars anywhere | The number beside the stars, or beside "Unknown" |
| Squad view, shortlist, search… | Right-click a column header → **Insert Column** → **Ability and Roles** → **Ability** / **Potential**. Click the header to sort by the real number. FM remembers columns per view. |
| Show / hide | **F9** |

Role ratings ("Centre Forward ★★★") get no number on purpose, because those stars are the player's ability *in that role*, not his CA.

## Configuration

`BepInEx/config/com.da3gal.fm27abilitynumbers.cfg` is created on first start. Edit it while the game is closed.

| Setting | Default | Meaning |
|---|---|---|
| `General.Visible` | `true` | Numbers on/off (F9 flips it) |
| `General.ToggleKey` | `F9` | Unity InputSystem key name (`F10`, `Backquote`, …). Leave it empty for no key |
| `General.ShowCurrentAbility` / `ShowPotentialAbility` | `true` | Show CA / PA separately |
| `General.ExactColumnSort` | `true` | Ability/Potential columns sort by the exact number |
| `Style.LabelClasses` | `body-xs-strong global-text-primary` | FM text classes for numbers beside stars |
| `Style.HeaderClasses` | `body-md-strong white` | FM text classes for `CA x · PA y` in headers |
| `Style.Placement` | `Flow` | `Flow` puts the number beside the stars in FM's layout. `Right` and `Below` overlay it |
| `Data.*` | | Only needed for a new FM27 build ([details](#after-an-fm27-update)) |
| `Debug.Enabled` | `false` | Verbose log and a live command file ([details](#troubleshooting)) |

## FAQ

<details>
<summary><b>Does it change my save or the game?</b></summary>

No. It reads values from memory and draws labels on top of FM's UI. Remove the DLL and your game is exactly as before.
</details>

<details>
<summary><b>Isn't this what the In-Game Editor or FMRTE do?</b></summary>

Those are editors, with their own windows. This plugin only *shows* CA and PA, inside FM's own screens. It doesn't unlock the In-Game Editor and works fine next to FMRTE.
</details>

<details>
<summary><b>Is it cheating?</b></summary>

It shows information FM normally hides, so yes, a little. It is meant for your own single-player careers.
</details>

<details>
<summary><b>Will it break after an FM update?</b></summary>

Usually not. It finds FM's classes by name rather than by fixed addresses. If an update does break it, the plugin switches itself off cleanly and the game runs normally. See [After an FM27 update](#after-an-fm27-update).
</details>

<details>
<summary><b>Why is a number missing for a staff member?</b></summary>

Only players are supported for now. Staff CA/PA comes from a different object.
</details>

## After an FM27 update

- BepInEx rebuilds its interop files by itself, so the first start is slower.
- If the numbers stop appearing, look for lines from `FM27 Ability Numbers` in `BepInEx/LogOutput.log` and [open an issue](https://github.com/da3gal/fm27-ability-numbers/issues/new/choose) with the log attached.

## Troubleshooting

| Symptom | What it means |
|---|---|
| No `BepInEx/LogOutput.log` | BepInEx is not loading. On Linux or Steam Deck, the launch option from step 3 is missing. |
| `FM classes not found` in the log | FM's native layout changed in this build. Please report it. |
| `turned itself off` in the log | The plugin hit 20 errors within a minute. It removed its numbers and gave FM back its own sort, and the game keeps running. Please report it with the log. |

<details>
<summary><b>Debug command file (for bug hunting)</b></summary>

With `Debug.Enabled = true`, write commands, one per line, into `BepInEx/config/FM27AbilityNumbers.cmd` while the game runs. The results go to `BepInEx/LogOutput.log`.

- `dump` lists the tracked labels and the patched sort columns.
- `mem [id]` shows the player index status, or one player's native object.
- `props [filter]` lists the binding properties FM exposes on a person.
- `probe <Property>` reads a property for every player on screen.
- `shot` saves a screenshot to `BepInEx/shots/`.
- `reload` re-reads the config and applies it.
- `enable` turns the plugin back on after a fail-safe stop.
- `throw [n]` forces errors, to test the fail-safe.
</details>

## Uninstall

- To remove only the plugin, delete `BepInEx/plugins/FM27AbilityNumbers/`.
- To remove BepInEx as well, delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `BepInEx/` and `dotnet/` from the FM27 folder, and remove the launch option.

Steam's *Verify integrity* does not remove these files.

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) and an FM27 folder where BepInEx has run once, which creates `BepInEx/interop`.

```
dotnet test tests/Tests.csproj                                          # game-independent logic
dotnet build src -c Release -p:GameDir="/path/to/Football Manager 27"   # -> src/bin/Release/net6.0/FM27AbilityNumbers.dll
```

[tools/](tools/) has an offline interop generator (build without starting the game) and scripts for researching FM27's UI.

## How it works

<details>
<summary><b>Where the numbers come from</b></summary>

- FM27's binding properties for the real values (`PlayerCurrentAbility`, `PlayerPotentialAbility`) only answer when the In-Game Editor is active and return null otherwise. The plugin doesn't unlock them.
- Like FMRTE, it reads the values from FM's native player objects in `game_plugin.dll`.
  - The classes are found by their MSVC RTTI names (`db::ACTUAL_PLAYER`, `db::PLAYING_DATA`).
  - A background thread indexes record id → object in about half a second.
  - CA is the int16 at `P-0x24`, PA at `P-0x22`.
- Every read goes through `ReadProcessMemory` on its own process and first checks three vtables and the record id, so freed or moved memory can't crash the game.
</details>

<details>
<summary><b>Where they are shown</b></summary>

- Every CA/PA star is FM's UI Toolkit element `FMStarRatingTemp`, inside a star cell template (a `BindingExpect` with a `starrating` parameter).
- FM only creates the star widget when it shows stars, so the plugin anchors to the cell itself. That is why "Unknown" cells get numbers too.
- Harmony postfixes on bind and unbind only queue work, and the UI changes later in the frame, outside FM's own bind walk.
- For each cell, the plugin walks FM's binding tree up to the player (following remap aliases) and tells CA from PA by the path, e.g. `…ScoutedPotentialAbilityInfo.StarRange`. It then puts a `Label` with FM's text classes beside the stars.
- Player headers use the database-id display as their anchor, so they work without any stars.
- Values are re-read when cells are rebound (sorted tables reuse cells), when list rows are reused, and every 5 s while visible.
</details>

<details>
<summary><b>Exact sorting</b></summary>

- CA/PA columns' sort paths (`TableTemplate` rows) point at `PlayerCurrentAbility` / `PlayerPotentialAbility`.
- For players, the plugin answers those binding channels in `InteropDataHandler` from memory instead of the native side, so FM's own sort uses the real numbers.
</details>

## Contributing

Bug reports and ideas are very welcome. Please use the [issue templates](https://github.com/da3gal/fm27-ability-numbers/issues/new/choose) and attach `BepInEx/LogOutput.log`. Pull requests are welcome too. Please keep them small and run `dotnet test` first.

## Credits

- The player memory layout comes from the research in [SiRRAHx/fm27-live-editor](https://github.com/SiRRAHx/fm27-live-editor).
- Built on [BepInEx](https://github.com/BepInEx/BepInEx), [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop), [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) and [HarmonyX](https://github.com/BepInEx/HarmonyX).

## License

[MIT](LICENSE). FM27 Ability Numbers is an unofficial fan project and is not made by or affiliated with Sports Interactive or SEGA. Football Manager is a trademark of Sports Interactive / SEGA. Screenshots are from the author's own career save.
