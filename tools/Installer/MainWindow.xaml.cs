using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FellSellPlus.Setup;

// One window, three states: choose (game folder + Install), working (steps), done (what next).
// The MGI Studio look of the configurator, in Russian or English by the PC's language.
public partial class MainWindow : Window
{
    string _game, _lang;
    bool Ru => _lang == "ru";
    string L(string ru, string en) => Ru ? ru : en;
    Brush B(string k) => (Brush)FindResource(k);
    FontFamily F(string k) => (FontFamily)FindResource(k);
    static string Version => typeof(MainWindow).Assembly.GetName().Version.ToString(3);

    // Relaunched elevated when the game folder is protected: --game=<dir> [--uninstall] [--ml] [--addons=<dll>:1,<dll>:0]
    readonly string _argGame;
    readonly bool _argUninstall, _argMl;

    public MainWindow()
    {
        InitializeComponent();
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        _lang = two is "ru" or "uk" or "be" or "kk" ? "ru" : "en";
        foreach (var a in Environment.GetCommandLineArgs())
        {
            if (a.StartsWith("--game=")) _argGame = a.Substring(7);
            if (a == "--uninstall") _argUninstall = true;
            if (a == "--ml") _argMl = true;
            if (a.StartsWith("--addons="))
                foreach (var kv in a.Substring(9).Split(','))
                {
                    var p = kv.Split(':');
                    if (p.Length == 2) _addonChoice[p[0]] = p[1] == "1";
                }
        }
        _game = Setup.IsGame(_argGame) ? _argGame : Setup.FindGame();
        Loaded += (_, _) =>
        {
            BuildSky();
            if (_argGame != null) { if (_argUninstall) RunUninstall(_argMl); else RunInstall(); }
        };
        if (_argGame == null) RenderChoose();
    }

    // ---------- chrome ----------

