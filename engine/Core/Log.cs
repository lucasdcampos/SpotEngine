using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Spot.Core;

/// <summary>
/// Provides the engine (core) and client (application) loggers.
/// </summary>
public static class Log
{
    private const string OutputTemplate = "[{Timestamp:HH:mm:ss}] {Name}: {Message:l}{NewLine}{Exception}";

    private static ILogger? s_coreLogger;
    private static ILogger? s_clientLogger;

    /// <summary>
    /// Gets the engine (core) logger.
    /// </summary>
    public static ILogger CoreLogger =>
        s_coreLogger ?? throw new InvalidOperationException("Log has not been initialized. Call Log.Init() first.");

    /// <summary>
    /// Gets the client (application) logger.
    /// </summary>
    public static ILogger ClientLogger =>
        s_clientLogger ?? throw new InvalidOperationException("Log has not been initialized. Call Log.Init() first.");

    /// <summary>
    /// Initializes the core and client loggers.
    /// </summary>
    /// <param name="extraSink">An optional additional sink both loggers write to.</param>
    /// <param name="logDirectory">
    /// Where to write the rolling log file. Defaults to a <c>logs/</c> folder next to the running
    /// executable. Pass <c>null</c> for the default; file logging is skipped if the directory can't be created.
    /// </param>
    public static void Init(ILogEventSink? extraSink = null, string? logDirectory = null)
    {
        string? logFile = ResolveLogFile(logDirectory);

        s_coreLogger = CreateLogger("SPOT", extraSink, logFile);
        s_clientLogger = CreateLogger("APP", extraSink, logFile);

        if (logFile is not null)
        {
            s_coreLogger.Information("Logging to '{LogFile}'", logFile);
        }
    }

    /// <summary>
    /// Flushes and releases the loggers, closing the rolling log file. Called on application shutdown so
    /// the final lines reach disk. After this, logging is disabled until <see cref="Init"/> is called again.
    /// </summary>
    public static void CloseAndFlush()
    {
        (s_coreLogger as IDisposable)?.Dispose();
        (s_clientLogger as IDisposable)?.Dispose();
        s_coreLogger = null;
        s_clientLogger = null;
    }

    // Resolves the rolling log-file path, creating its directory. Returns null (file logging disabled) when
    // the directory can't be created: logging must never take the process down, so we fall back to console.
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

    private static ILogger CreateLogger(string name, ILogEventSink? extraSink, string? logFile)
    {
        LoggerConfiguration configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithProperty("Name", name)
            .WriteTo.Console(LogEventLevel.Verbose, OutputTemplate);

        if (logFile is not null)
        {
            // Persist Information and above to a daily rolling file so a bad session or a shipped-build crash
            // leaves something to diagnose once the process is gone. The console/in-app console stay Verbose
            // for live work; the file skips per-frame trace spam. Bounded by daily rolling, a 50 MB size cap
            // and a 7-file retention limit, and shared so the core and client loggers append to one
            // chronological file.
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

        if (extraSink is not null)
        {
            configuration = configuration.WriteTo.Sink(extraSink);
        }

        return configuration.CreateLogger();
    }

    public static void CoreTrace(string message, params object?[] args) => CoreLogger.Verbose(message, args);
    public static void CoreInfo(string message, params object?[] args) => CoreLogger.Information(message, args);
    public static void CoreWarn(string message, params object?[] args) => CoreLogger.Warning(message, args);
    public static void CoreError(string message, params object?[] args) => CoreLogger.Error(message, args);

    public static void Trace(string message, params object?[] args) => ClientLogger.Verbose(message, args);
    public static void Info(string message, params object?[] args) => ClientLogger.Information(message, args);
    public static void Warn(string message, params object?[] args) => ClientLogger.Warning(message, args);
    public static void Error(string message, params object?[] args) => ClientLogger.Error(message, args);
}
