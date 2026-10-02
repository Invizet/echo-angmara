using static EchoAngmara.Texts;
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
        Title = T("components.window_title");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg");

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock
        {
            Text = T("components.header", ("версия", manifest.Version)), FontSize = 20,
            FontFamily = (FontFamily)Application.Current.FindResource("Display"),
            Foreground = (Brush)Application.Current.FindResource("Gold"),
        });
        root.Children.Add(new TextBlock
        {
            Text = T("components.hint"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14), FontSize = 12,
            Foreground = (Brush)Application.Current.FindResource("Muted"),
        });

        foreach (var c in manifest.Components)
        {
            var box = new CheckBox { IsChecked = selected.Contains(c.Id), Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)Application.Current.FindResource("Text") };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{PartTitle(c)}  ·  {Size(c.Size)}", FontSize = 13 });
            if (!string.IsNullOrEmpty(PartDescription(c)))
                text.Children.Add(new TextBlock { Text = PartDescription(c), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Foreground = (Brush)Application.Current.FindResource("Muted") });
            box.Content = text;
            System.Windows.Automation.AutomationProperties.SetName(box, PartTitle(c));
            box.Checked += (_, _) => Validate();
            box.Unchecked += (_, _) => Validate();
            _boxes[c.Id] = box;
            root.Children.Add(box);
        }

        _warn = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), FontSize = 12, Foreground = (Brush)Application.Current.FindResource("Bad") };
        root.Children.Add(_warn);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = T("components.apply"), Style = (Style)Application.Current.FindResource("Ghost"), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = T("components.cancel"), Style = (Style)Application.Current.FindResource("Ghost"), IsCancel = true };
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
            .Select(x => T("components.requires_warning", ("часть", PartTitle(x.c)), ("нужная_часть", PartTitle(x.req!))));
        _warn.Text = string.Join("\n", problems);
    }

    static string PartTitle(Component c) => Has($"part.{c.Id}.title") ? T($"part.{c.Id}.title") : c.Title;
    static string? PartDescription(Component c) => Has($"part.{c.Id}.description") ? T($"part.{c.Id}.description") : c.Description;

    static string Size(long b) => b >= 1L << 20
        ? T("size.mb", ("число", (b / (double)(1L << 20)).ToString("0")))
        : T("size.kb", ("число", (b / 1024.0).ToString("0")));
}
