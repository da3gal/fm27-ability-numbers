"""Tests for the game-independent parts of install.py (python3 -m unittest discover -s tests)."""
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("installer", Path(__file__).resolve().parent.parent / "install.py")
inst = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inst)

REG = """WINE REGISTRY Version 2
;; All keys relative to REGISTRY\\\\User\\\\S-1-5-21

[Software\\\\Wine\\\\DllOverrides] 1793301782
#time=1dd67daeaac4a98
"api-ms-win-crt-conio-l1-1-0"="native,builtin"

[Software\\\\Wine\\\\Fonts] 1793301782
"Replacements"="x"
"""


class Override(unittest.TestCase):
    def test_adds_under_header_and_time_line(self):
        new = inst.set_winhttp_override(REG, True)
        lines = new.split("\n")
        i = lines.index(inst.OVERRIDE_LINE)
        self.assertTrue(lines[i - 1].startswith("#time="))
        self.assertTrue(lines[i - 2].startswith(inst.OVERRIDE_SECTION))
        self.assertIn('"api-ms-win-crt-conio-l1-1-0"="native,builtin"', new)

    def test_idempotent(self):
        once = inst.set_winhttp_override(REG, True)
        self.assertIsNone(inst.set_winhttp_override(once, True))

    def test_replaces_other_value(self):
        reg = REG.replace('#time=1dd67daeaac4a98\n', '#time=1dd67daeaac4a98\n"winhttp"="builtin"\n')
        new = inst.set_winhttp_override(reg, True)
        self.assertEqual(new.count('"winhttp"='), 1)
        self.assertIn(inst.OVERRIDE_LINE, new)

    def test_creates_missing_section(self):
        reg = 'WINE REGISTRY Version 2\n\n[Software\\\\Wine\\\\Fonts] 1\n"a"="b"\n'
        new = inst.set_winhttp_override(reg, True)
        self.assertTrue(new.endswith(inst.OVERRIDE_LINE + "\n"))
        self.assertIn("\n\n" + inst.OVERRIDE_SECTION + " ", new)

    def test_remove_restores_original(self):
        self.assertEqual(inst.set_winhttp_override(inst.set_winhttp_override(REG, True), False), REG)

    def test_remove_when_absent_changes_nothing(self):
        self.assertIsNone(inst.set_winhttp_override(REG, False))

    def test_other_sections_untouched(self):
        new = inst.set_winhttp_override(REG, True)
        self.assertIn('[Software\\\\Wine\\\\Fonts] 1793301782\n"Replacements"="x"', new)


class Libraries(unittest.TestCase):
    def test_parses_library_paths(self):
        vdf = '"libraryfolders"\n{\n\t"0"\n\t{\n\t\t"path"\t\t"/home/a/.local/share/Steam"\n\t}\n\t"1"\n\t{\n\t\t"path"\t\t"/mnt/games/SteamLibrary"\n\t}\n}'
        self.assertEqual(inst.library_paths(vdf), ["/home/a/.local/share/Steam", "/mnt/games/SteamLibrary"])

    def test_prefix_is_in_the_games_library(self):
        game = Path("/lib/steamapps/common/Football Manager 27")
        self.assertEqual(inst.prefix_registry(game), Path("/lib/steamapps/compatdata/4530370/pfx/user.reg"))


if __name__ == "__main__":
    unittest.main()
