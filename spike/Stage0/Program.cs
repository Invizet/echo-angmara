// Этап 0: проверяем, что можно жить поверх установленного официального лаунчера Echoes.
// Ничего не пишет на диск, кроме того, что делает само ядро в конструкторе (создаёт папку конфига, если её нет).
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;

Console.OutputEncoding = Encoding.UTF8;
bool doAuth = args.Contains("--auth");

// 1. Где стоит официальный лаунчер
string? officialDir = OfficialLauncher.Find();
Console.WriteLine($"[1] Официальный лаунчер: {officialDir ?? "НЕ НАЙДЕН"}");
if (officialDir == null) return 1;

// 2. Подгружаем его ядро из его папки (не копируем)
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    string p = Path.Combine(officialDir, name.Name + ".dll");
    return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
};
var officialVersion = AssemblyName.GetAssemblyName(Path.Combine(officialDir, "EchoesLauncher.dll")).Version!;
var coreVersion = AssemblyName.GetAssemblyName(Path.Combine(officialDir, "EchoesLauncher.Common.dll")).Version!;
Console.WriteLine($"[2] Версия официального лаунчера: {officialVersion}, ядра: {coreVersion}");

return await Run();

async Task<int> Run()
{
    // 3. Аккаунты — читаем конфиг сами, только чтение
    var accounts = OfficialConfig.ReadAccounts(out string? installDir, out string? selectedUser, out string? selectedServer);
    Console.WriteLine($"[3] Папка игры: {installDir}; мир: {selectedServer}; аккаунтов: {accounts.Count}, " +
                      $"с расшифрованным паролем: {accounts.Count(a => a.Password != null)}; выбран: {selectedUser}");

    // 4. Realmlist со своей устойчивой загрузкой
    var (xml, note) = await RealmlistFetcher.Fetch();
    Console.WriteLine($"[4] Realmlist: {note}");
    if (xml == null) return 2;
    var realmlist = (EchoesLauncher.Common.Realmlist)new XmlSerializer(typeof(EchoesLauncher.Common.Realmlist))
        .Deserialize(new StringReader(xml))!;
    Console.WriteLine($"    AuthServer={realmlist.AuthServer} PatchServer={realmlist.PatchServer} LauncherVersion={realmlist.LauncherVersion}");
    if (realmlist.LatestLauncherVersion > officialVersion)
        Console.WriteLine("    ! Официальный лаунчер устарел — попросим игрока обновить его");

    // 5. Создаём ядро и отдаём ему наш realmlist через приватный сеттер
    var core = new EchoesLauncher.Common.LauncherCore { Version = officialVersion };
    typeof(EchoesLauncher.Common.LauncherCore).GetProperty("Realmlist")!.SetValue(core, realmlist);
    Console.WriteLine($"[5] Ядро создано, Realmlist подставлен: {core.Realmlist?.AuthServer}");

    // 6. Онлайн без входа
    var realmNames = new[] { selectedServer ?? "Dor-lómin" };
    Console.WriteLine($"[6] refresh без входа: {await Refresh(realmlist.AuthServer, realmNames)}");

    if (!doAuth) { Console.WriteLine("    (вход не выполнялся; запустить с --auth)"); return 0; }

    // 7. Вход сохранённым аккаунтом — получаем список миров
    var acc = accounts.FirstOrDefault(a => a.User == selectedUser && a.Password != null) ?? accounts.FirstOrDefault(a => a.Password != null);
    if (acc == null) { Console.WriteLine("[7] Нет аккаунта с паролем"); return 3; }
    bool ok = await core.Authenticate(acc.User, acc.Password!, false, e => Console.WriteLine($"    ошибка: {e}"));
    Console.WriteLine($"[7] Authenticate({acc.User}): {ok}");
    foreach (var r in core.AvailableRealms)
        Console.WriteLine($"    мир «{r.Name}» host={r.Host} онлайн={r.NumPlayers}/{r.MaxPlayers} очередь={r.QueueLength} IsOnline={r.IsOnline}");
    if (ok)
    {
        bool upd = await core.UpdateServerList(false);
        Console.WriteLine($"[8] UpdateServerList: {upd}; " + string.Join(", ", core.AvailableRealms.Select(r => $"{r.Name} {r.NumPlayers}/{r.MaxPlayers} q{r.QueueLength}")));
    }
    return 0;
}

static async Task<string> Refresh(string authServer, string[] realms)
{
    int i = authServer.IndexOf(':');
    using var tcp = new System.Net.Sockets.TcpClient();
    await tcp.ConnectAsync(authServer[..i], int.Parse(authServer[(i + 1)..]));
    using var s = tcp.GetStream();
    var cmd = Encoding.UTF8.GetBytes("$refresh$" + string.Join("$", realms) + "$");
    await s.WriteAsync(BitConverter.GetBytes(cmd.Length).Concat(cmd).ToArray());
    var buf = new byte[1024];
    int n = await s.ReadAsync(buf);
    return Encoding.UTF8.GetString(buf, 0, n);
}

