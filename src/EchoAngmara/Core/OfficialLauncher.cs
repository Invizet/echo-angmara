using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace EchoAngmara.Core;

/// <summary>Установленный официальный Echoes of Angmar Launcher: где лежит, какой версии.</summary>
public sealed class OfficialLauncher
{
    public const string DownloadUrl = "https://www.echoesofangmar.com/launcher/EchoesLauncher.msi";
    const string CoreDll = "EchoesLauncher.Common.dll";

    public string Directory { get; }
    public Version Version { get; }

    OfficialLauncher(string dir)
    {
        Directory = dir;
        Version = AssemblyName.GetAssemblyName(Path.Combine(dir, "EchoesLauncher.dll")).Version ?? new Version(0, 0, 0);
    }

    public static OfficialLauncher? Find()
    {
        foreach (var root in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
        {
            string lnk = Path.Combine(Environment.GetFolderPath(root), "Echoes of Angmar", "Echoes of Angmar Launcher.lnk");
            if (!File.Exists(lnk)) continue;
            // MSI ставит «рекламный» ярлык: TargetPath ведёт в C:\Windows\Installer,
            // а настоящая папка лаунчера записана в WorkingDirectory
            string? dir = ShortcutWorkingDir(lnk);
            if (IsLauncherDir(dir)) return new OfficialLauncher(dir!);
        }
        foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string dir = Path.Combine(Environment.GetFolderPath(pf), "Echoes of Angmar");
            if (IsLauncherDir(dir)) return new OfficialLauncher(dir);
        }
        // последняя попытка — рядом с игрой (бывает, лаунчер ставят в подпапку клиента)
        string? game = OfficialConfig.TryRead()?.ClientInstallLocation;
        if (game != null && System.IO.Directory.Exists(game))
            foreach (var dll in System.IO.Directory.EnumerateFiles(game, CoreDll, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }))
                if (IsLauncherDir(Path.GetDirectoryName(dll))) return new OfficialLauncher(Path.GetDirectoryName(dll)!);
        return null;
    }

    static bool IsLauncherDir(string? dir) =>
        !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, CoreDll)) && File.Exists(Path.Combine(dir, "EchoesLauncher.dll"));

    static string? ShortcutWorkingDir(string lnk)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        if (t == null) return null;
        object? sh = null;
        try
        {
            sh = Activator.CreateInstance(t)!;
            dynamic s = sh;
            return (string)s.CreateShortcut(lnk).WorkingDirectory;
        }
        catch { return null; }
        finally { if (sh != null) Marshal.FinalReleaseComObject(sh); }
    }

    /// <summary>Подключает DLL ядра из папки официального лаунчера. Вызывать до первого обращения к типам EchoesLauncher.Common.</summary>
    public void HookAssemblyResolve()
    {
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string p = Path.Combine(Directory, name.Name + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
    }
}

public sealed record Account(string User, string? Password)
{
    public bool HasPassword => !string.IsNullOrEmpty(Password);
    public override string ToString() => User;
}

/// <summary>Конфиг официального лаунчера. Только чтение: сами туда не пишем никогда.</summary>
public sealed class OfficialConfig
{
    static readonly byte[] Entropy = { 2, 8, 1, 6, 5 };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Echoes of Angmar Launcher", "EchoesLauncher.config");

    public string? ClientInstallLocation { get; private init; }
    public string? SelectedServer { get; private init; }
    public string? SelectedUser { get; private init; }
    public List<Account> Accounts { get; } = new();

    public static OfficialConfig? TryRead()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            // FileShare.ReadWrite — официальный лаунчер может держать файл открытым
            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            var r = doc.RootElement;
            var cfg = new OfficialConfig
            {
                ClientInstallLocation = Str(r, "ClientInstallLocation"),
                SelectedServer = Str(r, "SelectedServer"),
                SelectedUser = Str(r, "SelectedUser"),
            };
            if (r.TryGetProperty("SavedLogins", out var logins) && logins.ValueKind == JsonValueKind.Array)
                foreach (var l in logins.EnumerateArray())
                    cfg.Accounts.Add(new Account(Str(l, "User") ?? "", Decrypt(Str(l, "Pass"))));
            return cfg;
        }
        catch { return null; }
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string? Decrypt(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(b64), Entropy, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }
}
