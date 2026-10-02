using Velopack;

namespace EchoAngmara;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack обрабатывает свои ключи установки/обновления/удаления и при необходимости завершает процесс
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
