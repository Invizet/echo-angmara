using static EchoAngmara.Texts;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EchoAngmara.Localization;

/// <summary>
/// Манифест версии перевода (site/l10n/ru.v2.json + .sig; старый ru.json — для лаунчеров до 0.3.0).
/// Пути файлов — относительно папки игры. echoespatch/local/* и raw/en/* переключаются языком и галочками,
/// raw/ru/* лежит всегда (без путей из текстов игра его не видит). У одного пути может быть несколько вариантов
/// (файлы с условием when) — ставится первый подходящий.
/// </summary>
public sealed class Manifest
{
    public int Format { get; set; } = 1;
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

    public static Manifest Parse(byte[] json)
    {
        var m = JsonSerializer.Deserialize<Manifest>(json, Json) ?? throw new UserFacingException(T("error.empty_manifest"));
        // на складе файлы лежат по имени; у вариантов одного пути имя общее — к нему добавляется начало хеша
        var shared = m.AllFiles.Where(f => f.Switchable).GroupBy(f => System.IO.Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.AllFiles)
        {
            string name = System.IO.Path.GetFileName(f.Path);
            f.StoreName = shared.Contains(name) ? f.Sha256[..12] + "_" + name : name;
        }
        return m;
    }
    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

public sealed class Component
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Компоненты, без которых этот бессмыслен (окно состава предупредит). С формата 2 видео от текстов не зависит.</summary>
    public List<string> Requires { get; set; } = new();
    public bool Default { get; set; } = true;
    /// <summary>Служебная часть (оригинальные файлы игры): в окне состава не показывается, нужна всегда.</summary>
    public bool Hidden { get; set; }
    public List<ManifestFile> Files { get; set; } = new();
    [JsonIgnore] public long Size => Files.Sum(f => f.Size);
}

public sealed class ManifestFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public List<string> Urls { get; set; } = new();
    /// <summary>
    /// Доп. условие по галочкам других частей («ru» — язык): {"video": false} = только без роликов.
    /// Пути к роликам прописаны в текстах, поэтому у файла с этими строками три варианта под сочетания «Тексты»/«Видеоролики».
    /// </summary>
    public Dictionary<string, bool>? When { get; set; }
    /// <summary>Файлы echoespatch/local и raw/en переключаются языком и галочками; raw/ru лежит постоянно.</summary>
    [JsonIgnore] public bool Switchable => Path.StartsWith(LocalDir + "/", StringComparison.OrdinalIgnoreCase)
                                           || Path.StartsWith("raw/en/", StringComparison.OrdinalIgnoreCase);
    /// <summary>Имя на складе (заполняет Manifest.Parse).</summary>
    [JsonIgnore] public string StoreName { get; set; } = "";
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
