# How it works

## Where the numbers come from

- FM27's binding properties for the real values (`PlayerCurrentAbility`, `PlayerPotentialAbility`) only answer when the In-Game Editor is active and return null otherwise. The plugin doesn't unlock them.
- Like FMRTE, it reads the values from FM's native player objects in `game_plugin.dll`.
  - The classes are found by their MSVC RTTI names (`db::ACTUAL_PLAYER`, `db::PLAYING_DATA`).
  - A background thread indexes record id → object in about half a second.
  - CA is the int16 at `P-0x24`, PA at `P-0x22`.
  - Some players, mostly older ones, are a `db::ACTUAL_PLAYER_AND_NON_PLAYER`, which adds a staff part. Its person part sits further in (`+0x380` instead of `+0x288`), so CA and PA are at `P-0x11C` and `P-0x11A` there.
- Every read goes through `ReadProcessMemory` on its own process and first checks three vtables and the record id, so freed or moved memory can't crash the game.

## Where they are shown

- Every CA/PA star is FM's UI Toolkit element `FMStarRatingTemp`, inside a star cell template (a `BindingExpect` with a `starrating` parameter).
- FM only creates the star widget when it shows stars, so the plugin anchors to the cell itself. That is why "Unknown" cells get numbers too.
- Harmony postfixes on bind and unbind only queue work, and the UI changes later in the frame, outside FM's own bind walk.
- For each cell, the plugin walks FM's binding tree up to the player (following remap aliases) and tells CA from PA by the path, e.g. `…ScoutedPotentialAbilityInfo.StarRange`. It then puts a `Label` with FM's text classes beside the stars.
- Player headers use the database-id display as their anchor, so they work without any stars.
- Values are re-read when cells are rebound (sorted tables reuse cells), when list rows are reused, and every 5 s while visible.

## Exact sorting

- CA/PA columns' sort paths (`TableTemplate` rows) point at `PlayerCurrentAbility` / `PlayerPotentialAbility`.
- For players, the plugin answers those binding channels in `InteropDataHandler` from memory instead of the native side, so FM's own sort uses the real numbers.

## After an FM27 update

- BepInEx rebuilds its interop files by itself, so the first start is slower.
- The plugin finds FM's classes by name, so most updates need nothing. If a build moves CA/PA inside the player object, `Data.*` in the [configuration](configuration.md) can follow it without a rebuild. The `layout` debug command finds the new offsets.

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) and an FM27 folder where BepInEx has run once, which creates `BepInEx/interop`.

```
dotnet test tests/Tests.csproj                                          # game-independent logic
dotnet build src -c Release -p:GameDir="/path/to/Football Manager 27"   # -> src/bin/Release/net6.0/FM27AbilityNumbers.dll
```

[tools/](../tools/) has an offline interop generator (build without starting the game) and scripts for researching FM27's UI.
