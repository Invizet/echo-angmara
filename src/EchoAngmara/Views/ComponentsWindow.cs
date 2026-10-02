using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EchoAngmara.Localization;

namespace EchoAngmara.Views;

/// <summary>Выбор частей перевода. Тексты и шрифты лежат в одном файле — разделить их нельзя по построению.</summary>
public sealed class ComponentsWindow : Window
{
    readonly Dictionary<string, CheckBox> _boxes = new();
    readonly TextBlock _warn;
    readonly Manifest _manifest;

    public HashSet<string> Selected => _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToHashSet();

    public ComponentsWindow(Manifest manifest, IReadOnlyCollection<string> selected)
    {
        _manifest = manifest;
        Title = "Состав перевода";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock
        {
            Text = $"Перевод v{manifest.Version}", FontSize = 20,
            FontFamily = (FontFamily)Application.Current.FindResource("Display"),
            Foreground = (Brush)Application.Current.FindResource("Gold"),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Отметьте, что ставить в игру. Снятые части не удаляются, а убираются на склад — вернуть можно в любой момент.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14), FontSize = 12,
            Foreground = (Brush)Application.Current.FindResource("Muted"),
        });

        foreach (var c in manifest.Components)
        {
            var box = new CheckBox { IsChecked = selected.Contains(c.Id), Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)Application.Current.FindResource("Text") };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{c.Title}  ·  {Size(c.Size)}", FontSize = 13 });
            if (!string.IsNullOrEmpty(c.Description))
                text.Children.Add(new TextBlock { Text = c.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Foreground = (Brush)Application.Current.FindResource("Muted") });
            box.Content = text;
            System.Windows.Automation.AutomationProperties.SetName(box, c.Title);
            box.Checked += (_, _) => Validate();
            box.Unchecked += (_, _) => Validate();
            _boxes[c.Id] = box;
            root.Children.Add(box);
        }

        _warn = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 12, Foreground = (Brush)Application.Current.FindResource("Bad") };
        root.Children.Add(_warn);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "Применить", Style = (Style)Application.Current.FindResource("Ghost"), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Отмена", Style = (Style)Application.Current.FindResource("Ghost"), IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;
        Validate();
    }

    void Validate()
    {
        var sel = Selected;
        var problems = _manifest.Components
            .Where(c => sel.Contains(c.Id))
            .SelectMany(c => c.Requires.Where(r => !sel.Contains(r)).Select(r => (c, req: _manifest.Get(r))))
            .Where(x => x.req != null)
            .Select(x => $"«{x.c.Title}» работает только вместе с «{x.req!.Title}» — без них игра их не увидит.");
        _warn.Text = string.Join("\n", problems);
    }

    static string Size(long b) => b >= 1L << 20 ? $"{b / (double)(1L << 20):0} МБ" : $"{b / 1024.0:0} КБ";
}
