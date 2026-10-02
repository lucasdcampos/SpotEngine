using System.Globalization;
using System.Text;

namespace Spot.Core;

/// <summary>
/// The severity of a log entry.
/// </summary>
public enum LogLevel
{
    /// <summary>Authoring and diagnostic detail, usually hidden.</summary>
    Trace,

    /// <summary>Something the user acted on or should know about.</summary>
    Info,

    /// <summary>Something went wrong but was handled.</summary>
    Warn,

    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>
/// One rendered log entry, as delivered to every <see cref="ILogSink"/>.
/// </summary>
/// <param name="Timestamp">When the entry was written.</param>
/// <param name="Level">The entry's severity.</param>
/// <param name="Source">The logger that wrote it: <see cref="Log.CoreSource"/> or <see cref="Log.ClientSource"/>.</param>
/// <param name="Message">The message with its arguments already substituted.</param>
public readonly record struct LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Source, string Message)
{
    /// <summary>
    /// Formats the entry as a single line: <c>[HH:mm:ss] SOURCE: message</c>.
    /// </summary>
    /// <returns>The formatted line.</returns>
    public override string ToString() =>
        $"[{Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {Source}: {Message}";
}

/// <summary>
/// A destination for log entries (the terminal, a file, an in-game console, a test recorder). Sinks may be
/// called from any thread, so implementations must be thread-safe.
/// </summary>
public interface ILogSink
{
    /// <summary>
    /// Writes one entry. Throwing is tolerated — the logger drops the failure rather than propagating it — but
    /// a sink should not rely on that.
    /// </summary>
    /// <param name="entry">The entry to write.</param>
    void Write(in LogEntry entry);
}

/// <summary>
/// The default sink: writes each entry as one line to standard output (errors to standard error).
/// </summary>
public sealed class ConsoleLogSink : ILogSink
{
    /// <inheritdoc />
    public void Write(in LogEntry entry)
    {
        TextWriter writer = entry.Level == LogLevel.Error ? System.Console.Error : System.Console.Out;
        writer.WriteLine(entry.ToString());
    }
}

/// <summary>
/// Logging for the engine (core) and the application (client). Usable with no setup: until sinks are configured,
/// entries go to the terminal through a <see cref="ConsoleLogSink"/>. Logging never throws — a bad message
/// template or a failing sink is swallowed rather than taking the caller down.
/// </summary>
/// <remarks>
/// Messages are templates: a hole is either positional (<c>{0}</c>, <c>{1:F2}</c>) or named (<c>{Path}</c>), and
/// named holes take the arguments in order. Doubled braces (<c>{{</c>, <c>}}</c>) are literal.
/// </remarks>
public static class Log
{
    /// <summary>The source name of entries written by the engine (<c>Core*</c> methods).</summary>
    public const string CoreSource = "SPOT";

    /// <summary>The source name of entries written by the application (the unprefixed methods).</summary>
    public const string ClientSource = "APP";

    // Copy-on-write so a write never contends with a sink being added or removed, on any thread.
    private static volatile ILogSink[] s_sinks = { new ConsoleLogSink() };
    private static readonly object s_sinksGate = new();

    /// <summary>
    /// Gets or sets the lowest level that is delivered to sinks. Defaults to <see cref="LogLevel.Trace"/>.
    /// </summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>
    /// Gets the sinks entries are currently delivered to.
    /// </summary>
    public static IReadOnlyList<ILogSink> Sinks => s_sinks;

    /// <summary>
    /// Adds a sink. Adding the same instance twice has no effect.
    /// </summary>
    /// <param name="sink">The sink to add.</param>
    public static void AddSink(ILogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (s_sinksGate)
        {
            if (Array.IndexOf(s_sinks, sink) < 0)
            {
                s_sinks = [.. s_sinks, sink];
            }
        }
    }

    /// <summary>
    /// Removes a sink.
    /// </summary>
    /// <param name="sink">The sink to remove.</param>
    /// <returns><see langword="true"/> if the sink was registered.</returns>
    public static bool RemoveSink(ILogSink sink)
    {
        lock (s_sinksGate)
        {
            int index = Array.IndexOf(s_sinks, sink);
            if (index < 0)
            {
                return false;
            }

            s_sinks = [.. s_sinks[..index], .. s_sinks[(index + 1)..]];
            return true;
        }
    }

    /// <summary>
    /// Removes every sink, silencing logging until one is added.
    /// </summary>
    public static void ClearSinks()
    {
        lock (s_sinksGate)
        {
            s_sinks = [];
        }
    }

