using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Core.Models
{
    public class Envelope
    {
        /// <summary>
        /// Parameterless constructor for JSON deserialization.
        /// </summary>
        public Envelope() { }

        /// <summary>
        /// Full constructor for rehydrating from JSON.
        /// </summary>
        [JsonConstructor]
        public Envelope(
            string topic,
            string payloadType,
            JsonElement payload,
            DateTimeOffset timestamp)
        {
            Topic = topic;
            PayloadType = payloadType;
            Payload = payload;
            Timestamp = timestamp;
        }

        /// <summary>
        /// Creates a new envelope from a typed payload.
        /// </summary>
        public Envelope(string topic, object payload)
        {
            Topic = topic;
            Payload = JsonSerializer.SerializeToElement(payload);
            PayloadType = payload.GetType().FullName ?? "System.Object";
            Timestamp = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// Creates a new envelope from a raw JSON payload.
        /// </summary>
        public Envelope(string topic, string payloadType, JsonElement payload)
        {
            Topic = topic;
            PayloadType = payloadType;
            Payload = payload;
            Timestamp = DateTimeOffset.UtcNow;
        }

        /// <summary>
        /// “Topic” or stream name—e.g. “WorkerStatus” or “MySubsystem”.
        /// </summary>
        public string Topic { get; set; } = default!;

        /// <summary>
        /// The CLR type name of the payload.
        /// </summary>
        public string PayloadType { get; set; } = default!;

        /// <summary>
        /// Payload in raw JSON form.
        /// </summary>
        public JsonElement Payload { get; set; }

        /// <summary>
        /// Timestamp indicating when this envelope was created.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Deserialize the payload to a specific type.
        /// </summary>
        public T GetPayload<T>()
        {
            if (Payload.ValueKind == JsonValueKind.Undefined || Payload.ValueKind == JsonValueKind.Null)
                throw new InvalidOperationException("Envelope payload is null or undefined.");

            return Payload.Deserialize<T>()!;
        }
    }
}
