using System.IO;
using System.Windows;
using EchoAngmara.Core;

namespace EchoAngmara;

public partial class App : Application
{
    public static OfficialLauncher Official { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write("UNHANDLED " + ex.Exception);
            MessageBox.Show("Непредвиденная ошибка:\n" + ex.Exception.Message + "\n\nПодробности: " + Log.FilePath,
                "Эхо Ангмара", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        LoadDisplayFont();
        var official = OfficialLauncher.Find();
        if (official == null)
        {
            // Без официального лаунчера не работаем: его ядро и его сохранённые логины — основа нашего
            MainWindow = new Views.MissingOfficialWindow();
            MainWindow.Show();
            return;
        }
        Official = official;
        official.HookAssemblyResolve();
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}

public partial class App
{
    /// <summary>Trajan Pro 3 из Assets/Fonts, если он там есть (в репозиторий шрифт не кладём).</summary>
    void LoadDisplayFont()
    {
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            if (!Directory.Exists(dir) || !Directory.EnumerateFiles(dir).Any(f => f.EndsWith(".otf") || f.EndsWith(".ttf"))) return;
            Resources["Display"] = new System.Windows.Media.FontFamily(new Uri(dir + Path.DirectorySeparatorChar), "./#Trajan Pro 3");
        }
        catch (Exception ex) { Log.Write("Шрифт: " + ex.Message); }
    }
}

public static class Log
{
    public static string FilePath => Path.Combine(Localization.L10nState.Dir, "launcher.log");

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Localization.L10nState.Dir);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }
}
