// Пинг by mysik — пинг до серверов в реальном времени, потери, обрывы и отчёт для провайдера.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Mysik;
using IOPath = System.IO.Path;
using WF = System.Windows.Forms;
using SD = System.Drawing;
using NetPing = System.Net.NetworkInformation.Ping;

[assembly: AssemblyTitle("Пинг")]
[assembly: AssemblyDescription("Пинг by mysik — монитор пинга и обрывов интернета")]
[assembly: AssemblyCompany("mysik")]
[assembly: AssemblyProduct("Пинг by mysik")]
[assembly: AssemblyCopyright("by mysik")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PingApp
{
    static class Program
    {
        [STAThread]
        static void Main() { Boot.Run("Пинг", "PingByMysik_SingleInstance", () => new MainWindow()); }
    }

    class Target
    {
        public string Name, Host;
        public bool Gateway, Custom;
        public readonly List<double> Samples = new List<double>();  // мс, -1 — пакет потерян
        public bool Pending;
        public NetPing Pinger = new NetPing();
        public long Sent, Lost;
        // интерфейс
        public Border Row;
        public TextBlock Ms, Sub;
        public Canvas Spark;

        public const int Keep = 300; // 5 минут при опросе раз в секунду

        public void Add(double ms)
        {
            Samples.Add(ms);
            if (Samples.Count > Keep) Samples.RemoveAt(0);
            Sent++;
            if (ms < 0) Lost++;
        }

        public IEnumerable<double> Last(int n) { return Samples.Skip(Math.Max(0, Samples.Count - n)); }

        public double Loss(int n)
        {
            var l = Last(n).ToList();
            return l.Count == 0 ? 0 : l.Count(x => x < 0) * 100.0 / l.Count;
        }

        public double Avg(int n)
        {
            var ok = Last(n).Where(x => x >= 0).ToList();
            return ok.Count == 0 ? -1 : ok.Average();
        }

        public double Jitter(int n)
        {
            var ok = Last(n).Where(x => x >= 0).ToList();
            if (ok.Count < 2) return 0;
            double s = 0;
            for (int i = 1; i < ok.Count; i++) s += Math.Abs(ok[i] - ok[i - 1]);
            return s / (ok.Count - 1);
        }

        public bool FailedLast(int n)
        {
            var l = Last(n).ToList();
            return l.Count >= n && l.All(x => x < 0);
        }
    }

    class Outage
    {
        public DateTime Start, End;
        public bool LocalProblem;  // роутер тоже не отвечал
    }

    class MainWindow : ShellWindow
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static readonly string Dir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ping by mysik");

        readonly List<Target> targets = new List<Target>();
        readonly List<Outage> outages = new List<Outage>();
        Target selected;
        DateTime started = DateTime.Now;
        DateTime? offlineSince;
        bool offlineLocal, notify = true, suppress, startHidden, reallyExit, hintShown;
        int status = -1;
        DispatcherTimer timer;
        WF.NotifyIcon tray;
        SD.Icon[] trayIcons;

        Ellipse statusGlow, statusDot;
        TextBlock statusTitle, statusSub, chartTitle, chartStats, addPlaceholder, outageEmpty;
        Canvas chart;
        StackPanel targetList, outageList;
        TextBox addBox;
        Button addBtn, reportBtn;
        CheckBox swNotify, swAutostart;

        public MainWindow()
        {
            LoadUi("Пинг by mysik", 460, 820);
            startHidden = Environment.GetCommandLineArgs().Any(a => a == "--tray");
            if (startHidden) { Opacity = 0; ShowInTaskbar = false; }

            statusGlow = F<Ellipse>("StatusGlow");
            statusDot = F<Ellipse>("StatusDot");
            statusTitle = F<TextBlock>("StatusTitle");
            statusSub = F<TextBlock>("StatusSub");
            chartTitle = F<TextBlock>("ChartTitle");
            chartStats = F<TextBlock>("ChartStats");
            addPlaceholder = F<TextBlock>("AddPlaceholder");
            outageEmpty = F<TextBlock>("OutageEmpty");
            chart = F<Canvas>("Chart");
            targetList = F<StackPanel>("TargetList");
            outageList = F<StackPanel>("OutageList");
            addBox = F<TextBox>("AddBox");
            addBtn = F<Button>("AddBtn");
            reportBtn = F<Button>("ReportBtn");
            swNotify = F<CheckBox>("SwNotify");
            swAutostart = F<CheckBox>("SwAutostart");

            addBox.TextChanged += (s, e) => addPlaceholder.Visibility = addBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            addBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) AddCustom(); };
            addBtn.Click += (s, e) => AddCustom();
            reportBtn.Click += (s, e) => SaveReport();
            swNotify.Checked += (s, e) => { if (!suppress) { notify = true; SaveSettings(); } };
            swNotify.Unchecked += (s, e) => { if (!suppress) { notify = false; SaveSettings(); } };
            swAutostart.Checked += (s, e) => SetAutostart(true);
            swAutostart.Unchecked += (s, e) => SetAutostart(false);
            chart.SizeChanged += (s, e) => DrawChart();

            trayIcons = new[] { MakeIcon(SD.Color.FromArgb(47, 179, 110)), MakeIcon(SD.Color.FromArgb(242, 179, 61)), MakeIcon(SD.Color.FromArgb(240, 82, 95)) };
            BuildTray();
        }

        public override void Init()
        {
            string gw = Gateway();
            if (gw != null) targets.Add(new Target { Name = "Роутер", Host = gw, Gateway = true });
            targets.Add(new Target { Name = "Google DNS", Host = "8.8.8.8" });
            targets.Add(new Target { Name = "Cloudflare", Host = "1.1.1.1" });
            targets.Add(new Target { Name = "Яндекс", Host = "ya.ru" });
            LoadSettings();
            foreach (var t in targets) BuildRow(t);
            Select(targets.FirstOrDefault(t => !t.Gateway) ?? targets[0]);

            suppress = true;
            swNotify.IsChecked = notify;
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) swAutostart.IsChecked = k != null && k.GetValue("Ping by mysik") != null;
            suppress = false;

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => Tick();
            timer.Start();
            RenderOutages();
            if (startHidden) { Hide(); Opacity = 1; ShowInTaskbar = true; }
        }

        static string Gateway()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var g = ni.GetIPProperties().GatewayAddresses.Select(x => x.Address)
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                    if (g != null) return g.ToString();
                }
            }
            catch { }
            return null;
        }

        // -------------------------------------------------- опрос

        void Tick()
        {
            foreach (var t in targets)
            {
                if (t.Pending) continue;
                t.Pending = true;
                var target = t;
                PingOnce(target);
            }
            UpdateStatus();
            foreach (var t in targets) UpdateRow(t);
            DrawChart();
        }

        async void PingOnce(Target t)
        {
            double ms = -1;
            try
            {
                var r = await t.Pinger.SendPingAsync(t.Host, 2000);
                if (r.Status == IPStatus.Success) ms = Math.Max(0.5, r.RoundtripTime);
            }
            catch { }
            t.Add(ms);
            t.Pending = false;
        }

        void UpdateStatus()
        {
            var net = targets.Where(t => !t.Gateway).ToList();
            if (net.Count == 0 || net.All(t => t.Samples.Count < 3)) return;
            var gw = targets.FirstOrDefault(t => t.Gateway);

            bool offline = net.All(t => t.FailedLast(3));
            if (offline && offlineSince == null)
            {
                offlineSince = DateTime.Now.AddSeconds(-3);
                offlineLocal = gw != null && gw.FailedLast(3);
                if (notify) Balloon("Интернет пропал", offlineLocal ? "Не отвечает даже роутер — проверь Wi-Fi или кабель" : "Роутер работает, а интернета нет — похоже, проблема у провайдера", WF.ToolTipIcon.Warning);
            }
            else if (offline && gw != null && gw.FailedLast(3)) offlineLocal = true;
            else if (!offline && offlineSince != null)
            {
                var o = new Outage { Start = offlineSince.Value, End = DateTime.Now, LocalProblem = offlineLocal };
                outages.Insert(0, o);
                offlineSince = null;
                SaveOutage(o);
                RenderOutages();
                if (notify) Balloon("Интернет вернулся", "Не было связи " + Dur(o.End - o.Start), WF.ToolTipIcon.Info);
            }

            double loss = net.Average(t => t.Loss(60));
            var avgs = net.Select(t => t.Avg(60)).Where(a => a >= 0).ToList();
            double avg = avgs.Count > 0 ? avgs.Min() : -1;
            double jitter = net.Select(t => t.Jitter(60)).DefaultIfEmpty(0).Min();

            int st = offline ? 2 : (loss > 3 || avg > 150 || jitter > 40) ? 1 : 0;
            var c = st == 0 ? CGreen : st == 1 ? CAmber : CRed;
            statusDot.Fill = Br(c);
            statusGlow.Fill = Br(c);
            if (offline)
            {
                statusTitle.Text = "Нет интернета";
                statusSub.Text = (offlineLocal ? "Роутер не отвечает — проверь Wi-Fi или кабель" : "Роутер отвечает — похоже, проблема у провайдера") +
                                 " · уже " + Dur(DateTime.Now - offlineSince.Value);
            }
            else
            {
                statusTitle.Text = st == 0 ? "Связь стабильная" : loss > 3 ? "Теряются пакеты" : avg > 150 ? "Высокий пинг" : "Пинг скачет";
                statusSub.Text = "Пинг " + (avg >= 0 ? Math.Round(avg) + " мс" : "—") + " · потери " + Math.Round(loss, 1) + "% · разброс " + Math.Round(jitter) + " мс" +
                                 " · слежу " + Dur(DateTime.Now - started);
            }
            if (st != status)
            {
                status = st;
                tray.Icon = trayIcons[st];
                tray.Text = "Пинг by mysik — " + statusTitle.Text.ToLowerInvariant();
            }
        }

        static string Dur(TimeSpan d)
        {
            if (d.TotalSeconds < 60) return (int)d.TotalSeconds + " с";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " мин " + d.Seconds + " с";
            return (int)d.TotalHours + " ч " + d.Minutes + " мин";
        }

        // -------------------------------------------------- серверы

        void BuildRow(Target t)
        {
            var g = Columns(Star, Auto, Auto);
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(Txt(t.Name, 13.5, CText, true));
            t.Sub = Txt(t.Host, 11.5, CFaint, false);
            left.Children.Add(t.Sub);
            g.Children.Add(left);
            t.Spark = new Canvas { Width = 96, Height = 26, Margin = new Thickness(10, 0, 10, 0), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(At(t.Spark, 1));
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            t.Ms = new TextBlock { Text = "…", FontSize = 16, FontWeight = FontWeights.SemiBold, MinWidth = 62, TextAlignment = TextAlignment.Right, Foreground = Br(CText), VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(t.Ms);
            if (t.Custom)
            {
                var x = new Button { Style = S("IconBtn"), Content = Glyph("", 10, CFaint), Width = 26, Height = 26, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Убрать" };
                x.Click += (s, e) => { e.Handled = true; RemoveTarget(t); };
                right.Children.Add(x);
            }
            g.Children.Add(At(right, 2));
            t.Row = new Border { Child = g, Padding = new Thickness(10, 9, 10, 9), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            t.Row.MouseLeftButtonUp += (s, e) => Select(t);
            targetList.Children.Add(t.Row);
        }

        void Select(Target t)
        {
            selected = t;
            foreach (var x in targets) if (x.Row != null) x.Row.Background = x == t ? Br(CCard2) : Brushes.Transparent;
            DrawChart();
        }

        void UpdateRow(Target t)
        {
            double last = t.Samples.Count > 0 ? t.Samples[t.Samples.Count - 1] : double.NaN;
            if (double.IsNaN(last)) return;
            t.Ms.Text = last < 0 ? "нет" : Math.Round(last) + " мс";
            t.Ms.Foreground = Br(last < 0 ? CRed : last > 150 ? CAmber : CText);
            double loss = t.Loss(60);
            t.Sub.Text = t.Host + (loss > 0 ? " · потери " + Math.Round(loss) + "%" : "");
            DrawLine(t.Spark, t.Last(60).ToList(), 60, false);
        }

        void DrawLine(Canvas c, List<double> data, int slots, bool big)
        {
            c.Children.Clear();
            double w = c.ActualWidth > 0 ? c.ActualWidth : c.Width, h = c.ActualHeight > 0 ? c.ActualHeight : c.Height;
            if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0 || data.Count == 0) return;
            double max = Math.Max(big ? 50 : 30, data.Where(x => x >= 0).DefaultIfEmpty(0).Max() * 1.2);
            if (big)
            {
                double step = max > 400 ? 200 : max > 160 ? 100 : max > 80 ? 50 : 20;
                max = Math.Ceiling(max / step) * step;
                for (double v = step; v < max; v += step)
                {
                    double y = h - v / max * (h - 12);
                    c.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = Br(CLine), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } });
                    var lbl = new TextBlock { Text = v + " мс", FontSize = 10, Foreground = Br(CFaint) };
                    Canvas.SetLeft(lbl, 2);
                    Canvas.SetTop(lbl, y - 13);
                    c.Children.Add(lbl);
                }
            }
            double dx = w / Math.Max(1, slots - 1);
            double x0 = w - (data.Count - 1) * dx;
            var line = new Polyline { Stroke = Br(CGreen), StrokeThickness = big ? 2 : 1.5, StrokeLineJoin = PenLineJoin.Round };
            var fillPts = new PointCollection();
            for (int i = 0; i < data.Count; i++)
            {
                double x = x0 + i * dx;
                if (data[i] < 0)
                {
                    c.Children.Add(new Rectangle { Width = Math.Max(1.5, dx * 0.8), Height = big ? 10 : 6, Fill = Br(CRed) });
                    Canvas.SetLeft(c.Children[c.Children.Count - 1], x - dx * 0.4);
                    Canvas.SetTop(c.Children[c.Children.Count - 1], h - (big ? 10 : 6));
                    if (line.Points.Count > 0) { AddLine(c, line, fillPts, h, big); line = new Polyline { Stroke = Br(CGreen), StrokeThickness = big ? 2 : 1.5, StrokeLineJoin = PenLineJoin.Round }; fillPts = new PointCollection(); }
                    continue;
                }
                double y = h - Math.Min(data[i], max) / max * (h - (big ? 12 : 3));
                line.Points.Add(new Point(x, y));
                fillPts.Add(new Point(x, y));
            }
            if (line.Points.Count > 0) AddLine(c, line, fillPts, h, big);
        }

        void AddLine(Canvas c, Polyline line, PointCollection pts, double h, bool big)
        {
            if (big && pts.Count > 1)
            {
                var area = new PointCollection(pts);
                area.Add(new Point(pts[pts.Count - 1].X, h));
                area.Add(new Point(pts[0].X, h));
                c.Children.Add(new Polygon
                {
                    Points = area,
                    Fill = new LinearGradientBrush(Color.FromArgb(70, CGreen.R, CGreen.G, CGreen.B), Color.FromArgb(0, CGreen.R, CGreen.G, CGreen.B), 90)
                });
            }
            c.Children.Add(line);
            if (big && line.Points.Count > 0)
            {
                var p = line.Points[line.Points.Count - 1];
                var dot = new Ellipse { Width = 7, Height = 7, Fill = Br(CGreen) };
                Canvas.SetLeft(dot, p.X - 3.5);
                Canvas.SetTop(dot, p.Y - 3.5);
                c.Children.Add(dot);
            }
        }

        void DrawChart()
        {
            if (selected == null) return;
            chartTitle.Text = selected.Name + " · " + selected.Host;
            double avg = selected.Avg(Target.Keep);
            var ok = selected.Samples.Where(x => x >= 0).ToList();
            chartStats.Text = ok.Count == 0 ? "нет ответа" :
                "мин " + Math.Round(ok.Min()) + " · сред " + Math.Round(avg) + " · макс " + Math.Round(ok.Max()) + " мс · потери " + Math.Round(selected.Loss(Target.Keep), 1) + "%";
            DrawLine(chart, selected.Samples.ToList(), Target.Keep, true);
        }

        void AddCustom()
        {
            string h = addBox.Text.Trim();
            h = h.Replace("https://", "").Replace("http://", "").Split('/')[0];
            if (h.Length == 0) return;
            if (targets.Any(t => t.Host.Equals(h, StringComparison.OrdinalIgnoreCase))) { Toast("Этот сервер уже в списке"); return; }
            if (targets.Count(t => t.Custom) >= 6) { Toast("Можно добавить до 6 своих серверов"); return; }
            var t2 = new Target { Name = h, Host = h, Custom = true };
            targets.Add(t2);
            BuildRow(t2);
            Select(t2);
            addBox.Text = "";
            SaveSettings();
        }

        void RemoveTarget(Target t)
        {
            targets.Remove(t);
            targetList.Children.Remove(t.Row);
            if (selected == t) Select(targets.FirstOrDefault(x => !x.Gateway) ?? targets.FirstOrDefault());
            SaveSettings();
        }

        // -------------------------------------------------- обрывы и отчёт

        void RenderOutages()
        {
            outageList.Children.Clear();
            foreach (var o in outages.Take(30))
            {
                var g = Columns(Auto, Star, Auto);
                g.Margin = new Thickness(0, 8, 0, 8);
                g.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Br(o.LocalProblem ? CAmber : CRed), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
                var mid = new StackPanel();
                mid.Children.Add(Txt(o.Start.ToString("d MMMM, HH:mm:ss", Ru) + " — " + o.End.ToString("HH:mm:ss"), 13, CText, true));
                mid.Children.Add(Txt(o.LocalProblem ? "не отвечал роутер — Wi-Fi или кабель" : "роутер работал — сбой у провайдера", 11.5, CSub, false));
                g.Children.Add(At(mid, 1));
                g.Children.Add(At(Txt(Dur(o.End - o.Start), 13, CText, true), 2));
                outageList.Children.Add(g);
            }
            outageEmpty.Visibility = outages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void SaveOutage(Outage o)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(IOPath.Combine(Dir, "outages.txt"), o.Start.Ticks + "\t" + o.End.Ticks + "\t" + (o.LocalProblem ? 1 : 0) + "\r\n");
            }
            catch { }
        }

        void SaveReport()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Сохранить отчёт о связи", FileName = "Отчёт о связи " + DateTime.Now.ToString("yyyy-MM-dd HH-mm"),
                Filter = "Страница (откроется в браузере)|*.html", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(this) != true) return;
            var all = outages.ToList();
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=utf-8><title>Отчёт о качестве связи</title><style>body{font:15px/1.5 Segoe UI,sans-serif;max-width:860px;margin:30px auto;padding:0 20px;color:#222}" +
                      "table{border-collapse:collapse;width:100%;margin:10px 0 26px}td,th{border-bottom:1px solid #ddd;padding:7px 8px;text-align:left}th{background:#f4f4f4}.r{color:#c22}small{color:#777}</style>");
            sb.Append("<h1>Отчёт о качестве интернет-соединения</h1>");
            sb.Append("<p>Составлен " + DateTime.Now.ToString("d MMMM yyyy, HH:mm", Ru) + ". Наблюдение велось с " + started.ToString("d MMMM, HH:mm", Ru) +
                      " (" + Dur(DateTime.Now - started) + "). Проверка каждую секунду программой «Пинг by mysik».</p>");
            sb.Append("<h2>Пропадания интернета: " + all.Count + "</h2>");
            if (all.Count == 0) sb.Append("<p>За время наблюдения связь не пропадала.</p>");
            else
            {
                sb.Append("<table><tr><th>Начало</th><th>Конец</th><th>Длительность</th><th>Роутер</th></tr>");
                foreach (var o in all)
                    sb.Append("<tr><td>" + o.Start.ToString("dd.MM.yyyy HH:mm:ss") + "</td><td>" + o.End.ToString("HH:mm:ss") + "</td><td>" + Dur(o.End - o.Start) +
                              "</td><td>" + (o.LocalProblem ? "не отвечал" : "<b>отвечал</b> (сбой вне квартиры)") + "</td></tr>");
                sb.Append("</table>");
                var total = all.Aggregate(TimeSpan.Zero, (a, o) => a + (o.End - o.Start));
                sb.Append("<p>Всего без связи: <b>" + Dur(total) + "</b>. Из них при работающем роутере: <b>" +
                          Dur(all.Where(o => !o.LocalProblem).Aggregate(TimeSpan.Zero, (a, o) => a + (o.End - o.Start))) + "</b>.</p>");
            }
            sb.Append("<h2>Задержка и потери за последние 5 минут</h2><table><tr><th>Сервер</th><th>Адрес</th><th>Средний пинг</th><th>Разброс</th><th>Потери</th></tr>");
            foreach (var t in targets)
            {
                double a = t.Avg(Target.Keep);
                sb.Append("<tr><td>" + System.Net.WebUtility.HtmlEncode(t.Name) + "</td><td>" + System.Net.WebUtility.HtmlEncode(t.Host) + "</td><td>" + (a >= 0 ? Math.Round(a) + " мс" : "нет ответа") +
                          "</td><td>" + Math.Round(t.Jitter(Target.Keep)) + " мс</td><td" + (t.Loss(Target.Keep) > 1 ? " class=r" : "") + ">" + Math.Round(t.Loss(Target.Keep), 1) + "%</td></tr>");
            }
            sb.Append("</table><p><small>Если роутер отвечает, а внешние серверы (Google, Cloudflare, Яндекс) — нет, проблема не в домашней сети, а на стороне провайдера.</small></p>");
            try
            {
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                Process.Start(dlg.FileName);
            }
            catch (Exception ex) { Toast("Не удалось сохранить: " + ex.Message); }
        }

        // -------------------------------------------------- настройки, трей

        void LoadSettings()
        {
            try
            {
                string f = IOPath.Combine(Dir, "settings.txt");
                if (File.Exists(f))
                    foreach (var line in File.ReadAllLines(f))
                    {
                        if (line == "notify=0") notify = false;
                        else if (line.StartsWith("host=")) { var h = line.Substring(5); if (h.Length > 0) targets.Add(new Target { Name = h, Host = h, Custom = true }); }
                    }
                string o = IOPath.Combine(Dir, "outages.txt");
                if (File.Exists(o))
                    foreach (var line in File.ReadAllLines(o).Reverse().Take(200))
                    {
                        var p = line.Split('\t');
                        if (p.Length < 3) continue;
                        outages.Add(new Outage { Start = new DateTime(long.Parse(p[0])), End = new DateTime(long.Parse(p[1])), LocalProblem = p[2] == "1" });
                    }
            }
            catch { }
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var lines = new List<string> { "notify=" + (notify ? 1 : 0) };
                lines.AddRange(targets.Where(t => t.Custom).Select(t => "host=" + t.Host));
                File.WriteAllLines(IOPath.Combine(Dir, "settings.txt"), lines);
            }
            catch { }
        }

        static SD.Icon MakeIcon(SD.Color c)
        {
            var bmp = new SD.Bitmap(32, 32);
            using (var g = SD.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(SD.Color.Transparent);
                using (var bg = new SD.SolidBrush(SD.Color.FromArgb(30, 34, 44))) g.FillEllipse(bg, 1, 1, 30, 30);
                using (var dot = new SD.SolidBrush(c)) g.FillEllipse(dot, 9, 9, 14, 14);
            }
            return SD.Icon.FromHandle(bmp.GetHicon());
        }

        void BuildTray()
        {
            var menu = new WF.ContextMenuStrip();
            var open = new WF.ToolStripMenuItem("Открыть", null, (s, e) => ShowMain());
            open.Font = new SD.Font(open.Font, SD.FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(new WF.ToolStripMenuItem("Выход", null, (s, e) => { reallyExit = true; Close(); }));
            tray = new WF.NotifyIcon { ContextMenuStrip = menu, Visible = true, Text = "Пинг by mysik", Icon = trayIcons[0] };
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) ShowMain(); };
        }

        void Balloon(string title, string text, WF.ToolTipIcon icon)
        {
            try { tray.ShowBalloonTip(5000, title, text, icon); } catch { }
        }

        void ShowMain()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!reallyExit)
            {
                e.Cancel = true;
                Hide();
                if (!hintShown)
                {
                    hintShown = true;
                    Balloon("Пинг следит за связью в трее", "Значок у часов: зелёный — всё хорошо, жёлтый — потери, красный — нет интернета.", WF.ToolTipIcon.Info);
                }
                return;
            }
            if (timer != null) timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            base.OnClosing(e);
        }

        void SetAutostart(bool on)
        {
            if (suppress) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue("Ping by mysik", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" --tray");
                    else k.DeleteValue("Ping by mysik", false);
                }
                Toast(on ? "Будет следить за связью с момента включения компьютера" : "Автозапуск выключен");
            }
            catch (Exception ex) { Toast("Не получилось: " + ex.Message); }
        }
    }
}
