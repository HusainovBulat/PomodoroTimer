using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PomodoroTimer;

public partial class MainWindow : Window
{
    // ---- состояние ----
    readonly Stopwatch sw = new();
    long accMs;              // накоплено до последней паузы
    long blockMs = 9 * 60_000; // длина отрезка из настройки
    long totalMs, segStartMs, segLenMs;
    bool started, pausedByUser;
    readonly List<long> lapAt = new();

    readonly DispatcherTimer timer = new(DispatcherPriority.Normal);
    readonly ObservableCollection<string> log = new();
    readonly Brush fg, accent, done, bg2, bad, over;

    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PomodoroTimer", "settings.txt");

    public MainWindow()
    {
        InitializeComponent();
        fg = (Brush)FindResource("Fg");
        accent = (Brush)FindResource("Accent");
        done = (Brush)FindResource("Done");
        bg2 = (Brush)FindResource("Bg2");
        bad = (Brush)FindResource("Bad");
        over = (Brush)FindResource("Over");

        lstLog.ItemsSource = log;

        btnAdd.Click += (_, _) => AddBlock();
        btnPause.Click += (_, _) => TogglePause();
        btnReset.Click += (_, _) => ResetAll();
        txtInterval.TextChanged += (_, _) => ApplyInterval();
        lblClose.MouseLeftButtonDown += (_, e) => e.Handled = true; // не начинать перетаскивание
        lblClose.MouseLeftButtonUp += (_, _) => Close();
        miLog.Click += (_, _) => SetLogVisible(miLog.IsChecked);
        miExit.Click += (_, _) => Close();

        // перетаскивание за любое место, кроме кнопок, поля и списка (они сами обрабатывают нажатие)
        MouseLeftButtonDown += (_, _) => DragMove();

        timer.Tick += (_, _) => { UpdateView(); Schedule(); };

        LoadSettings();
        ResetAll();
    }

    // ---- логика ----
    // после истечения всего времени отсчёт не останавливается — идёт перебор
    long Elapsed() => accMs + sw.ElapsedMilliseconds;

    void Start()
    {
        if (sw.IsRunning) return;
        started = true;
        pausedByUser = false;
        sw.Restart();
    }

    void StopClock()
    {
        accMs = Elapsed();
        sw.Reset();
    }

    void TogglePause()
    {
        if (sw.IsRunning) { StopClock(); pausedByUser = true; }
        else Start();
        Refresh2();
    }

    void AddBlock()
    {
        if (!started) { Start(); Refresh2(); return; } // первое нажатие просто запускает отсчёт
        long e = Elapsed();
        long prev = lapAt.Count > 0 ? lapAt[^1] : 0;
        lapAt.Add(e);
        log.Insert(0, $"{lapAt.Count,-3} +{FmtUp(e - prev),-9} {FmtUp(e)}");
        logScroll.ScrollToTop();
        totalMs += blockMs; // перебор вычитается из нового отрезка
        segStartMs = e;
        segLenMs = blockMs;
        if (!pausedByUser) Start(); // на паузе только добавляем
        Refresh2();
    }

    void ResetAll()
    {
        sw.Reset();
        accMs = 0;
        started = pausedByUser = false;
        lapAt.Clear();
        log.Clear();
        totalMs = segLenMs = blockMs;
        segStartMs = 0;
        Refresh2();
    }

    void ApplyInterval()
    {
        long? ms = ParseInterval(txtInterval.Text);
        txtInterval.Background = ms is null ? bad : bg2;
        if (ms is null) return;
        blockMs = ms.Value;
        if (!started) totalMs = segLenMs = blockMs;
        btnAdd.Content = "+" + Short(blockMs);
        Refresh2();
    }

    void Refresh2() { UpdateView(); Schedule(); }

    void UpdateView()
    {
        long e = Elapsed();
        long rem = totalMs - e;
        bool overtime = rem <= 0;
        long seg = Math.Clamp(segLenMs - (e - segStartMs), 0, Math.Max(0, rem));

        bool paused = pausedByUser && !sw.IsRunning;
        // при переборе оба поля показывают, сколько времени прошло сверх плана
        string overStr = "+" + FmtUp(-rem);
        Set(lblSeg, overtime ? overStr : Fmt(seg));
        lblSeg.Foreground = paused ? accent : overtime ? over : seg <= 0 ? done : fg;
        Set(lblTotal, "Σ " + (overtime ? overStr : Fmt(rem)));
        lblTotal.Foreground = overtime ? over : fg;
        string state = paused ? "ПАУЗА" : overtime ? "перебор" : $"отр. {lapAt.Count + 1}/{Short(segLenMs)}";
        Set(lblInfo, $"{state} · прошло {FmtUp(e)} · из {Fmt(totalMs)}");
        string pause = sw.IsRunning ? "⏸" : "▶";
        if (!Equals(btnPause.Content, pause)) btnPause.Content = pause;
    }

