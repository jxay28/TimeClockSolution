using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string command = args[0].Trim().ToLowerInvariant();
Dictionary<string, string> options = ParseOptions(args.Skip(1).ToArray());

try
{
    return command switch
    {
        "keygen" => RunKeygen(options),
        "issue" => RunIssue(options),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Errore: {ex.Message}");
    return 1;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Comando non riconosciuto: {command}");
    PrintUsage();
    return 1;
}

static int RunKeygen(IReadOnlyDictionary<string, string> options)
{
    string outDir = GetOption(options, "--out-dir", Path.Combine(Environment.CurrentDirectory, "license_keys"));
    Directory.CreateDirectory(outDir);

    string privateKeyPath = Path.Combine(outDir, "license_private_key.pem");
    string publicKeyPath = Path.Combine(outDir, "license_public_key.pem");

    using RSA rsa = RSA.Create(2048);
    File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

    Console.WriteLine("Chiavi generate.");
    Console.WriteLine($"Privata: {privateKeyPath}");
    Console.WriteLine($"Pubblica: {publicKeyPath}");
    Console.WriteLine("Distribuisci solo license_public_key.pem con il client.");
    return 0;
}

static int RunIssue(IReadOnlyDictionary<string, string> options)
{
    string privateKeyPath = GetRequiredOption(options, "--private-key");
    string machineId = GetRequiredOption(options, "--machine-id");
    string customer = GetRequiredOption(options, "--customer");
    string product = GetOption(options, "--product", "TimeClock.Client");
    string licenseId = GetOption(options, "--license-id", Guid.NewGuid().ToString("N"));

    DateTimeOffset issued = DateTimeOffset.UtcNow;
    DateTimeOffset expires = ParseExpiry(options, issued);

    if (!File.Exists(privateKeyPath))
    {
        throw new FileNotFoundException("Private key non trovata.", privateKeyPath);
    }

    string pem = File.ReadAllText(privateKeyPath);

    var payload = new LicensePayload
    {
        LicenseId = licenseId,
        Customer = customer,
        Product = product,
        MachineId = machineId,
        IssuedAtUtc = issued,
        ExpiresAtUtc = expires
    };

    byte[] payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
    byte[] signature;
    using (RSA rsa = RSA.Create())
    {
        rsa.ImportFromPem(pem);
        signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    string token = $"{Base64Url.Encode(payloadBytes)}.{Base64Url.Encode(signature)}";
    Console.WriteLine(token);
    return 0;
}

static DateTimeOffset ParseExpiry(IReadOnlyDictionary<string, string> options, DateTimeOffset issued)
{
    if (options.TryGetValue("--expires-utc", out string? explicitExpiry))
    {
        if (!DateTimeOffset.TryParse(explicitExpiry, out DateTimeOffset parsed))
        {
            throw new ArgumentException("Valore --expires-utc non valido.");
        }

        return parsed.ToUniversalTime();
    }

    int days = 365;
    if (options.TryGetValue("--days", out string? daysText))
    {
        if (!int.TryParse(daysText, out days) || days <= 0)
        {
            throw new ArgumentException("Valore --days non valido.");
        }
    }

    return issued.AddDays(days);
}

static Dictionary<string, string> ParseOptions(string[] raw)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < raw.Length; i++)
    {
        string current = raw[i];
        if (!current.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        if (i + 1 >= raw.Length || raw[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[current] = string.Empty;
            continue;
        }

        result[current] = raw[i + 1];
        i++;
    }

    return result;
}

static string GetRequiredOption(IReadOnlyDictionary<string, string> options, string name)
{
    if (!options.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
    {
        throw new ArgumentException($"Parametro obbligatorio mancante: {name}");
    }

    return value;
}

static string GetOption(IReadOnlyDictionary<string, string> options, string name, string fallback)
{
    return options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : fallback;
}

static void PrintUsage()
{
    Console.WriteLine("TimeClock.LicensingTool");
    Console.WriteLine("Comandi:");
    Console.WriteLine("  keygen --out-dir <cartella>");
    Console.WriteLine("  issue --private-key <pem> --machine-id <id> --customer <nome> [--days 365] [--expires-utc 2030-12-31T23:59:59Z] [--product TimeClock.Client] [--license-id id]");
}

internal static class Base64Url
{
    public static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal sealed class LicensePayload
{
    public string LicenseId { get; init; } = string.Empty;
    public string Customer { get; init; } = string.Empty;
    public string Product { get; init; } = string.Empty;
    public string MachineId { get; init; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
