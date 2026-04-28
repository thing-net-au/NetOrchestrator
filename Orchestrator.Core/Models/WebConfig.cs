namespace Orchestrator.Core.Models
{
    public class WebConfig
    {
        public int UiPort { get; set; } = 5000;
        public int ApiPort { get; set; } = 5001;
        public string BindIP { get; set; } = "127.0.0.1";
        public int StreamBufferSize { get; set; } = 8192;
        public string? ApiBaseUrl { get; set; }
    }
}