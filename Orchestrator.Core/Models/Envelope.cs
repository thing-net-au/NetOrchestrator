using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Orchestrator.Core.Models
{
    public class Envelope
    {
        // parameterless ctor for deserialization
        public Envelope() { }
        [JsonConstructor]
        public Envelope(
            string Topic,
            string Type,
            JsonElement Payload,
            DateTimeOffset Timestamp)
        {
            this.Topic = Topic;
            this.Type = Type;
            this.Payload = Payload;
            this.Timestamp = Timestamp;
        }

        public Envelope(string topic, object payload)
        {
            Topic = topic;
            Payload = JsonSerializer.SerializeToElement(payload);
            Type = payload.GetType().FullName!;
            Timestamp = DateTimeOffset.UtcNow;
        }
        public Envelope(string topic, string type, JsonElement payload)
        {
            Topic = topic;
            Payload = JsonSerializer.SerializeToElement(payload);
            Type = type;
            Timestamp = DateTimeOffset.UtcNow;
        }

        /// <summary>“Topic” or stream name—e.g. “WorkerStatus” or “MySubsystem”.</summary>
        public string Topic { get; set; } = default!;

        /// <summary>The CLR type name of the payload.</summary>
        public string Type { get; set; } = default!;

        /// <summary>Payload in JSON form.</summary>
        public JsonElement Payload { get; set; }

        /// <summary>When we stamped this envelope.</summary>
        public DateTimeOffset Timestamp { get; set; }
    }

}
