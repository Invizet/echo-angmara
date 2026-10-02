using static EchoAngmara.Texts;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace EchoAngmara.Localization;

public enum GameLanguage { Russian, English }

public sealed record FileStatus(ManifestFile File, Component Component, bool Ok);

public sealed record DownloadProgress(long Done, long Total, string CurrentFile);

/// <summary>
/// Русский перевод на диске.
///   echoespatch\local\                 — то, что читает игра (папку патчер ядра не трогает — удаляет только файлы в корне echoespatch\)
///   echoespatch\echo_angmara\ru\       — склад: скачанные файлы local\, выключенные языком или галочкой
///   raw\ru\...                         — видео, лежат всегда (без русских текстов игра их не видит)
/// Переключение языка и галочки = перемещение файлов local\ ↔ склад (мгновенно, на одном диске).
/// </summary>
public sealed class LocalizationManager
{
    public string GameDir { get; }
    string LocalDir => Path.Combine(GameDir, "echoespatch", "local");
    string StoreDir => Path.Combine(GameDir, "echoespatch", "echo_angmara", "ru");
    public L10nState State { get; }

    public LocalizationManager(string gameDir)
    {
        GameDir = gameDir;
        State = L10nState.Load();
    }

    // ---------- манифест ----------

    public static async Task<Manifest> FetchManifestAsync(HttpClient http, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var site in AppLinks.SiteMirrors)
        {
            try
            {
                byte[] json = await http.GetByteArrayAsync(site + "l10n/ru.json", ct);
                byte[]? sig = null;
                try { sig = await http.GetByteArrayAsync(site + "l10n/ru.json.sig", ct); } catch (HttpRequestException) { }
                if (!ManifestSignature.Verify(json, sig))
                    throw new UserFacingException(T("error.l10n_signature"));
                return Manifest.Parse(json);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { last = ex; }
        }
        throw new UserFacingException(T("error.l10n_info", ("сообщение", UserFacingException.Reason(last))), last);
    }

    // ---------- состояние ----------

    /// <summary>Где сейчас должен/может лежать файл: для local\ — в local\ или на складе.</summary>
    string[] Candidates(ManifestFile f)
    {
        string target = Path.Combine(GameDir, f.Path.Replace('/', '\\'));
        return f.Switchable ? new[] { target, Path.Combine(StoreDir, Path.GetFileName(f.Path)) } : new[] { target };
    }

    public List<FileStatus> Check(Manifest m, IEnumerable<string> componentIds)
    {
        var ids = componentIds.ToHashSet();
        var res = new List<FileStatus>();
        foreach (var c in m.Components.Where(c => ids.Contains(c.Id)))
            foreach (var f in c.Files)
                res.Add(new FileStatus(f, c, Candidates(f).Any(p => HashMatches(p, f))));
        State.Save();
        return res;
    }

