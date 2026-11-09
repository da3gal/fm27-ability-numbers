using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FM27AbilityNumbers
{
    [BepInPlugin(Guid, "FM27 Ability Numbers", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.da3gal.fm27abilitynumbers";
        public const string Version = "1.0.0";

        internal static ManualLogSource L;
        internal static Settings S;
        internal static ConfigFile Cfg;

        public override void Load()
        {
            L = Log;
            Cfg = Config;
            S = new Settings(Config);
            AddComponent<Driver>(); // first: the hooks queue work that only the Driver drains
            var harmony = new Harmony(Guid);
            // Patched separately: if a game update renames one target, the other features keep working.
            TryPatch(harmony, typeof(StarPatches), "number labels");
            TryPatch(harmony, typeof(ExpectPatches), "numbers in star cells and the profile/popup header");
            TryPatch(harmony, typeof(RebindPatch), "numbers in scrolled lists");
            TryPatch(harmony, typeof(SortPatches), "exact CA/PA column sort");
            TryPatch(harmony, typeof(ChannelPatches), "exact CA/PA values for FM's own bindings (sorting)");
            L.LogInfo($"FM27 Ability Numbers {Version} loaded. Toggle key: {S.ToggleKey.Value}. " +
                      $"CA property: {S.CaProperty.Value}, PA property: {S.PaProperty.Value}.");
        }

        static void TryPatch(Harmony harmony, Type patches, string feature)
        {
            try { harmony.PatchAll(patches); }
            catch (Exception e) { L.LogError($"Could not hook FM27 for {feature}; that feature is off. {e}"); }
        }
    }

    internal sealed class Settings
    {
        public readonly ConfigEntry<bool> Visible, ShowCA, ShowPA, ExactSort, Debug;
        public readonly ConfigEntry<string> ToggleKey, CaProperty, PaProperty, LabelClasses, HeaderClasses, Placement;
        public readonly ConfigEntry<int> CaOffset, PaOffset;

        public Settings(ConfigFile c)
        {
            Visible = c.Bind("General", "Visible", true, "Show the CA/PA numbers. The toggle key flips this and it is remembered.");
            ToggleKey = c.Bind("General", "ToggleKey", "F9", "Key that shows/hides the numbers (Unity InputSystem key name, e.g. F9, F10, Backquote). Empty = no key.");
            ShowCA = c.Bind("General", "ShowCurrentAbility", true, "Show the number next to Current Ability stars.");
            ShowPA = c.Bind("General", "ShowPotentialAbility", true, "Show the number next to Potential Ability stars.");
            ExactSort = c.Bind("General", "ExactColumnSort", true, "Clicking a CA/PA star column header sorts by the exact number instead of half-star buckets.");
            CaProperty = c.Bind("Data", "CurrentAbilityProperty", "PlayerCurrentAbility", "FM27 binding property the plugin answers with the real Current Ability (used for exact column sorting).");
            PaProperty = c.Bind("Data", "PotentialAbilityProperty", "PlayerPotentialAbility", "FM27 binding property the plugin answers with the real Potential Ability (used for exact column sorting).");
            CaOffset = c.Bind("Data", "CurrentAbilityOffset", -0x24, "Offset of the int16 Current Ability from FM's native person object (-0x24 = -36). Only change for a new FM27 build.");
            PaOffset = c.Bind("Data", "PotentialAbilityOffset", -0x22, "Offset of the int16 Potential Ability from FM's native person object (-0x22 = -34). Only change for a new FM27 build.");
            LabelClasses = c.Bind("Style", "LabelClasses", "body-xs-strong global-text-primary", "FM27 USS classes given to the number label (font, size, colour).");
            HeaderClasses = c.Bind("Style", "HeaderClasses", "body-md-strong white", "FM27 USS classes for the dedicated \"CA x · PA y\" in player headers and popups.");
            Placement = c.Bind("Style", "Placement", "Flow", new ConfigDescription(
                "Flow = placed after the stars in FM's own layout (beside them in rows, under them in stacks). Right / Below = overlaid.",
                new AcceptableValueList<string>("Flow", "Right", "Below")));
            Debug = c.Bind("Debug", "Enabled", false, "Verbose log, plus the command file BepInEx/config/FM27AbilityNumbers.cmd " +
                                                      "(lines: dump | props [filter] | probe <Property> | shot | reload | enable | throw [n]).");
        }
    }

    public class Driver : MonoBehaviour
    {
        public Driver(IntPtr ptr) : base(ptr) { }

        float _nextSlowTick;
        bool _scannedTemplates;
        string _keyName;
        Key _key = Key.None;
        bool _legacyInputBroken;

        void Update()
        {
            Labels.DriverAlive = true;
            Labels.Guard("update", Tick);
        }

        void LateUpdate() => Labels.Guard("late-update", Labels.ProcessPending);

        void Tick()
        {
            float now = Time.realtimeSinceStartup;
            bool slow = now >= _nextSlowTick;
            if (slow)
            {
                _nextSlowTick = now + 1f;
                if (Plugin.S.Debug.Value) Diagnostics.PollCommands(); // also works while disabled ("enable")
            }
            if (Labels.Disabled) { Labels.CleanupIfDisabled(); return; }

            if (ToggleKeyPressed())
            {
                Plugin.S.Visible.Value = !Plugin.S.Visible.Value;
                Labels.ApplyAll();
                Plugin.L.LogInfo($"Numbers {(Plugin.S.Visible.Value ? "shown" : "hidden")}.");
            }
            Labels.ProcessPending();
            Channels.Tick(now);
            if (!slow) return;

            if (!_scannedTemplates && Labels.SeenBind)
            {
                _scannedTemplates = true; // templates loaded before the hook existed
                Sort.ScanLoaded();
            }
            Labels.SlowTick(now);
        }

        bool ToggleKeyPressed()
        {
            var name = Plugin.S.ToggleKey.Value?.Trim();
            if (name != _keyName)
            {
                _keyName = name;
                _key = Enum.TryParse<Key>(name, true, out var k) ? k : Key.None;
                if (!string.IsNullOrEmpty(name) && _key == Key.None) Plugin.L.LogWarning($"Unknown ToggleKey '{name}'.");
            }
            if (_key == Key.None) return false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                try { return kb[_key].wasPressedThisFrame; }
                catch (Exception e) { Plugin.L.LogWarning($"ToggleKey '{_keyName}' not usable, hotkey off: {e.Message}"); _key = Key.None; return false; }
            }

            if (_legacyInputBroken) return false;
            try { return Enum.TryParse<KeyCode>(_keyName, true, out var code) && Input.GetKeyDown(code); }
            catch (Exception) { _legacyInputBroken = true; return false; } // legacy input disabled in this build
        }
    }
}
