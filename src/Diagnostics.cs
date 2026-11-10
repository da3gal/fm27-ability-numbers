using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using FM.UI;
using Il2CppInterop.Runtime;
using SI.Bindable;
using SI.Bindable.Reference.Core;
using SI.Core;
using UnityEngine;

namespace FM27AbilityNumbers
{
    // Debug-only command channel: write lines into BepInEx/config/FM27AbilityNumbers.cmd while the game runs.
    // Lets the plugin be inspected and tuned without restarting FM.
    internal static class Diagnostics
    {
        static string CmdPath => Path.Combine(Paths.ConfigPath, "FM27AbilityNumbers.cmd");

        internal static void PollCommands()
        {
            if (!File.Exists(CmdPath)) return;
            string[] lines;
            try { lines = File.ReadAllLines(CmdPath); File.Delete(CmdPath); }
            catch (IOException) { return; } // still being written; next tick
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Plugin.L.LogInfo($"cmd> {line}");
                try { Run(line); }
                catch (Exception e) { Plugin.L.LogError($"cmd '{line}' failed: {e}"); }
            }
        }

        static void Run(string line)
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var arg = parts.Length > 1 ? parts[1].Trim() : null;
            switch (parts[0].ToLowerInvariant())
            {
                case "dump":
                    Labels.Dump();
                    Plugin.L.LogInfo($"sort columns ({Sort.Count}):\n  {Sort.Describe()}");
                    break;
                case "props":
                    LogPersonProperties(arg);
                    break;
                case "probe":
                    Probe.Start(arg);
                    break;
                case "shot":
                    var dir = Path.Combine(Paths.BepInExRootPath, "shots");
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, $"shot-{System.DateTime.Now:yyyyMMdd-HHmmss}.png");
                    ScreenCapture.CaptureScreenshot(file);
                    Plugin.L.LogInfo($"screenshot -> {file}");
                    break;
                case "reload":
                    Plugin.Cfg.Reload();
                    Props.Validate();
                    Sort.ReapplyAll();
                    Labels.ReapplyAll();
                    Plugin.L.LogInfo($"reloaded: CA={Plugin.S.CaProperty.Value} PA={Plugin.S.PaProperty.Value} " +
                                     $"placement={Plugin.S.Placement.Value} classes='{Plugin.S.LabelClasses.Value}' sort={Plugin.S.ExactSort.Value}");
                    break;
                case "mem": // index status, or one player's native object: mem <record id>
                    Plugin.L.LogInfo(int.TryParse(arg, out var mid) ? Memory.Describe(mid) : $"memory: {Memory.Status}, {Memory.Count} players, {Channels.Count} sort channels, {Channels.Waiting} without a value yet");
                    break;
                case "find": // why a player is missing from the index: find <record id>
                    if (int.TryParse(arg, out var fid)) Memory.Find(fid); else Plugin.L.LogWarning("find needs a record id");
                    break;
                case "enable":
                    Labels.Enable();
                    break;
                case "throw": // fail-safe test: n forced errors through the same guard the hooks use
                    int n = int.TryParse(arg, out var c) ? c : 25;
                    for (int i = 0; i < n; i++) Labels.Guard("forced", () => throw new InvalidOperationException("debug: forced error"));
                    break;
                default:
                    Plugin.L.LogWarning($"unknown command '{parts[0]}'");
                    break;
            }
        }

        // Lists the binding properties FM27 exposes on a person, to find the raw CA/PA ones.
        static void LogPersonProperties(string filter)
        {
            if (Props.Loaded) // the full list, read from a live person
            {
                var live = new List<string>(Props.Names(filter));
                live.Sort(StringComparer.OrdinalIgnoreCase);
                Plugin.L.LogInfo($"person properties ({live.Count}, filter '{filter}'): {string.Join(", ", live)}");
                return;
            }
            var ids = new Il2CppSystem.Collections.Generic.List<PropertyID>();
            PersonReference.GetPropertiesInternal(ids);
            var set = IdentifierSet<PropertyIdentifierSet, PropertyID, PropertyIDInfo>.Instance;
            var names = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                var name = set.GetInfo(ids[i].id)?.Name;
                if (name != null && (filter == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            Plugin.L.LogInfo($"person properties ({names.Count}/{ids.Count}, filter '{filter}'): {string.Join(", ", names)}");
        }
    }

    // `probe <Property>` asks FM for that property on every person currently showing CA/PA stars and logs it next
    // to the number we display, so candidate properties can be compared against the Live Editor's values.
    internal static class Probe
    {
        static readonly List<(Bindings.Key key, Bindings.ValueChangedCallback cb)> s_live = new();
        static float s_expireAt;

        internal static void Start(string prop)
        {
            if (string.IsNullOrWhiteSpace(prop)) { Plugin.L.LogWarning("probe needs a property name"); return; }
            Stop();
            var b = Labels.Bindings;
            if (b == null) return;
            int asked = 0;
            foreach (var (person, key, value, owner) in Labels.LivePersons())
            {
                if (++asked > 25) break;
                var shown = Labels.TextFor(person);
                var cb = DelegateSupport.ConvertDelegate<Bindings.ValueChangedCallback>(new Action<Bindings.Key, TypedValue>((_, v) =>
                    Plugin.L.LogInfo($"probe person={person} {prop}={(v == null || v.IsNull ? "null" : v.AsString())} (shown '{shown}')")));
                s_live.Add((BindingUtils.Request(b, key, TypedValue.CreateCopyFrom(value), prop, owner, cb), cb));
            }
            s_expireAt = Time.realtimeSinceStartup + 5f;
            Plugin.L.LogInfo($"probe {prop}: asked for {s_live.Count} persons");
        }

        internal static void Expire(float now)
        {
            if (s_live.Count > 0 && now >= s_expireAt) Stop();
        }

        static void Stop()
        {
            var b = Labels.Bindings;
            foreach (var (key, cb) in s_live)
            {
                var k = key;
                try { b?.Unbind(ref k, cb); } catch { }
            }
            s_live.Clear();
        }
    }
}
