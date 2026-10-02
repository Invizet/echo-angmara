// Сигнатуры сняты с официального EchoesLauncher.Common 1.0.0.0 (Echoes of Angmar Launcher 3.7.2).
// Тела пустые: этот файл нужен только компилятору.
using System.Diagnostics;
using System.Security.Cryptography;

namespace EchoesLauncher.Common;

[Serializable]
public class Realmlist
{
    public string PatchServer;
    public string PatchUser;
    public string PatchPass;
    public string AuthServer;
    public string LauncherVersion;
    public Version LatestLauncherVersion => throw Stub.Error;
}

public class RealmInfo
{
    public string Name;
    public string Host;
    public int MaxPlayers;
    public int NumPlayers;
    public int QueueLength;
    public bool IsOnline => throw Stub.Error;
    public bool IsFull => throw Stub.Error;
}

[Serializable]
public class LauncherConfig
{
    public string ClientInstallLocation { get; set; }
    public string SelectedServer { get; set; }
    public string SelectedUser { get; set; }
    public bool UseLegacyFtp { get; set; }
}

public class LauncherCore
{
    public Version Version;
    public LauncherConfig Config { get; private set; }
    public Realmlist Realmlist { get; private set; }
    public RSA ServerRsa { get; private set; }
    public IEnumerable<RealmInfo> AvailableRealms => throw Stub.Error;
    public string CurrentLoginToken { get; private set; }
    public Process CurrentClientProcess { get; private set; }
    public string TempPath => throw Stub.Error;

    public bool PrepareEnvironment() => throw Stub.Error;
    public Task<bool> FetchRealmlist(Action<string> errorReporter) => throw Stub.Error;
    public Task<bool> PatchData(Action<string> errorReporter, Action<long, long> progressReporter) => throw Stub.Error;
    public Task<bool> Authenticate(string username, string password, bool localhost, Action<string> errorReporter) => throw Stub.Error;
    public Task<bool> UpdateServerList(bool localhost) => throw Stub.Error;
    public Task<bool> Login(string username, string password, string realmName, bool localhost, Action<string> errorReporter, Action<int, int> loginQueueHandler) => throw Stub.Error;
    public void LaunchClient(string user, string token, string host, string language, Action exitCallback = null) => throw Stub.Error;
    public void SaveConfig() => throw Stub.Error;
}

static class Stub
{
    public static Exception Error => new InvalidOperationException("Заглушка EchoesLauncher.Common: настоящая DLL не загружена");
}
