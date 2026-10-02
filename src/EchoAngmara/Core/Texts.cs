using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Markup;

namespace EchoAngmara;

/// <summary>
/// Все тексты интерфейса — из texts_ru.txt (корень репозитория, вшит в exe).
/// Если рядом с exe лежит свой texts_ru.txt, строки берутся из него: так их можно править без пересборки.
/// Формат: «ключ = текст», {имя} — подстановка, \n — перенос, «(убрать)» — пустая строка.
/// </summary>
public static class Texts
{
    static readonly Regex Line = new(@"^([a-z0-9_.]+)\s*= ?(.*)$", RegexOptions.Compiled);
    static readonly Dictionary<string, string> Map = Load();

    static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        Parse(ReadEmbedded(), map);
        string local = Path.Combine(AppContext.BaseDirectory, "texts_ru.txt");
        if (File.Exists(local))
            try { Parse(File.ReadAllText(local), map); } catch { }
        return map;
    }

    static string ReadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("texts_ru.txt");
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    static void Parse(string text, Dictionary<string, string> map)
    {
        foreach (var raw in text.Split('\n'))
        {
            var m = Line.Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;
            string v = m.Groups[2].Value.TrimEnd();
            map[m.Groups[1].Value] = v == "(убрать)" ? "" : v.Replace("\\n", "\n");
        }
    }

    /// <summary>Строка по ключу; {имя} заменяются значениями. Нет ключа — видно сам ключ, чтобы опечатка бросалась в глаза.</summary>
    public static string T(string key, params (string name, object? value)[] args)
    {
        string s = Map.TryGetValue(key, out var v) ? v : key;
        foreach (var (name, value) in args)
            s = s.Replace("{" + name + "}", value?.ToString() ?? "");
        return s;
    }

    public static bool Has(string key) => Map.ContainsKey(key);
}

/// <summary>Для XAML: Text="{local:T main.play}".</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public TExtension() { }
    public TExtension(string key) => Key = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => Texts.T(Key);
}