    bool HashMatches(string path, ManifestFile f)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length != f.Size) return false;
        return string.Equals(State.Hash(fi), f.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Что из манифеста реально стоит: версия «установлена», если все файлы выбранных компонентов на месте.</summary>
    public bool IsInstalled(Manifest m) => Check(m, State.Selected).All(s => s.Ok);

    public static bool GameRunning() => Process.GetProcessesByName("lotroclient").Length > 0 || Process.GetProcessesByName("lotroclient64").Length > 0;

    // ---------- загрузка ----------

    public async Task InstallAsync(HttpClient http, Manifest m, IReadOnlyCollection<string> componentIds,
        IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var check = Check(m, componentIds);
        var todo = check.Where(s => !s.Ok).Select(s => s.File).ToList();
        // Одинаковые файлы (русский стартовый ролик лежит и в raw/ru, и в raw/en) качаем один раз, остальные копируем
        var groups = todo.GroupBy(f => f.Sha256, StringComparer.OrdinalIgnoreCase).ToList();
        long total = groups.Sum(g => g.First().Size), done = 0;
        Directory.CreateDirectory(StoreDir);
        progress.Report(new DownloadProgress(0, total, ""));

        using var gate = new SemaphoreSlim(3);
        var tasks = groups.Select(async g =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var first = g.First();
                string src = Destination(first);
                // такой файл уже есть на диске под другим именем — копируем, не качаем
                string? local = m.AllFiles.Where(x => x.Sha256.Equals(first.Sha256, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(Candidates).FirstOrDefault(p => HashMatches(p, first));
                if (local != null)
                {
                    CopyKnown(local, src, first);
                    Interlocked.Add(ref done, first.Size);
                }
                else
                    await DownloadFileAsync(http, first, src, n =>
                    {
                        long d = Interlocked.Add(ref done, n);
                        progress.Report(new DownloadProgress(d, total, Path.GetFileName(first.Path)));
                    }, ct);
                foreach (var twin in g.Skip(1))
                    CopyKnown(src, Destination(twin), twin);
            }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks);

        State.InstalledVersion = m.Version;
        State.Selected = componentIds.ToHashSet();
        State.Save();
    }

    string Destination(ManifestFile f) => f.Switchable
        ? Path.Combine(StoreDir, Path.GetFileName(f.Path))
        : Path.Combine(GameDir, f.Path.Replace('/', '\\'));

    void CopyKnown(string from, string to, ManifestFile f)
    {
        if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
        State.Remember(new FileInfo(to), f.Sha256);
    }

    async Task DownloadFileAsync(HttpClient http, ManifestFile f, string dest, Action<long> onBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        string part = dest + ".part";
        Exception? last = null;
        // 3 круга по всем зеркалам; .part докачивается с места обрыва
        foreach (var (url, attempt) in Enumerable.Range(0, 3).SelectMany(a => f.Urls.Select(u => (u, a))))
        {
            if (attempt > 0 && f.Urls.IndexOf(url) == 0) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            long counted = 0;
            try
            {
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (have > f.Size) { File.Delete(part); have = 0; }
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                if (have > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0; // сервер не умеет докачку
                onBytes(have); counted = have;
                await using (var fs = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
                await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                {
                    var buf = new byte[1 << 16];
                    int n;
                    while ((n = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await fs.WriteAsync(buf.AsMemory(0, n), ct);
                        onBytes(n); counted += n;
                    }
                }
                string sha = await Sha256Async(part, ct);
                if (!sha.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(part);
                    throw new UserFacingException(T("error.l10n_checksum", ("имя_файла", Path.GetFileName(f.Path))));
                }
                File.Move(part, dest, overwrite: true);
                State.Remember(new FileInfo(dest), sha);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                onBytes(-counted); // откатываем прогресс, следующее зеркало начнёт заново или докачает
            }
        }
        throw new UserFacingException(T("error.l10n_download", ("имя_файла", Path.GetFileName(f.Path)), ("сообщение", UserFacingException.Reason(last))), last);
    }

    static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
    }

    // ---------- переключение ----------

    /// <summary>Раскладывает файлы local\ ↔ склад под язык и выбранные компоненты. Чужие файлы в local\ не трогает.</summary>
    public void Apply(Manifest m, GameLanguage lang, IReadOnlyCollection<string> componentIds)
    {
        if (GameRunning()) throw new UserFacingException(T("error.close_game_first"));
        Directory.CreateDirectory(LocalDir);
        Directory.CreateDirectory(StoreDir);
        var on = componentIds.ToHashSet();
        foreach (var c in m.Components)
            foreach (var f in c.Files.Where(f => f.Switchable))
            {
                string name = Path.GetFileName(f.Path);
                string inLocal = Path.Combine(LocalDir, name), inStore = Path.Combine(StoreDir, name);
                bool want = lang == GameLanguage.Russian && on.Contains(c.Id);
                if (want && !HashMatches(inLocal, f) && HashMatches(inStore, f))
                    MoveKnown(inStore, inLocal, f);
                else if (!want && HashMatches(inLocal, f))
                    MoveKnown(inLocal, inStore, f);
            }
        State.Language = lang;
        State.Selected = on;
        State.Save();
    }

    void MoveKnown(string from, string to, ManifestFile f)
    {
        File.Move(from, to, overwrite: true);
        State.Remember(new FileInfo(to), f.Sha256); // хеш уже проверен — не пересчитываем после переезда
    }
}

/// <summary>Состояние лаунчера: %LOCALAPPDATA%\EchoAngmara\state.json.</summary>
public sealed class L10nState
{
    public string? InstalledVersion { get; set; }
    public GameLanguage Language { get; set; } = GameLanguage.Russian;
    public HashSet<string> Selected { get; set; } = new() { "text", "sounds", "images", "xlat", "video" };
    public string? SelectedUser { get; set; }
    public Dictionary<string, Stamp> Hashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Stamp(long Size, long Mtime, string Sha256);

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EchoAngmara");
    static string FilePath => Path.Combine(Dir, "state.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static L10nState Load()
    {
        try { return JsonSerializer.Deserialize<L10nState>(File.ReadAllText(FilePath), Opts) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>SHA-256 с кэшем по (размер, время изменения): видео на 700 МБ не пересчитываем на каждом старте.</summary>
    public string Hash(FileInfo fi)
    {
        lock (Hashes)
            if (Hashes.TryGetValue(fi.FullName, out var s) && s.Size == fi.Length && s.Mtime == fi.LastWriteTimeUtc.Ticks)
                return s.Sha256;
        using var fs = fi.OpenRead();
        string sha = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        Remember(fi, sha);
        return sha;
    }

    public void Remember(FileInfo fi, string sha)
    {
        fi.Refresh();
        lock (Hashes) Hashes[fi.FullName] = new Stamp(fi.Length, fi.LastWriteTimeUtc.Ticks, sha);
    }
}
