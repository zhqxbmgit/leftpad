using System.Reflection;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class NativeReceiverCommonTests
{
    [Fact]
    public void StableMoveConstants_RemainUnchanged()
    {
        Assert.Equal(26.0, VirtualJoystickController.JoystickRadius);
        Assert.Equal(2.0, VirtualJoystickController.ActivationRadius);
        Assert.Equal(128, VirtualJoystickController.NeutralAxis);
    }

    [Fact]
    public void NativeCloseTrayLifecycleEntryPoints_RemainUnchanged()
    {
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.NotNull(typeof(MainForm).GetMethod("MainForm_FormClosing", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ExitProgram", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("ShowMainForm", privateInstance));
        Assert.NotNull(typeof(MainForm).GetMethod("WndProc", privateInstance));
    }

    [Fact]
    public void Buffer_IncludesAllExistingLinesInOrder()
    {
        var buffer = new ReceiverLogBuffer();
        buffer.AppendWithEviction("first line");
        buffer.AppendWithEviction("second line");

        IReadOnlyList<ReceiverLogEntry> entries = buffer.GetEntries();

        Assert.Equal(["first line", "second line"], entries.Select(entry => entry.RawText));
        Assert.Equal([1L, 2L], entries.Select(entry => entry.SequenceId));
    }

    [Fact]
    public void Buffer_PreservesThreeHundredLineCapAndEvictsOldestEntries()
    {
        var buffer = new ReceiverLogBuffer();
        for (int index = 1; index <= ReceiverLogBuffer.MaximumEntries + 5; index++)
            buffer.AppendWithEviction($"line {index}");

        IReadOnlyList<ReceiverLogEntry> entries = buffer.GetEntries();

        Assert.Equal(300, entries.Count);
        Assert.Equal("line 6", entries[0].RawText);
        Assert.Equal("line 305", entries[^1].RawText);
        Assert.Equal(6, entries[0].SequenceId);
        Assert.Equal(305, entries[^1].SequenceId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(150)]
    [InlineData(300)]
    public void Buffer_DoesNotEvictBeforeCapacity(int count)
    {
        var buffer = new ReceiverLogBuffer();
        for (int index = 0; index < count; index++)
            buffer.AppendWithEviction($"line {index}");

        Assert.Equal(count, buffer.GetEntries().Count);
    }

    [Fact]
    public void RawText_IsPreservedExactlyIncludingMarkupPathsAndWhitespace()
    {
        const string raw = "  <script>alert('x')</script>  C:\\temp\\a<b>.txt  ";
        var buffer = new ReceiverLogBuffer();

        ReceiverLogEntry entry = buffer.AppendWithEviction(raw).Entry;
        IReadOnlyList<ReceiverLogEntry> entries = buffer.GetEntries();

        Assert.Equal(raw, entry.RawText);
        Assert.Equal(raw, Assert.Single(entries).RawText);
    }
}
