// MyTunes - Musik-Browser fuer "iTunes Media\Music" (WPF, single-file, csc.exe)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Threading;

static class Program {
    [STAThread]
    static void Main() { new Application().Run(new MainWindow()); }
}

public class Track {
    public string Num, Title, Artist, Album, Path;
    public bool Cached;
    public string AlbumKey { get { return Artist + " - " + Album; } }
}

public class MainWindow : Window {
    const string Root = @"G:\Meine Ablage\Synchronized\iTunes reduced Copy\iTunes Media\Music";
    static readonly string[] Exts = { ".mp3", ".m4a", ".aac", ".m4p", ".wav", ".flac", ".wma", ".aif", ".aiff" };
    readonly string Cache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tracks.txt");
    readonly string WinFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window.txt");

    readonly List<Track> items = new List<Track>();
    ICollectionView view;
    ListView lv; GridView gv;
    dynamic wmp;
    Button btnPrev, btnPlay, btnNext;
    TextBlock nowLbl, timeLbl, statusLbl, statusIcon;
    Slider vol;
    Grid seek; Border seekFill, seekTrack, bar;
    Ellipse seekThumb;
    Menu menu; StatusBar sb;
    FontFamily fa;

    readonly List<Track> history = new List<Track>();
    int histPos = -1;
    bool darkOn, seeking, scanDone, scanning;

    ConcurrentQueue<Track> queue = new ConcurrentQueue<Track>();
    readonly List<Track> scanned = new List<Track>();
    readonly List<Track> pending = new List<Track>();
    readonly DispatcherTimer scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
    readonly DispatcherTimer uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

    // Palette (Dark/Light)
    Color BgC, FgC, CtlC, SepC, AccC, DimC;
    SolidColorBrush Bg, Fg, Ctl, Sep, Acc, Dim;

