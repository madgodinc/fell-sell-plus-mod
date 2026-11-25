using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;

namespace FellSellPlus.Configurator;

// The configurator in the MGI Studio look: MGI brand in the title bar, dark sky with
// breathing glows and embers, glass panels, gradient main button. Inside, the game's own art: a
// location shot for every section and the game's icons, so a player recognises what each part is.
public partial class MainWindow : Window
{
    static readonly string[] Order = { "Prayers", "Skills", "Progression", "Challenge", "Loot", "LootFilter", "Inventory", "Shop", "Crafting", "Info", "Console", "Dev" };

    string _game, _lang, _current = "Prayers", _query = "";
    List<Setting> _settings = new();
    bool _dirty;
    readonly List<Storyboard> _sky = new();

    Brush B(string k) => (Brush)FindResource(k);
    FontFamily F(string k) => (FontFamily)FindResource(k);
    bool Ru => _lang == "ru";
    string L(string ru, string en) => Ru ? ru : en;

    public MainWindow()
    {
        InitializeComponent();
        _lang = Data.RememberedLang() ?? SystemLang();
        _game = Data.FindGame();
        foreach (var a in Environment.GetCommandLineArgs())
        {
            if (a.StartsWith("--section=")) _current = a[10..];
            if (a.StartsWith("--lang=")) _lang = a[7..] == "en" ? "en" : "ru";
            if (a.StartsWith("--game=") && Data.IsGame(a[7..])) _game = a[7..];
        }
        if (Environment.GetCommandLineArgs().Contains("--selftest"))
        {
            _settings = Data.LoadSchema(_game);
            Data.ReadValues(_game, _settings);
            _settings.First(x => x.Key == "CardsPerStatue").Value = "6";
            _settings.First(x => x.Key == "Preset").Value = "Nightmare";
            _settings.First(x => x.Key == "EnemyHealth" && x.Section.EndsWith("Combat")).Value = "1.75";
            Data.WriteValues(_game, _settings);
            Application.Current.Shutdown();
            return;
        }

        Loaded += (_, _) => BuildSky();
        SizeChanged += (_, _) => BuildSky();
        Activated += (_, _) => { foreach (var s in _sky) s.Resume(Sky); };
        Deactivated += (_, _) => { foreach (var s in _sky) s.Pause(Sky); };
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

        Reload();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => UpdateStatus();
        timer.Start();
        Closing += (_, e) =>
        {
            if (!_dirty) return;
            var r = MessageBox.Show(L("Есть несохранённые изменения. Сохранить?", "There are unsaved changes. Save them?"), "Fell & Sell Plus",
                MessageBoxButton.YesNoCancel);
            if (r == MessageBoxResult.Cancel) e.Cancel = true;
            else if (r == MessageBoxResult.Yes) Save();
        };
    }

