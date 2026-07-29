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
    public static void Init(ILogEventSink? extraSink = null)
    {
        s_coreLogger = CreateLogger("SPOT", extraSink);
        s_clientLogger = CreateLogger("APP", extraSink);
    }

    private static ILogger CreateLogger(string name, ILogEventSink? extraSink)
    {
        LoggerConfiguration configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithProperty("Name", name)
            .WriteTo.Console(LogEventLevel.Verbose, OutputTemplate);

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
