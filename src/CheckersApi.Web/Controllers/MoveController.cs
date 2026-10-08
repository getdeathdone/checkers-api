using CheckersApi.Core.Logic;
using CheckersApi.Core.Models;
using CheckersApi.Web.Configuration;
using CheckersApi.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CheckersApi.Web.Controllers;

[ApiController]
[Route("v1/move")]
public class MoveController : ControllerBase
{
    private readonly ICheckersService _checkersService;
    private readonly LimitsOptions _limitsOptions;
    private readonly ILogger<MoveController> _logger;

    public MoveController(
        ICheckersService checkersService,
        IOptions<LimitsOptions> limitsOptions,
        ILogger<MoveController> logger)
    {
        _checkersService = checkersService;
        _limitsOptions = limitsOptions.Value;
        _logger = logger;
    }

    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest([FromBody] MoveSuggestRequest request)
    {
        if (request == null || request.State == null || string.IsNullOrWhiteSpace(request.State.Position))
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                error = "State position is required."
            });
        }

        int hardTimeMs = request.Limits?.HardTimeMs ?? _limitsOptions.DefaultHardTimeMs;
        using var hardTimeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(hardTimeMs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, hardTimeoutCts.Token);

        try
        {
            var response = await _checkersService.SuggestMoveAsync(request, linkedCts.Token);
            return Ok(response);
        }
        catch (PdnParseException ex)
        {
            _logger.LogWarning("Invalid PDN received: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { error = ex.Message });
        }
        catch (OperationCanceledException) when (hardTimeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("Request exceeded hardTimeMs limit of {HardTimeMs}ms", hardTimeMs);
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                error = $"Search exceeded hard timeout of {hardTimeMs}ms."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Engine error during move search.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = ex.Message
            });
        }
    }

    [HttpPost("validate")]
    public IActionResult Validate([FromBody] MoveValidateRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Position))
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                error = "Position is required."
            });
        }

        try
        {
            var result = _checkersService.ValidateMove(request);
            return Ok(result);
        }
        catch (PdnParseException ex)
        {
            _logger.LogWarning("Invalid PDN for move validation: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during move validation.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}
