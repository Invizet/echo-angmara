namespace EchoAngmara;

/// <summary>Адреса, которые знает лаунчер.</summary>
public static class AppLinks
{
    public const string Owner = "Invizet";
    public const string Repo = "echo-angmara";

    /// <summary>Ветка site: news/, l10n/, catalog.json. Сначала GitHub Pages, запасной — raw.</summary>
    public static readonly string[] SiteMirrors =
        Environment.GetEnvironmentVariable("ECHO_SITE") is { Length: > 0 } dev   // для разработки: локальный сервер
            ? new[] { dev.TrimEnd('/') + "/" }
            : new[]
            {
                $"https://{Owner.ToLowerInvariant()}.github.io/{Repo}/",
                $"https://raw.githubusercontent.com/{Owner}/{Repo}/site/",
            };

    public const string Telegram = "https://t.me/echoesofangmar";
    public const string Vk = "https://vk.ru/echoesofangmar";
    public const string Wiki = "https://echoes.miraheze.org/wiki/Main_Page/ru";
    public const string Huuva = "https://lotro.huuva.org/";
    public const string OverlayMap = "https://storage.huuva.org/downloadFile?id=dPDvSDCupu";
    public const string LoreDbLc = "https://sourceforge.net/projects/lotrocompanion/files/loredb/lotro-lore-database-SoABook11-5.1.0.zip/download";
    public const string EchoesSite = "https://www.echoesofangmar.com/";
    public static string GitHub => $"https://github.com/{Owner}/{Repo}";
}