    // будим таймер ровно тогда, когда изменится хотя бы одна цифра (≤ 3 раз в секунду, на паузе — никогда)
    void Schedule()
    {
        if (!sw.IsRunning) { timer.Stop(); return; }
        long e = Elapsed();
        long rem = totalMs - e;
        long seg = segLenMs - (e - segStartMs);
        long next = 1000 - e % 1000;
        if (rem > 0) next = Math.Min(next, rem % 1000 == 0 ? 1000 : rem % 1000);
        else next = Math.Min(next, 1000 - -rem % 1000); // перебор растёт вверх
        if (seg > 0) next = Math.Min(next, seg % 1000 == 0 ? 1000 : seg % 1000);
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(next + 8, 10, 1010));
        timer.Start();
    }

    static void Set(TextBlock t, string s) { if (t.Text != s) t.Text = s; }

    static string Fmt(long ms) => Hms((ms + 999) / 1000);   // обратный отсчёт: вверх
    static string FmtUp(long ms) => Hms(ms / 1000);          // прошедшее: вниз
    static string Hms(long t) => t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60}:{t % 60:00}";
    static string Short(long ms) { long t = ms / 1000; return t % 60 == 0 ? $"{t / 60}" : $"{t / 60}:{t % 60:00}"; }

    static long? ParseInterval(string s)
    {
        s = s.Trim().Replace(',', '.');
        long ms;
        var p = s.Split(':');
        if (p.Length == 2 && int.TryParse(p[0], out int m) && int.TryParse(p[1], out int sec) && (p[1].Length is 1 or 2) && sec < 60 && m >= 0)
            ms = (m * 60L + sec) * 1000;
        else if (p.Length == 1 && double.TryParse(s, System.Globalization.NumberStyles.AllowDecimalPoint,
                     System.Globalization.CultureInfo.InvariantCulture, out double mins))
            ms = (long)Math.Round(mins * 60) * 1000;
        else return null;
        return ms is >= 5_000 and <= 600 * 60_000 ? ms : null;
    }

    // ---- клавиши ----
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Add or Key.OemPlus:
                AddBlock(); e.Handled = true; break;
            case Key.Space when !txtInterval.IsKeyboardFocused:
                TogglePause(); e.Handled = true; break;
            case Key.Escape:
                Keyboard.Focus(this); e.Handled = true; break; // увести фокус из поля, но оставить окну
        }
        base.OnPreviewKeyDown(e);
    }

    // ---- окно ----
    void SetLogVisible(bool v)
    {
        var vis = v ? Visibility.Visible : Visibility.Collapsed;
        logScroll.Visibility = lblLogHead.Visibility = vis;
        miLog.IsChecked = v;
    }

    // позиция хранится в DIP, поэтому не зависит от масштаба монитора
    void LoadSettings()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - 260;
        Top = wa.Top + 40;
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var l = File.ReadAllLines(SettingsPath);
            if (l.Length > 0 && ParseInterval(l[0]) is not null) txtInterval.Text = l[0];
            if (l.Length > 2 && double.TryParse(l[1], out double x) && double.TryParse(l[2], out double y)
                && x + 20 >= SystemParameters.VirtualScreenLeft && y + 20 >= SystemParameters.VirtualScreenTop
                && x + 20 <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                && y + 20 <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            {
                Left = x;
                Top = y;
            }
            if (l.Length > 3) SetLogVisible(l[3] != "0");
        }
        catch { /* настройки необязательны */ }
        finally { ApplyInterval(); }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllLines(SettingsPath, [txtInterval.Text.Trim(), ((int)Left).ToString(), ((int)Top).ToString(),
                logScroll.Visibility == Visibility.Visible ? "1" : "0"]);
        }
        catch { }
        base.OnClosing(e);
    }
}
