# Maintainer tools

You don't need any of these to use or build the plugin. They help when a new FM27 build breaks something.

## interopgen

An offline copy of BepInEx be.793's interop generation, so the plugin can be compiled without starting the game.

```
dotnet build tools/interopgen -c Release
dotnet tools/interopgen/bin/Release/net8.0/interopgen.dll "<FM27 dir>" 6000.3.18f1 <unity-libs dir> vendor/interop vendor/dummy
```

- It references `vendor/bepinex` (an extracted BepInEx zip).
- `<unity-libs dir>` is the extracted `https://unity.bepinex.dev/libraries/<unity version>.zip`.
- The output stays in `vendor/`, which git ignores.

## research

Python ([UnityPy](https://github.com/K0lb3/UnityPy)) scripts for looking at FM27's UI layouts (UXML `VisualTreeAsset`s inside its Addressables bundles).

```
python3 tools/research/vta_scan.py "<FM27 dir>/fm_Data/StreamingAssets/aa/StandaloneWindows64" vendor/vta
python3 tools/research/vta_print.py vendor/vta/<Template>.json
```

The dumps are game data, so keep them in `vendor/`.
