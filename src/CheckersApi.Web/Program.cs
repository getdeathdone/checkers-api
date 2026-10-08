using CheckersApi.Core.Cache;
using CheckersApi.Core.Engine;
using CheckersApi.Core.Models;
using CheckersApi.Web.Configuration;
using CheckersApi.Web.Middleware;
using CheckersApi.Web.Services;

// Global unhandled exception handlers for non-UI background threads and tasks
AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
{
    string report = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff UTC}] FATAL UNHANDLED EXCEPTION:{Environment.NewLine}{eventArgs.ExceptionObject}{Environment.NewLine}";
    try
    {
        FileLoggerProvider.WriteRawLog("startup_error.log", report);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(report);
        Console.ResetColor();
    }
    catch { }
};

TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
{
    string report = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff UTC}] UNOBSERVED TASK EXCEPTION:{Environment.NewLine}{eventArgs.Exception}{Environment.NewLine}";
    try
    {
        FileLoggerProvider.WriteRawLog("startup_error.log", report);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(report);
        Console.ResetColor();
    }
    catch { }
    eventArgs.SetObserved();
};

string baseLogDir = Path.Combine(AppContext.BaseDirectory, "logs");
string curLogDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Logging configuration: Console + Persistent File Logger
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddProvider(new FileLoggerProvider(baseLogDir, curLogDir));
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Options configuration
    var engineSection = builder.Configuration.GetSection(EngineOptions.SectionName);
    var cacheSection = builder.Configuration.GetSection(CacheOptions.SectionName);
    var limitsSection = builder.Configuration.GetSection(LimitsOptions.SectionName);

    builder.Services.Configure<EngineOptions>(engineSection);
    builder.Services.Configure<CacheOptions>(cacheSection);
    builder.Services.Configure<LimitsOptions>(limitsSection);

    var engineOptions = engineSection.Get<EngineOptions>() ?? new EngineOptions();
    var cacheOptions = cacheSection.Get<CacheOptions>() ?? new CacheOptions();

    // Cache registration (LRU Memory Cache)
    builder.Services.AddSingleton<ILruCache<string, MoveSuggestResponse>>(_ =>
        new MemoryLruCache<string, MoveSuggestResponse>(
            capacity: cacheOptions.Capacity,
            ttl: TimeSpan.FromMinutes(cacheOptions.TtlMinutes)));

    // Engine Worker Pool registration
    builder.Services.AddSingleton<IEngineWorkerPool>(_ =>
    {
        int workers = engineOptions.Workers > 0 ? engineOptions.Workers : 2;
        return new EngineWorkerPool(workers, workerId =>
        {
            return new KingsRowCliAdapter(engineOptions.Path, engineOptions.Databases);
        });
    });

    // Warmup Hosted Service (creates and warms up workers on startup)
    builder.Services.AddHostedService<WorkerPoolWarmupService>();

    // Checkers Business Service
    builder.Services.AddSingleton<ICheckersService, CheckersService>();

    builder.Services.AddControllers();

    var app = builder.Build();

    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("==================================================");
    logger.LogInformation("Checkers REST Web API is starting up");
    logger.LogInformation("Base Directory: {BaseDir}", AppContext.BaseDirectory);
    logger.LogInformation("Process ID: {Pid} | .NET: {DotNetVersion}", Environment.ProcessId, Environment.Version);
    logger.LogInformation("Log Directory: {LogDir}", baseLogDir);
    logger.LogInformation("==================================================");

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        logger.LogInformation("Checkers REST Web API successfully started and listening for requests.");
    });
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        logger.LogInformation("Checkers REST Web API is stopping...");
    });
    app.Lifetime.ApplicationStopped.Register(() =>
    {
        logger.LogInformation("Checkers REST Web API stopped.");
    });

    // Request logging middleware
    app.UseMiddleware<RequestLoggingMiddleware>();

    // Serve interactive UI from wwwroot
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseRouting();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    string crashReport = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff UTC}] CRITICAL STARTUP ERROR:{Environment.NewLine}{ex}{Environment.NewLine}";

    try
    {
        FileLoggerProvider.WriteRawLog("startup_error.log", crashReport);
    }
    catch { }

    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(crashReport);
    Console.ResetColor();

    throw;
}
