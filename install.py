#!/usr/bin/env python3
"""FM27 Ability Numbers installer for Linux and Steam Deck (Proton).

Finds Football Manager 27 in your Steam libraries, installs BepInEx and the plugin, and tells Proton to load
BepInEx (a winhttp override in the game's own Wine prefix, so no launch option is needed).

    curl -fsSL https://raw.githubusercontent.com/da3gal/fm27-ability-numbers/main/install.py | python3 -
    python3 install.py [--game-dir DIR] [--check] [--uninstall]
"""
import argparse
import hashlib
import re
import shutil
import sys
import tempfile
import time
import urllib.request
import zipfile
from pathlib import Path

APP_ID = "4530370"
GAME_FOLDER = "Football Manager 27"
# Pinned to the BepInEx build the plugin is tested with; the checksum guards the download.
BEPINEX_URL = "https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip"
BEPINEX_SHA256 = "5b63d7d0e92a2448ccd13b738ff95ff1cc1026c6d718555d4df8e6c824953ac4"
PLUGIN_URL = "https://github.com/da3gal/fm27-ability-numbers/releases/latest/download/FM27AbilityNumbers.dll"
PLUGIN_DIR = Path("BepInEx/plugins/FM27AbilityNumbers")
BEPINEX_MARKER = Path("BepInEx/core/BepInEx.Unity.IL2CPP.dll")
BEPINEX_ITEMS = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version", "BepInEx", "dotnet"]
STEAM_ROOTS = [
    "~/.local/share/Steam",
    "~/.steam/steam",
    "~/.steam/root",
    "~/snap/steam/common/.local/share/Steam",
    "~/.var/app/com.valvesoftware.Steam/.local/share/Steam",
]
OVERRIDE_SECTION = "[Software\\\\Wine\\\\DllOverrides]"
OVERRIDE_LINE = '"winhttp"="native,builtin"'


def say(msg):
    print(f"  {msg}", flush=True)


def fail(msg):
    print(f"\nError. {msg}", file=sys.stderr)
    sys.exit(1)


def library_paths(vdf_text):
    """Library folders listed in a Steam libraryfolders.vdf."""
    return [p.replace("\\\\", "\\") for p in re.findall(r'"path"\s+"([^"]+)"', vdf_text)]


def steam_libraries(roots=STEAM_ROOTS):
    found = []
    for root in roots:
        base = Path(root).expanduser()
        candidates = [base]
        vdf = base / "steamapps" / "libraryfolders.vdf"
        if vdf.is_file():
            candidates += [Path(p) for p in library_paths(vdf.read_text(errors="replace"))]
        for c in candidates:
            try:
                c = c.resolve()
            except OSError:
                continue
            if (c / "steamapps").is_dir() and c not in found:
                found.append(c)
    return found


def find_game():
    for lib in steam_libraries():
        game = lib / "steamapps" / "common" / GAME_FOLDER
        if (game / "fm.exe").is_file():
            return game
    return None


def prefix_registry(game):
    # compatdata lives in the same Steam library as the game: <library>/steamapps/{common,compatdata}
    return game.parent.parent / "compatdata" / APP_ID / "pfx" / "user.reg"


def game_running():
    for proc in Path("/proc").iterdir():
        if not proc.name.isdigit():
            continue
        try:
            cmd = (proc / "cmdline").read_bytes()
        except OSError:
            continue
        if b"fm.exe" in cmd and GAME_FOLDER.encode() in cmd:
            return True
    return False


def set_winhttp_override(text, enable):
    """Returns the user.reg text with the winhttp override added or removed, or None if nothing changes."""
    lines = text.split("\n")
    start = next((i for i, l in enumerate(lines) if l.startswith(OVERRIDE_SECTION)), None)
    if start is None:
        if not enable:
            return None
        if lines and lines[-1] == "":
            lines.pop()
        lines += ["", f"{OVERRIDE_SECTION} {int(time.time())}", OVERRIDE_LINE, ""]
        return "\n".join(lines)
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("[")), len(lines))
    at = next((i for i in range(start + 1, end) if lines[i].lower().startswith('"winhttp"=')), None)
    if enable:
        if at is not None and lines[at] == OVERRIDE_LINE:
            return None
        if at is not None:
            lines[at] = OVERRIDE_LINE
        else:
            insert = start + 1
            while insert < end and lines[insert].startswith("#"):  # keep the #time line right under the header
                insert += 1
            lines.insert(insert, OVERRIDE_LINE)
    else:
        if at is None:
            return None
        del lines[at]
    return "\n".join(lines)


def write_atomic(path, data):
    tmp = path.with_name(path.name + ".fm27an-tmp")
    if isinstance(data, bytes):
        tmp.write_bytes(data)
    else:
        tmp.write_text(data, encoding="utf-8", errors="surrogateescape")
    tmp.replace(path)


