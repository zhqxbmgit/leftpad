using System.Text;

namespace PcDs4Server;

internal sealed record ReceiverLogEntry(long SequenceId, string RawText);

internal sealed record ReceiverLogMutation(
    ReceiverLogEntry Entry,
    ReceiverLogEntry? EvictedEntry);

internal sealed class ReceiverLogBuffer
{
    public const int MaximumEntries = 300;

    private readonly object _sync = new();
    private readonly List<ReceiverLogEntry> _entries = [];
    private long _nextSequenceId = 1;

    public ReceiverLogMutation AppendWithEviction(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        lock (_sync)
        {
            var entry = new ReceiverLogEntry(
                _nextSequenceId++,
                rawText);
            ReceiverLogEntry? evictedEntry = null;
            if (_entries.Count == MaximumEntries)
            {
                evictedEntry = _entries[0];
                _entries.RemoveAt(0);
            }
            _entries.Add(entry);
            return new ReceiverLogMutation(entry, evictedEntry);
        }
    }

    public IReadOnlyList<ReceiverLogEntry> GetEntries()
    {
        lock (_sync)
        {
            return _entries.ToArray();
        }
    }
}

internal static class NativeLogProjection
{
    public static string CreateEntryText(ReceiverLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.RawText + Environment.NewLine;
    }

    public static string CreateText(IReadOnlyList<ReceiverLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) return string.Empty;

        var text = new StringBuilder();
        foreach (ReceiverLogEntry entry in entries)
            text.Append(CreateEntryText(entry));
        return text.ToString();
    }

    public static int GetRichTextCharacterCount(ReceiverLogEntry entry) =>
        CreateEntryText(entry)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Length;
}
