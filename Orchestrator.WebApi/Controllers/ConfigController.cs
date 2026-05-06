using Microsoft.AspNetCore.Mvc;
using Orchestrator.Core;
using Orchestrator.Core.Models;

namespace Orchestrator.WebApi.Controllers
{
    [ApiController]
    [Route("api/config")]
    public class ConfigController : ControllerBase
    {
        private readonly OrchestratorConfig _orchestratorConfig;

        public ConfigController()
        {
            _orchestratorConfig = OrchestratorConfig.Current;
        }

        [HttpGet]
        public ActionResult<OrchestratorConfig> Get()
        {
            if (_orchestratorConfig == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Configuration is not initialized.");
            }

            return _orchestratorConfig;
        }

        [HttpPut("services/{name}")]
        public IActionResult UpdateService(string name, [FromBody] ServiceConfig updated)
        {
            if (_orchestratorConfig == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Configuration is not initialized.");
            }

            if (string.IsNullOrWhiteSpace(name) || updated == null)
            {
                return BadRequest("Service name and payload are required.");
            }

            if (updated.MinInstances < 0 || updated.MaxInstances < updated.MinInstances)
            {
                return BadRequest("Invalid instance bounds.");
            }

            updated.Name = name;
            _orchestratorConfig.Services[name] = updated;

            return Accepted(new
            {
                message = "Runtime configuration updated in memory only. Persisting orchestrator.json is not yet implemented.",
                service = name
            });
        }
    }
}
