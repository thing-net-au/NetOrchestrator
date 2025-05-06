using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Orchestrator.Core;
using Orchestrator.Core.Models;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly OrchestratorConfig _orchestratorConfig;

    public ConfigController(IConfiguration configuration)
    {
        _configuration = configuration;
        _orchestratorConfig = OrchestratorConfig.Current!;
    }

    // GET /api/config
    [HttpGet]
    public OrchestratorConfig Get() => _orchestratorConfig;

    // PUT /api/config/services/{name}
    [HttpPut("services/{name}")]
    public IActionResult UpdateService(string name, [FromBody] ServiceConfig updated)
    {
        _orchestratorConfig.Services[name] = updated;
        // You’d also need to persist back to orchestrator.json on disk.
        return NoContent();
    }
}
