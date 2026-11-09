using System;
using System.Collections.Generic;
using System.Text;
using FM.UI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using SI.Bindable;
using SI.Bindable.Reference.Core;
using SI.Core;
using SI.Interop;
using SI.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FM27AbilityNumbers
{
    // All hooks only queue work: they fire while FM walks its element/binding trees, and changing those trees
    // there could disturb the walk. The queue is drained in Driver.Update/LateUpdate.

    // Star widgets used directly by a few templates (most stars sit in a star cell, handled below).
    [HarmonyPatch(typeof(FMStarRatingTemp))]
    static class StarPatches
    {
        [HarmonyPostfix, HarmonyPatch(nameof(FMStarRatingTemp.BindToData))]
        static void BindToData(FMStarRatingTemp __instance) => Labels.Mark(__instance, true, true, false, true);

        [HarmonyPostfix, HarmonyPatch(nameof(FMStarRatingTemp.UnbindFromData))]
        static void UnbindFromData(FMStarRatingTemp __instance) => Labels.Mark(__instance, true, false, false, false);

        // Stars changed: the ability may have changed too, so read it again.
        [HarmonyPostfix, HarmonyPatch(nameof(FMStarRatingTemp.OnMinBindingValueChanged))]
        static void OnMin(FMStarRatingTemp __instance) => Labels.Mark(__instance, true, true, true, false);

        [HarmonyPostfix, HarmonyPatch(nameof(FMStarRatingTemp.OnMaxBindingValueChanged))]
        static void OnMax(FMStarRatingTemp __instance) => Labels.Mark(__instance, true, true, true, false);
    }

    // Template roots. Two kinds matter:
    //  - star cells ("starrating" parameter): show stars or "Unknown"; FM only creates the star widget when it
    //    shows stars, so the number is anchored to the cell, not the stars;
    //  - the database-id display ("objectID" parameter) in player banners (profile header, hover popup): gets
    //    the dedicated "CA x · PA y" next to it, for every player, scouted or not.
    [HarmonyPatch(typeof(BindingExpect))]
    static class ExpectPatches
    {
        [HarmonyPostfix, HarmonyPatch(nameof(BindingExpect.BindToData))]
        static void BindToData(BindingExpect __instance) => Labels.Mark(__instance, false, true, false, true);

        [HarmonyPostfix, HarmonyPatch(nameof(BindingExpect.UnbindFromData))]
        static void UnbindFromData(BindingExpect __instance) => Labels.Mark(__instance, false, false, false, false);
    }

    // Virtualised lists (squad view, shortlist, search...) reuse a row for another player without rebinding it.
    [HarmonyPatch(typeof(StreamedListView), nameof(StreamedListView.OnRebindItemInternal))]
    static class RebindPatch
    {
        [HarmonyPostfix]
        static void Postfix(VisualElement element) => Labels.MarkRow(element);
    }

    internal static class Labels
    {
        const uint PersonUID = 1145176065u;   // FM.UI.PersonReference.UID
        const uint RecordField = 0x64726566u; // "dref": DB table in the top byte, record id below
        const float ReaskEvery = 5f;          // CA can move inside the same half-star bucket; FM sends no signal then
        const string StarCellPath = "starrating.minvalue", HeaderPath = "objectID";

        enum Mode { Stars, Header }

        sealed class Tracker
        {
            public VisualElement Source;      // element whose binding scope tells us the player (star, cell root, id root)
            public string Path;               // path bound from that scope
            public Mode Mode;
            public VisualElement Anchor;      // the label goes right after this element
            public AbilityKind Kind;
            public int Person;
            public bool HasScope;
            public Bindings.Key ScopeKey;
            public Label Label;
            public string Text;
            public bool Inline;               // star cell: lay the number out beside the stars (rows have a fixed height)
        }

        // Rebind: FM bound the element again, possibly to another row/column (sorted tables reuse cells), so our
        // scope binding must be made again too; otherwise it keeps following the old row.
        struct Pending { public VisualElement Element; public bool IsStar, Bound, Reask, Rebind; }

        static readonly Dictionary<IntPtr, Tracker> s_trackers = new();     // by Source pointer
        static readonly Dictionary<ulong, IntPtr> s_scopeOwners = new();    // scope key -> Source pointer
        static readonly Dictionary<IntPtr, Pending> s_pending = new();
        static readonly Dictionary<IntPtr, VisualElement> s_pendingRows = new();
        static readonly List<string> s_names = new();
        static Bindings.ValueChangedCallback s_scopeCallback;
        static int s_seenReady, s_errorsLogged, s_windowErrors, s_debugLines;
        static long s_windowStart;
        static float s_nextReask, s_nextPrune;
        static bool s_cleanupPending;

        internal static bool Disabled;
        internal static bool DriverAlive;   // set by Driver; until then nothing drains the queues
        internal static bool SeenBind;      // a bindable element bound => FM's Bindings subsystem exists

        static bool Active => DriverAlive && !Disabled;
        static Settings S => Plugin.S;
        internal static Bindings Bindings => SeenBind ? GameSubsystems.GetOptional<Bindings>() : null;

        // ---------- errors / fail-safe ----------

        internal static void Guard(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { ReportError(what, e); }
        }

        // 20 errors within a minute turns the plugin off (a broken hook fails on every call).
        internal static void ReportError(string what, Exception e)
        {
            if (s_errorsLogged++ < 10) Plugin.L.LogError($"[{what}] {e}");
            long now = Environment.TickCount64;
            if (now - s_windowStart > 60_000) { s_windowStart = now; s_windowErrors = 0; }
            if (++s_windowErrors >= 20 && !Disabled)
            {
                Disabled = true;          // cleanup happens in the next Update, outside FM's call stack
                s_cleanupPending = true;
                Plugin.L.LogError("FM27 Ability Numbers turned itself off (20 errors within a minute). The game is unaffected. " +
                                  "Send BepInEx/LogOutput.log to fix it; 'enable' in the debug command file turns it back on.");
            }
        }

        internal static void CleanupIfDisabled()
        {
            if (!s_cleanupPending) return;
            s_cleanupPending = false;
            foreach (var t in s_trackers.Values) { try { t.Label?.RemoveFromHierarchy(); UnbindScope(t); } catch { } }
            s_trackers.Clear();
            s_pending.Clear();
            s_pendingRows.Clear();
            Sort.ReapplyAll(); // back to FM's own sort paths while we are off
        }

        internal static void Enable()
        {
            Disabled = false;
            s_windowErrors = 0;
            s_errorsLogged = 0;
            Sort.ReapplyAll();
            Plugin.L.LogInfo("Re-enabled. Numbers return as screens rebind (open another screen).");
        }

        // ---------- hook side: queue only ----------

        internal static void Mark(VisualElement e, bool isStar, bool bound, bool reask, bool rebind)
        {
            if (!Active) return;
            try
            {
                SeenBind = true;
                var p = e.Pointer;
                if (s_pending.TryGetValue(p, out var old))
                {
                    rebind |= old.Rebind;
                    if (bound && old.Bound) reask |= old.Reask;
                }
                s_pending[p] = new Pending { Element = e, IsStar = isStar, Bound = bound, Reask = reask, Rebind = rebind }; // last event wins
            }
            catch (Exception ex) { ReportError("mark", ex); }
        }

        internal static void MarkRow(VisualElement row)
        {
            if (!Active || row == null || s_trackers.Count == 0) return;
            try { s_pendingRows[row.Pointer] = row; }
            catch (Exception e) { ReportError("mark-row", e); }
        }

        // ---------- main-thread processing ----------

        internal static void ProcessPending()
        {
            if (!Active) return;
            if (s_pendingRows.Count > 0)
            {
                var rows = new HashSet<IntPtr>(s_pendingRows.Keys);
                s_pendingRows.Clear();
                foreach (var t in s_trackers.Values)
                    if (HasAncestor(t.Anchor, rows)) Requeue(t, rebind: true); // the row now shows another item
            }
            if (s_pending.Count == 0) return;
            var work = new List<Pending>(s_pending.Values);
            s_pending.Clear();
            foreach (var w in work) Guard("refresh", () => Process(w));
        }

        static void Requeue(Tracker t, bool rebind = false)
        {
            var p = t.Source.Pointer;
            if (s_pending.TryGetValue(p, out var old)) { if (rebind && old.Bound) { old.Rebind = true; s_pending[p] = old; } return; }
            s_pending[p] = new Pending { Element = t.Source, IsStar = t.Source.TryCast<FMStarRatingTemp>() != null, Bound = true, Reask = true, Rebind = rebind };
        }

        static void Process(Pending w)
        {
            var e = w.Element;
            if (!w.Bound || e.WasCollected) { Release(e.Pointer); return; }
            if (s_trackers.TryGetValue(e.Pointer, out var known))
            {
                if (w.Rebind) { UnbindScope(known); known.Text = null; }
                Refresh(known, w.Reask || w.Rebind);
                return;
            }

            Tracker t;
            if (w.IsStar)
            {
                var star = e.TryCast<FMStarRatingTemp>();
                if (star == null || InStarCell(star)) return; // star cells are handled through their root
                var path = star.MinBinding;
                if (path == null || !path.IsValid()) path = star.MaxBinding;
                if (path == null || !path.IsValid()) return;
                t = new Tracker { Source = star, Path = path.Path, Mode = Mode.Stars, Anchor = star };
            }
            else
            {
                var expect = e.TryCast<BindingExpect>();
                if (expect == null) return;
                var param = InterestingParameter(expect);
                if (param == StarCellPath)
                {
                    // Cells nest (StarRangeCell > stats-star-ratings, both with "starrating"): only the root that holds
                    // the stars/"Unknown" switch gets the number, so it appears once.
                    var sw = SwitchChild(expect);
                    if (sw == null) return;
                    t = new Tracker { Source = expect, Path = StarCellPath, Mode = Mode.Stars, Anchor = sw, Inline = true };
                }
                else if (param == HeaderPath) t = new Tracker { Source = expect, Path = HeaderPath, Mode = Mode.Header, Anchor = expect.parent ?? expect };
                else return;
            }
            Refresh(t, true);
        }

        // "starrating.minvalue" for a star cell root, "objectID" for the database-id display root, else null.
        static string InterestingParameter(BindingExpect e)
        {
            var ps = e.Parameters;
            if (ps == null) return null;
            for (int i = 0, n = ps.Count; i < n && i < 4; i++)
            {
                var name = ps[i]?.name;
                if (name == "starrating") return StarCellPath;
                if (name == "objectID" && n == 1) return HeaderPath;
            }
            return null;
        }

        static bool InStarCell(FMStarRatingTemp star)
        {
            var p = star.parent;
            for (int i = 0; i < 3 && p != null; i++, p = p.parent)
            {
                var e = p.TryCast<BindingExpect>();
                if (e != null) return InterestingParameter(e) == StarCellPath;
            }
            return false;
        }

        // The switch (stars or "Unknown") inside a star cell; the number goes right after it.
        static VisualElement SwitchChild(BindingExpect e)
        {
            for (int i = 0, n = e.childCount; i < n; i++)
            {
                var c = e[i];
                if (c?.TryCast<SwitchElement>() != null) return c;
            }
            return null;
        }

        static bool HasAncestor(VisualElement e, HashSet<IntPtr> set)
        {
            var p = e?.parent;
            for (int i = 0; i < 32 && p != null; i++, p = p.parent)
                if (set.Contains(p.Pointer)) return true;
            return false;
        }

        static void Refresh(Tracker t, bool reask)
        {
            var b = Bindings;
            if (b == null) return;
            bool isNew = !s_trackers.ContainsKey(t.Source.Pointer);
            if (!t.HasScope) BindScope(t);
            bool resolved = Resolve(b, t, out var kind, out var person);
            if (!resolved && t.HasScope) // the scope key went stale (template rebound elsewhere): bind it again once
            {
                UnbindScope(t);
                BindScope(t);
                resolved = Resolve(b, t, out kind, out person);
            }
            if (!resolved)
            {
                if (isNew) UnbindScope(t); else Release(t.Source.Pointer);
                return;
            }
            if (isNew) s_trackers[t.Source.Pointer] = t;

            bool same = t.Kind == kind && t.Person == person && t.Text != null;
            t.Kind = kind;
            t.Person = person;
            if (!same || reask)
            {
                bool ok = Memory.TryRead(person, out var ca, out var pa);
                t.Text = !ok ? null
                    : t.Mode == Mode.Header ? Logic.FormatPair(ca, pa, S.ShowCA.Value, S.ShowPA.Value)
                    : Logic.FormatAbility(kind == AbilityKind.Current ? ca : pa);
            }
            EnsureLabel(t);
            Apply(t);

            if (S.Debug.Value && !same && s_debugLines++ < 300)
                Plugin.L.LogInfo($"track {t.Mode} {kind} person={person} text='{t.Text}' path={SafePath(b, t.ScopeKey)}");
        }

        static bool Resolve(Bindings b, Tracker t, out AbilityKind kind, out int person)
        {
            kind = AbilityKind.None;
            person = 0;
            if (!t.HasScope) return false;
            return Resolve(b, t.ScopeKey, t.Mode == Mode.Stars, out kind, out person);
        }

        // Our own binding of the tracker's path, made from its source element's binding scope.
        static void BindScope(Tracker t)
        {
            s_scopeCallback ??= DelegateSupport.ConvertDelegate<Bindings.ValueChangedCallback>(
                new Action<Bindings.Key, TypedValue>(OnScopeChanged));
            var path = new BindingPath(t.Path);
            var key = BindingUtils.BindWithinScopeIfValid(t.Source, ref path, t.Source, s_scopeCallback);
            if (!key.IsValid()) return;
            t.ScopeKey = key;
            t.HasScope = true;
            s_scopeOwners[key.m_key] = t.Source.Pointer;
        }

        static void UnbindScope(Tracker t)
        {
            if (!t.HasScope) return;
            t.HasScope = false;
            var key = t.ScopeKey;
            s_scopeOwners.Remove(key.m_key);
            BindingUtils.UnbindIfValid(ref key, s_scopeCallback);
        }

        // Runs inside Bindings.Update: queue only.
        static void OnScopeChanged(Bindings.Key key, TypedValue value)
        {
            if (!Active) return;
            try { if (s_scopeOwners.TryGetValue(key.m_key, out var p) && s_trackers.TryGetValue(p, out var t)) Requeue(t); }
            catch (Exception e) { ReportError("scope", e); }
        }

        // Walks from a value node up to the person it describes, following remap aliases. For stars, the node
        // names passed on the way say whether they are CA or PA.
        static bool Resolve(Bindings b, Bindings.Key key, bool needKind, out AbilityKind kind, out int person)
        {
            kind = AbilityKind.None;
            person = 0;
            s_names.Clear();
            // TryGetNode: a stale key (rows rebuilt after a sort, panel closed) must not throw.
            if (!b.TryGetNode(ref key, out var node)) return false;
            for (int i = 0; i < 64 && node != null; i++)
            {
                var r = AsPerson(node.Value);
                if (r != null)
                {
                    if (needKind)
                    {
                        kind = Logic.Classify(s_names);
                        if (kind == AbilityKind.None) return false;
                    }
                    if (!Props.Loaded) Props.Load(r);
                    person = PersonId(r);
                    return person != 0;
                }
                s_names.Add(node.Name);
                var alias = node.Alias;
                if (!alias.IsValid()) node = node.Parent;
                else if (!b.TryGetNode(ref alias, out node)) return false;
            }
            return false;
        }

        static InteropReference AsPerson(TypedValue value)
        {
            if (value == null || value.IsNull) return null;
            var r = value.Get()?.TryCast<InteropReference>();
            return r != null && r.ID.ID == PersonUID ? r : null;
        }

        // The DB record id (low 24 bits of "dref"), the same id the native player object carries at +8.
        static int PersonId(InteropReference r) => r.TryGetProperty(RecordField, out var id) ? id & 0xFFFFFF : 0;

        internal static bool TryPersonId(TypedValue value, out int id)
        {
            id = 0;
            var r = AsPerson(value);
            if (r == null) return false;
            id = PersonId(r);
            return id != 0;
        }

        internal static void Release(IntPtr sourcePtr)
        {
            if (!s_trackers.Remove(sourcePtr, out var t)) return;
            t.Label?.RemoveFromHierarchy();
            UnbindScope(t);
        }

        // ---------- label ----------

        static void EnsureLabel(Tracker t)
        {
            string placement = t.Mode == Mode.Header ? "Flow" : S.Placement.Value;
            bool flow = placement == "Flow";
            VisualElement anchor = t.Anchor;
            // Switches show children by index: never insert into one.
            if (flow)
                while (anchor.parent != null && anchor.parent.TryCast<SwitchElement>() != null) anchor = anchor.parent;
            var host = flow ? anchor.parent : anchor;
            if (host == null) return;
            if (flow && host.TryCast<IOwnsChildren>() != null) return; // FM manages that container's children itself

            t.Label ??= CreateLabel(t.Mode);
            var l = t.Label;
            if (l.parent != null && l.parent.Pointer == host.Pointer && (!flow || host.IndexOf(l) == host.IndexOf(anchor) + 1)) return;

            l.RemoveFromHierarchy();
            if (flow) host.Insert(host.IndexOf(anchor) + 1, l); else host.Add(l);
            Place(l, placement, t.Mode);
            if (flow && t.Inline)
            {
                // The cell root only holds the stars/"Unknown" switch and our label: a row keeps the number
                // beside the stars instead of below them, where fixed-height table rows would cut it off.
                host.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
                host.style.alignItems = new StyleEnum<Align>(Align.Center);
            }
        }

        static Label CreateLabel(Mode mode)
        {
            var l = new Label { name = mode == Mode.Header ? "fm27-ability-header" : "fm27-ability-number" };
            l.pickingMode = PickingMode.Ignore;
            foreach (var c in Logic.SplitClasses(mode == Mode.Header ? S.HeaderClasses.Value : S.LabelClasses.Value)) l.AddToClassList(c);
            l.style.flexShrink = new StyleFloat(0f);
            l.style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.MiddleLeft);
            l.style.display = new StyleEnum<DisplayStyle>(DisplayStyle.None);
            return l;
        }

        static void Place(Label l, string placement, Mode mode)
        {
            var s = l.style;
            if (placement == "Flow")
            {
                s.position = new StyleEnum<Position>(Position.Relative);
                s.marginLeft = new StyleLength(mode == Mode.Header ? 8f : 4f);
                s.marginTop = new StyleLength(0f);
                return;
            }
            s.position = new StyleEnum<Position>(Position.Absolute);
            if (placement == "Right")
            {
                s.left = new StyleLength(Length.Percent(100f));
                s.top = new StyleLength(0f);
                s.marginLeft = new StyleLength(4f);
            }
            else
            {
                s.left = new StyleLength(0f);
                s.top = new StyleLength(Length.Percent(100f));
                s.marginLeft = new StyleLength(0f);
            }
        }

        static void Apply(Tracker t)
        {
            if (t.Label == null) return;
            bool show = !Disabled && S.Visible.Value && t.Text != null &&
                        (t.Mode == Mode.Header || (t.Kind == AbilityKind.Current ? S.ShowCA.Value : S.ShowPA.Value));
            if (show) t.Label.text = t.Text;
            t.Label.style.display = new StyleEnum<DisplayStyle>(show ? DisplayStyle.Flex : DisplayStyle.None);
        }

        internal static void ApplyAll()
        {
            foreach (var t in s_trackers.Values) Apply(t);
        }

        // After a config reload: classes, placement and the CA/PA choice take effect on every visible number.
        internal static void ReapplyAll()
        {
            foreach (var t in s_trackers.Values)
            {
                t.Label?.RemoveFromHierarchy();
                t.Label = null;
                t.Text = null;
                Requeue(t);
            }
        }

        // ---------- periodic ----------

        internal static void SlowTick(float now)
        {
            Probe.Expire(now);
            if (now >= s_nextReask || s_seenReady != Memory.Ready) // periodic, and as soon as an index build finishes
            {
                s_seenReady = Memory.Ready;
                s_nextReask = now + ReaskEvery;
                foreach (var t in s_trackers.Values)
                    if (t.Label?.panel != null || t.Text == null) Requeue(t);
            }
            if (now >= s_nextPrune)
            {
                s_nextPrune = now + 10f;
                var dead = new List<IntPtr>();
                foreach (var (ptr, t) in s_trackers)
                    if (t.Source.WasCollected || t.Anchor.WasCollected) dead.Add(ptr);
                foreach (var ptr in dead) Release(ptr);
            }
        }

        // ---------- diagnostics ----------

        internal static void Dump()
        {
            var b = Bindings;
            var sb = new StringBuilder($"dump: {s_trackers.Count} tracked, {Channels.Count} answered channels, memory: {Memory.Status}, " +
                                       $"visible={S.Visible.Value}, disabled={Disabled}, caOk={Props.CaOk}, paOk={Props.PaOk}\n");
            foreach (var t in s_trackers.Values)
                sb.Append($"  {t.Mode} {t.Kind} person={t.Person} text='{t.Text}' scope={t.HasScope} " +
                          $"attached={t.Label?.panel != null} path={SafePath(b, t.ScopeKey)}\n");
            Plugin.L.LogInfo(sb.ToString());
        }

        // For `probe`: the players currently tracked, so other properties can be compared.
        internal static IEnumerable<(int person, Bindings.Key key, TypedValue value, VisualElement owner)> LivePersons()
        {
            var b = Bindings;
            if (b == null) yield break;
            var seen = new HashSet<int>();
            foreach (var t in new List<Tracker>(s_trackers.Values))
            {
                if (!t.HasScope) continue;
                var key = t.ScopeKey;
                if (!b.TryGetNode(ref key, out var node)) continue;
                for (int i = 0; i < 64 && node != null; i++)
                {
                    var r = AsPerson(node.Value);
                    if (r != null) { if (seen.Add(PersonId(r))) yield return (PersonId(r), node.Key, node.Value, t.Source); break; }
                    var alias = node.Alias;
                    if (!alias.IsValid()) node = node.Parent;
                    else if (!b.TryGetNode(ref alias, out node)) break;
                }
            }
        }

        internal static string TextFor(int person)
        {
            var parts = new List<string>();
            foreach (var t in s_trackers.Values) if (t.Person == person && t.Text != null) parts.Add(t.Text);
            return string.Join(" / ", parts);
        }

        static string SafePath(Bindings b, Bindings.Key key)
        {
            try { return b?.GetPathDebug(ref key); } catch { return "?"; }
        }
    }

    // The CA/PA property names are checked against what FM actually exposes on a person before the sort uses them.
    internal static class Props
    {
        static HashSet<string> s_names;
        internal static volatile bool CaOk, PaOk;
        internal static bool Loaded => s_names != null;

        internal static void Load(InteropReference person)
        {
            var ids = new Il2CppSystem.Collections.Generic.List<PropertyID>();
            person.GetProperties(ids);
            var set = IdentifierSet<PropertyIdentifierSet, PropertyID, PropertyIDInfo>.Instance;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i++)
            {
                var name = set.GetInfo(ids[i].id)?.Name;
                if (name != null) names.Add(name);
            }
            s_names = names;
            Plugin.L.LogInfo($"Person exposes {names.Count} properties.");
            Validate();
        }

        internal static void Validate()
        {
            if (s_names == null) return;
            var s = Plugin.S;
            CaOk = s_names.Contains(s.CaProperty.Value?.Trim() ?? "");
            PaOk = s_names.Contains(s.PaProperty.Value?.Trim() ?? "");
            if (CaOk && PaOk)
                Plugin.L.LogInfo($"Exact sort uses {s.CaProperty.Value} / {s.PaProperty.Value}.");
            else
                Plugin.L.LogWarning($"Property not found on Person: {(CaOk ? "" : s.CaProperty.Value + " ")}{(PaOk ? "" : s.PaProperty.Value)}. " +
                                    "The exact sort for it stays off.");
            Sort.ReapplyAll();
        }

        internal static IEnumerable<string> Names(string filter)
        {
            if (s_names == null) yield break;
            foreach (var n in s_names)
                if (filter == null || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) yield return n;
        }
    }
}
