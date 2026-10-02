using Spot.Core;

namespace Spot.Engine.Tests.Fakes;

/// <summary>
/// A log sink that keeps every entry, for asserting that a path logs (and continues) instead of throwing.
/// Use <see cref="Capture"/> to route <see cref="Log"/> to it for the duration of a test.
/// </summary>
internal sealed class RecordingLogSink : ILogSink, IDisposable
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = new();

    private RecordingLogSink()
    {
    }

    /// <summary>A snapshot of the entries written so far.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>Replaces Log's sinks with a new recorder; disposing it restores the default sinks.</summary>
    public static RecordingLogSink Capture()
    {
        var sink = new RecordingLogSink();
        Log.ClearSinks();
        Log.AddSink(sink);
        return sink;
    }

    /// <summary>Whether any entry at <paramref name="level"/> contains <paramref name="text"/>.</summary>
    public bool Contains(LogLevel level, string text) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(text, StringComparison.Ordinal));

    public void Write(in LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    public void Dispose() => Log.ResetToDefault();
}
