using System;

namespace Orchestrator.Core.Models
{
    /// <summary>
    /// Configuration for P2P networking
    /// </summary>
    public class PeerConfig
    {
      /// <summary>Enable P2P discovery and networking</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>Multicast group address for discovery</summary>
   public string MulticastGroup { get; set; } = "239.255.42.99";

  /// <summary>Multicast port for discovery</summary>
        public int MulticastPort { get; set; } = 5353;

        /// <summary>Heartbeat interval in milliseconds</summary>
   public int HeartbeatInterval { get; set; } = 5000;

   /// <summary>Peer timeout in milliseconds (3x heartbeat recommended)</summary>
        public int PeerTimeout { get; set; } = 15000;

        /// <summary>Maximum number of peers to track</summary>
     public int MaxPeers { get; set; } = 100;

        /// <summary>Enable automatic state synchronization</summary>
        public bool AutoSync { get; set; } = true;

        /// <summary>Sync interval in milliseconds</summary>
        public int SyncInterval { get; set; } = 10000;

/// <summary>Enable cluster-wide command execution</summary>
        public bool EnableClusterCommands { get; set; } = true;

     /// <summary>Network interface to bind to (empty for all)</summary>
        public string BindInterface { get; set; } = string.Empty;

        /// <summary>Custom peer metadata</summary>
        public Dictionary<string, string> Metadata { get; set; } = new();

  /// <summary>Peer-to-peer encryption key (for future use)</summary>
     public string? EncryptionKey { get; set; }
    }
}
