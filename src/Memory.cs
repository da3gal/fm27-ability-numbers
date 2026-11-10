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
    // Layout (from the public SiRRAHx/fm27-live-editor research): a player is three objects at fixed distances
    // from the person object P, each starting with an MSVC vtable of game_plugin.dll:
    //   P - 0x288 db::ACTUAL_PLAYER, P - 0x140 db::PLAYING_DATA, P db::ACTUAL_PLAYER's person part (+0x288);
    //   P + 8 = record id (the "dref" id FM's UI references carry); P - 0x24 CA, P - 0x22 PA (int16).
    // Every read goes through ReadProcessMemory on our own process, so memory freed by the game in the meantime
    // makes a read fail instead of crashing, and every value is only used after all three vtables and the id match.
    internal static unsafe class Memory
    {
        const int PlayerPart = -0x288, AttrPart = -0x140, IdAt = 8;
        const int ChunkSize = 4 << 20;

        static readonly IntPtr s_proc = GetCurrentProcess();
        // Every buffer that holds a copy of a player block lives in this one native allocation, which the scan skips:
        // a copy found by the scan would otherwise be indexed as a player. [scan chunk][scan block][lookup block]
        static readonly byte* s_mem = (byte*)NativeMemory.Alloc((nuint)(ChunkSize + 2 * BlockLength));
        static long MemStart => (long)s_mem;
        static long MemEnd => (long)s_mem + ChunkSize + 2 * BlockLength;
        static bool s_noClasses;
        static long s_personVt, s_playerVt, s_attrVt;
        static volatile Dictionary<int, long> s_index;
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
            if (index != null && index.TryGetValue(id, out var p))
            {
                if (ReadPlayer(p, id, out ca, out pa)) return true;
                RequestBuild(stale: true); // another save was loaded, or the game moved the player
                return false;
            }
            RequestBuild(stale: false);    // first use, or a player created after the last build (or a non-player)
            return false;
        }

        // One read of P-0x288 .. P+0x10 checks all three vtables and the id, and holds CA and PA.
        const int BlockStart = PlayerPart, BlockLength = -PlayerPart + 0x10;

        static bool ReadPlayer(long p, int id, out int ca, out int pa)
        {
            ca = pa = 0;
            var s = Plugin.S;
            int caAt = -BlockStart + s.CaOffset.Value, paAt = -BlockStart + s.PaOffset.Value;
            if (caAt < 0 || caAt + 2 > BlockLength || paAt < 0 || paAt + 2 > BlockLength) return false;
            var b = new Span<byte>(s_mem + ChunkSize + BlockLength, BlockLength); // main thread only
            if (!Read(p + BlockStart, b) || !IsPlayer(b, 0, id)) return false;
            ca = BitConverter.ToInt16(b.Slice(caAt));
            pa = BitConverter.ToInt16(b.Slice(paAt));
            return ca is >= 1 and <= 200 && pa is >= -200 and <= 200 && pa != 0;
        }

        // `at` = offset of P-0x288 in `b`. id < 0 = any id.
        static bool IsPlayer(ReadOnlySpan<byte> b, int at, int id) =>
            BitConverter.ToInt64(b.Slice(at)) == s_playerVt &&
            BitConverter.ToInt64(b.Slice(at - PlayerPart + AttrPart)) == s_attrVt &&
            BitConverter.ToInt64(b.Slice(at - PlayerPart)) == s_personVt &&
            (id < 0 || BitConverter.ToInt32(b.Slice(at - PlayerPart + IdAt)) == id);

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
                if (s_personVt == 0 && !FindClasses()) { s_noClasses = true; return; } // no retry: same module, same result
                Status = "indexing players...";
                var index = new Dictionary<int, long>(400_000);
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
                        hits.Clear();
                        MemoryScan.FindQwords(data, s_personVt, hits);
                        foreach (int h in hits)
                        {
                            int at = h + BlockStart;
                            if (at >= 0 && at + BlockLength <= len)
                            {
                                if (IsPlayer(data, at, -1)) index[BitConverter.ToInt32(data.Slice(h + IdAt))] = chunk + h;
                            }
                            else if (Read(chunk + h + BlockStart, block) && IsPlayer(block, 0, -1))
                                index[BitConverter.ToInt32(block.Slice(-BlockStart + IdAt))] = chunk + h; // straddles chunks
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
            s_playerVt = player;
            s_attrVt = attr;
            s_personVt = person; // last: TryRead checks this one
            Plugin.L.LogInfo($"FM classes: person 0x{person - mod:X} player 0x{player - mod:X} attr 0x{attr - mod:X}");
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
            if (index == null || !index.TryGetValue(id, out var p)) return $"id {id}: not in index ({Status})";
            var bytes = new byte[0x40];
            bool ok = Read(p - 0x40, bytes);
            return $"id {id}: P=0x{p:X} read={ReadPlayer(p, id, out var ca, out var pa)} ca={ca} pa={pa} " +
                   $"P-0x40..P: {(ok ? BitConverter.ToString(bytes) : "unreadable")}";
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
                                       $"vtables person 0x{s_personVt - mod:X} player 0x{s_playerVt - mod:X} attr 0x{s_attrVt - mod:X}\n");
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
                                      $"P {ClassName(vt, mod)} | P-0x288 {ClassAt(p + PlayerPart, mod, imageSize)} | P-0x140 {ClassAt(p + AttrPart, mod, imageSize)}\n");
                        }
                    }
                }
            }
            finally { NativeMemory.Free(buf); }
            return sb.Append($"  {hits} hits").ToString();
        }

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
