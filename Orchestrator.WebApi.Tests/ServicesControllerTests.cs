using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Orchestrator.Core;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Xunit;

namespace Orchestrator.WebApi.Tests
{
    /// <summary>
    /// Integration tests for the Orchestrator.WebApi controllers using WebApplicationFactory.
    /// </summary>
    public class ServicesControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly Mock<IProcessSupervisor> _supervisorMock;
        private readonly Mock<IIpcServer> _ipcMock;
        private readonly HttpClient _client;

        public ServicesControllerTests(WebApplicationFactory<Program> factory)
        {
            _supervisorMock = new Mock<IProcessSupervisor>();
            _ipcMock = new Mock<IIpcServer>();

            // Set up sensible defaults
            _supervisorMock.Setup(s => s.ListStatusAsync())
                .ReturnsAsync(new List<ServiceStatus>
                {
                    new ServiceStatus { Name = "TestSvc", RunningInstances = 1, State = State.Running }
                });
            _ipcMock.Setup(i => i.GetLatestStatuses(It.IsAny<string>()))
                .Returns(new List<WorkerStatus>());
            _ipcMock.Setup(i => i.GetInternalStatuses())
                .Returns(new List<InternalStatus>());
            _ipcMock.Setup(i => i.ReportStatus(It.IsAny<WorkerStatus>()))
                .Returns(Task.CompletedTask);

            var cfg = new OrchestratorConfig
            {
                Web = new WebConfig { ApiPort = 5001, BindIP = "127.0.0.1" },
                Global = new GlobalConfig { HealthCheckInterval = 10000 }
            };

            _client = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((ctx, cfgBuilder) =>
                {
                    cfgBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Web:ApiPort"] = "5001",
                        ["Web:BindIP"] = "127.0.0.1",
                        ["Web:StreamBufferSize"] = "8192",
                        ["Web:UiPort"] = "5000",
                        ["Global:HealthCheckInterval"] = "10000",
                        ["Scheduling:DemandThreshold"] = "80"
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(_supervisorMock.Object);
                    services.AddSingleton(_ipcMock.Object);
                });
            }).CreateClient();
        }

        [Fact]
        public async Task GetAll_ReturnsOk_WithServiceList()
        {
            var response = await _client.GetAsync("/api/services");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("TestSvc", body);
        }

        [Fact]
        public async Task GetWorkerStatuses_ReturnsOk()
        {
            var response = await _client.GetAsync("/api/services/TestSvc/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Start_CallsSupervisorStartAsync()
        {
            _supervisorMock.Setup(s => s.StartAsync("TestSvc", 1))
                .Returns(Task.CompletedTask);

            var response = await _client.PostAsync("/api/services/TestSvc/start", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _supervisorMock.Verify(s => s.StartAsync("TestSvc", 1), Times.Once);
        }

        [Fact]
        public async Task Stop_CallsSupervisorStopAsync()
        {
            _supervisorMock.Setup(s => s.StopAsync("TestSvc", 1))
                .Returns(Task.CompletedTask);

            var response = await _client.PostAsync("/api/services/TestSvc/stop", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _supervisorMock.Verify(s => s.StopAsync("TestSvc", 1), Times.Once);
        }

        [Fact]
        public async Task GetInternalStatuses_ReturnsOk()
        {
            var response = await _client.GetAsync("/api/services/internal");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task GetConfig_ReturnsOk()
        {
            var response = await _client.GetAsync("/api/config");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task MetricsEndpoint_ReturnsPrometheusFormat()
        {
            var response = await _client.GetAsync("/metrics");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("orchestrator_running_instances", body);
        }
    }
}
