using static EchoAngmara.Texts;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Serialization;
using EchoesLauncher.Common;

namespace EchoAngmara.Core;

/// <summary>
/// Загрузка realmlist взамен LauncherCore.FetchRealmlist.
/// Фикс: официальный http-адрес редиректит на https, и с протухшим сертификатом ядро не может стартовать.
/// Мы принимаем сертификат с единственной ошибкой «истёк срок» при совпадающем имени хоста —
/// подменённый или самоподписанный сертификат по-прежнему отвергается.
/// </summary>
public static class RealmlistFetcher
{
    public const string Url = "http://echoesofangmar.com/realmlist";

    public sealed record Result(Realmlist? Realmlist, string? Error, bool ExpiredCertAccepted);

    public static async Task<Result> FetchAsync(CancellationToken ct = default)
    {
        bool relaxed = false;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None) return true;
                if (errors == SslPolicyErrors.RemoteCertificateChainErrors && chain != null &&
                    chain.ChainStatus.All(s => s.Status is X509ChainStatusFlags.NotTimeValid or X509ChainStatusFlags.NoError))
                {
                    relaxed = true;
                    return true;
                }
                return false;
            }
        };
        try
        {
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            string xml = await http.GetStringAsync(Url, ct);
            var rl = (Realmlist?)new XmlSerializer(typeof(Realmlist)).Deserialize(new StringReader(xml));
            return rl?.AuthServer == null
                ? new Result(null, T("error.realmlist_empty"), relaxed)
                : new Result(rl, null, relaxed);
        }
        catch (Exception ex)
        {
            return new Result(null, ex is HttpRequestException or TaskCanceledException
                ? T("error.realmlist_download")
                : T("error.realmlist_other", ("сообщение", ex.Message)), relaxed);
        }
    }
}
