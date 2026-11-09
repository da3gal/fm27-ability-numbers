// Pure byte-array helpers for finding FM's native classes, with no game dependencies (unit tested).
using System;
using System.Collections.Generic;
using System.Text;

namespace FM27AbilityNumbers
{
    public sealed record Section(long Va, byte[] Data);

    public static class MemoryScan
    {
        public static IEnumerable<(Section s, int at)> FindAll(IReadOnlyList<Section> sections, byte[] needle)
        {
            foreach (var s in sections)
            {
                int from = 0;
                while (from < s.Data.Length)
                {
                    int i = s.Data.AsSpan(from).IndexOf(needle);
                    if (i < 0) break;
                    yield return (s, from + i);
                    from += i + 1;
                }
            }
        }

        // Class table (vtable) of MSVC class db::NAME for the sub-object at offset `at`, found through RTTI:
        // name -> type descriptor (name - 16) -> complete object locator (signature 1, offset `at`,
        // type descriptor RVA at +12, own RVA at +20) -> the vtable slot right after a pointer to the locator.
        // 0 unless exactly one table matches.
        public static long FindVtable(IReadOnlyList<Section> sections, long moduleBase, string name, int at)
        {
            var found = new HashSet<long>();
            var needle = Encoding.ASCII.GetBytes($".?AV{name}@db@@\0");
            foreach (var (ts, ti) in new List<(Section, int)>(FindAll(sections, needle)))
            {
                uint td = (uint)(ts.Va + ti - 16 - moduleBase);
                foreach (var (cs, ci) in new List<(Section, int)>(FindAll(sections, BitConverter.GetBytes(td))))
                {
                    int c = ci - 12;
                    if (c < 0 || c + 24 > cs.Data.Length) continue;
                    long col = cs.Va + c;
                    if (BitConverter.ToInt32(cs.Data, c) != 1 || BitConverter.ToInt32(cs.Data, c + 4) != at
                        || BitConverter.ToUInt32(cs.Data, c + 20) != (uint)(col - moduleBase)) continue;
                    foreach (var (vs, vi) in FindAll(sections, BitConverter.GetBytes(col))) found.Add(vs.Va + vi + 8);
                }
            }
            if (found.Count != 1) return 0;
            foreach (var v in found) return v;
            return 0;
        }

        // Offsets (8-byte aligned, relative to `buffer`) where the qword equals `value`.
        public static void FindQwords(ReadOnlySpan<byte> buffer, long value, List<int> hits)
        {
            int n = buffer.Length / 8;
            var longs = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, long>(buffer.Slice(0, n * 8));
            int from = 0;
            while (from < longs.Length)
            {
                int i = longs.Slice(from).IndexOf(value);
                if (i < 0) break;
                hits.Add((from + i) * 8);
                from += i + 1;
            }
        }
    }
}
