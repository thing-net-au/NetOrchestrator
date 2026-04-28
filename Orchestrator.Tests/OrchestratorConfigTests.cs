using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Models;
using Xunit;

namespace Orchestrator.Tests
{
    public class OrchestratorConfigTests
    {
        [Fact]
        public void Config_BindsFromDictionary()
        {
            var data = new Dictionary<string, string?>
            {
                ["Services:Svc1:Name"] = "Svc1",
                ["Services:Svc1:ExecutablePath"] = "apps/svc1.dll",
                ["Services:Svc1:Arguments"] = "--env=test",
                ["Services:Svc1:MinInstances"] = "1",
                ["Services:Svc1:MaxInstances"] = "5",
                ["Services:Svc1:SchedulePolicy:Type"] = "steady",
                ["Global:HealthCheckInterval"] = "5000",
                ["Global:LoggingLevel"] = "Debug",
                ["Scheduling:DemandThreshold"] = "70",
                ["Web:ApiPort"] = "5001",
                ["Web:UiPort"] = "5000",
                ["Web:BindIP"] = "127.0.0.1",
                ["Web:StreamBufferSize"] = "4096",
                ["Web:ApiBaseUrl"] = "http://127.0.0.1:5001"
            };

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(data)
                .Build();

            var cfg = config.Get<OrchestratorConfig>()!;

            Assert.NotNull(cfg);
            Assert.Single(cfg.Services);
            Assert.True(cfg.Services.ContainsKey("Svc1"));
            Assert.Equal("Svc1", cfg.Services["Svc1"].Name);
            Assert.Equal(1, cfg.Services["Svc1"].MinInstances);
            Assert.Equal(5, cfg.Services["Svc1"].MaxInstances);
            Assert.Equal("steady", cfg.Services["Svc1"].SchedulePolicy.Type);
            Assert.Equal(5000, cfg.Global.HealthCheckInterval);
            Assert.Equal("Debug", cfg.Global.LoggingLevel);
            Assert.Equal(70, cfg.Scheduling.DemandThreshold);
            Assert.Equal(5001, cfg.Web.ApiPort);
            Assert.Equal("127.0.0.1", cfg.Web.BindIP);
            Assert.Equal(4096, cfg.Web.StreamBufferSize);
            Assert.Equal("http://127.0.0.1:5001", cfg.Web.ApiBaseUrl);
        }

        [Fact]
        public void WebConfig_DefaultBindIP_Is_Loopback()
        {
            var web = new WebConfig();
            Assert.Equal("127.0.0.1", web.BindIP);
        }

        [Fact]
        public void WebConfig_ApiBaseUrl_IsNullable()
        {
            var web = new WebConfig();
            Assert.Null(web.ApiBaseUrl);
        }

        [Fact]
        public void OrchestratorConfig_DefaultProperties_NotNull()
        {
            var cfg = new OrchestratorConfig();
            Assert.NotNull(cfg.Services);
            Assert.NotNull(cfg.Global);
            Assert.NotNull(cfg.Scheduling);
            Assert.NotNull(cfg.Web);
        }
    }
}
