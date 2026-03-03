using System;
using System.Text.Json.Serialization;

namespace TimeClock.Server.Licensing
{
    public sealed class ServerLicenseRecord
    {
        [JsonPropertyName("token")]
        public string Token { get; init; } = string.Empty;

        [JsonPropertyName("licenseKey")]
        public string LicenseKey { get; init; } = string.Empty;

        [JsonPropertyName("generatedAtUtc")]
        public DateTimeOffset GeneratedAtUtc { get; init; }
    }
}