    // The PC's language picks the first language: Russian for ru/uk/be/kk systems, English otherwise.
    static string SystemLang()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return two is "ru" or "uk" or "be" or "kk" ? "ru" : "en";
    }

    void Reload()
    {
        _settings = Data.LoadSchema(_game);
        if (_game != null) Data.ReadValues(_game, _settings);
        _dirty = false;
        RenderAll();
    }

    static string SectionOf(Setting s)
    {
        var sec = s.Section.Replace("FellSellPlus_", "");
        if (sec == "Challenge_Loot") return "Loot";
        if (sec.StartsWith("Challenge")) return "Challenge";
        return sec;
    }

    // ================= sky =================

    void BuildSky()
    {
        foreach (var s in _sky) s.Stop(Sky);
        _sky.Clear();
        Sky.Children.Clear();
        double w = Math.Max(ActualWidth, 800), h = Math.Max(ActualHeight, 600);
        Glow(Color.FromRgb(0xFF, 0x6A, 0x3D), 0x4A, -0.18 * w, 0.10 * h, 0.95 * w, 0);
        Glow(Color.FromRgb(0xFF, 0x3D, 0x9A), 0x42, 0.55 * w, -0.45 * h, 0.85 * w, 3.3);
        Glow(Color.FromRgb(0xFF, 0xC2, 0x3D), 0x2C, 0.25 * w, 0.62 * h, 0.7 * w, 6.6);

        var rnd = new Random(7);
        for (int i = 0; i < 34; i++)
        {
            double size = 1.5 + rnd.NextDouble() * 2.5;
            var c = rnd.Next(3) switch { 0 => Color.FromRgb(0xFF, 0xC2, 0x3D), 1 => Color.FromRgb(0xFF, 0x6A, 0x3D), _ => Color.FromRgb(0xFF, 0x3D, 0x9A) };
            var e = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(c), Opacity = 0 };
            e.Effect = new BlurEffect { Radius = 1.5 };
            var tt = new TranslateTransform();
            e.RenderTransform = tt;
            Canvas.SetLeft(e, rnd.NextDouble() * w);
            Canvas.SetTop(e, h * (0.75 + rnd.NextDouble() * 0.3));
            Sky.Children.Add(e);
            var dur = TimeSpan.FromSeconds(9 + rnd.NextDouble() * 10);
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * 18) };
            var up = new DoubleAnimation(0, -h * (0.7 + rnd.NextDouble() * 0.4), dur);
            var side = new DoubleAnimation(0, (rnd.NextDouble() - 0.5) * 160, dur);
            var fade = new DoubleAnimationUsingKeyFrames { Duration = dur };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.1)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0.75)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Storyboard.SetTarget(up, e); Storyboard.SetTargetProperty(up, new PropertyPath("RenderTransform.Y"));
            Storyboard.SetTarget(side, e); Storyboard.SetTargetProperty(side, new PropertyPath("RenderTransform.X"));
            Storyboard.SetTarget(fade, e); Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(up); sb.Children.Add(side); sb.Children.Add(fade);
            sb.Begin(Sky, true);
            _sky.Add(sb);
        }
        if (!IsActive) foreach (var s in _sky) s.Pause(Sky);
    }

    void Glow(Color c, byte alpha, double x, double y, double size, double delay)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0.72));
        var e = new Ellipse { Width = size, Height = size, Fill = brush, RenderTransformOrigin = new Point(0.5, 0.5) };
        var st = new ScaleTransform(1, 1);
        e.RenderTransform = st;
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        Sky.Children.Add(e);
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true, BeginTime = TimeSpan.FromSeconds(-delay) };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        foreach (var prop in new[] { "RenderTransform.ScaleX", "RenderTransform.ScaleY" })
        {
            var a = new DoubleAnimation(1, 1.14, TimeSpan.FromSeconds(5)) { EasingFunction = ease };
            Storyboard.SetTarget(a, e); Storyboard.SetTargetProperty(a, new PropertyPath(prop));
            sb.Children.Add(a);
        }
        var o = new DoubleAnimation(1, 0.7, TimeSpan.FromSeconds(5)) { EasingFunction = ease };
        Storyboard.SetTarget(o, e); Storyboard.SetTargetProperty(o, new PropertyPath("Opacity"));
        sb.Children.Add(o);
        sb.Begin(Sky, true);
        _sky.Add(sb);
    }

    // ================= chrome =================

    void RenderAll()
    {
        ProductWord.Text = Spaced("FELL & SELL PLUS");
        Title = L("Fell & Sell Plus · Настройки", "Fell & Sell Plus · Settings");
        Credit.Text = Spaced(L("РАЗРАБОТЧИК MGI · MAD GOD INC", "MADE BY MGI · MAD GOD INC")) + "   v" + Version();
        BrowseBtn.Content = L("изменить папку игры", "change game folder");
        RestoreBtn.Content = L("вернуть игру как было", "restore the original game");
        RestoreBtn.Visibility = _game != null ? Visibility.Visible : Visibility.Collapsed;
        GameLabel.Text = _game ?? L("папка игры не найдена", "game folder not found");
        SaveBtn.Content = Spaced(L("СОХРАНИТЬ", "SAVE"));
        ResetBtn.Content = L("Сбросить раздел", "Reset section");
        SaveBtn.IsEnabled = _game != null;
        SearchHint.Text = L("Поиск настройки", "Find a setting");
        SearchHint.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed;
        RenderLang();
        RenderTemplates();
        RenderIndex();
        RenderPage();
        UpdateStatus();
    }

    static string Version() => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";

    // WPF has no letter-spacing; hair spaces give the wide uppercase brand words their rhythm.
    static string Spaced(string s) => string.Join(" ", s.ToCharArray());

    void UpdateStatus()
    {
        if (_game == null) { StatusDot.Fill = B("Bad"); StatusText.Text = L("игра не найдена", "game not found"); return; }
        if (Data.GameRunning()) { StatusDot.Fill = B("Hot"); StatusText.Text = L("игра запущена", "game is running"); }
        else { StatusDot.Fill = B("Ok"); StatusText.Text = L("игра найдена", "game found"); }
        StatusPill.ToolTip = Data.GameRunning()
            ? L("Меняйте настройки при закрытой игре: при выходе игра перезаписывает файл настроек.",
                "Change settings with the game closed: the game rewrites the settings file when it exits.")
            : null;
    }

    void RenderLang()
    {
        LangSeg.Children.Clear();
        foreach (var (code, label) in new[] { ("ru", "RU"), ("en", "EN") })
        {
            bool on = _lang == code;
            var b = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(11, 5, 11, 5), Cursor = Cursors.Hand, Background = on ? B("Brand") : Brushes.Transparent };
            b.Child = new TextBlock { Text = label, FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 10.5, Foreground = on ? Brushes.White : B("Dim") };
            var c = code;
            b.MouseLeftButtonUp += (_, _) => { _lang = c; try { Data.Remember(_game, _lang); } catch { } RenderAll(); };
            LangSeg.Children.Add(b);
        }
    }

    // ================= templates =================

    sealed record ModTemplate(string Id, string Icon, string Ru, string En, string HintRu, string HintEn, Dictionary<string, string> Over);

    static readonly ModTemplate[] TemplateList =
    {
        new("vanilla", "preset_vanilla", "Как в игре", "Like the game",
            "Только удобства: баланс игры не меняется (3 карты у статуй, без новых даров, без прокачки у могилы, уровень 30, без испытаний).",
            "Convenience only: the game's balance stays (3 statue cards, no new prayers, no grave progression, level 30, no challenge).",
            new()
            {
                ["Prayers.CardsPerStatue"] = "3", ["Prayers.NewPrayers"] = "false", ["Progression.Enabled"] = "false",
                ["Skills.MaxPlayerLevel"] = "30", ["Challenge.Preset"] = "Off", ["Inventory.PickupRadiusMultiplier"] = "1",
            }),
        new("recommended", "preset_recommended", "Рекомендуемый", "Recommended",
            "Всё, как задумано в моде: новые дары и навыки, прокачка у могилы, все удобства.",
            "Everything as the mod intends: new prayers and skills, grave progression, every convenience.",
            new()),
        new("easy", "preset_easy", "Лёгкий", "Relaxed",
            "Для спокойной игры: враги слабее, больше опыта, добычи и денег за продажи, 4 карты у статуй.",
            "For a calm game: weaker enemies, more XP, loot and sale money, 4 statue cards.",
            new()
            {
                ["Prayers.CardsPerStatue"] = "4", ["Challenge.Preset"] = "Off",
                ["Challenge_Combat.EnemyDamage"] = "0.7", ["Challenge_Combat.EnemyHealth"] = "0.8",
                ["Challenge_XP.All"] = "1.5", ["Challenge_Shop.SalePrices"] = "1.2", ["Challenge_Loot.ItemAmount"] = "1.3",
                ["Progression.CostStepAfter"] = "300",
            }),
        new("hard", "preset_hard", "Хардкор", "Hardcore",
            "Испытание «Кошмар»: враги заметно сильнее, добыча скуднее, прокачка у могилы дороже.",
            "The Nightmare challenge: much stronger enemies, scarcer loot, pricier grave progression.",
            new()
            {
                ["Challenge.Preset"] = "Nightmare", ["Prayers.CardsPerStatue"] = "3", ["Progression.CostStepAfter"] = "750",
            }),
    };

    // Template values = the mod defaults plus the template's own changes.
    Dictionary<string, string> TemplateValues(ModTemplate t)
    {
        var d = _settings.Where(s => SectionOf(s) != "Dev" && SectionOf(s) != "Coop").ToDictionary(s => s.Id, s => s.Default);
        foreach (var kv in t.Over) if (d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
        return d;
    }

    ModTemplate Matching() => TemplateList.FirstOrDefault(t => TemplateValues(t).All(kv =>
        Setting.Same(_settings.First(s => s.Id == kv.Key).Value, kv.Value)));

    void RenderTemplates()
    {
        Templates.Children.Clear();
        var match = Matching();
        foreach (var t in TemplateList)
        {
            bool on = match == t;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Image { Source = Art("icons/" + t.Icon + ".png"), Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), Opacity = on ? 1 : 0.7 });
            row.Children.Add(new TextBlock { Text = (Ru ? t.Ru : t.En).ToUpper(CultureInfo.CurrentCulture), FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Foreground = on ? Brushes.White : B("Dim") });
            var b = new Border
            {
                CornerRadius = new CornerRadius(999), Padding = new Thickness(14, 8, 16, 8), Cursor = Cursors.Hand, Child = row,
                Background = on ? B("Brand") : Brushes.Transparent, ToolTip = Ru ? t.HintRu : t.HintEn,
            };
            if (on) b.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x3D, 0x9A), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.55 };
            var tt = t;
            b.MouseEnter += (_, _) => { if (match != tt) b.Background = B("GlassHi"); };
            b.MouseLeave += (_, _) => { if (match != tt) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (_, _) => ApplyTemplate(tt);
            Templates.Children.Add(b);
        }
        var custom = new TextBlock
        {
            Text = (match == null ? L("● свой", "● custom") : L("○ свой", "○ custom")), FontFamily = F("TextSemi"), FontSize = 11.5,
            Foreground = match == null ? B("Hot") : B("Faint"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 12, 0),
            ToolTip = L("Свои настройки: ни один шаблон не совпадает целиком.", "Your own settings: no template matches exactly."),
        };
        Templates.Children.Add(custom);
    }

    void ApplyTemplate(ModTemplate t)
    {
        foreach (var kv in TemplateValues(t))
        {
            var s = _settings.First(x => x.Id == kv.Key);
            s.Value = kv.Value;
        }
        MarkDirty();
        Status.Text = L($"Применён шаблон «{t.Ru}». Нажмите «Сохранить», чтобы записать.", $"ModTemplate \"{t.En}\" applied. Press Save to write it.");
        RenderTemplates(); RenderIndex(); RenderPage();
    }

    // ================= section list =================

    // The game's pictures are not shipped with the configurator: the mod exports them from the
    // running game into <game>/UserData/FellSellPlus/Art on its first launch. Until then (or without
    // a game folder) pictures are simply left out.
    static readonly Dictionary<string, BitmapImage> ArtCache = new();
    BitmapImage Art(string path)
    {
        if (ArtCache.TryGetValue(path, out var b)) return b;
        try
        {
            var file = _game == null ? null : System.IO.Path.Combine(_game, "UserData", "FellSellPlus", "Art", path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (file == null || !System.IO.File.Exists(file)) return null; // not cached: may appear after the game ran
            b = new BitmapImage();
            b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(file); b.EndInit();
            b.Freeze();
            return ArtCache[path] = b;
        }
        catch { return null; }
    }

    bool HasArt => Art("icons/Prayers.png") != null;
    static bool JustInstalled => Environment.GetCommandLineArgs().Contains("--installed");
    static bool DevMode => Environment.GetCommandLineArgs().Contains("--dev");

    void RenderIndex()
    {
        Index.Children.Clear();
        foreach (var sec in Order)
        {
            if (sec == "Dev" && !DevMode) continue; // debugging switches: only with --dev
            var items = _settings.Where(s => SectionOf(s) == sec).ToList();
            if (items.Count == 0) continue;
            bool selected = sec == _current && _query.Length == 0;
            int changed = items.Count(s => s.Changed);
            var (title, sub) = Texts.Section(sec, _lang);
            if (sec == "Console") Index.Children.Add(new Border { Height = 1, Background = B("Line"), Margin = new Thickness(10, 10, 10, 10) });

            var g = new Grid { Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bg = new Border { CornerRadius = new CornerRadius(12), Background = selected ? B("GlassHi") : Brushes.Transparent, BorderBrush = selected ? B("LineHi") : Brushes.Transparent, BorderThickness = new Thickness(1) };
            Grid.SetColumnSpan(bg, 3);
            g.Children.Add(bg);
            if (selected)
                g.Children.Add(new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = B("BrandV"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 10) });

            var icon = new Image { Source = Art("icons/" + sec + ".png"), Width = 30, Height = 30, Margin = new Thickness(10, 8, 0, 8), Opacity = selected ? 1 : 0.82 };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            g.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 8, 8, 8) };
            text.Children.Add(new TextBlock { Text = title, FontFamily = F("TextSemi"), FontSize = 14, Foreground = selected ? B("Ink") : new SolidColorBrush(Color.FromRgb(0xD9, 0xD3, 0xEA)), TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = B("Faint"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            if (changed > 0)
            {
                var pill = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x3D, 0x9A)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x3D, 0x9A)), BorderThickness = new Thickness(1), ToolTip = L("Изменено настроек", "Settings changed") };
                pill.Child = new TextBlock { Text = changed.ToString(), FontFamily = F("Mono"), FontSize = 11, Foreground = Brushes.White };
                Grid.SetColumn(pill, 2);
                g.Children.Add(pill);
            }
            var s1 = sec;
            g.MouseEnter += (_, _) => { if (!selected) bg.Background = B("Glass"); };
            g.MouseLeave += (_, _) => { if (!selected) bg.Background = Brushes.Transparent; };
            g.MouseLeftButtonUp += (_, _) =>
            {
                _current = s1;
                if (_query.Length > 0) { Search.Text = ""; return; } // clearing the search re-renders
                RenderIndex(); RenderPage(); Scroll.ScrollToTop();
            };
            Index.Children.Add(g);
        }
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        _query = Search.Text.Trim();
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderIndex(); RenderPage(); Scroll.ScrollToTop();
    }

    // ================= page =================

    void RenderPage()
    {
        Page.Children.Clear();
        if (_query.Length > 0) { RenderSearch(); return; }

        var (title, sub) = Texts.Section(_current, _lang);
        Page.Children.Add(Hero(_current, title, sub));

        if (_game == null)
            Page.Children.Add(Note(L("Папка игры не найдена. Нажмите «изменить папку игры» внизу и укажите папку, где лежит «Fell & Sell.exe». Пока можно смотреть настройки, но не сохранять.",
                                     "Game folder not found. Use \"change game folder\" below and pick the folder with \"Fell & Sell.exe\". You can browse settings but not save."), true));
        if (JustInstalled)
            Page.Children.Add(Note(L("Мод установлен. Первый запуск игры после установки длится от 2 до 5 минут: загрузчик модов готовит код игры, и вы увидите его чёрное окно. Это нормально, дождитесь меню. Потом игра запускается как обычно. Ваши сохранения скопированы в «Документы\\Fell & Sell Plus».",
                                     "The mod is installed. The first game launch after installing takes 2 to 5 minutes: the mod loader prepares the game code and you will see its black window. That is normal, wait for the menu. After that the game starts as usual. Your saves were copied to \"Documents\\Fell & Sell Plus\"."), true));
        if (_game != null && !HasArt)
            Page.Children.Add(Note(L("Картинки из игры появятся после первого запуска игры с модом: мод сам возьмёт их из игры, когда вы зайдёте в деревню.",
                                     "Pictures from the game appear after you first run the game with the mod: the mod takes them from the game once you reach the village."), false));
        if (_current == "Dev")
            Page.Children.Add(Note(L("Эти настройки нужны только для отладки мода. Обычному игроку их трогать не нужно.", "These settings are only for debugging the mod. Players can leave them alone."), true));

        var items = _settings.Where(s => SectionOf(s) == _current).ToList();
        if (_current == "Challenge")
        {
            var preset = items.FirstOrDefault(s => s.Key == "Preset" && s.Section == "FellSellPlus_Challenge");
            if (preset != null) Page.Children.Add(Panel(null, new[] { PresetPicker(preset) }));
            Page.Children.Add(Note(L("Каждое значение ниже либо берётся «из пресета», либо задаётся своим числом. 1 = как в обычной игре, больше 1 = сложнее или больше, меньше 1 = легче или меньше.",
                                     "Each value below either comes \"from preset\" or is your own number. 1 = the normal game, above 1 = harder or more, below 1 = easier or less."), false));
        }

        foreach (var grp in items.Where(s => !(s.Key == "Preset" && s.Section == "FellSellPlus_Challenge")).GroupBy(s => s.Section))
        {
            string head = _current == "Challenge" ? Texts.Group(grp.Key.Replace("FellSellPlus_", ""), _lang) : null;
            Page.Children.Add(Panel(head, grp.Select(Row)));
        }
    }

    void RenderSearch()
    {
        var q = _query.ToLower(CultureInfo.CurrentCulture);
        var hits = _settings.Where(s =>
        {
            var (n, h) = Texts.Key(s.Id, _lang, s);
            return (n + " " + h + " " + s.Key).ToLower(CultureInfo.CurrentCulture).Contains(q);
        }).ToList();
        Page.Children.Add(new TextBlock
        {
            Text = hits.Count == 0 ? L("Ничего не нашлось", "Nothing found") : L($"Найдено: {hits.Count}", $"Found: {hits.Count}"),
            FontFamily = F("Display"), FontSize = 20, Margin = new Thickness(4, 6, 0, 14),
        });
        foreach (var grp in hits.GroupBy(SectionOf).OrderBy(g => Array.IndexOf(Order, g.Key)))
            Page.Children.Add(Panel(Texts.Section(grp.Key, _lang).title, grp.Select(Row)));
    }

    // Section header: the game's own location shot behind the title, darkened towards the text.
    UIElement Hero(string sec, string title, string sub)
    {
        var g = new Grid { Height = 176, Margin = new Thickness(0, 0, 0, 16) };
        var clip = new Border { CornerRadius = new CornerRadius(18), BorderBrush = B("Line"), BorderThickness = new Thickness(1) };
        var shot = Art("scenes/" + sec + ".png");
        clip.Background = shot != null ? new ImageBrush(shot) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center } : B("Glass");
        g.Children.Add(clip);
        var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.4) };
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, 0x07, 0x06, 0x0C), 0));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0x07, 0x06, 0x0C), 0.45));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0x07, 0x06, 0x0C), 1));
        g.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = shade });
        // a warm brand edge along the bottom
        g.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 18, 0), Background = B("Brand"), Opacity = 0.8 });

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
        var icon = new Image { Source = Art("icons/" + sec + ".png"), Width = 64, Height = 64, Margin = new Thickness(0, 0, 20, 0) };
        icon.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 2, Opacity = 0.6 };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        row.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 700 };
        text.Children.Add(new TextBlock { Text = title, FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 26, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = sub, FontSize = 14.5, Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xD3, 0xEA)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        row.Children.Add(text);
        g.Children.Add(row);
        return g;
    }

    UIElement Note(string text, bool warn)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 14),
            Background = warn ? new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0x6A, 0x3D)) : B("Well"),
            BorderBrush = warn ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x6A, 0x3D)) : B("Line"), BorderThickness = new Thickness(1),
        };
        b.Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = warn ? B("Ink") : B("Dim"), LineHeight = 20 };
        return b;
    }

    // Glass panel with an optional diamond-marked title; rows are separated by hairlines.
    UIElement Panel(string title, IEnumerable<UIElement> rows)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 14, 18, 8), Margin = new Thickness(0, 0, 0, 14),
            Background = B("Glass"), BorderBrush = B("Line"), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = 0.35 },
        };
        var sp = new StackPanel();
        if (title != null)
        {
            var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            var diamond = new Rectangle { Width = 8, Height = 8, RadiusX = 2, RadiusY = 2, Fill = B("Brand"), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45), VerticalAlignment = VerticalAlignment.Center };
            diamond.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x3D, 0x9A), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
            h.Children.Add(diamond);
            h.Children.Add(new TextBlock { Text = Spaced(title.ToUpper(CultureInfo.CurrentCulture)), FontFamily = F("DisplayBold"), FontWeight = FontWeights.Bold, FontSize = 11.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(h);
        }
        bool first = true;
        foreach (var r in rows)
        {
            if (!first) sp.Children.Add(new Border { Height = 1, Background = B("Line") });
            first = false;
            sp.Children.Add(r);
        }
        b.Child = sp;
        return b;
    }

    // Challenge difficulty: five pills, the chosen one lit.
    UIElement PresetPicker(Setting preset)
    {
        var wrap = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        var (name, hint) = Texts.Key(preset.Id, _lang, preset);
        wrap.Children.Add(new TextBlock { Text = name, FontFamily = F("TextSemi"), FontSize = 15.5 });
        wrap.Children.Add(new TextBlock { Text = hint, FontSize = 13, Foreground = B("Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
        string[] ru = { "Выкл", "Ветеран", "Кошмар", "Богохульный", "Безумие" };
        string[] icons = { "preset_vanilla", "preset_easy", "Challenge", "preset_hard", "evil" };
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        for (int i = 0; i < Data.PresetNames.Length; i++)
        {
            var key = Data.PresetNames[i];
            row.Children.Add(Pill(Ru ? ru[i] : key, icons[i], string.Equals(preset.Value, key, StringComparison.OrdinalIgnoreCase),
                () => { preset.Value = key; MarkDirty(); RenderTemplates(); RenderPage(); RenderIndex(); }));
        }
        wrap.Children.Add(row);
        return wrap;
    }

    Border Pill(string text, string icon, bool on, Action click)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null) sp.Children.Add(new Image { Source = Art("icons/" + icon + ".png"), Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
        sp.Children.Add(new TextBlock { Text = text, FontFamily = F("TextSemi"), FontSize = 13, Foreground = on ? Brushes.White : B("Dim"), VerticalAlignment = VerticalAlignment.Center });
        var b = new Border
        {
            CornerRadius = new CornerRadius(999), Padding = new Thickness(14, 7, 16, 7), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Child = sp,
            Background = on ? B("Brand") : B("Well"), BorderBrush = on ? Brushes.Transparent : B("Line"), BorderThickness = new Thickness(1),
        };
        b.MouseEnter += (_, _) => { if (!on) b.BorderBrush = B("Hot"); };
        b.MouseLeave += (_, _) => { if (!on) b.BorderBrush = B("Line"); };
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    // One setting: changed dot | name + explanation | control | reset.
    UIElement Row(Setting s)
    {
        var g = new Grid { MinHeight = 68 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        if (s.Changed)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Fill = B("Brand"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = L("Изменено", "Changed") };
            dot.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x3D, 0x9A), BlurRadius = 8, ShadowDepth = 0 };
            g.Children.Add(dot);
        }

        var (name, hint) = Texts.Key(s.Id, _lang, s);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 20, 12) };
        text.Children.Add(new TextBlock { Text = name, FontFamily = F("TextSemi"), FontSize = 15, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(hint))
            text.Children.Add(new TextBlock { Text = hint, FontSize = 12.5, Foreground = B("Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 18 });
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        var control = Control(s);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 2);
        g.Children.Add(control);

        if (s.Changed)
        {
            var reset = new Button { Content = L("сбросить", "reset"), Style = (Style)FindResource("Link"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = L("Вернуть значение по умолчанию: ", "Back to the default: ") + s.Default };
            reset.Click += (_, _) => { s.Value = s.Default; Changed(); };
            Grid.SetColumn(reset, 3);
            g.Children.Add(reset);
        }
        return g;
    }

    void Changed() { MarkDirty(); RenderTemplates(); RenderPage(); RenderIndex(); }

    static readonly Dictionary<string, string[]> Choices = new()
    {
        ["Role"] = new[] { "host", "client" },
        ["Transport"] = new[] { "steam", "udp" },
    };

    FrameworkElement Control(Setting s)
    {
        if (s.IsChallengeParam) return ChallengeControl(s);
        if (Choices.TryGetValue(s.Key, out var options))
        {
            var row = new WrapPanel();
            foreach (var o in options)
            {
                var label = o switch { "host" => L("Хост", "Host"), "client" => L("Клиент", "Client"), "steam" => "Steam", "udp" => L("Локальная сеть", "LAN (UDP)"), _ => o };
                var oo = o;
                row.Children.Add(Pill(label, null, string.Equals(s.Value, o, StringComparison.OrdinalIgnoreCase), () => { s.Value = oo; Changed(); }));
            }
            return row;
        }
        if (s.Type == "String" && s.Key.EndsWith("Key") && s.Key != "SkipModules") return KeyControl(s);
        switch (s.Type)
        {
            case "Boolean":
            {
                bool on = s.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                var cb = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = on };
                cb.Content = new TextBlock { Text = on ? L("включено", "on") : L("выключено", "off"), FontSize = 13, Foreground = on ? B("Ink") : B("Faint") };
                cb.Click += (_, _) => { s.Value = cb.IsChecked == true ? "true" : "false"; Changed(); };
                return cb;
            }
            case "Int32":
            case "Single":
                return NumberControl(s, s.Value, v => { s.Value = v; MarkDirty(); });
            default:
                return TextControl(s);
        }
    }

    FrameworkElement TextControl(Setting s)
    {
        var sp = new StackPanel();
        var tb = new TextBox { Text = s.Value, Style = (Style)FindResource("Field") };
        tb.LostFocus += (_, _) => { if (s.Value != tb.Text) { s.Value = tb.Text; MarkDirty(); RenderIndex(); RenderTemplates(); } };
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
        sp.Children.Add(tb);
        string fmt = s.Key switch
        {
            "PrestigeTiers" => L("формат: престиж:лимит, через запятую", "format: prestige:cap, comma-separated"),
            "CostSchedule" => L("формат: цены через запятую", "format: prices, comma-separated"),
            "FriendSteamIds" => L("формат: SteamID64 через запятую", "format: SteamID64, comma-separated"),
            "SkipModules" => L("только для отладки", "debugging only"),
            _ => null,
        };
        if (fmt != null) sp.Children.Add(new TextBlock { Text = fmt, FontSize = 11.5, Foreground = B("Faint"), Margin = new Thickness(4, 5, 0, 0) });
        return sp;
    }

    // Key binding: click the field, press a key. Names follow Unity's Input System (J, F7, Tab, Digit1).
    FrameworkElement KeyControl(Setting s)
    {
        var tb = new TextBox { Text = s.Value, Style = (Style)FindResource("Field"), IsReadOnly = true, Cursor = Cursors.Hand, Width = 170, HorizontalAlignment = HorizontalAlignment.Left, TextAlignment = TextAlignment.Center };
        tb.GotKeyboardFocus += (_, _) => tb.Text = L("нажмите клавишу…", "press a key…");
        tb.LostKeyboardFocus += (_, _) => tb.Text = s.Value;
        tb.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (k == Key.Escape) { Keyboard.ClearFocus(); return; }
            string name = k switch
            {
                >= Key.D0 and <= Key.D9 => "Digit" + (k - Key.D0),
                >= Key.NumPad0 and <= Key.NumPad9 => "Numpad" + (k - Key.NumPad0),
                Key.Tab => "Tab", Key.Space => "Space", Key.Return => "Enter", Key.Back => "Backspace",
                Key.LeftShift => "LeftShift", Key.RightShift => "RightShift", Key.LeftCtrl => "LeftCtrl", Key.RightCtrl => "RightCtrl",
                Key.LeftAlt => "LeftAlt", Key.RightAlt => "RightAlt", Key.OemTilde => "Backquote",
                _ => k.ToString(),
            };
            s.Value = name;
            Keyboard.ClearFocus();
            Changed();
        };
        return tb;
    }

    // Slider + number box. Ranges come from the mod; settings without one get a span around the default.
    FrameworkElement NumberControl(Setting s, string value, Action<string> set)
    {
        bool isInt = s.Type == "Int32";
        double.TryParse(value?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
        double.TryParse(s.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out var def);
        double min = s.IsChallengeParam ? 0 : s.Min ?? 0;
        double max = s.Max ?? Math.Max(Math.Abs(def) * 4, isInt ? 100 : 10);
        if (max <= min) max = min + 1;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        var slider = new Slider
        {
            Style = (Style)FindResource("Range"), Minimum = min, Maximum = max, Value = Math.Clamp(v, min, max),
            IsSnapToTickEnabled = true, TickFrequency = isInt ? 1 : (max <= 2 ? 0.01 : max <= 20 ? 0.05 : 0.5), VerticalAlignment = VerticalAlignment.Center,
        };
        var box = new TextBox { Text = Show(v, isInt), Style = (Style)FindResource("Field"), Margin = new Thickness(14, 0, 0, 0), TextAlignment = TextAlignment.Right };
        Grid.SetColumn(box, 1);
        bool sync = false;
        slider.ValueChanged += (_, e) =>
        {
            if (sync) return;
            sync = true; box.Text = Show(e.NewValue, isInt); sync = false;
            set(Show(e.NewValue, isInt, invariant: true));
        };
        slider.PreviewMouseLeftButtonUp += (_, _) => RefreshSoon();
        box.LostFocus += (_, _) =>
        {
            if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var nv)) { box.Text = Show(slider.Value, isInt); return; }
            if (s.Min != null) nv = Math.Max(nv, s.IsChallengeParam ? 0 : s.Min.Value);
            if (s.Max != null) nv = Math.Min(nv, s.Max.Value);
            sync = true; slider.Value = Math.Clamp(nv, min, max); sync = false;
            box.Text = Show(nv, isInt);
            set(Show(nv, isInt, invariant: true));
            RefreshSoon();
        };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
        grid.Children.Add(slider);
        grid.Children.Add(box);
        return grid;
    }

    static string Show(double v, bool isInt, bool invariant = false)
    {
        var c = invariant ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
        return isInt ? Math.Round(v).ToString(c) : Math.Round(v, 2).ToString("0.##", c);
    }

    // Challenge value: "from preset" switch, else a number (or on/off for 0/1 switches).
    FrameworkElement ChallengeControl(Setting s)
    {
        double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
        bool fromPreset = v < 0;
        double presetValue = PresetValue(s);
        bool isSwitch = s.Max == 1 && s.Description.Contains("(0/1)");

        var sp = new StackPanel();
        var cb = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = fromPreset };
        cb.Content = new TextBlock
        {
            Text = L("из пресета", "from preset") + "  ·  " + (isSwitch ? OnOff(presetValue) : Show(presetValue, false)),
            FontSize = 13, Foreground = fromPreset ? B("Ink") : B("Faint"),
        };
        cb.Click += (_, _) => { s.Value = cb.IsChecked == true ? "-1" : presetValue.ToString(CultureInfo.InvariantCulture); Changed(); };
        sp.Children.Add(cb);

        if (!fromPreset)
        {
            if (isSwitch)
            {
                var on = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = v >= 0.5, Margin = new Thickness(0, 10, 0, 0) };
                on.Content = new TextBlock { Text = OnOff(v), FontSize = 13 };
                on.Click += (_, _) => { s.Value = on.IsChecked == true ? "1" : "0"; Changed(); };
                sp.Children.Add(on);
            }
            else
            {
                var num = NumberControl(s, s.Value, nv => { s.Value = nv; MarkDirty(); });
                num.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(num);
            }
        }
        return sp;
    }

    string OnOff(double v) => v >= 0.5 ? L("включено", "on") : L("выключено", "off");

    double PresetValue(Setting s)
    {
        var preset = _settings.FirstOrDefault(x => x.Section == "FellSellPlus_Challenge" && x.Key == "Preset")?.Value ?? "Off";
        int i = Array.FindIndex(Data.PresetNames, n => n.Equals(preset, StringComparison.OrdinalIgnoreCase));
        if (i <= 0 || s.Presets == null || s.Presets.Length < i) return s.Vanilla ?? 1;
        return s.Presets[i - 1];
    }

    // ================= actions =================

    bool _refreshPending;
    void RefreshSoon()
    {
        if (_refreshPending) return;
        _refreshPending = true;
        Dispatcher.BeginInvoke(() => { _refreshPending = false; RenderIndex(); RenderTemplates(); }, DispatcherPriority.Background);
    }

    void MarkDirty()
    {
        _dirty = true;
        Status.Text = L("Есть несохранённые изменения", "Unsaved changes");
        Status.Foreground = B("Hot");
    }

    void Save()
    {
        if (_game == null) return;
        try
        {
            Data.WriteValues(_game, _settings);
            _dirty = false;
            Status.Foreground = B("Ok");
            Status.Text = Data.GameRunning()
                ? L("Сохранено. Игра запущена: изменения заработают после перезапуска.", "Saved. The game is running: restart it to apply.")
                : L("Сохранено. Изменения заработают при следующем запуске игры.", "Saved. Changes apply the next time the game starts.");
        }
        catch (Exception e) { Status.Foreground = B("Bad"); Status.Text = L("Не удалось сохранить: ", "Could not save: ") + e.Message; }
    }

    void Save_Click(object sender, RoutedEventArgs e) { Keyboard.ClearFocus(); Save(); RenderPage(); RenderIndex(); RenderTemplates(); }

    void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        var items = _query.Length > 0 ? new List<Setting>() : _settings.Where(s => SectionOf(s) == _current).ToList();
        if (items.All(s => !s.Changed)) { Status.Foreground = B("Dim"); Status.Text = L("В этом разделе всё и так по умолчанию.", "Everything in this section is already default."); return; }
        foreach (var s in items) s.Value = s.Default;
        Changed();
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = L("Папка игры Fell & Sell", "Fell & Sell game folder") };
        if (dlg.ShowDialog(this) != true) return;
        if (!Data.IsGame(dlg.FolderName)) { Status.Foreground = B("Bad"); Status.Text = L("В этой папке нет «Fell & Sell.exe».", "No \"Fell & Sell.exe\" in that folder."); return; }
        _game = dlg.FolderName;
        Data.Remember(_game, _lang);
        Reload();
    }

    // Removes the mod, MelonLoader (unless other mods need it), this app and its shortcuts, so the
    // game starts exactly as it shipped. Saves stay.
    void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_game == null) return;
        if (Data.GameRunning()) { MessageBox.Show(this, L("Сначала закройте игру.", "Close the game first."), Title); return; }
        bool others = Restore.OtherMods(_game);
        var text = others
            ? L("Удалить Fell & Sell Plus и эту программу?\n\nУ вас есть другие моды, поэтому загрузчик MelonLoader останется, чтобы они работали.\nСохранения не трогаются.",
                "Remove Fell & Sell Plus and this app?\n\nYou have other mods, so the MelonLoader loader stays for them.\nSaves are not touched.")
            : L("Вернуть игру как было?\n\nБудут удалены мод, загрузчик MelonLoader, эта программа и ярлыки. Игра будет запускаться как обычная.\nСохранения не трогаются.",
                "Restore the original game?\n\nThe mod, the MelonLoader loader, this app and its shortcuts will be removed. The game will start as it shipped.\nSaves are not touched.");
        if (MessageBox.Show(this, text, Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        try
        {
            Restore.Run(_game);
            _dirty = false;
            MessageBox.Show(this, L("Готово. Игра вернулась в обычный вид.", "Done. The game is back to its original state."), Title);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Status.Foreground = B("Bad");
            Status.Text = L("Не получилось: ", "Failed: ") + ex.Message;
        }
    }

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
