using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipGuard.Services;

/// <summary>
/// Small JSON-file store under %LOCALAPPDATA%\ClipGuard. Everything ClipGuard
/// keeps stays on this machine — that promise is load-bearing for the product,
/// so there is deliberately no network path out of here.
///
/// Files holding clipboard content are encrypted at rest with DPAPI. Without
/// that, the event log is a plaintext record of everything the user ever copied
/// — passwords, tokens, recovery phrases — sitting in a predictable path. A
/// clipboard security tool must not be the thing that creates that file.
/// </summary>
public static class Storage
{
    /// <summary>Marks a file as DPAPI-protected so plaintext migration can be detected.</summary>
    private const string EncryptionHeader = "CGENC1:";

    /// <summary>
    /// Where state lives. Overridable via CLIPGUARD_DATA_DIR so tests stay
    /// hermetic — and so a portable install can keep its data beside the exe.
    /// </summary>
    public static string Root { get; } =
        Environment.GetEnvironmentVariable("CLIPGUARD_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipGuard");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static Storage() => Directory.CreateDirectory(Root);

    /// <param name="encrypted">
    /// True for anything that can contain clipboard content. Scoped to the
    /// current user, so another account on the machine cannot read it.
    /// </param>
    public static T Load<T>(string fileName, Func<T> fallback, bool encrypted = false)
    {
        var path = Path.Combine(Root, fileName);
        try
        {
            if (!File.Exists(path)) return fallback();

            var raw = File.ReadAllText(path);
            var json = encrypted ? Decrypt(raw) : raw;

            return json is null
                ? fallback()
                : JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        // FormatException matters here: a truncated or tampered encrypted file
        // fails in Convert.FromBase64String, and that must not stop startup.
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                      or CryptographicException or FormatException)
        {
            // A corrupt, unreadable or undecryptable store should not stop
            // protection from starting.
            return fallback();
        }
    }

    public static void Save<T>(string fileName, T value, bool encrypted = false)
    {
        var path = Path.Combine(Root, fileName);
        try
        {
            var json = JsonSerializer.Serialize(value, Options);
            var payload = encrypted ? Encrypt(json) : json;

            // Write-then-replace so a crash mid-write can't truncate the store.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, payload);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Losing a write is survivable; crashing the tray app is not.
        }
    }

    private static string Encrypt(string json)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);

        return EncryptionHeader + Convert.ToBase64String(protectedBytes);
    }

    /// <summary>
    /// Returns the plaintext JSON, transparently accepting a legacy unencrypted
    /// file so an existing install keeps its history. The next save rewrites it
    /// encrypted.
    /// </summary>
    private static string? Decrypt(string raw)
    {
        if (!raw.StartsWith(EncryptionHeader, StringComparison.Ordinal))
        {
            // Pre-encryption file from an earlier build.
            return raw;
        }

        var payload = Convert.FromBase64String(raw[EncryptionHeader.Length..]);
        var plain = ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Removes a store outright — used when the user turns off history.</summary>
    public static void Delete(string fileName)
    {
        try
        {
            var path = Path.Combine(Root, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
