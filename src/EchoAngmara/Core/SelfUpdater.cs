using Velopack;
using Velopack.Sources;

namespace EchoAngmara.Core;

/// <summary>
/// Самообновление лаунчера из GitHub Releases (Velopack). Релизы перевода помечены pre-release,
/// поэтому источник с prerelease:false их не видит.
/// </summary>
public sealed class SelfUpdater
{
    readonly UpdateManager _mgr = new(new GithubSource(AppLinks.GitHub, null, false));
    UpdateInfo? _ready;

    /// <summary>Запущен из установки Velopack (а не из папки сборки).</summary>
    public bool IsInstalled => _mgr.IsInstalled;
    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    /// <summary>Проверяет и тихо скачивает обновление; применится само при закрытии лаунчера.</summary>
    public async Task<bool> CheckAndDownloadAsync()
    {
        if (!_mgr.IsInstalled) return false;
        var info = await _mgr.CheckForUpdatesAsync();
        if (info == null) return false;
        await _mgr.DownloadUpdatesAsync(info);
        _ready = info;
        _mgr.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: false);
        return true;
    }

    public void ApplyAndRestart()
    {
        if (_ready != null) _mgr.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }
}
