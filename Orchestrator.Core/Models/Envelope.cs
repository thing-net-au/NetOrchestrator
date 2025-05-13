using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Orchestrator.Core.Models
{
    public class Envelope
    {
        /// <summary>“Topic” or stream name—e.g. “WorkerStatus” or “MySubsystem”.</summary>
        public string Topic { get; set; } = default!;
        /// <summary>The CLR type name (assembly qualified optional) of the payload.</summary>
        public string Type { get; set; } = default!;
        /// <summary>Payload in JSON form.</summary>
        public JsonElement Payload { get; set; }
        /// <summary>When we stamped this envelope.</summary>
        public DateTimeOffset Timestamp { get; set; }

    }
    public class ConsoleLogMessage
    {
        public string process { get; set; }
        public int processId { get; set; }
        public string message { get; set; }
    }

        public class Envelope<T>
    {
        public string Topic { get; set; } = default!;
        public string Type => typeof(T).FullName!;
        public T Payload { get; set; } = default!;
        public DateTimeOffset Timestamp { get; set; }
    }
}