    public MainWindow() {
        Title = "MyTunes"; Width = 1280; Height = 840; MinWidth = 860; MinHeight = 540;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        LoadBounds();

        var fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts", "fa-solid-900.ttf");
        fa = File.Exists(fontPath)
            ? new FontFamily(new Uri(AppDomain.CurrentDomain.BaseDirectory), "./fonts/#Font Awesome 6 Free Solid")
            : new FontFamily("Segoe MDL2 Assets");
        Icon = RenderIcon('\uF001', System.Windows.Media.Color.FromRgb(0, 120, 215), 64);

        wmp = Activator.CreateInstance(Type.GetTypeFromProgID("WMPlayer.OCX"));
        wmp.settings.volume = 80;

        var dock = new DockPanel();
        Content = dock;

        // --- Menü ---
        menu = new Menu();
        var mDatei = new MenuItem { Header = "Datei" };
        mDatei.Items.Add(MI('\uF021', "Medien neu scannen", "F5", () => StartJob(false, true)));
        mDatei.Items.Add(MI('\uF08B', "Beenden", null, () => Close()));
        var mBearb = new MenuItem { Header = "Bearbeiten" };
        mBearb.Items.Add(MI('\uF0E2', "Rückgängig", "Strg+Z", () => Undo()));
        mBearb.Items.Add(MI('\uF01E', "Wiederherstellen", "Strg+Y", () => Redo()));
        mBearb.Items.Add(new Separator());
        mBearb.Items.Add(MI('\uF0C4', "Ausschneiden", "Strg+X", () => CopyTracks(true)));
        mBearb.Items.Add(MI('\uF0C5', "Kopieren", "Strg+C", () => CopyTracks(false)));
        mBearb.Items.Add(MI('\uF328', "Einfügen", "Strg+V", () => PasteTracks()));
        mBearb.Items.Add(new Separator());
        mBearb.Items.Add(MI('\uF14A', "Alles auswählen", "Strg+A", () => lv.SelectAll()));
        mBearb.Items.Add(new Separator());
        mBearb.Items.Add(MI('\uF002', "Seite durchsuchen...", "Strg+F", () => SearchTracks()));
        var mAnsicht = new MenuItem { Header = "Ansicht" };
        var mDark = new MenuItem { Header = "Dunkelmodus", IsCheckable = true };
        mDark.Checked += (s, e) => SetDark(true);
        mDark.Unchecked += (s, e) => SetDark(false);
        mAnsicht.Items.Add(MI('\uF186', "Dunkelmodus", null, () => mDark.IsChecked = !mDark.IsChecked));
        menu.Items.Add(mDatei); menu.Items.Add(mBearb); menu.Items.Add(mAnsicht);
        DockPanel.SetDock(menu, Dock.Top);
        dock.Children.Add(menu);

        // --- Statuszeile unten ---
        statusLbl = new TextBlock { Padding = new Thickness(6, 2, 6, 2) };
        statusIcon = new TextBlock { Text = "\uF001", FontFamily = fa, Margin = new Thickness(0, 0, 6, 0) };
        sb = new StatusBar();
        sb.Items.Add(new StatusBarItem { Content = new StackPanel { Orientation = Orientation.Horizontal,
            Children = { statusIcon, statusLbl } } });
        DockPanel.SetDock(sb, Dock.Bottom);
        dock.Children.Add(sb);

        // --- Player-Leiste unten ---
        seek = new Grid { Height = 34, Cursor = Cursors.Hand };
        seekTrack = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), VerticalAlignment = VerticalAlignment.Center };
        seekFill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        seekThumb = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-6, 0, 0, 0) };
        seek.Children.Add(seekTrack); seek.Children.Add(seekFill); seek.Children.Add(seekThumb);
        DockPanel.SetDock(seek, Dock.Top);

        btnPrev = Btn('\uF048'); btnPlay = Btn('\uF04B'); btnNext = Btn('\uF051');
        btnPrev.Click += (s, e) => PrevOrRestart();
        btnPlay.Click += (s, e) => TogglePlay();
        btnNext.Click += (s, e) => Step(1);
        nowLbl = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), Foreground = Brushes.Gray };
        var volIco = new TextBlock { Text = "\uE767", FontFamily = new FontFamily("Segoe MDL2 Assets"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 6, 0) };
        vol = new Slider { Maximum = 100, Value = 80, Width = 140, VerticalAlignment = VerticalAlignment.Center };
        vol.ValueChanged += (s, e) => wmp.settings.volume = (int)vol.Value;
        timeLbl = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), Foreground = Brushes.Gray };
        var barTop = new DockPanel();
        DockPanel.SetDock(seek, Dock.Top);
        barTop.Children.Add(seek);
        var barFlow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6, 0, 6) };
        foreach (var c in new UIElement[] { btnPrev, btnPlay, btnNext, nowLbl, volIco, vol, timeLbl }) barFlow.Children.Add(c);
        barTop.Children.Add(barFlow);
        bar = new Border { Child = barTop, Padding = new Thickness(0), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        bar.BorderBrush = Sep = NewBrush(SepC = Color.FromRgb(224, 224, 224));

        // --- Liste ---
        gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "#", Width = 55, DisplayMemberBinding = new Binding("Num") });
        gv.Columns.Add(new GridViewColumn { Header = "Titel", Width = 450, DisplayMemberBinding = new Binding("Title") });
        gv.Columns.Add(new GridViewColumn { Header = "Interpret", Width = 270, DisplayMemberBinding = new Binding("Artist") });
        gv.Columns.Add(new GridViewColumn { Header = "Album", Width = 270, DisplayMemberBinding = new Binding("Album") });
        lv = new ListView { View = gv, ItemsSource = items, SelectionMode = SelectionMode.Extended };
        view = CollectionViewSource.GetDefaultView(items);
        lv.GroupStyle.Add(new GroupStyle());
        lv.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        lv.MouseDoubleClick += (s, e) => { if (lv.SelectedItem != null) PlayItem((Track)lv.SelectedItem); };
        lv.PreviewKeyDown += (s, e) => {
            if (e.Key == Key.Enter && lv.SelectedItem != null) { PlayItem((Track)lv.SelectedItem); e.Handled = true; }
        };
        lv.PreviewMouseLeftButtonDown += (s, e) => dragStart = e.GetPosition(lv);
        lv.PreviewMouseMove += (s, e) => {
            if (e.LeftButton == MouseButtonState.Pressed && lv.SelectedItems.Count > 0 &&
                (e.GetPosition(lv) - dragStart).Length > 6) {
                var col = new StringCollection();
                col.AddRange(lv.SelectedItems.Cast<Track>().Select(t => t.Path).ToArray());
                var d = new System.Windows.DataObject(); d.SetFileDropList(col);
                DragDrop.DoDragDrop(lv, d, System.Windows.DragDropEffects.Copy);
            }
        };
        dock.Children.Add(lv);

        // --- Tasten ---
        KeyDown += (s, e) => {
            switch (e.Key) {
                case Key.Right: Step(1); e.Handled = true; break;
                case Key.Left: PrevOrRestart(); e.Handled = true; break;
                case Key.Space: TogglePlay(); e.Handled = true; break;
                case Key.F5: StartJob(false, true); e.Handled = true; break;
                case Key.Z: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) Undo(); break;
                case Key.Y: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) Redo(); break;
                case Key.X: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) CopyTracks(true); break;
                case Key.C: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) CopyTracks(false); break;
                case Key.V: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) PasteTracks(); break;
                case Key.A: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) { lv.SelectAll(); e.Handled = true; } break;
                case Key.F: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) SearchTracks(); break;
            }
        };

        // --- Seek-Interaktion ---
        seek.MouseLeftButtonDown += (s, e) => { seeking = true; seek.CaptureMouse(); SeekTo(e.GetPosition(seek).X); };
        seek.MouseMove += (s, e) => { if (seeking) SeekTo(e.GetPosition(seek).X); };
        seek.MouseLeftButtonUp += (s, e) => { seeking = false; seek.ReleaseMouseCapture(); SeekTo(e.GetPosition(seek).X); };

        uiTimer.Tick += (s, e) => {
            btnPlay.Content = Glyph((int)wmp.playState == 3 ? '\uF04C' : '\uF04B');
            double d = 0;
            try { if (wmp.currentMedia != null) d = wmp.currentMedia.duration; } catch { }
            if (d > 0) {
                double p = wmp.controls.currentPosition;
                if (!seeking) SetSeekFrac(p / d);
                timeLbl.Text = TimeSpan.FromSeconds(p).ToString(@"mm\:ss") + " / " + TimeSpan.FromSeconds(d).ToString(@"mm\:ss");
            }
        };
        uiTimer.Start();
        scanTimer.Tick += ScanTick;
        Closing += (s, e) => SaveBounds();
        Closed += (s, e) => { try { wmp.close(); } catch { } };

        SetDark(false);
        hadCache = File.Exists(Cache);
        StartJob(hadCache, true);
    }

    // ---------- UI-Helfer ----------
    SolidColorBrush NewBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    Button Btn(char c) {
        var b = new Button { Content = Glyph(c), Width = 52, Height = 40, Margin = new Thickness(0, 0, 4, 0) };
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bd.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;
        var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        trig.Setters.Add(new Setter(Button.BackgroundProperty, NewBrush(Color.FromArgb(45, 0, 120, 215))));
        tpl.Triggers.Add(trig);
        b.Template = tpl; b.Background = Brushes.Transparent;
        return b;
    }

    TextBlock Glyph(char c) { return new TextBlock { Text = c.ToString(), FontFamily = fa, FontSize = 15 }; }

    MenuItem MI(char c, string text, string key, Action act) {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(new TextBlock { Text = c.ToString(), FontFamily = fa, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var mi = new MenuItem { Header = p, InputGestureText = key };
        if (act != null) mi.Click += (s, e) => act();
        return mi;
    }
    void SetItemColors(MenuItem mi, Brush bg, Brush fg) {
        mi.Background = bg; mi.Foreground = fg;
        foreach (var c in mi.Items) { var s = c as MenuItem; if (s != null) SetItemColors(s, bg, fg); }
    }

    BitmapSource RenderIcon(char c, Color col, int size) {
        var ft = new FormattedText(c.ToString(), System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface(fa, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size * 0.62, new SolidColorBrush(col), 96);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawText(ft, new System.Windows.Point((size - ft.Width) / 2, (size - ft.Height) / 2));
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv); rtb.Freeze();
        return rtb;
    }

    void SetSeekFrac(double f) {
        f = Math.Max(0, Math.Min(1, f));
        double w = seek.ActualWidth;
        seekFill.Width = f * w;
        seekThumb.Margin = new Thickness(Math.Max(0, f * w - 6), 0, 0, 0);
    }
    void SeekTo(double x) {
        if (seek.ActualWidth <= 0) return;
        double f = Math.Max(0, Math.Min(1, x / seek.ActualWidth));
        SetSeekFrac(f);
        try { if (wmp.currentMedia != null) { double d = wmp.currentMedia.duration; if (d > 0) wmp.controls.currentPosition = d * f; } } catch { }
    }

    void OnHeaderClick(object s, RoutedEventArgs e) {
        var h = e.OriginalSource as GridViewColumnHeader;
        if (h == null || h.Column == null) return;
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(items);
        view.SortDescriptions.Clear();
        var col = h.Column.Header.ToString();
        var prop = col == "#" ? "Num" : col == "Titel" ? "Title" : col == "Interpret" ? "Artist" : "Album";
        var dir = ListSortDirection.Ascending;
        if (lastSort == prop) dir = lastAsc ? ListSortDirection.Descending : ListSortDirection.Ascending;
        lastSort = prop; lastAsc = dir == ListSortDirection.Ascending;
        view.SortDescriptions.Add(new SortDescription(prop, dir));
        if (prop != "Album") view.SortDescriptions.Add(new SortDescription("Album", ListSortDirection.Ascending));
        if (prop != "Num") view.SortDescriptions.Add(new SortDescription("Num", ListSortDirection.Ascending));
        if (prop == "Album") { if (view.GroupDescriptions.Count == 0) view.GroupDescriptions.Add(new PropertyGroupDescription("AlbumKey")); }
        else view.GroupDescriptions.Clear();
    }
    string lastSort; bool lastAsc;

    // ---------- Scan/Cache ----------
    Track Parse(string fullName, bool cached) {
        var rel = fullName.Substring(Root.Length).TrimStart('\\').Split('\\');
        var title = System.IO.Path.GetFileNameWithoutExtension(fullName); var num = "";
        var m = System.Text.RegularExpressions.Regex.Match(title, @"^(\d+)\s+(.+)$");
        if (m.Success) { num = m.Groups[1].Value; title = m.Groups[2].Value; }
        return new Track { Num = num, Title = title, Artist = rel[0], Album = rel.Length >= 3 ? rel[1] : "", Path = fullName, Cached = cached };
    }

    IEnumerable<string> EnumFiles(string root) {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0) {
            var dir = stack.Pop();
            string[] files; try { files = Directory.GetFiles(dir); } catch { continue; }
            foreach (var f in files) if (Exts.Contains(Path.GetExtension(f).ToLowerInvariant())) yield return f;
            string[] subs; try { subs = Directory.GetDirectories(dir); } catch { continue; }
            foreach (var sd in subs) stack.Push(sd);
        }
    }

    void StartJob(bool doCache, bool doScan) {
        if (scanning) return;
        scanning = true; scanDone = false;
        if (doScan) scanned.Clear();
        queue = new ConcurrentQueue<Track>();
        Task.Run(() => {
            try {
                if (doCache && File.Exists(Cache)) foreach (var l in File.ReadLines(Cache)) queue.Enqueue(Parse(l, true));
                if (doScan) foreach (var f in EnumFiles(Root)) queue.Enqueue(Parse(f, false));
            } finally { scanDone = true; }
        });
        scanTimer.Start();
    }
    bool hadCache;
    Point dragStart;

    void ScanTick(object s, EventArgs e) {
        Track t;
        while (queue.TryDequeue(out t)) {
            if (t.Cached || !hadCache) pending.Add(t);
            if (!t.Cached) scanned.Add(t);
        }
        int limit = items.Count > 0 ? 2000 : 400;
        if (pending.Count >= limit || (scanDone && pending.Count > 0)) {
            items.AddRange(pending);
            view.Refresh();
            pending.Clear();
        }
        statusLbl.Text = scanning ? items.Count + " Tracks (scanne... " + scanned.Count + ")" : items.Count + " Tracks";
        if (scanDone && queue.IsEmpty) {
            scanTimer.Stop();
            if (scanned.Count > 0) {
                if (hadCache) MergeDiff();
                File.WriteAllLines(Cache, scanned.Select(x => x.Path));
            }
            scanning = false;
            statusLbl.Text = items.Count + " Tracks";
        }
    }

    void MergeDiff() {
        var scanPaths = new HashSet<string>(scanned.Select(x => x.Path));
        var have = new HashSet<string>(items.Select(i => i.Path));
        items.RemoveAll(it => !scanPaths.Contains(it.Path));
        foreach (var t in scanned) if (!have.Contains(t.Path)) items.Add(t);
        view.Refresh();
    }

    // ---------- Wiedergabe ----------
    void PlayItem(Track t, bool record = true) {
        if (t == null) return;
        lv.SelectedItem = t; lv.ScrollIntoView(t);
        wmp.URL = t.Path;
        SetSeekFrac(0);
        nowLbl.Text = t.Artist + " - " + t.Title;
        if (record) {
            histPos++;
            if (histPos < history.Count) history.RemoveRange(histPos, history.Count - histPos);
            history.Add(t);
        }
    }

    void Step(int d) {
        var view = CollectionViewSource.GetDefaultView(items);
        var ordered = view.Cast<Track>().ToList();
        int cur = lv.SelectedItem != null ? ordered.IndexOf((Track)lv.SelectedItem) : -1;
        int n = cur >= 0 ? cur + d : 0;
        if (n >= 0 && n < ordered.Count) PlayItem(ordered[n]);
    }

    void PrevOrRestart() {
        if ((double)wmp.controls.currentPosition > 0.5) wmp.controls.currentPosition = 0;
        else Step(-1);
    }
    void TogglePlay() {
        if ((int)wmp.playState == 3) wmp.controls.pause();
        else if (wmp.currentMedia != null) wmp.controls.play();
        else if (lv.SelectedItem != null) PlayItem((Track)lv.SelectedItem);
    }
    void Undo() { if (histPos > 0 && items.Contains(history[histPos - 1])) { histPos--; PlayItem(history[histPos], false); } }
    void Redo() { if (histPos < history.Count - 1 && items.Contains(history[histPos + 1])) { histPos++; PlayItem(history[histPos], false); } }

    // ---------- Bearbeiten ----------
    void CopyTracks(bool cut) {
        var paths = lv.SelectedItems.Cast<Track>().Select(t => t.Path).ToArray();
        if (paths.Length == 0) return;
        var col = new StringCollection(); col.AddRange(paths);
        var d = new System.Windows.DataObject(); d.SetFileDropList(col);
        var ms = new MemoryStream(); ms.WriteByte((byte)(cut ? 2 : 5));
        d.SetData("Preferred DropEffect", ms);
        Clipboard.SetDataObject(d);
    }
    void PasteTracks() {
        var cdo = Clipboard.GetDataObject();
        if (!cdo.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        var dest = lv.SelectedItem != null ? Path.GetDirectoryName(((Track)lv.SelectedItem).Path) : Root;
        bool move = false;
        try { move = (((MemoryStream)cdo.GetData("Preferred DropEffect")).ReadByte() & 2) != 0; } catch { }
        foreach (var src in (string[])cdo.GetData(System.Windows.DataFormats.FileDrop)) {
            var target = Path.Combine(dest, Path.GetFileName(src));
            try { if (!File.Exists(target)) { if (move) File.Move(src, target); else File.Copy(src, target); } } catch { }
        }
        StartJob(false, true);
    }
    void SearchTracks() {
        var q = Microsoft.VisualBasic.Interaction.InputBox("Suchen nach (Titel, Interpret, Album):", "Medien durchsuchen");
        if (string.IsNullOrEmpty(q)) return;
        var q2 = q.ToLowerInvariant();
        var hit = items.FirstOrDefault(t => (t.Title + " " + t.Artist + " " + t.Album).ToLowerInvariant().Contains(q2));
        if (hit != null) { lv.SelectedItem = hit; lv.ScrollIntoView(hit); }
        else System.Windows.MessageBox.Show("Kein Treffer für '" + q + "'", "Medien durchsuchen");
    }

    // ---------- Darkmode ----------
    void SetDark(bool on) {
        darkOn = on;
        BgC = on ? Color.FromRgb(30, 30, 30) : Colors.White;
        FgC = on ? Color.FromRgb(220, 220, 220) : Colors.Black;
        CtlC = on ? Color.FromRgb(45, 45, 48) : Color.FromRgb(240, 240, 240);
        SepC = on ? Color.FromRgb(62, 62, 66) : Color.FromRgb(224, 224, 224);
        AccC = on ? Color.FromRgb(96, 150, 255) : Color.FromRgb(0, 120, 215);
        DimC = on ? Color.FromRgb(160, 160, 160) : Colors.Gray;
        Bg = NewBrush(BgC); Fg = NewBrush(FgC); Ctl = NewBrush(CtlC); Sep = NewBrush(SepC); Acc = NewBrush(AccC); Dim = NewBrush(DimC);

        Background = Ctl; Foreground = Fg;
        lv.Background = Bg; lv.Foreground = Fg;
        menu.Background = Ctl; menu.Foreground = Fg;
        foreach (MenuItem top in menu.Items) SetItemColors(top, Ctl, Fg);
        sb.Background = Ctl;
        bar.Background = Ctl; bar.BorderBrush = Sep;
        seekTrack.Background = NewBrush(on ? Color.FromRgb(62, 62, 66) : Color.FromRgb(214, 214, 214));
        seekFill.Background = Acc; seekThumb.Fill = Acc;
        nowLbl.Foreground = Dim; timeLbl.Foreground = Dim;
        statusLbl.Foreground = Fg; statusIcon.Foreground = Fg;
        foreach (var b in new[] { btnPrev, btnPlay, btnNext }) b.Foreground = Fg;
        // ListView: Zeilen, Selektion, Header und Gruppentitel einfaerben
        lv.ItemContainerStyle = RowStyle();
        var hstyle = new Style(typeof(GridViewColumnHeader));
        hstyle.Setters.Add(new Setter(Control.BackgroundProperty, Ctl));
        hstyle.Setters.Add(new Setter(Control.ForegroundProperty, Fg));
        hstyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        gv.ColumnHeaderContainerStyle = hstyle;
        var gs = lv.GroupStyle[0];
        gs.HeaderTemplate = new DataTemplate();
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        f.SetValue(TextBlock.ForegroundProperty, Acc);
        f.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        f.SetValue(TextBlock.PaddingProperty, new Thickness(4, 10, 0, 4));
        gs.HeaderTemplate.VisualTree = f;
    }

    Style RowStyle() {
        var st = new Style(typeof(ListViewItem));
        st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 5, 6, 5)));
        st.Setters.Add(new Setter(Control.ForegroundProperty, Fg));
        st.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        var hov = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hov.Setters.Add(new Setter(Control.BackgroundProperty, NewBrush(darkOn ? Color.FromRgb(55, 55, 60) : Color.FromRgb(229, 243, 255))));
        st.Triggers.Add(hov);
        var sel = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        sel.Setters.Add(new Setter(Control.BackgroundProperty, NewBrush(darkOn ? Color.FromRgb(55, 80, 120) : Color.FromRgb(204, 232, 255))));
        st.Triggers.Add(sel);
        return st;
    }

    // ---------- Fenster-Persistenz ----------
    void SaveBounds() {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        try { File.WriteAllText(WinFile, r.X + "," + r.Y + "," + r.Width + "," + r.Height + (WindowState == WindowState.Maximized ? ",max" : "")); } catch { }
    }
    void LoadBounds() {
        try {
            var p = File.ReadAllText(WinFile).Split(',');
            var r = new Rect(double.Parse(p[0]), double.Parse(p[1]), double.Parse(p[2]), double.Parse(p[3]));
            if (SystemParameters.VirtualScreenWidth > 0) { Left = r.X; Top = r.Y; Width = r.Width; Height = r.Height; }
            if (p.Length > 4 && p[4] == "max") WindowState = WindowState.Maximized;
        } catch { }
    }
}
