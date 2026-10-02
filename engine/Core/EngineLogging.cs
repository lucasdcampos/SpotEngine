using System.Collections.Concurrent;
using Serilog;
using Serilog.Events;

namespace Spot.Core;

/// <summary>
/// The engine's logging setup: routes <see cref="Log"/> to the terminal and a rolling log file through Serilog,
/// plus an optional extra sink (the editor and runtime pass the developer console's).
/// </summary>
/// <remarks>
/// The framework's <see cref="Log"/> works without this (it writes to the terminal by default); the engine
/// layers persistent logs on top so a bad session or a shipped-build crash leaves something to diagnose.
/// </remarks>
public static class EngineLogging
{
    private static SerilogLogSink? s_serilog;
    private static ILogSink? s_extra;

    /// <summary>
    /// Replaces <see cref="Log"/>'s sinks with the engine's: Serilog to the terminal and a rolling file, and
    /// <paramref name="extraSink"/> when given. Safe to call again; the previous engine sinks are closed first.
    /// </summary>
    /// <param name="extraSink">An optional additional sink (for example the developer console's).</param>
    /// <param name="logDirectory">
    /// Where to write the rolling log file. Defaults to a <c>logs/</c> folder next to the running executable;
    /// file logging is skipped (terminal only) if the directory can't be created.
    /// </param>
    public static void Init(ILogSink? extraSink = null, string? logDirectory = null)
    {
        CloseAndFlush();

        s_serilog = new SerilogLogSink(logDirectory);
        s_extra = extraSink;

        Log.ClearSinks();
        Log.AddSink(s_serilog);
        if (extraSink is not null)
        {
            Log.AddSink(extraSink);
        }
    }

    /// <summary>
    /// Flushes and closes the engine sinks (so the final lines reach the log file) and restores
    /// <see cref="Log"/>'s default terminal sink, so logging keeps working after shutdown.
    /// </summary>
    public static void CloseAndFlush()
    {
        if (s_serilog is null && s_extra is null)
        {
            return;
        }

        if (s_serilog is not null)
        {
            Log.RemoveSink(s_serilog);
            s_serilog.Dispose();
            s_serilog = null;
        }

        if (s_extra is not null)
        {
            Log.RemoveSink(s_extra);
            s_extra = null;
        }

        if (Log.Sinks.Count == 0)
        {
            Log.ResetToDefault();
        }
    }

    // Forwards Log entries to Serilog: Verbose and up to the terminal, Information and up to a daily rolling file.
    private sealed class SerilogLogSink : ILogSink, IDisposable
    {
        private const string OutputTemplate = "[{Timestamp:HH:mm:ss}] {Name}: {Message:l}{NewLine}{Exception}";

        private readonly Serilog.Core.Logger _logger;
        private readonly ConcurrentDictionary<string, ILogger> _bySource = new();

        public SerilogLogSink(string? logDirectory)
        {
            LoggerConfiguration configuration = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(LogEventLevel.Verbose, OutputTemplate);

            string? logFile = ResolveLogFile(logDirectory);
            if (logFile is not null)
            {
                // Bounded by daily rolling, a 50 MB size cap and a 7-file retention limit; shared so every source
                // appends to one chronological file. The file skips per-frame trace spam.
                configuration = configuration.WriteTo.File(
                    logFile,
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: OutputTemplate,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 50L * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 7,
                    shared: true);
            }

            _logger = configuration.CreateLogger();
        }

        public void Write(in LogEntry entry)
        {
            ILogger logger = _bySource.GetOrAdd(entry.Source, source => _logger.ForContext("Name", source));
            logger.Write(ToSerilog(entry.Level), "{Text:l}", entry.Message);
        }

        public void Dispose() => _logger.Dispose();

        private static LogEventLevel ToSerilog(LogLevel level) => level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Info => LogEventLevel.Information,
            LogLevel.Warn => LogEventLevel.Warning,
            _ => LogEventLevel.Error,
        };

        // Resolves the rolling log-file path, creating its directory. Returns null (file logging disabled) when
        // the directory can't be created: logging must never take the process down.
        private static string? ResolveLogFile(string? logDirectory)
        {
            try
            {
                string dir = logDirectory ?? Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "spot.log");
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"Spot: file logging disabled (could not prepare log directory): {ex.Message}");
                return null;
            }
        }
    }
}
