using System.IO;
using System.Reflection;
using EchoesLauncher.Common;

namespace EchoAngmara.Core;

/// <summary>
/// Обёртка над официальным LauncherCore. Протокол, проверка файлов и запуск клиента — целиком их код;
/// мы только подставляем realmlist (фикс SSL) и не даём ядру переписывать конфиг официального лаунчера.
/// </summary>
public sealed class GameSession
{
    readonly LauncherCore _core;
    public string GameDir { get; }
    public Realmlist Realmlist { get; }
    public IReadOnlyList<RealmInfo> Realms => _core.AvailableRealms.ToList();

    public GameSession(OfficialLauncher official, Realmlist realmlist, string gameDir)
    {
        GameDir = gameDir;
        Realmlist = realmlist;
        // Версию честно берём у установленного официального лаунчера — её же сервер сверяет с LauncherVersion
        _core = new LauncherCore { Version = official.Version };
        _core.Config.ClientInstallLocation = gameDir;
        typeof(LauncherCore).GetProperty(nameof(LauncherCore.Realmlist), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(_core, realmlist);
        PrepareEnvironment();
    }

    /// <summary>То же, что LauncherCore.PrepareEnvironment, но без SaveConfig (он стирает и перезаписывает их конфиг).</summary>
    void PrepareEnvironment()
    {
        Directory.CreateDirectory(Path.Combine(GameDir, "echoespatch"));
        string temp = _core.TempPath;
        try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
        Directory.CreateDirectory(temp);
    }

    public bool OfficialOutdated(Version official) => Realmlist.LatestLauncherVersion > official;

    public Task<bool> AuthenticateAsync(Account acc, Action<string> onError) =>
        _core.Authenticate(acc.User, acc.Password ?? "", false, e => onError(Translate(e)));

    public Task<bool> RefreshRealmsAsync() => _core.UpdateServerList(false);

    public Task<bool> PatchGameAsync(Action<string> onError, Action<long, long> onProgress) =>
        _core.PatchData(e => onError(Translate(e)), onProgress);

    public Task<bool> LoginAsync(Account acc, string realm, Action<string> onError, Action<int, int> onQueue) =>
        _core.Login(acc.User, acc.Password ?? "", realm, false, e => onError(Translate(e)), onQueue);

    public void LaunchClient(Account acc, RealmInfo realm, Action onExit)
    {
        // как у официального: en_gb, а de/fr — если стоят их .dat. Русский перевод живёт в echoespatch\local поверх en_gb
        string lang = "en_gb";
        if (File.Exists(Path.Combine(GameDir, "client_local_DE.dat"))) lang = "de";
        if (File.Exists(Path.Combine(GameDir, "client_local_FR.dat"))) lang = "fr";
        _core.LaunchClient(acc.User, _core.CurrentLoginToken, realm.Host, lang, onExit);
    }

    /// <summary>Сообщения ядра на русский; незнакомые показываем как есть.</summary>
    static string Translate(string msg) => msg switch
    {
        "Authentication failed" => "Неверный логин или пароль. Проверьте их в официальном лаунчере.",
        "This account has been banned" or "This account has been banned." => "Аккаунт заблокирован.",
        "Authentication service error." => "Ошибка сервера авторизации.",
        "Error connecting to game server." => "Не удалось подключиться к игровому серверу.",
        "Launcher out of date. Restart the launcher (from Start menu) to update it" =>
            "Официальный лаунчер устарел. Запустите его из меню «Пуск», чтобы он обновился.",
        "Couldn't connect to the authentication service!" => "Не удалось подключиться к серверу авторизации.",
        "Access to game server denied." => "Игровой сервер отказал во входе.",
        "Couldn't download the patch manifest! The patch server might be down." =>
            "Не удалось получить список файлов игры — сервер обновлений недоступен.",
        _ when msg.StartsWith("Couldn't verify client DAT file") => "Файл клиента повреждён: " + msg + " Возможно, клиент придётся переустановить.",
        _ when msg.StartsWith("Couldn't patch") => "Не удалось обновить файл игры: " + msg,
        _ when msg.StartsWith("Couldn't remove old patch file") => "Не удалось удалить старый патч: " + msg,
        _ => msg,
    };
}

/// <summary>Онлайн миров без входа: команда refresh сервера авторизации, ответ «max;num;queue;» на каждый мир.</summary>
public static class RealmStatus
{
    public sealed record Info(string Name, int Max, int Num, int Queue)
    {
        public bool Online => Max > 0;
    }

    public static async Task<List<Info>> QueryAsync(string authServer, IReadOnlyList<string> realms, CancellationToken ct = default)
    {
        int i = authServer.IndexOf(':');
        using var tcp = new System.Net.Sockets.TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await tcp.ConnectAsync(authServer[..i], int.Parse(authServer[(i + 1)..]), cts.Token);
        using var s = tcp.GetStream();
        var cmd = System.Text.Encoding.UTF8.GetBytes("$refresh$" + string.Join("$", realms) + "$");
        await s.WriteAsync(BitConverter.GetBytes(cmd.Length).Concat(cmd).ToArray(), cts.Token);
        var buf = new byte[1024];
        int n = await s.ReadAsync(buf, cts.Token);
        var parts = System.Text.Encoding.UTF8.GetString(buf, 0, n).Split(';', StringSplitOptions.RemoveEmptyEntries);
        var res = new List<Info>();
        for (int k = 0; k + 2 < parts.Length && k / 3 < realms.Count; k += 3)
            res.Add(new Info(realms[k / 3], int.Parse(parts[k]), int.Parse(parts[k + 1]), int.Parse(parts[k + 2])));
        return res;
    }
}
