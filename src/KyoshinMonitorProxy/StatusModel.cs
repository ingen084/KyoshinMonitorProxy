using System.Text.Json.Serialization;

namespace KyoshinMonitorProxy
{
    public class StatusModel
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }
        [JsonPropertyName("requestCount")]
        public int? RequestCount { get; set; }
        [JsonPropertyName("hitCacheCount")]
        public int? HitCacheCount { get; set; }
        [JsonPropertyName("missCacheCount")]
        public int? MissCacheCount { get; set; }
        [JsonPropertyName("usedMemoryBytes")]
        public ulong? UsedMemoryBytes { get; set; }
        [JsonPropertyName("savedBytes")]
        public ulong? SavedBytes { get; set; }
    }

    [JsonSerializable(typeof(StatusModel))]
    [JsonSerializable(typeof(GitHubRelease[]))]
    public partial class StatusModelContext : JsonSerializerContext
    {
    }
}