def update_registry(game, enable):
    reg = prefix_registry(game)
    if not reg.is_file():
        return False
    text = reg.read_text(encoding="utf-8", errors="surrogateescape")
    new = set_winhttp_override(text, enable)
    if new is not None:
        backup = reg.with_name("user.reg.fm27an-backup")
        if not backup.exists():
            shutil.copy2(reg, backup)
        write_atomic(reg, new)
    return True


def download(url):
    req = urllib.request.Request(url, headers={"User-Agent": "fm27-ability-numbers-installer"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def install_bepinex(game):
    if (game / BEPINEX_MARKER).is_file():
        say("BepInEx (IL2CPP) is already installed, keeping it.")
        return
    say("Downloading BepInEx 6.0.0-be.793...")
    data = download(BEPINEX_URL)
    if hashlib.sha256(data).hexdigest() != BEPINEX_SHA256:
        fail("The BepInEx download does not match its checksum. Nothing was installed.")
    with tempfile.TemporaryDirectory() as tmp:
        zpath = Path(tmp) / "bepinex.zip"
        zpath.write_bytes(data)
        with zipfile.ZipFile(zpath) as z:
            members = [m for m in z.namelist() if m.split("/")[0] in BEPINEX_ITEMS]  # skips changelog.txt
            z.extractall(game, members)
    say("BepInEx installed.")


def install_plugin(game):
    say("Downloading the latest FM27 Ability Numbers...")
    data = download(PLUGIN_URL)
    if not data.startswith(b"MZ") or len(data) < 10_000:
        fail("The plugin download is not a valid DLL. Nothing was changed.")
    target = game / PLUGIN_DIR
    target.mkdir(parents=True, exist_ok=True)
    write_atomic(target / "FM27AbilityNumbers.dll", data)
    say(f"Plugin installed ({len(data) // 1024} KB).")


def uninstall(game):
    shutil.rmtree(game / PLUGIN_DIR, ignore_errors=True)
    say("Plugin removed.")
    plugins = game / "BepInEx" / "plugins"
    others = [p.name for p in plugins.iterdir()] if plugins.is_dir() else []
    if others:
        say(f"BepInEx kept, it still has other plugins ({', '.join(others)}).")
        return
    for item in BEPINEX_ITEMS + ["changelog.txt"]:
        path = game / item
        if path.is_dir():
            shutil.rmtree(path, ignore_errors=True)
        elif path.exists():
            path.unlink()
    update_registry(game, enable=False)
    say("BepInEx removed, the game is back to vanilla.")


def main(argv=None):
    ap = argparse.ArgumentParser(description="Install FM27 Ability Numbers (and BepInEx) on Linux / Steam Deck.")
    ap.add_argument("--game-dir", type=Path, help="the folder that contains fm.exe (found automatically if omitted)")
    ap.add_argument("--uninstall", action="store_true", help="remove the plugin, and BepInEx if nothing else uses it")
    ap.add_argument("--check", action="store_true", help="only show what was found, change nothing")
    args = ap.parse_args(argv)

    if not sys.platform.startswith("linux"):
        fail("This installer is for Linux and Steam Deck. On Windows use install.ps1.")
    print("FM27 Ability Numbers installer")
    game = args.game_dir.expanduser().resolve() if args.game_dir else find_game()
    if game is None or not (game / "fm.exe").is_file():
        fail("Football Manager 27 was not found. Run again with --game-dir \"/path/to/Football Manager 27\".")
    say(f"Game folder is {game}")

    if args.check:
        say(f"BepInEx installed is {(game / BEPINEX_MARKER).is_file()}")
        say(f"Plugin installed is {(game / PLUGIN_DIR / 'FM27AbilityNumbers.dll').is_file()}")
        reg = prefix_registry(game)
        has = reg.is_file() and OVERRIDE_LINE in reg.read_text(errors="replace")
        say(f"Proton winhttp override set is {has}" if reg.is_file() else "Proton prefix not created yet (start FM27 once).")
        return 0
    if game_running():
        fail("Football Manager 27 is running. Quit the game and run the installer again.")

    if args.uninstall:
        uninstall(game)
        return 0
    install_bepinex(game)
    install_plugin(game)
    if update_registry(game, enable=True):
        say("Proton is set to load BepInEx (winhttp override in the game's Wine prefix).")
    else:
        say("The game's Proton prefix does not exist yet. Either start FM27 once, quit, and run this installer")
        say('again, or set the Steam launch option  WINEDLLOVERRIDES="winhttp=n,b" %command%')
    print("\nDone. Start FM27. The first start after installing BepInEx takes a few extra minutes.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
