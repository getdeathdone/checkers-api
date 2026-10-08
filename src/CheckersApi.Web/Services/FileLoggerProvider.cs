namespace CheckersApi.Web.Services;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly List<string> _logDirectories = new();
    private static readonly object LockObj = new();
    private static readonly List<string> GlobalDirectories = new();

    public FileLoggerProvider(params string[] directories)
    {
        foreach (var dir in directories)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                Directory.CreateDirectory(dir);
                if (!_logDirectories.Contains(dir))
                {
                    _logDirectories.Add(dir);
                }
            }
            catch
            {
                // Ignore folder creation errors
            }
        }

        if (_logDirectories.Count == 0)
        {
            try
            {
                string tempLogs = Path.Combine(Path.GetTempPath(), "CheckersApi_logs");
                Directory.CreateDirectory(tempLogs);
                _logDirectories.Add(tempLogs);
            }
            catch { }
        }

        lock (LockObj)
        {
            foreach (var d in _logDirectories)
            {
                if (!GlobalDirectories.Contains(d))
                {
                    GlobalDirectories.Add(d);
                }
            }
        }
    }

    public static void WriteRawLog(string fileName, string content)
    {
        var dirs = new List<string>();
        lock (LockObj)
        {
            dirs.AddRange(GlobalDirectories);
        }

        // Always also attempt AppContext.BaseDirectory and CurrentDirectory
        try
        {
            string baseLogs = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!dirs.Contains(baseLogs)) dirs.Add(baseLogs);
        }
        catch { }

        try
        {
            string curLogs = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (!dirs.Contains(curLogs)) dirs.Add(curLogs);
        }
        catch { }

        try
        {
            string tempLogs = Path.Combine(Path.GetTempPath(), "CheckersApi_logs");
            if (!dirs.Contains(tempLogs)) dirs.Add(tempLogs);
        }
        catch { }

        lock (LockObj)
        {
            foreach (var dir in dirs)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string target = Path.Combine(dir, fileName);
                    File.AppendAllText(target, content + Environment.NewLine);
                }
                catch
                {
                    // Fail silently for inaccessible directory
                }
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _logDirectories);
    }

    public void Dispose() { }

    private class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly List<string> _dirs;

        public FileLogger(string category, List<string> dirs)
        {
            _category = category;
            _dirs = dirs;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string line = $"[{timestamp}] [{logLevel}] [{_category}] {message}";

            if (exception != null)
            {
                line += Environment.NewLine + exception.ToString();
            }

            string fileName = $"app-{DateTime.UtcNow:yyyyMMdd}.log";

            lock (LockObj)
            {
                foreach (var dir in _dirs)
                {
                    try
                    {
                        string filePath = Path.Combine(dir, fileName);
                        File.AppendAllText(filePath, line + Environment.NewLine);
                    }
                    catch
                    {
                        // Ignore individual write failure
                    }
                }
            }
        }
    }
}
