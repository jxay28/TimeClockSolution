using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TimeClock.Client.Licensing
{
    public sealed class LicenseTokenValidator
    {
        private const string ExpectedProduct = "TimeClock.Client";

        public LicenseValidationResult Validate(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return LicenseValidationResult.Invalid("Chiave licenza vuota.");
            }

            string[] parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                return LicenseValidationResult.Invalid("Formato licenza non valido.");
            }

            byte[] payloadBytes;
            byte[] signatureBytes;
            try
            {
                payloadBytes = Base64Url.Decode(parts[0]);
                signatureBytes = Base64Url.Decode(parts[1]);
            }
            catch
            {
                return LicenseValidationResult.Invalid("Licenza non decodificabile.");
            }

            if (payloadBytes.Length == 0 || signatureBytes.Length == 0)
            {
                return LicenseValidationResult.Invalid("Licenza incompleta.");
            }

            string? pem = LicensePublicKeyProvider.TryLoadPem();
            if (string.IsNullOrWhiteSpace(pem))
            {
                return LicenseValidationResult.Invalid("Chiave pubblica non configurata. Aggiungi license_public_key.pem nella cartella del client.");
            }

            bool signatureValid;
            try
            {
                using RSA rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                signatureValid = rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return LicenseValidationResult.Invalid("Chiave pubblica non valida.");
            }

            if (!signatureValid)
            {
                return LicenseValidationResult.Invalid("Firma digitale della licenza non valida.");
            }

            LicensePayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes);
            }
            catch
            {
                return LicenseValidationResult.Invalid("Contenuto licenza non valido.");
            }

            if (payload == null)
            {
                return LicenseValidationResult.Invalid("Licenza assente.");
            }

            if (!string.Equals(payload.Product, ExpectedProduct, StringComparison.Ordinal))
            {
                return LicenseValidationResult.Invalid("Licenza emessa per un prodotto diverso.");
            }

            string currentMachineId = MachineFingerprintProvider.GetMachineId();
            if (!string.Equals(payload.MachineId?.Trim(), currentMachineId, StringComparison.OrdinalIgnoreCase))
            {
                return LicenseValidationResult.Invalid("Licenza non valida per questa macchina.");
            }

            if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                return LicenseValidationResult.Invalid("Licenza scaduta.");
            }

            if (string.IsNullOrWhiteSpace(payload.Customer) || string.IsNullOrWhiteSpace(payload.LicenseId))
            {
                return LicenseValidationResult.Invalid("Campi licenza mancanti.");
            }

            return LicenseValidationResult.Valid(payload);
        }

        public static string BuildTokenPayloadJson(LicensePayload payload)
        {
            return JsonSerializer.Serialize(payload);
        }

        public static string BuildUnsignedToken(string payloadJson)
        {
            return Base64Url.Encode(Encoding.UTF8.GetBytes(payloadJson));
        }
    }
}
