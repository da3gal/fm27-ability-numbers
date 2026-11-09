using System;
using System.Collections.Generic;
using System.Text;
using FM27AbilityNumbers;
using Xunit;

public class MemoryScanTests
{
    const long Base = 0x180000000;

    // A fake module section laid out like MSVC x64 RTTI: type descriptor, two complete object locators
    // (sub-objects at 0 and 0x288) and the vtables that follow a pointer to each locator.
    static List<Section> FakeModule(string name, bool duplicateVtable = false)
    {
        long va = Base + 0x1000;
        var d = new byte[0x1000];
        int td = 0x100;
        Encoding.ASCII.GetBytes($".?AV{name}@db@@\0").CopyTo(d, td + 16);
        void Col(int at, int offset)
        {
            BitConverter.GetBytes(1).CopyTo(d, at);
            BitConverter.GetBytes(offset).CopyTo(d, at + 4);
            BitConverter.GetBytes((uint)(va + td - Base)).CopyTo(d, at + 12);
            BitConverter.GetBytes((uint)(va + at - Base)).CopyTo(d, at + 20);
        }
        Col(0x200, 0);
        Col(0x240, 0x288);
        BitConverter.GetBytes(va + 0x200).CopyTo(d, 0x300); // vtable for offset 0 at 0x308
        BitConverter.GetBytes(va + 0x240).CopyTo(d, 0x400); // vtable for offset 0x288 at 0x408
        if (duplicateVtable) BitConverter.GetBytes(va + 0x240).CopyTo(d, 0x500);
        return new List<Section> { new Section(va, d) };
    }

    [Fact]
    public void FindsVtableForEachSubObject()
    {
        var secs = FakeModule("ACTUAL_PLAYER");
        Assert.Equal(Base + 0x1000 + 0x308, MemoryScan.FindVtable(secs, Base, "ACTUAL_PLAYER", 0));
        Assert.Equal(Base + 0x1000 + 0x408, MemoryScan.FindVtable(secs, Base, "ACTUAL_PLAYER", 0x288));
    }

    [Fact]
    public void MissingNameOrOffsetGivesZero()
    {
        var secs = FakeModule("ACTUAL_PLAYER");
        Assert.Equal(0, MemoryScan.FindVtable(secs, Base, "PLAYING_DATA", 0));
        Assert.Equal(0, MemoryScan.FindVtable(secs, Base, "ACTUAL_PLAYER", 0x140));
    }

    [Fact]
    public void AmbiguousVtableGivesZero() =>
        Assert.Equal(0, MemoryScan.FindVtable(FakeModule("ACTUAL_PLAYER", duplicateVtable: true), Base, "ACTUAL_PLAYER", 0x288));

    [Fact]
    public void NameMustEndExactly() =>
        Assert.Equal(0, MemoryScan.FindVtable(FakeModule("ACTUAL_PLAYER_X"), Base, "ACTUAL_PLAYER", 0));

    [Fact]
    public void FindsAlignedQwordsOnly()
    {
        var buf = new byte[64];
        long v = 0x7FF6_1234_5678;
        BitConverter.GetBytes(v).CopyTo(buf, 8);
        BitConverter.GetBytes(v).CopyTo(buf, 41); // unaligned: ignored
        BitConverter.GetBytes(v).CopyTo(buf, 56);
        var hits = new List<int>();
        MemoryScan.FindQwords(buf, v, hits);
        Assert.Equal(new[] { 8, 56 }, hits);
    }

    [Fact]
    public void IgnoresTrailingPartialQword()
    {
        var buf = new byte[20];
        BitConverter.GetBytes(5L).CopyTo(buf, 8);
        var hits = new List<int>();
        MemoryScan.FindQwords(buf, 5L, hits);
        Assert.Equal(new[] { 8 }, hits);
    }
}
