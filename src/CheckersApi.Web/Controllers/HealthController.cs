using CheckersApi.Core.Engine;
using CheckersApi.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace CheckersApi.Web.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    private readonly IEngineWorkerPool _workerPool;

    public HealthController(IEngineWorkerPool workerPool)
    {
        _workerPool = workerPool;
    }

    [HttpGet("healthz")]
    public IActionResult HealthCheck()
    {
        var response = new HealthResponse
        {
            Ok = _workerPool.IsHealthy,
            Workers = _workerPool.WorkerCount
        };

        return Ok(response);
    }
}
