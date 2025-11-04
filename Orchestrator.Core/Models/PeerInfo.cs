using System;
using System.Net;
using System.Text.Json.Serialization;

namespace Orchestrator.Core.Models
{
    /// <summary>
    /// Represents information about a peer Orchestrator instance in the network
    /// </summary>
    public class PeerInfo
    {
        /// <summary>Unique identifier for this peer</summary>
        [JsonPropertyName("peerId")]
        public string PeerId { get; set; } = Guid.NewGuid().ToString();

 /// <summary>Hostname of the peer</summary>
        [JsonPropertyName("hostname")]
     public string Hostname { get; set; } = Environment.MachineName;

     /// <summary>IP address of the peer</summary>
 [JsonPropertyName("ipAddress")]
        public string IpAddress { get; set; } = string.Empty;

 /// <summary>API port for this peer</summary>
        [JsonPropertyName("apiPort")]
        public int ApiPort { get; set; }

        /// <summary>IPC port for this peer</summary>
     [JsonPropertyName("ipcPort")]
        public int IpcPort { get; set; }

      /// <summary>When this peer was last seen</summary>
      [JsonPropertyName("lastSeen")]
        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>Number of services managed by this peer</summary>
        [JsonPropertyName("serviceCount")]
   public int ServiceCount { get; set; }

        /// <summary>Total running instances across all services</summary>
        [JsonPropertyName("totalInstances")]
        public int TotalInstances { get; set; }

        /// <summary>Peer status (Online, Offline, Unknown)</summary>
     [JsonPropertyName("status")]
        public PeerStatus Status { get; set; } = PeerStatus.Unknown;

        /// <summary>Version of the Orchestrator software</summary>
        [JsonPropertyName("version")]
      public string Version { get; set; } = "1.0.0";

  /// <summary>Custom metadata for this peer</summary>
        [JsonPropertyName("metadata")]
     public Dictionary<string, string> Metadata { get; set; } = new();

        /// <summary>Calculate if this peer is considered alive</summary>
        public bool IsAlive(TimeSpan timeout)
        {
    return (DateTimeOffset.UtcNow - LastSeen) < timeout;
        }
    }

    public enum PeerStatus
    {
        Unknown = 0,
        Online = 1,
        Offline = 2,
        Degraded = 3
    }

    /// <summary>
    /// Message types for peer-to-peer communication
    /// </summary>
    public enum PeerMessageType
    {
        Announce = 0,      // Peer announcing presence
  Heartbeat = 1,     // Regular heartbeat
        StatusUpdate = 2,  // Service status update
        ControlCommand = 3,// Control command (start/stop)
        Query = 4,         // Query for information
   Response = 5,      // Response to query
Goodbye = 6        // Peer leaving network
    }

    /// <summary>
    /// P2P network message
    /// </summary>
    public class PeerMessage
    {
        [JsonPropertyName("messageId")]
        public string MessageId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("type")]
        public PeerMessageType Type { get; set; }

 [JsonPropertyName("senderId")]
   public string SenderId { get; set; } = string.Empty;

 [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("payload")]
        public string Payload { get; set; } = string.Empty;

      /// <summary>Target peer ID (empty for broadcast)</summary>
  [JsonPropertyName("targetId")]
        public string? TargetId { get; set; }
    }

    /// <summary>
    /// Control command to be executed on peer(s)
    /// </summary>
    public class PeerControlCommand
    {
      [JsonPropertyName("commandId")]
        public string CommandId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty; // "start", "stop", "restart"

        [JsonPropertyName("serviceName")]
  public string ServiceName { get; set; } = string.Empty;

   [JsonPropertyName("instanceCount")]
    public int InstanceCount { get; set; } = 1;

        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new();
    }
}