    /// <summary>
    /// Restores the default configuration: a single <see cref="ConsoleLogSink"/> and a
    /// <see cref="MinimumLevel"/> of <see cref="LogLevel.Trace"/>.
    /// </summary>
    public static void ResetToDefault()
    {
        lock (s_sinksGate)
        {
            s_sinks = [new ConsoleLogSink()];
        }

        MinimumLevel = LogLevel.Trace;
    }

    /// <summary>
    /// Renders a message template and delivers it to every sink.
    /// </summary>
    /// <param name="level">The entry's severity.</param>
    /// <param name="source">The logger name (see <see cref="CoreSource"/> and <see cref="ClientSource"/>).</param>
    /// <param name="message">The message template.</param>
    /// <param name="args">The template's arguments.</param>
    public static void Write(LogLevel level, string source, string message, params object?[] args)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        ILogSink[] sinks = s_sinks;
        if (sinks.Length == 0)
        {
            return;
        }

        string text;
        try
        {
            text = Render(message, args);
        }
        catch (Exception)
        {
            text = message;
        }

        var entry = new LogEntry(DateTimeOffset.Now, level, source, text);
        foreach (ILogSink sink in sinks)
        {
            try
            {
                sink.Write(entry);
            }
            catch (Exception)
            {
                // A sink must never take the caller down; there is nowhere left to report its failure.
            }
        }
    }

    /// <summary>Writes an engine trace entry.</summary>
    public static void CoreTrace(string message, params object?[] args) => Write(LogLevel.Trace, CoreSource, message, args);

    /// <summary>Writes an engine info entry.</summary>
    public static void CoreInfo(string message, params object?[] args) => Write(LogLevel.Info, CoreSource, message, args);

    /// <summary>Writes an engine warning.</summary>
    public static void CoreWarn(string message, params object?[] args) => Write(LogLevel.Warn, CoreSource, message, args);

    /// <summary>Writes an engine error.</summary>
    public static void CoreError(string message, params object?[] args) => Write(LogLevel.Error, CoreSource, message, args);

    /// <summary>Writes an application trace entry.</summary>
    public static void Trace(string message, params object?[] args) => Write(LogLevel.Trace, ClientSource, message, args);

    /// <summary>Writes an application info entry.</summary>
    public static void Info(string message, params object?[] args) => Write(LogLevel.Info, ClientSource, message, args);

    /// <summary>Writes an application warning.</summary>
    public static void Warn(string message, params object?[] args) => Write(LogLevel.Warn, ClientSource, message, args);

    /// <summary>Writes an application error.</summary>
    public static void Error(string message, params object?[] args) => Write(LogLevel.Error, ClientSource, message, args);

    /// <summary>
    /// Substitutes a message template's holes with their arguments (see the type remarks for the syntax). A hole
    /// with no matching argument is kept verbatim; a <see langword="null"/> argument renders as <c>null</c>.
    /// </summary>
    /// <param name="template">The message template.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>The rendered message.</returns>
    public static string Render(string template, params object?[]? args)
    {
        args ??= [];
        if (template.IndexOf('{') < 0 && template.IndexOf('}') < 0)
        {
            return template;
        }

        var sb = new StringBuilder(template.Length + 16);
        int nextNamed = 0;
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                sb.Append('{');
                i += 2;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }

            int close = c == '{' ? template.IndexOf('}', i + 1) : -1;
            if (close < 0)
            {
                sb.Append(c);
                i++;
                continue;
            }

            string hole = template.Substring(i + 1, close - i - 1);
            int argIndex = ParseHole(hole, out string? format, out int alignment, out bool positional);
            if (!positional)
            {
                argIndex = nextNamed++;
            }

            if (argIndex >= 0 && argIndex < args.Length)
            {
                string value = FormatArg(args[argIndex], format);
                sb.Append(alignment >= 0 ? value.PadLeft(alignment) : value.PadRight(-alignment));
            }
            else
            {
                sb.Append(template, i, close - i + 1);
            }

            i = close + 1;
        }

        return sb.ToString();
    }

    // Splits "name,alignment:format" (Serilog/composite-format style), dropping the @/$ destructuring hints.
    private static int ParseHole(string hole, out string? format, out int alignment, out bool positional)
    {
        format = null;
        alignment = 0;

        int colon = hole.IndexOf(':');
        if (colon >= 0)
        {
            format = hole[(colon + 1)..];
            hole = hole[..colon];
        }

        int comma = hole.IndexOf(',');
        if (comma >= 0)
        {
            _ = int.TryParse(hole[(comma + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out alignment);
            hole = hole[..comma];
        }

        hole = hole.TrimStart('@', '$');
        positional = int.TryParse(hole, NumberStyles.None, CultureInfo.InvariantCulture, out int index);
        return positional ? index : -1;
    }

    private static string FormatArg(object? arg, string? format) => arg switch
    {
        null => "null",
        IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
        _ => arg.ToString() ?? "",
    };
}
