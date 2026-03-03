using System;
using System.Text.Json.Serialization;

namespace TimeClock.Client.Licensing
{
    public sealed class LicensePayload
    {
        [JsonPropertyName("licenseId")]
        public string LicenseId { get; init; } = string.Empty;

        [JsonPropertyName("customer")]
        public string Customer { get; init; } = string.Empty;

        [JsonPropertyName("product")]
        public string Product { get; init; } = string.Empty;

        [JsonPropertyName("machineId")]
        public string MachineId { get; init; } = string.Empty;

        [JsonPropertyName("issuedAtUtc")]
        public DateTimeOffset IssuedAtUtc { get; init; }

        [JsonPropertyName("expiresAtUtc")]
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