static class OfficialLauncher
{
    public static string? Find()
    {
        // ярлык в меню Пуск — так ставит официальный MSI (InstallLocation в реестре пустой)
        foreach (var root in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
        {
            string lnk = Path.Combine(Environment.GetFolderPath(root), "Echoes of Angmar", "Echoes of Angmar Launcher.lnk");
            if (!File.Exists(lnk)) continue;
            // MSI ставит «рекламный» ярлык: TargetPath ведёт в C:\Windows\Installer, компонента в нём нет,
            // а настоящая папка лаунчера записана в WorkingDirectory ярлыка
            foreach (var dir in new[] { MsiShortcutTarget(lnk) is { } t ? Path.GetDirectoryName(t) : null, ShortcutWorkingDir(lnk) })
                if (dir != null && File.Exists(Path.Combine(dir, "EchoesLauncher.Common.dll")))
                    return dir;
        }
        foreach (var dir in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Echoes of Angmar"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Echoes of Angmar") })
            if (File.Exists(Path.Combine(dir, "EchoesLauncher.Common.dll"))) return dir;
        return null;
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern uint MsiGetShortcutTarget(string shortcut, StringBuilder productCode, StringBuilder featureId, StringBuilder componentCode);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern int MsiGetComponentPath(string product, string component, StringBuilder? path, ref uint len);

    static string? MsiShortcutTarget(string lnk)
    {
        var product = new StringBuilder(39); var feature = new StringBuilder(39); var component = new StringBuilder(39);
        if (MsiGetShortcutTarget(lnk, product, feature, component) != 0) return null;
        uint len = 1024; var path = new StringBuilder((int)len);
        int state = MsiGetComponentPath(product.ToString(), component.ToString(), path, ref len);
        return state == 3 /* INSTALLSTATE_LOCAL */ ? path.ToString() : null;
    }

    static string? ShortcutWorkingDir(string lnk)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        if (t == null) return null;
        dynamic sh = Activator.CreateInstance(t)!;
        try { return (string)sh.CreateShortcut(lnk).WorkingDirectory; }
        finally { Marshal.FinalReleaseComObject(sh); }
    }
}

record Account(string User, string? Password);

static class OfficialConfig
{
    static readonly byte[] Entropy = { 2, 8, 1, 6, 5 };
    public static string ConfigFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Echoes of Angmar Launcher", "EchoesLauncher.config");

    public static List<Account> ReadAccounts(out string? installDir, out string? selectedUser, out string? selectedServer)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ConfigFile));
        var r = doc.RootElement;
        installDir = r.TryGetProperty("ClientInstallLocation", out var v) ? v.GetString() : null;
        selectedUser = r.TryGetProperty("SelectedUser", out v) ? v.GetString() : null;
        selectedServer = r.TryGetProperty("SelectedServer", out v) ? v.GetString() : null;
        var list = new List<Account>();
        if (r.TryGetProperty("SavedLogins", out var logins))
            foreach (var l in logins.EnumerateArray())
            {
                string user = l.GetProperty("User").GetString() ?? "";
                string? enc = l.TryGetProperty("Pass", out var p) ? p.GetString() : null;
                string? pass = null;
                if (!string.IsNullOrEmpty(enc))
                    try { pass = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), Entropy, DataProtectionScope.CurrentUser)); }
                    catch { }
                list.Add(new Account(user, pass));
            }
        return list;
    }
}

static class RealmlistFetcher
{
    static string Url = Environment.GetEnvironmentVariable("RL_URL") ?? "http://echoesofangmar.com/realmlist";

    // Обычная загрузка; если сертификат https-редиректа протух — принимаем его,
    // только если ошибка ровно «срок действия» и имя сервера совпадает.
    public static async Task<(string? xml, string note)> Fetch()
    {
        string? certInfo = null;
        bool relaxed = false;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
            {
                certInfo = cert == null ? null :
                    $"{cert.Subject}, до {cert.NotAfter:yyyy-MM-dd}, SPKI sha256={Convert.ToHexString(SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo()))[..16]}…";
                if (errors == SslPolicyErrors.None) return true;
                if (errors == SslPolicyErrors.RemoteCertificateChainErrors && chain != null &&
                    chain.ChainStatus.All(s => s.Status is X509ChainStatusFlags.NotTimeValid or X509ChainStatusFlags.NoError))
                { relaxed = true; return true; }
                return false;
            }
        };
        try
        {
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            string xml = await http.GetStringAsync(Url);
            return (xml, $"OK{(relaxed ? " (сертификат просрочен — принят по исключению)" : "")}; сертификат: {certInfo ?? "не было https"}");
        }
        catch (Exception ex) { return (null, "ошибка: " + ex.Message); }
    }
}
