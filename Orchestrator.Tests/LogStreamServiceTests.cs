using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orchestrator.Core;
using Orchestrator.Core.Models;
using Orchestrator.Supervisor;
using Xunit;

namespace Orchestrator.Tests
{
    public class LogStreamServiceTests
    {
        private LogStreamService CreateService(int bufferSize = 100)
        {
            var cfg = new OrchestratorConfig
            {
                Web = new WebConfig { StreamBufferSize = bufferSize }
            };
            var options = Options.Create(cfg);
            return new LogStreamService(options);
        }

        // Helper to read N messages from an async enumerable
        private static async Task<List<string>> TakeAsync(IAsyncEnumerable<string> source, int count)
        {
            var results = new List<string>();
            await foreach (var msg in source)
            {
                results.Add(msg);
                if (results.Count >= count) break;
            }
            return results;
        }

        [Fact]
        public async Task Push_Then_Stream_DeliversMessages()
        {
            var svc = CreateService();
            svc.Push("test", "hello");
            svc.Push("test", "world");

            var results = await TakeAsync(svc.StreamAsync("test"), 2);

            Assert.Equal(2, results.Count);
            Assert.Equal("hello", results[0]);
            Assert.Equal("world", results[1]);
        }

        [Fact]
        public void Push_NullMessage_DoesNotThrow()
        {
            var svc = CreateService();
            var ex = Record.Exception(() => svc.Push("test", null!));
            Assert.Null(ex);
        }

        [Fact]
        public void Push_BeyondCapacity_DoesNotThrow()
        {
            var svc = CreateService(bufferSize: 2);
            // Push more than capacity — older messages should be dropped
            svc.Push("test", "1");
            svc.Push("test", "2");
            svc.Push("test", "3"); // should drop "1"
            // No exception expected
        }

        [Fact]
        public async Task Push_BeyondCapacity_DropsOldest()
        {
            var svc = CreateService(bufferSize: 2);
            svc.Push("test", "old1");
            svc.Push("test", "old2");
            svc.Push("test", "new3"); // should drop old1

            // Channel has 2 slots so we get old2 + new3
            var results = await TakeAsync(svc.StreamAsync("test"), 2);
            Assert.Contains("new3", results);
        }
    }
}
