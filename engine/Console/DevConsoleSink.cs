using System.Globalization;
using System.Numerics;
using Serilog.Core;
using Serilog.Events;

namespace Spot.Console;

/// <summary>
/// A Serilog sink that mirrors log events into the developer console.
/// </summary>
public sealed class DevConsoleSink : ILogEventSink
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
    public void Emit(LogEvent logEvent)
    {
        string name = logEvent.Properties.TryGetValue("Name", out LogEventPropertyValue? value)
                      && value is ScalarValue { Value: string scalar }
            ? scalar
            : "SPOT";

        string message = logEvent.RenderMessage(CultureInfo.InvariantCulture);
        string line = $"[{logEvent.Timestamp:HH:mm:ss}] {name}: {message}";

        _console.Print(line, ColorFor(logEvent.Level));
    }

    private static Vector4 ColorFor(LogEventLevel level) => level switch
    {
        LogEventLevel.Error or LogEventLevel.Fatal => ErrorColor,
        LogEventLevel.Warning => WarningColor,
        _ => DefaultColor,
    };
}
