using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EchoAngmara.Core;
using EchoAngmara.Localization;
using EchoAngmara.News;

namespace EchoAngmara;

public partial class MainWindow : Window
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    OfficialConfig? _cfg;
    Account? _account;
    string? _gameDir;
    GameSession? _session;
    LocalizationManager? _l10n;
    Manifest? _manifest;
    bool _manifestFromCache;
    bool _busy;
    bool _suppressLang;
    readonly DispatcherTimer _realmTimer = new() { Interval = TimeSpan.FromSeconds(60) };

    static string ManifestCache => Path.Combine(L10nState.Dir, "manifest-ru.json");

    public MainWindow()
    {
        InitializeComponent();
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("EchoAngmara/" + typeof(App).Assembly.GetName().Version?.ToString(3));
        _realmTimer.Tick += async (_, _) => await RefreshRealmAsync();
    }

    // ================= старт =================

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = $"v{typeof(App).Assembly.GetName().Version?.ToString(3)} · официальный лаунчер {App.Official.Version.ToString(3)}";
        LoadArt();
        if (!LoadAccounts()) return;

        _l10n = new LocalizationManager(_gameDir!);
        _suppressLang = true;
        (_l10n.State.Language == GameLanguage.Russian ? LangRu : LangEn).IsChecked = true;
        _suppressLang = false;

        await Task.WhenAll(LoadNewsAsync(), ConnectAsync(), LoadManifestAsync());
    }

    void LoadArt()
    {
        string dir = AppContext.BaseDirectory;
        foreach (var name in new[] { "background.jpg", "background.png" })
        {
            string p = Path.Combine(dir, "Assets", name);
            if (File.Exists(p)) { BackgroundArt.Source = new BitmapImage(new Uri(p)); break; }
        }
        string logo = Path.Combine(dir, "Assets", "logo.png");
        if (File.Exists(logo))
        {
            LogoImage.Source = new BitmapImage(new Uri(logo));
            LogoImage.Visibility = Visibility.Visible;
            LogoText.Visibility = Visibility.Collapsed;
        }
    }

    bool LoadAccounts()
    {
        _cfg = OfficialConfig.TryRead();
        // ECHO_GAME_DIR — для разработки: подставная папка игры вместо настоящей
        _gameDir = Environment.GetEnvironmentVariable("ECHO_GAME_DIR") is { Length: > 0 } dev ? dev : _cfg?.ClientInstallLocation;
        if (_gameDir == null || !File.Exists(Path.Combine(_gameDir, "lotroclient.exe")))
        {
            Status("Официальный лаунчер ещё не знает, где лежит игра. Запустите его один раз и укажите папку клиента.", bad: true);
            return false;
        }
        var withPass = _cfg!.Accounts.Where(a => a.HasPassword).ToList();
        if (withPass.Count == 0)
        {
            AccountText.Text = "нет сохранённых";
            Status("В официальном лаунчере нет аккаунтов с сохранённым паролем. Откройте его (ссылка внизу), войдите с галочкой «Save password» и вернитесь — список обновится по кнопке «Аккаунт».", bad: true);
            return true;
        }
        string? last = L10nState.Load().SelectedUser ?? _cfg.SelectedUser;
        _account = withPass.FirstOrDefault(a => a.User == last) ?? withPass.FirstOrDefault(a => a.User == _cfg.SelectedUser) ?? withPass[0];
        AccountText.Text = _account.User;
        return true;
    }

    void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AccountButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        foreach (var a in (OfficialConfig.TryRead() ?? _cfg)?.Accounts.Where(a => a.HasPassword) ?? Enumerable.Empty<Account>())
        {
            var item = new MenuItem { Header = a.User, IsCheckable = true, IsChecked = a.User == _account?.User };
            item.Click += (_, _) =>
            {
                _account = a;
                AccountText.Text = a.User;
                if (_l10n != null) { _l10n.State.SelectedUser = a.User; _l10n.State.Save(); }
                UpdatePlayButton();
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        var reload = new MenuItem { Header = "Обновить из официального лаунчера" };
        reload.Click += (_, _) => { LoadAccounts(); UpdatePlayButton(); };
        menu.Items.Add(reload);
        menu.IsOpen = true;
    }

    async Task ConnectAsync()
    {
        Status("Получаем список серверов…");
        var r = await RealmlistFetcher.FetchAsync();
        if (r.Realmlist == null) { Status(r.Error!, bad: true); return; }
        if (r.ExpiredCertAccepted) Log.Write("Сертификат echoesofangmar.com просрочен — принят по исключению");
        try
        {
            _session = new GameSession(App.Official, r.Realmlist, _gameDir!);
        }
        catch (Exception ex)
        {
            // сюда попадём, если новое ядро официального лаунчера поменяло API
            Log.Write("Ядро: " + ex);
            Status("Не удалось подключить официальный лаунчер (возможно, он обновился и нужна новая версия «Эха Ангмара»): " + ex.Message, bad: true);
            return;
        }
        if (_session.OfficialOutdated(App.Official.Version))
            Status("Официальный лаунчер устарел: запустите его, он обновится сам, затем вернитесь сюда.", bad: true);
        else
            Status(r.ExpiredCertAccepted ? "Готово. (У сайта Echoes истёк сертификат — мы это обошли.)" : "Готово к игре.");
        await RefreshRealmAsync();
        _realmTimer.Start();
        UpdatePlayButton();
    }

    async Task RefreshRealmAsync()
    {
        if (_session == null) return;
        string realm = _cfg?.SelectedServer ?? "Dor-lómin";
        try
        {
            var info = (await RealmStatus.QueryAsync(_session.Realmlist.AuthServer, new[] { realm })).FirstOrDefault();
            if (info == null || !info.Online)
            {
                RealmDot.Fill = (Brush)FindResource("Bad");
                RealmText.Text = $"{realm} — недоступен";
            }
            else
            {
                RealmDot.Fill = (Brush)FindResource("Good");
                // задел на будущее: онлайн/вместимость/очередь
                RealmText.Text = info.Queue > 0 ? $"{realm} — очередь {info.Queue}" : $"{realm} — в сети";
                RealmText.ToolTip = $"Игроков: {info.Num} из {info.Max}";
            }
        }
        catch
        {
            RealmDot.Fill = (Brush)FindResource("Muted");
            RealmText.Text = $"{realm} — нет связи";
        }
    }

    // ================= новости =================

    async Task LoadNewsAsync()
    {
        var feed = await NewsService.LoadAsync(Http, CancellationToken.None);
        if (feed == null || feed.Items.Count == 0)
        {
            NewsPlaceholder.Text = "Новости пока недоступны. Загляните в канал Telegram.";
            return;
        }
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(16, 4, 16, 16),
            FontFamily = new FontFamily("Segoe UI, Segoe UI Emoji"),
            TextAlignment = TextAlignment.Left,
            FontSize = 13,
            Foreground = (Brush)FindResource("Text"),
            LineHeight = 19,
        };
        foreach (var item in feed.Items)
            doc.Blocks.Add(NewsService.Render(item, feed.BaseUrl, (Brush)FindResource("Gold"), (Brush)FindResource("Muted")));
        NewsViewer.Document = doc;
        NewsPlaceholder.Visibility = Visibility.Collapsed;
    }

    // ================= перевод =================

    async Task LoadManifestAsync()
    {
        try
        {
            _manifest = await LocalizationManager.FetchManifestAsync(Http, CancellationToken.None);
            Directory.CreateDirectory(L10nState.Dir);
            await File.WriteAllTextAsync(ManifestCache, _manifest.ToJson());
            _manifestFromCache = false;
        }
        catch (Exception ex)
        {
            Log.Write("Манифест: " + ex.Message);
            try { _manifest = Manifest.Parse(await File.ReadAllBytesAsync(ManifestCache)); _manifestFromCache = true; }
            catch { _manifest = null; }
        }
        await UpdateL10nStatusAsync();
    }

    async Task UpdateL10nStatusAsync()
    {
        if (_manifest == null || _l10n == null)
        {
            L10nText.Text = "хранилище недоступно";
            L10nButton.Visibility = Visibility.Collapsed;
            return;
        }
        L10nText.Text = "проверяем файлы…";
        var selected = _l10n.State.Selected.ToList();
        var check = await Task.Run(() => _l10n.Check(_manifest, selected));
        long missing = check.Where(c => !c.Ok).Sum(c => c.File.Size);
        if (missing == 0)
        {
            _l10n.State.InstalledVersion = _manifest.Version;
            // самовосстановление: файлы на месте, но раскладка local\ ↔ склад могла сорваться (или её сбил патчер)
            var lang = CurrentLang;
            try { await Task.Run(() => _l10n.Apply(_manifest, lang, selected)); }
            catch (Exception ex) { Log.Write("Раскладка при проверке: " + ex.Message); }
            L10nText.Text = $"v{_manifest.Version} установлен ✓" + (_manifestFromCache ? " (нет связи — проверка по кэшу)" : "");
            L10nText.Foreground = (Brush)FindResource("Good");
            L10nButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            bool fresh = _l10n.State.InstalledVersion == null;
            L10nText.Text = fresh
                ? $"v{_manifest.Version} не установлен — {Size(missing)}"
                : $"вышла v{_manifest.Version} — скачать {Size(missing)}";
            L10nText.Foreground = (Brush)FindResource("GoldBright");
            L10nButton.Content = fresh ? "Скачать" : "Обновить";
            L10nButton.Visibility = Visibility.Visible;
        }
    }

    async void L10nButton_Click(object sender, RoutedEventArgs e) => await InstallL10nAsync();

    async Task<bool> InstallL10nAsync()
    {
        if (_manifest == null || _l10n == null || _busy) return false;
        SetBusy(true);
        try
        {
            var ids = _l10n.State.Selected.ToList();
            Progress.Visibility = Visibility.Visible;
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress.Maximum = Math.Max(1, p.Total);
                Progress.Value = p.Done;
                Status($"Скачиваем перевод: {Size(p.Done)} из {Size(p.Total)}  {p.CurrentFile}");
            });
            var lang = CurrentLang; // читаем UI здесь — в Task.Run нельзя
            await Task.Run(() => _l10n.InstallAsync(Http, _manifest, ids, progress, CancellationToken.None));
            await Task.Run(() => _l10n.Apply(_manifest, lang, ids));
            Status($"Перевод v{_manifest.Version} установлен.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Установка перевода: " + ex);
            Status("Не удалось установить перевод: " + ex.Message + " Нажмите ещё раз — скачанное докачается.", bad: true);
            return false;
        }
        finally
        {
            Progress.Visibility = Visibility.Hidden;
            SetBusy(false);
            await UpdateL10nStatusAsync();
        }
    }

    GameLanguage CurrentLang => LangRu.IsChecked == true ? GameLanguage.Russian : GameLanguage.English;

    async void Lang_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressLang || _l10n == null) return;
        if (_manifest == null)
        {
            Status("Сначала нужно связаться с хранилищем перевода.", bad: true);
            RevertLang();
            return;
        }
        try
        {
            var lang = CurrentLang;
            await Task.Run(() => _l10n.Apply(_manifest, lang, _l10n.State.Selected.ToList()));
            Status(lang == GameLanguage.Russian ? "Игра запустится на русском." : "Игра запустится на английском (перевод убран на склад, вернуть — одним кликом).");
        }
        catch (Exception ex)
        {
            Status(ex.Message, bad: true);
            RevertLang();
        }
    }

    void RevertLang()
    {
        _suppressLang = true;
        (_l10n?.State.Language == GameLanguage.English ? LangEn : LangRu).IsChecked = true;
        _suppressLang = false;
    }

    async void Components_Click(object sender, RoutedEventArgs e)
    {
        if (_manifest == null || _l10n == null) { Status("Состав перевода появится, когда будет связь с хранилищем.", bad: true); return; }
        var dlg = new Views.ComponentsWindow(_manifest, _l10n.State.Selected) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _l10n.State.Selected = dlg.Selected;
        _l10n.State.Save();
        var lang = CurrentLang;
        try { await Task.Run(() => _l10n.Apply(_manifest, lang, dlg.Selected.ToList())); }
        catch (Exception ex) { Status(ex.Message, bad: true); }
        await UpdateL10nStatusAsync();
    }

    // ================= игра =================

    void UpdatePlayButton() => PlayButton.IsEnabled = !_busy && _session != null && _account != null;

    async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _account == null || _busy) return;
        if (LocalizationManager.GameRunning()) { Status("Игра уже запущена.", bad: true); return; }

        // перевод: если выбран русский, а файлов нет — предложим скачать
        if (CurrentLang == GameLanguage.Russian && _manifest != null && _l10n != null && L10nButton.Visibility == Visibility.Visible)
        {
            var ans = MessageBox.Show(this, "Русский перевод не установлен или устарел. Скачать сейчас?\n\n«Нет» — играть без обновления перевода.",
                "Эхо Ангмара", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ans == MessageBoxResult.Cancel) return;
            if (ans == MessageBoxResult.Yes && !await InstallL10nAsync()) return;
        }

        SetBusy(true);
        var acc = _account;
        try
        {
            string? error = null;
            void OnError(string m) => error = m;

            Status("Вход в аккаунт…");
            if (!await _session.AuthenticateAsync(acc, OnError)) { Status(error ?? "Вход не удался.", bad: true); return; }

            var realms = _session.Realms;
            var realm = realms.FirstOrDefault(r => r.Name == _cfg?.SelectedServer) ?? realms.FirstOrDefault();
            if (realm == null) { Status("Сервер не вернул ни одного мира.", bad: true); return; }
            if (!realm.IsOnline) { Status($"Мир {realm.Name} сейчас недоступен. Следите за новостями.", bad: true); return; }

            Status("Проверяем файлы игры…");
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            bool patched = await _session.PatchGameAsync(OnError, (total, done) => Dispatcher.Invoke(() =>
            {
                Progress.IsIndeterminate = false;
                Progress.Maximum = Math.Max(1, total);
                Progress.Value = done;
                Status($"Обновляем файлы игры: {Size(done)} из {Size(total)}");
            }));
            if (!patched) { Status(error ?? "Не удалось обновить файлы игры.", bad: true); return; }

            // патчер ядра мог что-то поменять — раскладываем перевод заново под выбранный язык
            if (_manifest != null && _l10n != null)
            {
                var lang = CurrentLang;
                await Task.Run(() => _l10n.Apply(_manifest, lang, _l10n.State.Selected.ToList()));
            }

            Status($"Подключаемся к миру {realm.Name}…");
            bool logged = await _session.LoginAsync(acc, realm.Name, OnError, (total, pos) =>
                Dispatcher.Invoke(() => Status($"Мир {realm.Name} заполнен. Вы в очереди: {pos + 1} из {total}.")));
            if (!logged) { Status(error ?? "Не удалось войти в мир.", bad: true); return; }

            var current = _session.Realms.First(r => r.Name == realm.Name);
            Status("Запускаем игру…");
            _session.LaunchClient(acc, current, () => Dispatcher.Invoke(() =>
            {
                WindowState = WindowState.Normal;
                Activate();
                Status("Игра закрыта.");
            }));
            await Task.Delay(1500);
            WindowState = WindowState.Minimized;
            Status("Игра запущена. Приятной игры!");
        }
        catch (Exception ex)
        {
            Log.Write("Запуск: " + ex);
            Status("Ошибка запуска: " + ex.Message, bad: true);
        }
        finally
        {
            Progress.IsIndeterminate = false;
            Progress.Visibility = Visibility.Hidden;
            SetBusy(false);
        }
    }

    // ================= мелочи =================

    void SetBusy(bool busy)
    {
        _busy = busy;
        LangRu.IsEnabled = LangEn.IsEnabled = ComponentsButton.IsEnabled = L10nButton.IsEnabled = AccountButton.IsEnabled = !busy;
        UpdatePlayButton();
    }

    void Status(string text, bool bad = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(bad ? "Bad" : "Text");
        if (bad) Log.Write(text);
    }

    static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} ГБ",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} МБ",
        _ => $"{bytes / 1024.0:0} КБ",
    };

    void Link_Click(object sender, RoutedEventArgs e)
    {
        string url = ((Button)sender).Tag switch
        {
            "Wiki" => AppLinks.Wiki,
            "Huuva" => AppLinks.Huuva,
            "Telegram" => AppLinks.Telegram,
            "Vk" => AppLinks.Vk,
            _ => AppLinks.EchoesSite,
        };
        Shell.Open(url);
    }

    void Telegram_Click(object sender, RoutedEventArgs e) => Shell.Open(AppLinks.Telegram);

    void OpenOfficial_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(App.Official.Directory, "EchoesLauncher.exe")) { WorkingDirectory = App.Official.Directory, UseShellExecute = true }); }
        catch (Exception ex) { Status("Не удалось открыть официальный лаунчер: " + ex.Message, bad: true); }
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