    void RenderLang()
    {
        ProductWord.Text = Spaced("FELL & SELL PLUS") + "   " + Version;
        Title = L("Fell & Sell Plus · Установка", "Fell & Sell Plus · Setup");
        LangSeg.Children.Clear();
        foreach (var (code, label) in new[] { ("ru", "RU"), ("en", "EN") })
        {
            bool on = _lang == code;
            var b = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(11, 5, 11, 5), Cursor = Cursors.Hand, Background = on ? B("Brand") : Brushes.Transparent };
            b.Child = new TextBlock { Text = label, FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 10.5, Foreground = on ? Brushes.White : B("Dim") };
            var c = code;
            b.MouseLeftButtonUp += (_, _) => { _lang = c; _rerender?.Invoke(); };
            LangSeg.Children.Add(b);
        }
    }

    Action _rerender;
    static string Spaced(string s) => string.Join(" ", s.ToCharArray());

    void BuildSky()
    {
        Sky.Children.Clear();
        double w = ActualWidth, h = ActualHeight;
        Glow(Color.FromRgb(0xFF, 0x6A, 0x3D), 0x50, -0.25 * w, 0.15 * h, 0.9 * w, 0);
        Glow(Color.FromRgb(0xFF, 0x3D, 0x9A), 0x46, 0.5 * w, -0.5 * h, 0.9 * w, 3);
        Glow(Color.FromRgb(0xFF, 0xC2, 0x3D), 0x2E, 0.3 * w, 0.6 * h, 0.7 * w, 6);
    }

    void Glow(Color c, byte alpha, double x, double y, double size, double delay)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0.72));
        var e = new Ellipse { Width = size, Height = size, Fill = brush, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        Sky.Children.Add(e);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var a = new DoubleAnimation(1, 1.14, TimeSpan.FromSeconds(5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(-delay) };
        e.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        e.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // ---------- state 1: choose ----------

    TextBox _path;
    readonly Dictionary<string, bool> _addonChoice = new();
    TextBlock _found;

    void RenderChoose()
    {
        _rerender = RenderChoose;
        RenderLang();
        Body.Children.Clear();
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = Setup.Installed(_game) ? L("Обновление мода", "Update the mod") : L("Установка мода", "Install the mod"), FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 28 });
        sp.Children.Add(new TextBlock
        {
            Text = L("Новые дары и навыки, прокачка у могилы бабушки, окно лавки, фильтр добычи и настройка сложности. Всё настраивается в отдельной программе.",
                     "New prayers and skills, progression at Grandma's grave, a shop window, a loot filter and difficulty settings. Everything is set up in a separate app."),
            FontSize = 14.5, Foreground = B("Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 26), MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left, LineHeight = 22,
        });

        sp.Children.Add(new TextBlock { Text = Spaced(L("ПАПКА ИГРЫ", "GAME FOLDER")), FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 10.5, Foreground = B("Dim"), Margin = new Thickness(2, 0, 0, 8) });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _path = new TextBox { Text = _game ?? "", Style = (Style)FindResource("Field"), FontSize = 13 };
        _path.TextChanged += (_, _) => { _game = _path.Text.Trim(); UpdateFound(); };
        row.Children.Add(_path);
        var browse = new Button { Content = L("Выбрать…", "Browse…"), Style = (Style)FindResource("Ghost"), Margin = new Thickness(12, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        sp.Children.Add(row);
        _found = new TextBlock { FontSize = 13, Margin = new Thickness(4, 10, 0, 0) };
        sp.Children.Add(_found);

        var lower = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var what = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };
        foreach (var line in new[]
                 {
                     L("MelonLoader (загрузчик модов), если его ещё нет", "MelonLoader (the mod loader), if it is not there yet"),
                     L("сам мод и программа настроек с ярлыком на рабочем столе", "the mod itself and the settings app with a desktop shortcut"),
                     L(".NET от Microsoft, если его нет на компьютере (скачается сам)", "Microsoft .NET, if the PC does not have it (downloaded for you)"),
                     L("перед установкой ваши сохранения копируются в «Документы\\Fell & Sell Plus»", "your saves are copied to \"Documents\\Fell & Sell Plus\" first"),
                 })
            what.Children.Add(Bullet(line, wrapWidth: 400));
        lower.Children.Add(what);

        // Add-ons: separate mods, on by default for a fresh install, as they are for an update.
        var addons = new StackPanel();
        addons.Children.Add(new TextBlock { Text = Spaced(L("ДОПОЛНЕНИЯ", "ADD-ONS")), FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 10.5, Foreground = B("Dim"), Margin = new Thickness(2, 0, 0, 12) });
        bool fresh = !Setup.Installed(_game);
        foreach (var a in Setup.Addons)
        {
            if (!_addonChoice.ContainsKey(a.File)) _addonChoice[a.File] = fresh ? a.OnByDefault : Setup.AddonEnabled(_game, a);
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = L(a.NameRu, a.NameEn), FontFamily = F("TextSemi"), FontSize = 14, Foreground = B("Ink") });
            label.Children.Add(new TextBlock { Text = L(a.DescRu, a.DescEn), FontSize = 12, Foreground = B("Dim"), TextWrapping = TextWrapping.Wrap, MaxWidth = 228, Margin = new Thickness(0, 2, 0, 0) });
            var sw = new CheckBox { Style = (Style)FindResource("Switch"), Content = label, IsChecked = _addonChoice[a.File], Margin = new Thickness(0, 0, 0, 14), VerticalContentAlignment = VerticalAlignment.Top };
            var file = a.File;
            sw.Checked += (_, _) => _addonChoice[file] = true;
            sw.Unchecked += (_, _) => _addonChoice[file] = false;
            addons.Children.Add(sw);
        }
        Grid.SetColumn(addons, 1);
        lower.Children.Add(addons);
        sp.Children.Add(lower);
        Body.Children.Add(sp);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        if (Setup.Installed(_game))
        {
            var un = new Button { Content = L("Удалить мод", "Uninstall"), Style = (Style)FindResource("Link"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
            un.Click += (_, _) => AskUninstall();
            actions.Children.Add(un);
        }
        var go = new Button { Content = Spaced(Setup.Installed(_game) ? L("ОБНОВИТЬ", "UPDATE") : L("УСТАНОВИТЬ", "INSTALL")), Style = (Style)FindResource("Go"), Padding = new Thickness(46, 16, 46, 16) };
        go.Click += (_, _) => RunInstall();
        actions.Children.Add(go);
        Body.Children.Add(actions);
        UpdateFound();
    }

    UIElement Bullet(string text, double wrapWidth = 0)
    {
        var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 9) };
        var d = new Rectangle { Width = 7, Height = 7, RadiusX = 2, RadiusY = 2, Fill = B("Brand"), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45), VerticalAlignment = VerticalAlignment.Center };
        r.Children.Add(d);
        var t = new TextBlock { Text = text, FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xD3, 0xEA)), Margin = new Thickness(12, 0, 0, 0) };
        if (wrapWidth > 0) { t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = wrapWidth; }
        r.Children.Add(t);
        return r;
    }

    void UpdateFound()
    {
        if (_found == null) return;
        bool ok = Setup.IsGame(_game);
        _found.Text = ok ? L("✓ Игра найдена", "✓ Game found") : L("Не вижу здесь «Fell & Sell.exe». Укажите папку игры.", "No \"Fell & Sell.exe\" here. Pick the game folder.");
        _found.Foreground = ok ? B("Ok") : B("Hot");
    }

    void Browse()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = L("Папка игры Fell & Sell", "Fell & Sell game folder"), SelectedPath = _game ?? "" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _path.Text = dlg.SelectedPath;
    }

    // ---------- state 2: working ----------

    readonly List<(string id, TextBlock text, Ellipse dot)> _steps = new();

    void RenderWorking(string title, IEnumerable<(string id, string text)> steps)
    {
        _rerender = null;
        Body.Children.Clear();
        _steps.Clear();
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 28, Margin = new Thickness(0, 0, 0, 28) });
        foreach (var (id, text) in steps)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 14) };
            var dot = new Ellipse { Width = 10, Height = 10, Fill = B("Faint"), VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBlock { Text = text, FontSize = 15, Foreground = B("Faint"), Margin = new Thickness(14, 0, 0, 0) };
            r.Children.Add(dot); r.Children.Add(t);
            sp.Children.Add(r);
            _steps.Add((id, t, dot));
        }
        Body.Children.Add(sp);
    }

    void Step(string id, string state, string note = null) => Dispatcher.Invoke(() =>
    {
        var s = _steps.FirstOrDefault(x => x.id == id);
        if (s.text == null) return;
        if (note != null) s.text.Text = note;
        switch (state)
        {
            case "run": s.dot.Fill = B("Brand"); s.text.Foreground = B("Ink"); s.dot.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x3D, 0x9A), BlurRadius = 12, ShadowDepth = 0 }; break;
            case "ok": s.dot.Fill = B("Ok"); s.text.Foreground = B("Dim"); s.dot.Effect = null; break;
            case "skip": s.dot.Fill = B("Faint"); s.text.Foreground = B("Faint"); s.dot.Effect = null; break;
        }
    });

    void RunInstall()
    {
        if (!Setup.IsGame(_game)) { UpdateFound(); return; }
        if (Setup.GameRunning())
        {
            MessageBox.Show(this, L("Закройте игру и нажмите «Установить» ещё раз.", "Close the game, then press Install again."), "Fell & Sell Plus");
            return;
        }
        if (Process.GetProcessesByName("FellSellPlusConfigurator").Length > 0)
        {
            MessageBox.Show(this, L("Закройте программу настроек Fell & Sell Plus и нажмите ещё раз.", "Close the Fell & Sell Plus settings app, then press again."), "Fell & Sell Plus");
            return;
        }
        foreach (var a in Setup.Addons) // not shown yet (elevated relaunch without the list): keep what is there
            if (!_addonChoice.ContainsKey(a.File)) _addonChoice[a.File] = !Setup.Installed(_game) ? a.OnByDefault : Setup.AddonEnabled(_game, a);
        if (!Setup.CanWrite(_game))
        {
            Elevate("--game=\"" + _game + "\" --addons=" + string.Join(",", _addonChoice.Select(kv => kv.Key + ":" + (kv.Value ? "1" : "0"))));
            return;
        }

        RenderWorking(L("Устанавливаю…", "Installing…"), new[]
        {
            ("saves", L("Копирую сохранения", "Backing up saves")),
            ("net", L("Проверяю .NET", "Checking .NET")),
            ("files", L("Ставлю MelonLoader, мод и настройки", "Installing MelonLoader, the mod and settings")),
            ("links", L("Создаю ярлык настроек", "Creating the settings shortcut")),
        });
        var game = _game;
        Task.Run(() =>
        {
            try
            {
                Step("saves", "run");
                var backup = Setup.BackupSaves();
                Step("saves", "ok", backup != null ? L("Сохранения скопированы", "Saves backed up") : L("Сохранений пока нет", "No saves yet"));

                Step("net", "run");
                if (Setup.HasDesktopRuntime()) Step("net", "ok", L(".NET уже есть", ".NET is already installed"));
                else
                {
                    Setup.InstallRuntime(phase => Step("net", "run", phase == "download" ? L("Скачиваю .NET с сайта Microsoft…", "Downloading .NET from Microsoft…") : L("Устанавливаю .NET (Windows спросит разрешение)…", "Installing .NET (Windows will ask for permission)…")));
                    Step("net", "ok", L(".NET установлен", ".NET installed"));
                }

                Step("files", "run");
                Setup.Unpack(game);
                Setup.InstallAddons(game, _addonChoice);
                Setup.TieredCompilationOff(game);
                Step("files", "ok", L("MelonLoader, мод и настройки на месте", "MelonLoader, the mod and settings are in place"));

                Step("links", "run");
                Setup.Shortcuts(game);
                Step("links", "ok", L("Ярлык «Fell & Sell Plus Settings» на рабочем столе", "\"Fell & Sell Plus Settings\" shortcut on the desktop"));
                // Straight into the settings app: it shows the first-launch note itself.
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(Setup.ConfiguratorExe(game), "--installed --lang=" + _lang) { UseShellExecute = true });
                        Close();
                    }
                    catch { RenderDone(game); }
                });
            }
            catch (Exception e)
            {
                Dispatcher.Invoke(() => RenderError(e.Message));
            }
        });
    }

    void Elevate(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, args) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch { MessageBox.Show(this, L("Нужны права администратора, чтобы писать в папку игры.", "Administrator rights are needed to write to the game folder."), "Fell & Sell Plus"); }
    }

    // ---------- state 3: done / error ----------

    void RenderDone(string game)
    {
        _rerender = () => RenderDone(game);
        RenderLang();
        Body.Children.Clear();
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = L("Готово", "Done"), FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 32 });
        sp.Children.Add(new TextBlock
        {
            Text = L("Первый запуск игры после установки длится от 2 до 5 минут: загрузчик готовит код игры, и вы увидите его чёрное окно. Это нормально, дождитесь меню. Потом окно больше не появится, а игра будет запускаться как обычно.",
                     "The first game launch after installing takes 2 to 5 minutes: the loader prepares the game code and you will see its black window. That is normal, wait for the menu. After that the window stays hidden and the game starts as usual."),
            FontSize = 14.5, Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xD3, 0xEA)), TextWrapping = TextWrapping.Wrap, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), LineHeight = 23,
        });
        sp.Children.Add(new TextBlock
        {
            Text = L("Настройки: ярлык «Fell & Sell Plus Settings» на рабочем столе. Меняйте их при закрытой игре.",
                     "Settings: the \"Fell & Sell Plus Settings\" shortcut on the desktop. Change them with the game closed."),
            FontSize = 13.5, Foreground = B("Dim"), TextWrapping = TextWrapping.Wrap, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0),
        });
        Body.Children.Add(sp);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var settings = new Button { Content = L("Открыть настройки", "Open settings"), Style = (Style)FindResource("Ghost"), Margin = new Thickness(0, 0, 12, 0) };
        settings.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(Setup.ConfiguratorExe(game)) { UseShellExecute = true }); } catch { } Close(); };
        var play = new Button { Content = Spaced(L("ИГРАТЬ", "PLAY")), Style = (Style)FindResource("Go"), Padding = new Thickness(46, 16, 46, 16) };
        play.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("steam://rungameid/" + Setup.AppId) { UseShellExecute = true }); } catch { } Close(); };
        actions.Children.Add(settings); actions.Children.Add(play);
        Body.Children.Add(actions);
    }

    void RenderError(string message)
    {
        _rerender = () => RenderError(message);
        RenderLang();
        Body.Children.Clear();
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = L("Не получилось", "Something went wrong"), FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 28, Foreground = B("Hot") });
        sp.Children.Add(new TextBlock { Text = message, FontFamily = F("Mono"), FontSize = 12.5, Foreground = B("Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
        sp.Children.Add(new TextBlock { Text = L("Ваши сохранения не тронуты. Закройте игру и Steam-загрузки и попробуйте ещё раз.", "Your saves are untouched. Close the game and try again."), FontSize = 13.5, Foreground = B("Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
        Body.Children.Add(sp);
        var back = new Button { Content = L("Назад", "Back"), Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        back.Click += (_, _) => RenderChoose();
        Body.Children.Add(back);
    }

    // ---------- uninstall ----------

    void AskUninstall()
    {
        bool others = Setup.OtherMods(_game);
        var r = MessageBox.Show(this, others
                ? L("Удалить Fell & Sell Plus?\n\nУ вас есть другие моды, поэтому загрузчик MelonLoader останется.\nСохранения не трогаются.",
                    "Remove Fell & Sell Plus?\n\nYou have other mods, so MelonLoader stays.\nSaves are not touched.")
                : L("Вернуть игру как было?\n\nБудут удалены мод, MelonLoader, программа настроек и ярлыки. Игра будет запускаться как обычная.\nСохранения не трогаются.",
                    "Restore the original game?\n\nThe mod, MelonLoader, the settings app and its shortcuts will be removed. The game will start as it shipped.\nSaves are not touched."),
            "Fell & Sell Plus", MessageBoxButton.OKCancel);
        if (r != MessageBoxResult.OK) return;
        if (Setup.GameRunning() || Process.GetProcessesByName("FellSellPlusConfigurator").Length > 0)
        { MessageBox.Show(this, L("Сначала закройте игру и программу настроек.", "Close the game and the settings app first."), "Fell & Sell Plus"); return; }
        bool ml = !others;
        if (!Setup.CanWrite(_game)) { Elevate("--game=\"" + _game + "\" --uninstall" + (ml ? " --ml" : "")); return; }
        RunUninstall(ml);
    }

    void RunUninstall(bool ml)
    {
        try
        {
            Setup.Uninstall(_game, ml);
            _rerender = null;
            Body.Children.Clear();
            Body.Children.Add(new TextBlock { Text = L("Мод удалён", "The mod is removed"), FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 28 });
            var close = new Button { Content = L("Закрыть", "Close"), Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            close.Click += (_, _) => Close();
            Body.Children.Add(close);
        }
        catch (Exception e) { RenderError(e.Message); }
    }

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
