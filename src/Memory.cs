using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FM27AbilityNumbers
{
    // Reads players' real CA/PA from FM's own native player objects in game_plugin.dll, the same data FMRTE and
    // the Live Editor read. FM's binding properties for it (PlayerCurrentAbility...) only answer with the
    // In-Game Editor, so they come back null.
    //
    // Layout (from the public SiRRAHx/fm27-live-editor research, checked against the RTTI): a player object B starts
    // with an MSVC vtable of game_plugin.dll and holds two more at fixed offsets:
    //   B db::ACTUAL_PLAYER, B + 0x148 its db::PLAYING_DATA member, B + 0x288 its virtual base db::PERSON (P);
    //   P + 8 = record id (the "dref" id FM's UI references carry); P - 0x24 CA, P - 0x22 PA (int16).
    // Some players, mostly older ones, are a db::ACTUAL_PLAYER_AND_NON_PLAYER instead, which adds a staff part: the same
    // ACTUAL_PLAYER part at B, so CA/PA stay at B + 0x264/0x266, but its own vtables and P at B + 0x380.
    // Every read goes through ReadProcessMemory on our own process, so memory freed by the game in the meantime
    // makes a read fail instead of crashing, and every value is only used after all three vtables and the id match.
    internal static unsafe class Memory
    {
        // Per kind of player object: class, where P sits in it, and its vtables at B and at P.
        static readonly string[] s_class = { "ACTUAL_PLAYER", "ACTUAL_PLAYER_AND_NON_PLAYER" };
        static readonly int[] s_personAt = { 0x288, 0x380 };
        static readonly long[] s_playerVt = new long[2], s_personVt = new long[2];
        const int AttrAt = 0x148, IdAt = 8, CaBase = 0x288; // the config's CA/PA offsets are from P of an ACTUAL_PLAYER
        const int ChunkSize = 4 << 20;

        static readonly IntPtr s_proc = GetCurrentProcess();
        // Every buffer that holds a copy of a player block lives in this one native allocation, which the scan skips:
        // a copy found by the scan would otherwise be indexed as a player. [scan chunk][scan block][lookup block]
        static readonly byte* s_mem = (byte*)NativeMemory.Alloc((nuint)(ChunkSize + 2 * BlockLength));
        static long MemStart => (long)s_mem;
        static long MemEnd => (long)s_mem + ChunkSize + 2 * BlockLength;
        static bool s_noClasses;
        static long s_attrVt;
        static volatile Dictionary<int, (long p, int kind)> s_index;
        static volatile bool s_busy;
        static long s_lastBuild = -1_000_000_000;
        static int s_ready; // bumped when a build finishes; the Driver re-reads everything then
        internal static volatile string Status = "idle (builds on first use)";

        internal static int Ready => Volatile.Read(ref s_ready);
        internal static int Count => s_index?.Count ?? 0;

        internal static bool TryRead(int id, out int ca, out int pa)
        {
            ca = pa = 0;
            var index = s_index;
            if (index != null && index.TryGetValue(id, out var e))
            {
                if (ReadPlayer(e.p, e.kind, id, out ca, out pa)) return true;
                RequestBuild(stale: true); // another save was loaded, or the game moved the player
                return false;
            }
            RequestBuild(stale: false);    // first use, or a player created after the last build (or a non-player)
            return false;
        }

        // One read of B .. P+0x10 checks all three vtables and the id, and holds CA and PA.
        const int BlockLength = 0x380 + 0x10; // the longest kind

        static bool ReadPlayer(long p, int kind, int id, out int ca, out int pa)
        {
            ca = pa = 0;
            var s = Plugin.S;
            int len = s_personAt[kind] + 0x10, caAt = CaBase + s.CaOffset.Value, paAt = CaBase + s.PaOffset.Value;
            if (caAt < 0 || caAt + 2 > len || paAt < 0 || paAt + 2 > len) return false;
            var b = new Span<byte>(s_mem + ChunkSize + BlockLength, len); // main thread only
            if (!Read(p - s_personAt[kind], b) || !IsPlayer(b, 0, kind, id)) return false;
            ca = BitConverter.ToInt16(b.Slice(caAt));
            pa = BitConverter.ToInt16(b.Slice(paAt));
            return ca is >= 1 and <= 200 && pa is >= -200 and <= 200 && pa != 0;
        }

        // `at` = offset of B in `b`. id < 0 = any id.
        static bool IsPlayer(ReadOnlySpan<byte> b, int at, int kind, int id) =>
            BitConverter.ToInt64(b.Slice(at)) == s_playerVt[kind] &&
            BitConverter.ToInt64(b.Slice(at + AttrAt)) == s_attrVt &&
            BitConverter.ToInt64(b.Slice(at + s_personAt[kind])) == s_personVt[kind] &&
            (id < 0 || BitConverter.ToInt32(b.Slice(at + s_personAt[kind] + IdAt)) == id);

        // Builds run on a background thread; at most every 20 s after a failed check, every 5 min for unknown ids.
        static void RequestBuild(bool stale)
        {
            if (s_busy || s_noClasses) return;
            long now = Environment.TickCount64;
            long gap = s_index == null || stale ? 20_000 : 300_000;
            if (now - Interlocked.Read(ref s_lastBuild) < gap) return;
            Interlocked.Exchange(ref s_lastBuild, now);
            s_busy = true;
            new Thread(Build) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "FM27 Ability Numbers index" }.Start();
        }

        static void Build()
        {
            var started = Environment.TickCount64;
            try
            {
                if (s_personVt[0] == 0 && !FindClasses()) { s_noClasses = true; return; } // no retry: same module, same result
                Status = "indexing players...";
                var index = new Dictionary<int, (long, int)>(400_000);
                var buf = new Span<byte>(s_mem, ChunkSize);
                var block = new Span<byte>(s_mem + ChunkSize, BlockLength);
                var hits = new List<int>();
                long scanned = 0;
                foreach (var (start, size) in Regions())
                {
                    if (start < MemEnd && start + size > MemStart) continue; // our own buffers
                    for (long off = 0; off < size; off += ChunkSize)
                    {
                        int len = (int)Math.Min(ChunkSize, size - off);
                        long chunk = start + off;
                        var data = buf.Slice(0, len);
                        if (!Read(chunk, data)) continue;
                        scanned += len;
                        for (int kind = 0; kind < s_class.Length; kind++)
                        {
                            if (s_personVt[kind] == 0) continue; // not found in this build
                            int personAt = s_personAt[kind];
                            hits.Clear();
                            MemoryScan.FindQwords(data, s_personVt[kind], hits);
                            foreach (int h in hits)
                            {
                                int at = h - personAt;
                                if (at >= 0 && h + 0x10 <= len)
                                {
                                    if (IsPlayer(data, at, kind, -1)) index[BitConverter.ToInt32(data.Slice(h + IdAt))] = (chunk + h, kind);
                                }
                                else if (Read(chunk + at, block.Slice(0, personAt + 0x10)) && IsPlayer(block, 0, kind, -1))
                                    index[BitConverter.ToInt32(block.Slice(personAt + IdAt))] = (chunk + h, kind); // straddles chunks
                            }
                        }
                    }
                }
                s_index = index;
                Interlocked.Increment(ref s_ready);
                Status = $"{index.Count} players indexed ({scanned >> 20} MB scanned in {(Environment.TickCount64 - started) / 1000.0:0.0} s)";
                Plugin.L.LogInfo(Status);
            }
            catch (Exception e)
            {
                Status = "index failed: " + e.Message;
                Plugin.L.LogError($"[index] {e}");
            }
            finally { s_busy = false; }
        }

        static bool FindClasses()
        {
            Status = "finding FM classes...";
            long mod = (long)GetModuleHandleW("game_plugin.dll");
            if (mod == 0) { Status = "game_plugin.dll not loaded"; return false; }
            var secs = Sections(mod);
            long person = MemoryScan.FindVtable(secs, mod, "ACTUAL_PLAYER", 0x288);
            long player = MemoryScan.FindVtable(secs, mod, "ACTUAL_PLAYER", 0);
            long attr = MemoryScan.FindVtable(secs, mod, "PLAYING_DATA", 0);
            if (person == 0 || player == 0 || attr == 0)
            {
                Status = $"FM classes not found in this build (person {person:X}, player {player:X}, attr {attr:X})";
                Plugin.L.LogError(Status);
                return false;
            }
            // Optional: without it, the players FM keeps as ACTUAL_PLAYER_AND_NON_PLAYER just show no number.
            long staffPerson = MemoryScan.FindVtable(secs, mod, s_class[1], s_personAt[1]);
            long staffPlayer = MemoryScan.FindVtable(secs, mod, s_class[1], 0);
            if (staffPerson == 0 || staffPlayer == 0)
                Plugin.L.LogWarning($"{s_class[1]} not found in this build; some players, mostly older ones, show no number.");
            else { s_playerVt[1] = staffPlayer; s_personVt[1] = staffPerson; }
            s_playerVt[0] = player;
            s_attrVt = attr;
            s_personVt[0] = person; // last: Build checks this one
            Plugin.L.LogInfo($"FM classes: person 0x{person - mod:X} player 0x{player - mod:X} attr 0x{attr - mod:X} " +
                             $"staff-player person 0x{Rva(staffPerson, mod)} player 0x{Rva(staffPlayer, mod)}");
            return true;
        }

        // The module's readable sections as they are in memory (very large ones, i.e. packed code, are skipped).
        static List<Section> Sections(long mod)
        {
            var list = new List<Section>();
            if (!ReadI32(mod + 0x3C, out var peAt)) return list;
            long pe = mod + peAt;
            Span<byte> head = stackalloc byte[24];
            if (!Read(pe, head)) return list;
            int count = BitConverter.ToUInt16(head.Slice(6)), optSize = BitConverter.ToUInt16(head.Slice(20));
            var table = new byte[count * 40];
            if (!Read(pe + 24 + optSize, table)) return list;
            for (int i = 0; i < count; i++)
            {
                int size = BitConverter.ToInt32(table, i * 40 + 8), va = BitConverter.ToInt32(table, i * 40 + 12);
                uint flags = BitConverter.ToUInt32(table, i * 40 + 36);
                if (size <= 0 || size > 160 << 20 || (flags & 0x40000000) == 0) continue; // readable only
                var data = new byte[size];
                for (int o = 0; o < size; o += 1 << 20)
                    Read(mod + va + o, data.AsSpan(o, Math.Min(1 << 20, size - o))); // unreadable pages stay zero
                list.Add(new Section(mod + va, data));
            }
            return list;
        }

        // Committed private read-write memory: where the game's heap (and its database) lives.
        static List<(long start, long size)> Regions()
        {
            var list = new List<(long, long)>();
            long addr = 0x10000;
            while (VirtualQuery((IntPtr)addr, out var m, (IntPtr)sizeof(MBI)) != IntPtr.Zero)
            {
                long size = (long)m.RegionSize;
                if (size <= 0) break;
                if (m.State == 0x1000 && m.Type == 0x20000 && m.Protect == 0x04) list.Add(((long)m.BaseAddress, size));
                addr = (long)m.BaseAddress + size;
            }
            return list;
        }

        internal static string Describe(int id)
        {
            var index = s_index;
            if (index == null || !index.TryGetValue(id, out var e)) return $"id {id}: not in index ({Status})";
            var (p, kind) = e;
            long b0 = p - s_personAt[kind], mod = (long)GetModuleHandleW("game_plugin.dll");
            string Q(long at) => ReadI64(at, out var v) ? $"0x{v - mod:X}" : "unreadable";
            ReadI32(p + IdAt, out var rid);
            ReadI16(b0 + CaBase + Plugin.S.CaOffset.Value, out var rca);
            ReadI16(b0 + CaBase + Plugin.S.PaOffset.Value, out var rpa);
            return $"id {id}: {s_class[kind]} P=0x{p:X} read={ReadPlayer(p, kind, id, out var ca, out var pa)} ca={ca} pa={pa} | " +
                   $"B {Q(b0)} want 0x{Rva(s_playerVt[kind], mod)}, B+0x148 {Q(b0 + AttrAt)} want 0x{Rva(s_attrVt, mod)}, " +
                   $"P {Q(p)} want 0x{Rva(s_personVt[kind], mod)}, id {rid}, raw ca {rca} pa {rpa}";
        }

        // Debug `find <id>`: objects whose qword at +8 holds this record id (low 24 bits) and whose first qword points
        // into game_plugin.dll, with their MSVC class and memory region, to see why the index misses a player.
        internal static void Find(int id) =>
            new Thread(() =>
            {
                try { Plugin.L.LogInfo(FindReport(id)); }
                catch (Exception e) { Plugin.L.LogError($"[find] {e}"); }
            }) { IsBackground = true, Name = "FM27 Ability Numbers find" }.Start();

        static string FindReport(int id)
        {
            long mod = (long)GetModuleHandleW("game_plugin.dll");
            if (mod == 0 || !ReadI32(mod + 0x3C, out var peAt) || !ReadI32(mod + peAt + 24 + 56, out var imageSize))
                return "find: game_plugin.dll not readable";
            var sb = new StringBuilder($"find {id}: {(s_index?.ContainsKey(id) == true ? "in" : "not in")} index, " +
                                       $"vtables person 0x{Rva(s_personVt[0], mod)}/0x{Rva(s_personVt[1], mod)} attr 0x{Rva(s_attrVt, mod)}\n");
            var needle = new byte[] { (byte)id, (byte)(id >> 8), (byte)(id >> 16) };
            var buf = (byte*)NativeMemory.Alloc(ChunkSize);
            int hits = 0;
            try
            {
                long addr = 0x10000;
                while (hits < 40 && VirtualQuery((IntPtr)addr, out var m, (IntPtr)sizeof(MBI)) != IntPtr.Zero)
                {
                    long start = (long)m.BaseAddress, size = (long)m.RegionSize;
                    if (size <= 0) break;
                    addr = start + size;
                    // committed, not NOACCESS/GUARD, not our own buffer
                    if (m.State != 0x1000 || (m.Protect & 0x101) != 0 || (start < (long)buf + ChunkSize && start + size > (long)buf)) continue;
                    for (long off = 0; off < size && hits < 40; off += ChunkSize)
                    {
                        var data = new Span<byte>(buf, (int)Math.Min(ChunkSize, size - off));
                        if (!Read(start + off, data)) continue;
                        for (int from = 8; hits < 40;)
                        {
                            int i = data.Slice(from).IndexOf(needle);
                            if (i < 0) break;
                            int at = from + i;
                            from = at + 1;
                            long vt = BitConverter.ToInt64(data.Slice(at - 8));
                            if ((at & 7) != 0 || vt < mod || vt >= mod + imageSize) continue;
                            long p = start + off + at - 8;
                            hits++;
                            sb.Append($"  P=0x{p:X} region type 0x{m.Type:X} protect 0x{m.Protect:X} id word 0x{BitConverter.ToInt32(data.Slice(at)):X8} " +
                                      $"P {ClassName(vt, mod)} | P-0x288 {ClassAt(p - 0x288, mod, imageSize)} | P-0x380 {ClassAt(p - 0x380, mod, imageSize)}\n");
                        }
                    }
                }
            }
            finally { NativeMemory.Free(buf); }
            return sb.Append($"  {hits} hits").ToString();
        }

        // Debug `layout`: for every indexed player, which int16 pair in the object looks like CA then PA (CA 1..200,
        // PA >= CA or a negative range code). After a game update moves CA/PA, the top pair gives the new Data.* offsets.
        internal static void Layout() =>
            new Thread(() =>
            {
                try { Plugin.L.LogInfo(LayoutReport()); }
                catch (Exception e) { Plugin.L.LogError($"[layout] {e}"); }
            }) { IsBackground = true, Name = "FM27 Ability Numbers layout" }.Start();

        static string LayoutReport()
        {
            var index = s_index;
            if (index == null) return $"layout: no index yet ({Status})";
            const int Len = 0x600, Slots = Len / 2 - 1;
            var buf = new byte[Len];
            var sb = new StringBuilder($"layout: config offset (from P of an ACTUAL_PLAYER), players where CA/PA look valid, " +
                                       $"mean CA, distinct CA values. Current config: CA {Plugin.S.CaOffset.Value}, PA {Plugin.S.PaOffset.Value}\n");
            for (int kind = 0; kind < s_class.Length; kind++)
            {
                var valid = new int[Slots];
                var sum = new long[Slots];
                var seen = new bool[Slots, 201];
                int players = 0;
                foreach (var (id, (p, k)) in index)
                {
                    if (k != kind || !Read(p - s_personAt[kind], buf) || !IsPlayer(buf, 0, kind, id)) continue;
                    players++;
                    for (int s = 0; s < Slots; s++)
                    {
                        int ca = BitConverter.ToInt16(buf, s * 2), pa = BitConverter.ToInt16(buf, s * 2 + 2);
                        if (ca < 1 || ca > 200 || !(pa >= ca && pa <= 200 || pa < 0 && pa >= -200)) continue;
                        valid[s]++;
                        sum[s] += ca;
                        seen[s, ca] = true;
                    }
                }
                var order = new List<int>();
                for (int s = 0; s < Slots; s++) if (valid[s] > 0) order.Add(s);
                order.Sort((a, b) => valid[b].CompareTo(valid[a]));
                sb.Append($"  {s_class[kind]}: {players} players read\n");
                int shown = 0;
                foreach (int s in order)
                {
                    int distinct = 0;
                    for (int v = 1; v <= 200; v++) if (seen[s, v]) distinct++;
                    if (distinct < 30) continue; // flags and small enums, not an ability
                    sb.Append($"    CA {s * 2 - CaBase} PA {s * 2 + 2 - CaBase}: {100.0 * valid[s] / Math.Max(1, players):0.0}% " +
                              $"mean {(double)sum[s] / valid[s]:0.0} distinct {distinct}\n");
                    if (++shown == 6) break;
                }
            }
            return sb.ToString();
        }

        // Debug `raw <id>`: the player's object as int16 words, offsets as config values (from P of an ACTUAL_PLAYER).
        internal static string Raw(int id)
        {
            var index = s_index;
            if (index == null || !index.TryGetValue(id, out var e)) return $"raw {id}: not in index ({Status})";
            var buf = new byte[s_personAt[e.kind] + 0x80];
            if (!Read(e.p - s_personAt[e.kind], buf)) return $"raw {id}: unreadable";
            var sb = new StringBuilder($"raw {id} ({s_class[e.kind]}), int16 words:\n");
            for (int o = 0; o < buf.Length; o += 32)
            {
                sb.Append($"  {o - CaBase,5}:");
                for (int i = o; i < o + 32 && i < buf.Length; i += 2) sb.Append($" {BitConverter.ToInt16(buf, i),6}");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        static string Rva(long va, long mod) => va == 0 ? "-" : (va - mod).ToString("X");

        static string ClassAt(long addr, long mod, int imageSize) =>
            ReadI64(addr, out var vt) && vt >= mod && vt < mod + imageSize ? ClassName(vt, mod) : "-";

        // vtable[-1] -> complete object locator; +12 = type descriptor RVA; its name starts 16 bytes in.
        static string ClassName(long vt, long mod)
        {
            if (!ReadI64(vt - 8, out var col) || !ReadI32(col + 12, out var td)) return $"0x{vt - mod:X}";
            var name = new byte[96];
            if (!Read(mod + td + 16, name)) return $"0x{vt - mod:X}";
            int end = Array.IndexOf(name, (byte)0);
            return $"0x{vt - mod:X} {Encoding.ASCII.GetString(name, 0, end < 0 ? name.Length : end)}";
        }

        static bool Read(long addr, Span<byte> dst)
        {
            fixed (byte* d = dst)
                return ReadProcessMemory(s_proc, (IntPtr)addr, d, (IntPtr)dst.Length, out var n) && (long)n == dst.Length;
        }

        static bool ReadI64(long addr, out long v) { long x; bool ok = Read(addr, new Span<byte>(&x, 8)); v = x; return ok; }
        static bool ReadI32(long addr, out int v) { int x; bool ok = Read(addr, new Span<byte>(&x, 4)); v = x; return ok; }
        static bool ReadI16(long addr, out short v) { short x; bool ok = Read(addr, new Span<byte>(&x, 2)); v = x; return ok; }

        [StructLayout(LayoutKind.Sequential)]
        struct MBI
        {
            public IntPtr BaseAddress, AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public IntPtr RegionSize;
            public uint State, Protect, Type;
        }

        [DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32")] static extern IntPtr VirtualQuery(IntPtr addr, out MBI info, IntPtr length);
        [DllImport("kernel32")] static extern bool ReadProcessMemory(IntPtr process, IntPtr addr, byte* buffer, IntPtr size, out IntPtr read);
    }
}
