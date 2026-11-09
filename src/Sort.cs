using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using SI.Bindable;
using UnityEngine;

namespace FM27AbilityNumbers
{
    // Table columns (squad view, shortlist, player search...) come from TableTemplate assets. Their CA/PA star
    // columns sort by "<row>.CurrentAbilityStarRange.MaxValue"; we point that at the exact property instead.
    [HarmonyPatch(typeof(TableTemplate), nameof(TableTemplate.OnAfterDeserialize))]
    static class SortPatches
    {
        [HarmonyPostfix]
        static void Postfix(TableTemplate __instance) => Sort.Patch(__instance);
    }

    internal static class Sort
    {
        static readonly object s_lock = new();
        // row pointer -> (row, the sort path FM shipped with), so reloads/toggles can re-derive or restore it
        static readonly Dictionary<IntPtr, (TableTemplate.RowData row, string original)> s_rows = new();
        static int s_errors;

        // May run on Unity's loading thread (OnAfterDeserialize): only touches managed fields, no Unity API.
        internal static void Patch(TableTemplate template)
        {
            try
            {
                var rows = template?.m_rowData;
                if (rows == null) return;
                lock (s_lock)
                {
                    for (int i = 0; i < rows.Length; i++)
                    {
                        var row = rows[i];
                        if (row == null) continue;
                        if (!s_rows.TryGetValue(row.Pointer, out var entry))
                        {
                            var original = row.m_sortBinding?.Path;
                            if (!Logic.TryRewriteSortPath(original, "x", "x", out _)) continue;
                            s_rows[row.Pointer] = entry = (row, original);
                        }
                        Apply(entry.row, entry.original);
                    }
                }
            }
            catch (Exception e)
            {
                if (s_errors++ < 5) Plugin.L.LogError($"[sort] {e}");
            }
        }

        static void Apply(TableTemplate.RowData row, string original)
        {
            var s = Plugin.S;
            var path = original;
            // Only names FM was seen to expose on a person (Props); until then FM's own sort path stays.
            var ca = Props.CaOk ? s.CaProperty.Value : "";
            var pa = Props.PaOk ? s.PaProperty.Value : "";
            if (s.ExactSort.Value && !Labels.Disabled && Logic.TryRewriteSortPath(original, ca, pa, out var exact))
                path = exact;
            if (row.m_sortBinding?.Path != path) SetSortPath(row, path);
        }

        static int s_sortFieldOffset = -1, s_pathInStructOffset = -1;

        // The interop setter for a struct field copies raw bytes without a GC write barrier, which is unsafe for a
        // struct holding a string under Unity's incremental GC. BindingPath is { string m_path }, so write that
        // string slot inside RowData.m_sortBinding directly, with the barrier.
        static unsafe void SetSortPath(TableTemplate.RowData row, string path)
        {
            if (s_sortFieldOffset < 0)
            {
                var rowField = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<TableTemplate.RowData>.NativeClassPtr, "m_sortBinding");
                var pathField = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<BindingPath>.NativeClassPtr, "m_path");
                if (rowField == IntPtr.Zero || pathField == IntPtr.Zero) throw new MissingFieldException("RowData.m_sortBinding / BindingPath.m_path");
                s_pathInStructOffset = (int)IL2CPP.il2cpp_field_get_offset(pathField) - 2 * IntPtr.Size; // value-type offsets include the object header
                s_sortFieldOffset = (int)IL2CPP.il2cpp_field_get_offset(rowField);
            }
            var obj = row.Pointer;
            IL2CPP.il2cpp_gc_wbarrier_set_field(obj, obj + s_sortFieldOffset + s_pathInStructOffset, IL2CPP.ManagedStringToIl2Cpp(path));
        }

        internal static void ScanLoaded()
        {
            var found = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TableTemplate>());
            foreach (var o in found) Patch(o.TryCast<TableTemplate>());
            Plugin.L.LogInfo($"Exact CA/PA sort: {(Plugin.S.ExactSort.Value ? "on" : "off")}, {Count} columns patched so far.");
        }

        internal static void ReapplyAll()
        {
            lock (s_lock)
                foreach (var (row, original) in s_rows.Values)
                    if (!row.WasCollected) Apply(row, original);
        }

        internal static int Count { get { lock (s_lock) return s_rows.Count; } }

        internal static string Describe()
        {
            lock (s_lock)
            {
                var list = new List<string>();
                foreach (var (row, original) in s_rows.Values)
                    if (!row.WasCollected) list.Add($"{row.m_id}: {original} -> {row.m_sortBinding?.Path}");
                return string.Join("\n  ", list);
            }
        }
    }
}
