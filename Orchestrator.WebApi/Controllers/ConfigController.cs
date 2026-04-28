using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Models;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly IOptionsMonitor<OrchestratorConfig> _config;
    private readonly string _configPath;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ConfigController(
        IOptionsMonitor<OrchestratorConfig> config,
        IWebHostEnvironment env)
    {
        _config = config;
        _configPath = Path.Combine(env.ContentRootPath, "orchestrator.json");
    }

    // GET /api/config
    [HttpGet]
    public OrchestratorConfig Get() => _config.CurrentValue;

    // PUT /api/config/services/{name}
    [HttpPut("services/{name}")]
    public IActionResult UpdateService(string name, [FromBody] ServiceConfig updated)
    {
        // Take a working copy so we don't mutate the live config object directly
        var current = _config.CurrentValue;
        current.Services[name] = updated;

        // Persist atomically: write to temp file then rename
        var tmp = _configPath + ".tmp";
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(current, WriteOptions);
            System.IO.File.WriteAllBytes(tmp, json);
            System.IO.File.Move(tmp, _configPath, overwrite: true);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Failed to persist configuration: {ex.Message}");
        }
        finally
        {
            if (System.IO.File.Exists(tmp))
                System.IO.File.Delete(tmp);
        }

        return NoContent();
    }
}
