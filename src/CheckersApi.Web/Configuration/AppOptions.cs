namespace CheckersApi.Web.Configuration;

public class EngineOptions
{
    public const string SectionName = "Engine";

    public string Type { get; set; } = "chinook";
    public string Path { get; set; } = @"C:\engines\chinook\chinook.exe";
    public int Workers { get; set; } = 2;
    public string Databases { get; set; } = @"D:\tb\chinook";
}

public class CacheOptions
{
    public const string SectionName = "Cache";

    public int Capacity { get; set; } = 20000;
    public int TtlMinutes { get; set; } = 15;
}

public class LimitsOptions
{
    public const string SectionName = "Limits";

    public int DefaultSoftTimeMs { get; set; } = 300;
    public int DefaultHardTimeMs { get; set; } = 1200;
}
