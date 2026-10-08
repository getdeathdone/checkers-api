using System.Text.Json.Serialization;

namespace CheckersApi.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DifficultyLevel
{
    [JsonPropertyName("weak")]
    Weak,

    [JsonPropertyName("medium")]
    Medium,

    [JsonPropertyName("strong")]
    Strong
}

public class SearchLimits
{
    [JsonPropertyName("maxDepth")]
    public int? MaxDepth { get; set; }

    [JsonPropertyName("softTimeMs")]
    public int? SoftTimeMs { get; set; }

    [JsonPropertyName("hardTimeMs")]
    public int? HardTimeMs { get; set; }
}

public class GameStateInput
{
    [JsonPropertyName("notation")]
    public string Notation { get; set; } = "PDN";

    [JsonPropertyName("position")]
    public string Position { get; set; } = string.Empty;
}

public class MoveSuggestRequest
{
    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = "checkers-8x8";

    [JsonPropertyName("state")]
    public GameStateInput State { get; set; } = new();

    [JsonPropertyName("level")]
    public string Level { get; set; } = "medium";

    [JsonPropertyName("limits")]
    public SearchLimits? Limits { get; set; }
}

public class MoveSuggestInfo
{
    [JsonPropertyName("tablebaseHit")]
    public bool TablebaseHit { get; set; }

    [JsonPropertyName("timeMs")]
    public long TimeMs { get; set; }
}

public class MoveSuggestResponse
{
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "chinook";

    [JsonPropertyName("bestMove")]
    public string BestMove { get; set; } = string.Empty;

    [JsonPropertyName("pv")]
    public List<string> Pv { get; set; } = new();

    [JsonPropertyName("scoreOrWDL")]
    public int ScoreOrWdl { get; set; }

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("nodes")]
    public long Nodes { get; set; }

    [JsonPropertyName("positionKey")]
    public string PositionKey { get; set; } = string.Empty;

    [JsonPropertyName("info")]
    public MoveSuggestInfo Info { get; set; } = new();
}

public class MoveValidateRequest
{
    [JsonPropertyName("position")]
    public string Position { get; set; } = string.Empty;

    [JsonPropertyName("move")]
    public string Move { get; set; } = string.Empty;
}

public class MoveValidateResponse
{
    [JsonPropertyName("legal")]
    public bool Legal { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

public class HealthResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("workers")]
    public int Workers { get; set; }
}
