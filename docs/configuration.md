# Configuration

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
| `Data.CurrentAbilityOffset` / `PotentialAbilityOffset` | `-36` / `-34` | Offset of CA / PA from FM's native person object. Only change it for a new FM27 build |
| `Data.CurrentAbilityProperty` / `PotentialAbilityProperty` | `PlayerCurrentAbility` / `PlayerPotentialAbility` | FM binding properties the plugin answers for the exact sort |
| `Debug.Enabled` | `false` | Verbose log and the live command file below |

## Debug command file

With `Debug.Enabled = true`, write commands, one per line, into `BepInEx/config/FM27AbilityNumbers.cmd` while the game runs. The results go to `BepInEx/LogOutput.log`.

- `dump` lists the tracked labels and the patched sort columns.
- `mem [id]` shows the player index status, or one player's native object.
- `layout` checks every player in the index and logs the offset pairs where CA and PA look valid. After an FM27 update that moves CA/PA, the top line gives the new `Data.*` offsets.
- `raw <id>` logs one player's native object as int16 words, with offsets in the same form as `Data.*`.
- `find <id>` searches memory for a player the index is missing and logs which native class holds that player.
- `props [filter]` lists the binding properties FM exposes on a person.
- `probe <Property>` reads a property for every player on screen.
- `shot` saves a screenshot to `BepInEx/shots/`.
- `reload` re-reads the config and applies it.
- `enable` turns the plugin back on after a fail-safe stop.
- `throw [n]` forces errors, to test the fail-safe.
