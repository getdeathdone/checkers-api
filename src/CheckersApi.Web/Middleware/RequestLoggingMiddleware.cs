using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CheckersApi.Web.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Request-Id"] = requestId;

        var path = context.Request.Path.Value ?? "";
        bool isMoveSuggest = path.Equals("/v1/move/suggest", StringComparison.OrdinalIgnoreCase);

        if (!isMoveSuggest)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[{RequestId}] HTTP {Method} {Path} failed after {TimeMs}ms",
                    requestId, context.Request.Method, path, sw.ElapsedMilliseconds);
                throw;
            }

            sw.Stop();
            // Avoid flooding logs with static assets, but log all API calls and non-200 responses
            if (path.StartsWith("/v1/") || path.Equals("/healthz", StringComparison.OrdinalIgnoreCase) || context.Response.StatusCode >= 400)
            {
                _logger.LogInformation("[{RequestId}] HTTP {Method} {Path} responded {StatusCode} in {TimeMs}ms",
                    requestId, context.Request.Method, path, context.Response.StatusCode, sw.ElapsedMilliseconds);
            }
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[{RequestId}] HTTP POST /v1/move/suggest failed after {TimeMs}ms",
                requestId, sw.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            sw.Stop();
            responseBody.Seek(0, SeekOrigin.Begin);
            string responseText = await new StreamReader(responseBody).ReadToEndAsync();
            responseBody.Seek(0, SeekOrigin.Begin);

            long nodes = 0;
            int depth = 0;
            bool tablebaseHit = false;

            if (context.Response.StatusCode == StatusCodes.Status200OK && !string.IsNullOrWhiteSpace(responseText))
            {
                try
                {
                    var json = JsonNode.Parse(responseText);
                    if (json != null)
                    {
                        depth = json["depth"]?.GetValue<int>() ?? 0;
                        nodes = json["nodes"]?.GetValue<long>() ?? 0;
                        tablebaseHit = json["info"]?["tablebaseHit"]?.GetValue<bool>() ?? false;
                    }
                }
                catch
                {
                    // Ignore parse error in logging
                }
            }

            var logEntry = new
            {
                requestId,
                timeMs = sw.ElapsedMilliseconds,
                depth,
                nodes,
                tablebaseHit,
                statusCode = context.Response.StatusCode,
                path
            };

            _logger.LogInformation("{RequestLogJson}", JsonSerializer.Serialize(logEntry));

            await responseBody.CopyToAsync(originalBodyStream);
        }
    }
}
