using System;
using System.Collections.Generic;
using FM.UI;
using HarmonyLib;
using SI.Bindable;
using SI.Core;
using SI.Interop;

namespace FM27AbilityNumbers
{
    // FM's native side only answers PlayerCurrentAbility / PlayerPotentialAbility for the In-Game Editor, so a
    // binding to them (like our exact-sort column path) stays null. For people we answer those channels ourselves
    // from Memory: the native channel is never opened (and so never closed), and the value is set through the
    // handler's own BindingTree, the way FM's own lookup handlers answer.
    [HarmonyPatch(typeof(InteropDataHandler))]
    static class ChannelPatches
    {
        [HarmonyPrefix, HarmonyPatch(nameof(InteropDataHandler.OpenChannel))]
        static bool OpenChannel(InteropDataHandler __instance, TypedValue source, Bindings.Property property, Bindings.Key key) =>
            Channels.Open(__instance, source, property, key);

        [HarmonyPrefix, HarmonyPatch(nameof(InteropDataHandler.UpdateChannel))]
        static bool UpdateChannel(Bindings.Key key, TypedValue value) => Channels.Update(key, value);

        [HarmonyPrefix, HarmonyPatch(nameof(InteropDataHandler.CloseChannel))]
        static bool CloseChannel(Bindings.Key key) => Channels.Close(key);
    }

    internal static class Channels
    {
        sealed class Channel
        {
            public Bindings.Key Key;
            public int Person;
            public AbilityKind Kind;
            public int? Last; // null = not answered yet
        }

        static readonly Dictionary<ulong, Channel> s_open = new();
        static readonly HashSet<ulong> s_dirty = new();
        static InteropDataHandler s_handler;
        static float s_nextRefresh;
        static int s_seenReady, s_debugLines;

        internal static int Count => s_open.Count;
        internal static int Waiting { get { int n = 0; foreach (var c in s_open.Values) if (c.Last == null) n++; return n; } }

        // Hook side: record only; values are set later from the Driver, outside FM's channel processing.
        internal static bool Open(InteropDataHandler handler, TypedValue source, Bindings.Property property, Bindings.Key key)
        {
            try
            {
                if (Labels.Disabled) return true;
                var kind = KindOf(property?.Name);
                if (kind == AbilityKind.None || !Labels.TryPersonId(source, out var person)) return true;
                s_handler = handler;
                s_open[key.m_key] = new Channel { Key = key, Person = person, Kind = kind };
                if (Plugin.S.Debug.Value && s_debugLines++ < 40) Plugin.L.LogInfo($"answer channel {property.Name} person={person}");
                s_dirty.Add(key.m_key);
                return false;
            }
            catch (Exception e) { Labels.ReportError("open-channel", e); return true; }
        }

        internal static bool Update(Bindings.Key key, TypedValue value)
        {
            try
            {
                if (!s_open.TryGetValue(key.m_key, out var ch)) return true;
                if (Labels.TryPersonId(value, out var person)) ch.Person = person; // list row reused for another player
                ch.Last = null;
                s_dirty.Add(key.m_key);
                return false;
            }
            catch (Exception e) { Labels.ReportError("update-channel", e); return true; }
        }

        // Always ours to close, even after a fail-safe stop: the native side never saw this channel.
        internal static bool Close(Bindings.Key key)
        {
            try
            {
                if (!s_open.Remove(key.m_key)) return true;
                s_dirty.Remove(key.m_key);
                return false;
            }
            catch (Exception e) { Labels.ReportError("close-channel", e); return true; }
        }

        static AbilityKind KindOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return AbilityKind.None;
            var s = Plugin.S;
            // FM only opens channels for properties it has, so the name itself is the check.
            if (name.Equals(s.CaProperty.Value?.Trim(), StringComparison.OrdinalIgnoreCase)) return AbilityKind.Current;
            if (name.Equals(s.PaProperty.Value?.Trim(), StringComparison.OrdinalIgnoreCase)) return AbilityKind.Potential;
            return AbilityKind.None;
        }

        // Driver, main thread: answer new channels at once, all of them every 2 s (CA changes) and after an index build.
        internal static void Tick(float now)
        {
            if (s_open.Count == 0 || s_handler == null) return;
            bool all = now >= s_nextRefresh || s_seenReady != Memory.Ready;
            if (!all && s_dirty.Count == 0) return;
            if (all) { s_nextRefresh = now + 2f; s_seenReady = Memory.Ready; }
            var tree = s_handler.BindingTree;
            foreach (var ch in s_open.Values)
            {
                if (!all && !s_dirty.Contains(ch.Key.m_key)) continue;
                bool read = Memory.TryRead(ch.Person, out var ca, out var pa);
                var v = Logic.ChannelAnswer(read, ch.Kind == AbilityKind.Current ? ca : pa, Memory.Ready > 0, ch.Last);
                if (v == null) continue;
                ch.Last = v;
                var key = ch.Key;
                tree.Set(ref key, TypedValue.Create(v.Value));
            }
            s_dirty.Clear();
        }
    }
}
