using System.Numerics;
using Spot.Engine;

namespace Spot.Engine.Console;

/// <summary>
/// A log sink that mirrors log entries into the developer console.
/// </summary>
public sealed class DevConsoleSink : ILogSink
{
    private static readonly Vector4 DefaultColor = new(0.7f, 0.7f, 0.7f, 1.0f);
    private static readonly Vector4 WarningColor = new(0.9f, 0.9f, 0.3f, 1.0f);
    private static readonly Vector4 ErrorColor = new(1.0f, 0.35f, 0.35f, 1.0f);

    private readonly DevConsole _console;

    /// <summary>
    /// Initializes a new instance of the <see cref="DevConsoleSink"/> class.
    /// </summary>
    /// <param name="console">The console that receives the log lines.</param>
    public DevConsoleSink(DevConsole console)
    {
        _console = console;
    }

    /// <inheritdoc />
    public void Write(in LogEntry entry) => _console.Print(entry.ToString(), ColorFor(entry.Level));

    private static Vector4 ColorFor(LogLevel level) => level switch
    {
        LogLevel.Error => ErrorColor,
        LogLevel.Warn => WarningColor,
        _ => DefaultColor,
    };
}
