using static EchoAngmara.Texts;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EchoAngmara.Localization;

/// <summary>
/// Манифест версии перевода (site/l10n/ru.json + ru.json.sig).
/// Пути файлов — относительно папки игры; echoespatch/local/* переключаются языком, остальное (raw/ru/*) лежит всегда.
/// </summary>
public sealed class Manifest
{
    public string Lang { get; set; } = "ru";
    public string Version { get; set; } = "";
    public string? Released { get; set; }
    public string? Notes { get; set; }
    public List<Component> Components { get; set; } = new();

    public IEnumerable<ManifestFile> AllFiles => Components.SelectMany(c => c.Files);
    public Component? Get(string id) => Components.FirstOrDefault(c => c.Id == id);

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static Manifest Parse(byte[] json) => JsonSerializer.Deserialize<Manifest>(json, Json) ?? throw new UserFacingException(T("error.empty_manifest"));
    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

public sealed class Component
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Компоненты, без которых этот бессмыслен (видео ← тексты: пути к роликам прописаны в текстах).</summary>
    public List<string> Requires { get; set; } = new();
    public bool Default { get; set; } = true;
    public List<ManifestFile> Files { get; set; } = new();
    [JsonIgnore] public long Size => Files.Sum(f => f.Size);
}

public sealed class ManifestFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public List<string> Urls { get; set; } = new();
    /// <summary>Файлы из echoespatch/local переключаются RU/EN; остальные лежат постоянно.</summary>
    [JsonIgnore] public bool Switchable => Path.StartsWith(LocalDir + "/", StringComparison.OrdinalIgnoreCase);
    public const string LocalDir = "echoespatch/local";
}

/// <summary>Подпись манифеста: ECDSA P-256 / SHA-256 над байтами ru.json. Приватный ключ — только у мейнтейнера.</summary>
public static class ManifestSignature
{
    // Публичный ключ (SubjectPublicKeyInfo, base64). Пусто = ключ ещё не сгенерирован, подпись не проверяется (только для разработки).
    public const string PublicKey = "";

    public static bool Verify(byte[] data, byte[]? signature)
    {
        if (PublicKey.Length == 0) return true;
        if (signature == null) return false;
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
    }
}
