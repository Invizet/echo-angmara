using System.Diagnostics;
using System.Windows;
using EchoAngmara.Core;
using EchoAngmara.News;

namespace EchoAngmara.Views;

public partial class MissingOfficialWindow : Window
{
    public MissingOfficialWindow() => InitializeComponent();

    void Download_Click(object sender, RoutedEventArgs e) => Shell.Open(OfficialLauncher.DownloadUrl);

    void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (OfficialLauncher.Find() == null)
        {
            MessageBox.Show(this, "Официальный лаунчер всё ещё не найден.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // ядро нужно подгрузить до первого обращения к его типам — проще всего начать с чистого процесса
        Process.Start(Environment.ProcessPath!);
        Application.Current.Shutdown();
    }
}
