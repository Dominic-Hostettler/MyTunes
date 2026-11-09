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

public class Track : INotifyPropertyChanged {
    public string Num { get; set; }
    public string Title { get; set; }
    public string Artist { get; set; }
    public string Album { get; set; }
    public string Path { get; set; }
    public bool Cached { get; set; }
    public string AlbumKey { get { return Artist + " - " + Album; } }
    public event PropertyChangedEventHandler PropertyChanged;
    bool playing; string glyph = "\u25B6";
    public bool IsPlaying { get { return playing; } set { if (playing != value) { playing = value; Prop("IsPlaying"); Prop("Mark"); } } }
    public void SetGlyph(string c) { if (glyph != c) { glyph = c; Prop("Mark"); } }
    public string Mark { get { return playing ? glyph : Num; } }
    public int NumInt { get { int n; return int.TryParse(Num, out n) ? n : int.MaxValue; } }   // numerisch sortierbar, leer -> hinten
    public long Len { get; set; }
    public DateTime Modified { get; set; }
    public DateTime Created { get; set; }
    public string Dur { get; set; }
    public double DurSec { get; set; }   // Sekunden -> Dauer sortierbar
    public string File { get; set; }
    void Prop(string n) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n)); }
}

public class MainWindow : Window {
    static readonly string DefaultRoot = @"G:\Meine Ablage\Synchronized\iTunes reduced Copy\iTunes Media\Music";
    string root = LoadRoot();
    readonly string RootFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "root.txt");
    static string LoadRoot() {
        try {
            var f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "root.txt");
            if (File.Exists(f)) { var r = File.ReadAllText(f).Trim(); if (r.Length > 0) return r; }
        } catch { }
        return DefaultRoot;
    }
    static readonly string[] Exts = { ".mp3", ".m4a", ".aac", ".m4p", ".wav", ".flac", ".wma", ".aif", ".aiff" };
    readonly string Cache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tracks.txt");
    readonly string WinFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window.txt");
    readonly string ColsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cols.txt");
    List<GridViewColumn> colOrder;

    readonly List<Track> items = new List<Track>();
    ICollectionView view;
    ListView lv; GridView gv;
    dynamic wmp;
    Button btnPrev, btnPlay, btnNext;
    TextBlock nowLbl, timeLbl, statusLbl;
    Track playing;
    Grid seek, volBar;
    Border seekFill, seekTrack, bar, vFill, vTrack;
    Ellipse seekThumb, vThumb;
    bool muted;
    double volFrac = 0.8;
    Menu menu; DockPanel topBar;
    ContextMenu colMenu;
    FontFamily fa;

    readonly List<Track> history = new List<Track>();
    int histPos = -1;
    bool darkOn, seeking, scanDone, scanning, wantDark;
    int theme;
    int lang;
    string S(string de, string en) { return lang == 1 ? en : de; }
    readonly string[] props = { "Num", "Title", "Artist", "Album", "DurSec", "Len", "Modified", "Created", "File" };
    MenuItem mDark;

    ConcurrentQueue<Track> queue = new ConcurrentQueue<Track>();
    readonly List<Track> scanned = new List<Track>();
    readonly List<Track> pending = new List<Track>();
    readonly DispatcherTimer scanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
    readonly DispatcherTimer uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

    // Palette (Dark/Light)
    Color BgC, FgC, CtlC, SepC, AccC, DimC;
    SolidColorBrush Bg, Fg, Ctl, Sep, Acc, Dim;

    public MainWindow() {
        Title = "MyTunes"; Width = 1100; Height = 700; MinWidth = 860; MinHeight = 540;
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
        var mDatei = new MenuItem { Header = S("Datei", "File") };
        mDatei.Items.Add(MI('\uF021', S("Medien neu scannen", "Rescan media"), "F5", () => StartJob(false, true)));
        mDatei.Items.Add(MI('\uF07B', S("Quellordner w" + "\u00E4" + "hlen", "Choose source folder"), null, PickRoot));
        mDatei.Items.Add(MI('\uF08B', S("Beenden", "Exit"), null, () => Close()));
        var mBearb = new MenuItem { Header = S("Bearbeiten", "Edit") };
        mBearb.Items.Add(MI('\uF0E2', S("Rückgängig", "Undo"), "Strg+Z", () => Undo()));
        mBearb.Items.Add(MI('\uF01E', S("Wiederherstellen", "Redo"), "Strg+Y", () => Redo()));
        mBearb.Items.Add(MI('\uF0C4', S("Ausschneiden", "Cut"), "Strg+X", () => CopyTracks(true)));
        mBearb.Items.Add(MI('\uF0C5', S("Kopieren", "Copy"), "Strg+C", () => CopyTracks(false)));
        mBearb.Items.Add(MI('\uF328', S("Einfügen", "Paste"), "Strg+V", () => PasteTracks()));
        mBearb.Items.Add(MI('\uF14A', S("Alles auswählen", "Select all"), "Strg+A", () => lv.SelectAll()));
        mBearb.Items.Add(MI('\uF002', S("Seite durchsuchen...", "Search..."), "Strg+F", () => SearchTracks()));
        mBearb.Items.Add(MI('\uF061', S("Zum gespielten Track", "Go to playing track"), "Strg+L", () => RevealPlaying()));
        var mAnsicht = new MenuItem { Header = S("Ansicht", "View") };
        mDark = new MenuItem { Header = S("Dunkelmodus", "Dark mode"), IsCheckable = true };
        mDark.Checked += (s, e) => SetDark(true);
        mDark.Unchecked += (s, e) => SetDark(false);
        mAnsicht.Items.Add(MI('\uF186', S("Dunkelmodus", "Dark mode"), null, () => mDark.IsChecked = !mDark.IsChecked));
        menu.Items.Add(mDatei); menu.Items.Add(mBearb); menu.Items.Add(mAnsicht);
        statusLbl = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), TextAlignment = TextAlignment.Right };
        topBar = new DockPanel { LastChildFill = false };
        menu.HorizontalAlignment = HorizontalAlignment.Left;
        DockPanel.SetDock(menu, Dock.Left);
        DockPanel.SetDock(statusLbl, Dock.Right);
        topBar.Children.Add(menu); topBar.Children.Add(statusLbl);
        DockPanel.SetDock(topBar, Dock.Top);
        dock.Children.Add(topBar);

        // --- Statuszeile unten ---
        // Track-Zahl sitzt rechts in der Menuezeile (topBar)

        // --- Player-Leiste unten ---
        seek = new Grid { Height = 34, Cursor = Cursors.Hand };
        seekTrack = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), VerticalAlignment = VerticalAlignment.Center };
        seekFill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        seekThumb = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-6, 0, 0, 0) };
        var es = new Style(typeof(Ellipse));
        var et = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        et.Setters.Add(new Setter(Ellipse.WidthProperty, 16.0));
        et.Setters.Add(new Setter(Ellipse.HeightProperty, 16.0));
        es.Triggers.Add(et);
        seekThumb.Style = es;   // Knubbel waechst bei Hover
        seek.Children.Add(seekTrack); seek.Children.Add(seekFill); seek.Children.Add(seekThumb);
        DockPanel.SetDock(seek, Dock.Top);

        btnPrev = Btn('\uF048'); btnPlay = Btn('\u25B6'); btnNext = Btn('\uF051');
        btnPrev.Click += (s, e) => PrevOrRestart();
        btnPlay.Click += (s, e) => TogglePlay();
        btnNext.Click += (s, e) => Step(1);
        nowLbl = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 8, 0), Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.Hand, ToolTip = S("Doppelklick: zum gespielten Track", "Double-click: go to playing track") };
        nowLbl.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) RevealPlaying(); };
        var volIco = new TextBlock { Text = "\uE767", FontFamily = new FontFamily("Segoe MDL2 Assets"), VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(18, 0, 2, 0), Cursor = Cursors.Hand, ToolTip = "Stumm" };
        volIco.MouseLeftButtonUp += (s, e) => {
            muted = !muted;
            wmp.settings.mute = muted;
            volIco.Text = muted ? "\uE74F" : "\uE767";
            vFill.Background = muted ? Brushes.Gray : Acc;   // Fuellung grau bei Mute
        };
        // eigene schmale Lautstaerke-Leiste (wie Seek-Bar): Fuellung grau bei Mute
        volBar = new Grid { Width = 150, Height = 26, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        vTrack = new Border { Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
        vFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        vThumb = new Ellipse { Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        volBar.Children.Add(vTrack); volBar.Children.Add(vFill); volBar.Children.Add(vThumb);
        SetVol(0.8);
        bool vSeek = false;
        volBar.MouseLeftButtonDown += (s, e) => { vSeek = true; volBar.CaptureMouse(); VolAt(e.GetPosition(volBar).X); };
        volBar.MouseMove += (s, e) => { if (vSeek) VolAt(e.GetPosition(volBar).X); };
        volBar.MouseLeftButtonUp += (s, e) => { vSeek = false; volBar.ReleaseMouseCapture(); };
        volBar.SizeChanged += (s, e) => SetVol(volFrac);   // Breite erst nach Layout bekannt
        timeLbl = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Foreground = Brushes.Gray };
        var seekRow = new DockPanel { Margin = new Thickness(12, 0, 12, 0) };
        DockPanel.SetDock(timeLbl, Dock.Left);
        seekRow.Children.Add(timeLbl);
        seekRow.Children.Add(seek);
        var barTop = new DockPanel();
        DockPanel.SetDock(seekRow, Dock.Top);
        barTop.Children.Add(seekRow);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new UIElement[] { volIco, volBar }) right.Children.Add(c);
        DockPanel.SetDock(right, Dock.Right);
        var transport = new Grid { Width = 186 };
        for (int i = 0; i < 3; i++) transport.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(btnPrev, 0); Grid.SetColumn(btnPlay, 1); Grid.SetColumn(btnNext, 2);
        transport.Children.Add(btnPrev); transport.Children.Add(btnPlay); transport.Children.Add(btnNext);
        DockPanel.SetDock(transport, Dock.Left);
        var barFlow = new DockPanel { Margin = new Thickness(12, 6, 12, 6), LastChildFill = true, MinHeight = 52 };
        foreach (var c in new UIElement[] { transport, right, nowLbl }) barFlow.Children.Add(c);
        barTop.Children.Add(barFlow);
        bar = new Border { Child = barTop, Padding = new Thickness(0), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        bar.BorderBrush = Sep = NewBrush(SepC = Color.FromRgb(224, 224, 224));

        // --- Liste ---
        gv = new GridView();
        gv.Columns.Add(new GridViewColumn { Header = "#", Width = 55, CellTemplate = TextCell("Mark") });
        gv.Columns.Add(new GridViewColumn { Header = S("Titel", "Title"), Width = 450, CellTemplate = TextCell("Title") });
        gv.Columns.Add(new GridViewColumn { Header = S("Interpret", "Artist"), Width = 270, CellTemplate = TextCell("Artist") });
        gv.Columns.Add(new GridViewColumn { Header = "Album", Width = 270, CellTemplate = TextCell("Album") });
        colOrder = gv.Columns.ToList();   // feste Reihenfolge fuer Ein/Aus-Blenden
        colOrder.Add(new GridViewColumn { Header = S("Dauer", "Length"), Width = 70, CellTemplate = TextCell("Dur") });
        colOrder.Add(new GridViewColumn { Header = S("Größe", "Size"), Width = 80, CellTemplate = TextCellFmt("Len", "{0:N0} KB") });
        colOrder.Add(new GridViewColumn { Header = S("Geändert", "Modified"), Width = 140, CellTemplate = TextCellFmt("Modified", "{0:dd.MM.yyyy HH:mm}") });
        colOrder.Add(new GridViewColumn { Header = S("Erstellt", "Created"), Width = 140, CellTemplate = TextCellFmt("Created", "{0:dd.MM.yyyy HH:mm}") });
        colOrder.Add(new GridViewColumn { Header = S("Dateiname", "Filename"), Width = 200, CellTemplate = TextCell("File") });
        var colOn = new bool[colOrder.Count];
        bool[] onSaved = null; double[] wSaved = null;
        try {   // gespeicherte Spalten: "sichtbar,breite|..." in cols.txt
            var parts = File.ReadAllText(ColsFile).Split('|');
            if (parts.Length == colOrder.Count) {
                onSaved = new bool[colOrder.Count]; wSaved = new double[colOrder.Count];
                for (int i = 0; i < parts.Length; i++) {
                    var kv = parts[i].Split(',');
                    onSaved[i] = kv[0] == "1"; wSaved[i] = double.Parse(kv[1]);
                }
            }
        } catch { }
        gv.Columns.Clear();
        for (int i = 0; i < colOrder.Count; i++) {
            colOn[i] = onSaved == null ? i < 4 : onSaved[i];
            if (onSaved != null && wSaved[i] > 0) colOrder[i].Width = wSaved[i];
            if (colOn[i]) gv.Columns.Add(colOrder[i]);
        }
        Func<GridViewColumn, string, bool, MenuItem> ColToggle = (col, name, on) => {   // alter Compiler: kein Local-Function-Syntax
            var mi = new MenuItem { Header = name, IsCheckable = true, IsChecked = on };
            mi.Click += (s, e) => {
                if (mi.IsChecked) {
                    int pos = 0; int want = colOrder.IndexOf(col);
                    for (int i = 0; i < want; i++) if (gv.Columns.Contains(colOrder[i])) pos++;
                    gv.Columns.Insert(Math.Min(pos, gv.Columns.Count), col);
                    var key = props[colOrder.IndexOf(col)];
                    if (key == "DurSec") FillDurations();
                    else if (key == "Len" || key == "Modified" || key == "Created") FillMeta();
                } else gv.Columns.Remove(col);
            };
            return mi;
        };
        colMenu = new ContextMenu();
        var names = new[] { "#", S("Titel", "Title"), S("Interpret", "Artist"), "Album", S("Dauer", "Length"), S("Größe", "Size"), S("Geändert", "Modified"), S("Erstellt", "Created"), S("Dateiname", "Filename") };

        var ctxT = new MenuItem[9];
        for (int i = 0; i < 9; i++) { ctxT[i] = ColToggle(colOrder[i], names[i], colOn[i]); colMenu.Items.Add(ctxT[i]); }
        var mTheme = MI('\uF53F', S("Design", "Theme"), null, null);   // Level-2-Untermenue mit Icon
        var mNorm = new MenuItem { Header = "Normal", IsCheckable = true, IsChecked = theme == 0 };
        var mAlch = new MenuItem { Header = "Alchemist", IsCheckable = true, IsChecked = theme == 1 };
        var mDes = new MenuItem { Header = "Desert", IsCheckable = true, IsChecked = theme == 2 };
        mNorm.Click += (s, e) => { theme = 0; mNorm.IsChecked = true; mAlch.IsChecked = false; mDes.IsChecked = false; SetDark(darkOn); };
        mAlch.Click += (s, e) => { theme = 1; mNorm.IsChecked = false; mAlch.IsChecked = true; mDes.IsChecked = false; SetDark(darkOn); };
        mDes.Click += (s, e) => { theme = 2; mNorm.IsChecked = false; mAlch.IsChecked = false; mDes.IsChecked = true; SetDark(darkOn); };
        mTheme.Items.Add(mNorm); mTheme.Items.Add(mAlch); mTheme.Items.Add(mDes);
        mAnsicht.Items.Add(mTheme);
        var mLang = MI('\uF0AC', S("Sprache", "Language"), null, null);   // Level-2-Untermenue
        var mDe = new MenuItem { Header = "Deutsch", IsCheckable = true, IsChecked = lang == 0 };
        var mEn = new MenuItem { Header = "English", IsCheckable = true, IsChecked = lang == 1 };
        mDe.Click += (s, e) => SetLang(0); mEn.Click += (s, e) => SetLang(1);
        mLang.Items.Add(mDe); mLang.Items.Add(mEn);
        mAnsicht.Items.Add(mLang);
        var mCols = MI('\uF0DB', S("Spalten", "Columns"), null, null);   // Level-2-Untermenue
        var ansT = new MenuItem[9];
        for (int i = 0; i < 9; i++) { ansT[i] = ColToggle(colOrder[i], names[i], colOn[i]); mCols.Items.Add(ansT[i]); }
        mAnsicht.Items.Add(mCols);
        for (int i = 0; i < 9; i++) {   // Haeckchen in beiden Menues synchron halten
            int j = i;
            ctxT[j].Click += (s, e) => ansT[j].IsChecked = ctxT[j].IsChecked;
            ansT[j].Click += (s, e) => ctxT[j].IsChecked = ansT[j].IsChecked;
        }
        lv = new ListView { View = gv, ItemsSource = items, SelectionMode = SelectionMode.Extended, BorderThickness = new Thickness(0) };
        VirtualizingPanel.SetIsVirtualizing(lv, true);
        VirtualizingPanel.SetVirtualizationMode(lv, VirtualizationMode.Recycling);
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(lv, true);   // sonst: 13k Zeilen-Container
        view = CollectionViewSource.GetDefaultView(items);
        lv.GroupStyle.Add(new GroupStyle());
        lv.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        lv.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler((s, e) => {   // Spalten-Resize mit Minimum
            var h = (e.OriginalSource as FrameworkElement).TemplatedParent as GridViewColumnHeader;
            if (h != null && h.Column != null) h.Column.Width = Math.Max(40, h.Column.Width + e.HorizontalChange);
        }));
        lv.MouseDoubleClick += (s, e) => { if (Up<ListViewItem>(e.OriginalSource as DependencyObject) != null && lv.SelectedItem != null) PlayItem((Track)lv.SelectedItem); };   // nur auf Zeilen
        var ctx = new ContextMenu();
        ctx.Items.Add(MI('\uF0C4', S("Ausschneiden", "Cut"), null, () => CopyTracks(true)));
        ctx.Items.Add(MI('\uF0C5', S("Kopieren", "Copy"), null, () => CopyTracks(false)));
        ctx.Items.Add(MI('\uF328', S("Einf" + "\u00FC" + "gen", "Paste"), null, () => PasteTracks()));
        ctx.Items.Add(MI('\uF14A', S("Alles ausw" + "\u00E4" + "hlen", "Select all"), null, () => lv.SelectAll()));
        ctx.Items.Add(MI('\uF07C', S("Datei im Explorer anzeigen", "Show in Explorer"), null, () => {
            var t = lv.SelectedItem as Track;
            if (t != null) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + t.Path + "\"");
        }));
        ctx.Items.Add(MI('\uF05A', S("Informationen", "Info"), null, () => {
            var t = lv.SelectedItem as Track;
            if (t != null) ShowInfo(t);
        }));
        lv.ContextMenu = ctx;
        lv.PreviewKeyDown += (s, e) => {
            if (e.Key == Key.Enter) {
                var fe = Keyboard.FocusedElement as FrameworkElement;
                var t = (fe != null ? fe.DataContext : null) as Track ?? lv.SelectedItem as Track ?? view.Cast<Track>().FirstOrDefault();
                if (t != null) { PlayItem(t); e.Handled = true; }
            }
        };
        lv.GiveFeedback += (s, e) => {   // Badge folgt dem Cursor
            if (dragBadge != null) {   // Cursor.Position = phys. Pixel -> DIP umrechnen (DPI-Skalierung)
                var p = System.Windows.Forms.Cursor.Position;
                var m = PresentationSource.FromVisual(lv).CompositionTarget.TransformToDevice;
                dragBadge.Left = p.X / m.M11 + 4; dragBadge.Top = p.Y / m.M22 + 6;
            }
            e.UseDefaultCursors = false;
            try { System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Arrow; } catch { }   // normaler Pfeil statt Verbotssymbol
            e.Handled = true;
        };
        lv.PreviewMouseLeftButtonDown += (s, e) => {
            dragStart = e.GetPosition(lv);
            var src = e.OriginalSource as DependencyObject;
            var hit = Up<ListViewItem>(src);
            dragOk = hit != null;   // Drag nur auf Zeilen, nicht Header
            dragPaths = null;
            if (dragOk) {   // selektierte Zeile gedrueckt -> ganze Mehrfachauswahl, sonst nur diese
                var t = hit.DataContext as Track;
                dragPaths = (lv.SelectedItems.Contains(t) ? lv.SelectedItems.Cast<Track>() : new[] { t }).Select(x => x.Path).ToArray();
                if (lv.SelectedItems.Contains(t) && (System.Windows.Input.Keyboard.Modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) == 0) { pendingItem = t; e.Handled = true; }   // Drag behaelt Mehrfachauswahl, echter Klick kollabiert spaeter
            }
            if (!dragOk) {   // Klick auf farbige Gruppenzeile -> ganze Gruppe selektieren
                var gi = Up<GroupItem>(src);
                if (gi != null) {
                    var g = gi.DataContext as System.Windows.Data.CollectionViewGroup ?? gi.Content as System.Windows.Data.CollectionViewGroup;
                    if (g != null) { lv.SelectedItems.Clear(); foreach (var x in g.Items) lv.SelectedItems.Add(x); }
                    e.Handled = true;
                }
            }
        };
        lv.PreviewMouseLeftButtonUp += (s, e) => {   // echter Klick (ohne Drag) auf selektierte Zeile kollabiert Auswahl
            if (pendingItem != null) { lv.SelectedItems.Clear(); lv.SelectedItems.Add(pendingItem); pendingItem = null; }
        };
        lv.PreviewMouseMove += (s, e) => {
            if (dragOk && dragPaths != null && dragPaths.Length > 0 && e.LeftButton == MouseButtonState.Pressed &&
                (e.GetPosition(lv) - dragStart).Length > 6) {
                pendingItem = null;
                var col = new StringCollection();
                col.AddRange(dragPaths);
                var d = new System.Windows.DataObject(); d.SetFileDropList(col);
                dragBadge = new Window {   // schwarz auf weissem Rechteck: Anzahl gezogene Tracks
                    WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                    ShowInTaskbar = false, Topmost = true, ShowActivated = false, IsHitTestVisible = false,
                    ResizeMode = ResizeMode.NoResize, MinWidth = 0, MinHeight = 0
                };
                var bb = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(14),   // Luft fuer den Schatten
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 4, Opacity = 0.33 } };
                bb.Child = new TextBlock { Text = dragPaths.Length.ToString(), Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold, FontSize = 12 };
                bb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));   // explizit: exakt auf Text + Margin
                dragBadge.Width = bb.DesiredSize.Width; dragBadge.Height = bb.DesiredSize.Height;
                dragBadge.Content = bb; dragBadge.Show();
                DragDrop.DoDragDrop(lv, d, System.Windows.DragDropEffects.Copy);
                dragBadge.Close(); dragBadge = null;
            }
        };
        dock.Children.Add(lv);

        // --- Tasten ---
        PreviewKeyDown += (s, e) => {
            if (e.Key == Key.Space) { TogglePlay(); e.Handled = true; }   // immer, egal welcher Fokus
        };
        KeyDown += (s, e) => {
            switch (e.Key) {
                case Key.Right: Step(1); e.Handled = true; break;
                case Key.Left: PrevOrRestart(); e.Handled = true; break;
                case Key.F5: StartJob(false, true); e.Handled = true; break;
                case Key.Z: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) Undo(); break;
                case Key.Y: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) Redo(); break;
                case Key.X: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) CopyTracks(true); break;
                case Key.C: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) CopyTracks(false); break;
                case Key.V: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) PasteTracks(); break;
                case Key.A: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) { lv.SelectAll(); e.Handled = true; } break;
                case Key.F: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) SearchTracks(); break;
                case Key.L: if (e.KeyboardDevice.Modifiers == ModifierKeys.Control) { RevealPlaying(); e.Handled = true; } break;
            }
        };

        // --- Seek-Interaktion ---
        seek.MouseLeftButtonDown += (s, e) => { seeking = true; seek.CaptureMouse(); SeekTo(e.GetPosition(seek).X); };
        seek.MouseMove += (s, e) => { if (seeking) SeekTo(e.GetPosition(seek).X); };
        seek.MouseLeftButtonUp += (s, e) => { seeking = false; seek.ReleaseMouseCapture(); SeekTo(e.GetPosition(seek).X); };

        uiTimer.Tick += (s, e) => {
            if (System.Windows.Input.Mouse.Captured is Thumb) return;   // laufenden Drag nicht stoeren
            btnPlay.Content = Glyph((int)wmp.playState == 3 ? '\uF04C' : '\uF04B');
            var pc = btnPlay.Content as TextBlock; if (pc != null) pc.Foreground = btnPlay.IsMouseOver ? Acc : Fg;
            if (playing != null) playing.SetGlyph((int)wmp.playState == 3 ? "\u25B6" : "\u275A\u275A");
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
        if (wantDark) mDark.IsChecked = true;   // letzter Modus wiederherstellen
        hadCache = File.Exists(Cache);
        StartJob(hadCache, true);
    }

    // ---------- UI-Helfer ----------
    SolidColorBrush NewBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    ControlTemplate FlatBtnTemplate() {
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);   // unsichtbar, aber hit-testbar
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.Name = "cp";
        bd.AppendChild(cp);
        tpl.VisualTree = bd;
        var trig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        trig.Setters.Add(new Setter(TextElement.ForegroundProperty, Acc ?? NewBrush(Color.FromRgb(0, 120, 215)), "cp"));
        tpl.Triggers.Add(trig);
        return tpl;
    }

    Button Btn(char c) {
        var b = new Button { Content = Glyph(c), VerticalAlignment = VerticalAlignment.Stretch, Cursor = Cursors.Hand };
        b.Template = FlatBtnTemplate();
        b.MouseEnter += (s, e) => { var t = b.Content as TextBlock; if (t != null) t.Foreground = Acc; };   // Glyph blau
        b.MouseLeave += (s, e) => { var t = b.Content as TextBlock; if (t != null) t.Foreground = Fg; };
        return b;
    }

    DataTemplate TextCellFmt(string prop, string fmt) {
        var t = new DataTemplate();
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding(prop) { StringFormat = fmt });
        tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        t.VisualTree = tb;
        return t;
    }

    bool metaBusy;
    void FillMeta() {   // Len/Modified/Created lazy aus Datei-Infos nachlesen
        if (metaBusy || items.Count == 0) return;
        metaBusy = true;
        Task.Run(() => {
            foreach (var t in items) {
                if (t.Len > 0) continue;
                try { var fi = new FileInfo(t.Path); t.Len = fi.Length / 1024; t.Modified = fi.LastWriteTime; t.Created = fi.CreationTime; } catch { }
            }
            metaBusy = false;
            WriteCache();   // Meta im Cache sichern -> naechster Start instant
            Dispatcher.BeginInvoke((Action)(() => CollectionViewSource.GetDefaultView(items).Refresh()));
        });
    }

    bool durBusy;
    void FillDurations() {   // Shell32 liest "Dauer"-Spalte lazy beim ersten Einblenden
        if (durBusy || items.Count == 0) return;
        durBusy = true;
        var th = new System.Threading.Thread(() => {   // STA: WMP-COM braucht Single-Threaded-Apartment
            try {
                dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic wmp2 = null; string lastDir = "\0"; dynamic folder = null; int durIdx = -1; int dn = 0;
                foreach (var t in items) {
                    if (t.Dur != null) continue;
                    string dir = System.IO.Path.GetDirectoryName(t.Path);
                    if (dir != lastDir) {   // Spaltenindex ist pro Ordner-Typ verschieden -> pro Ordner suchen
                        folder = app.NameSpace(dir); lastDir = dir; durIdx = -1;
                        try {
                            for (int i = 0; i < 320; i++) {
                                string n = folder.GetDetailsOf(null, i);
                                if (n == "Dauer" || n == "Länge" || n == "Length" || n == "Duration") { durIdx = i; break; }
                            }
                            if (durIdx < 0) {   // Wert-Heuristik: Format h:mm:ss ist die Dauer
                                dynamic pf = folder.ParseName(t.File);
                                for (int i = 0; i < 320; i++) {
                                    string v = folder.GetDetailsOf(pf, i);
                                    if (v != null && System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+:\d\d(:\d\d)?$")) { durIdx = i; break; }
                                }
                            }
                        } catch { }
                    }
                    try {
                        if (durIdx >= 0) {
                            string d = folder.GetDetailsOf(folder.ParseName(t.File), durIdx);
                            if (d != null && d.Length == 4 && d[1] == ':') d = "0" + d;   // m:ss -> mm:ss fuer Sortierung
                            t.Dur = d ?? "";
                        }
                    } catch { t.Dur = ""; }
                    if (string.IsNullOrEmpty(t.Dur)) {   // Shell kann manche nicht -> WMP liest sie
                        try {
                            if (wmp2 == null) wmp2 = Activator.CreateInstance(Type.GetTypeFromProgID("WMPlayer.OCX"));
                            string d2 = wmp2.newMedia(t.Path).durationString;
                            if (d2 != null && d2.Length == 4 && d2[1] == ':') d2 = "0" + d2;
                            t.Dur = d2 ?? "";
                        } catch { t.Dur = ""; }
                    }
                    t.DurSec = DurToSec(t.Dur);
                    if (++dn % 500 == 0) {   // inkrementell: Cache + Anzeige alle 500
                        WriteCache();
                        Dispatcher.BeginInvoke((Action)(() => CollectionViewSource.GetDefaultView(items).Refresh()));
                    }
                }
                int dn2 = 0;
                foreach (var t in items) {   // Fallback: ins WMP laden wie beim Abspielen (synct Drive-Dateien)
                    if (!string.IsNullOrEmpty(t.Dur)) continue;
                    try {
                        if (wmp2 == null) { wmp2 = Activator.CreateInstance(Type.GetTypeFromProgID("WMPlayer.OCX")); wmp2.settings.mute = true; }
                        wmp2.URL = t.Path;
                        wmp2.controls.play();   // wie echtes Abspielen -> forciert Sync+Parse, stumm
                        string d3 = null;
                        for (int w = 0; w < 50 && string.IsNullOrEmpty(d3); w++) {
                            try { d3 = wmp2.currentMedia.durationString; } catch { }
                            if (string.IsNullOrEmpty(d3)) System.Threading.Thread.Sleep(100);
                        }
                        wmp2.controls.stop();
                        if (d3 != null && d3.Length == 4 && d3[1] == ':') d3 = "0" + d3;
                        t.Dur = d3 ?? ""; t.DurSec = DurToSec(t.Dur);
                        try { wmp2.URL = ""; } catch { }   // Datei wieder freigeben
                    } catch { t.Dur = ""; }
                    if (++dn2 % 200 == 0) {
                        WriteCache();
                        Dispatcher.BeginInvoke((Action)(() => CollectionViewSource.GetDefaultView(items).Refresh()));
                    }
                }
            } catch { }
            durBusy = false;
            WriteCache();   // Dauer im Cache sichern
            Dispatcher.BeginInvoke((Action)(() => CollectionViewSource.GetDefaultView(items).Refresh()));
        });
        th.SetApartmentState(System.Threading.ApartmentState.STA);
        th.IsBackground = true;
        th.Start();
    }

    DataTemplate TextCell(string prop) {
        var t = new DataTemplate();
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding(prop));
        tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        t.VisualTree = tb;
        return t;
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
        mi.Foreground = fg; mi.BorderBrush = Sep; mi.Style = FlatMenuItemStyle();
        foreach (var c in mi.Items) {
            var s = c as MenuItem;
            if (s != null) SetItemColors(s, bg, fg);
            var sp = c as Separator;
            if (sp != null) sp.Height = 0;
        }
    }

    // flaches MenuItem-Template: Border+Content+Popup, kein Aero-Highlight
    Style FlatMenuItemStyle() {
        var st = new Style(typeof(MenuItem));
        st.Setters.Add(new Setter(MenuItem.BackgroundProperty, Ctl));   // Setter < Trigger -> Hover greift
        var tpl = new ControlTemplate(typeof(MenuItem));
        var bd = new FrameworkElementFactory(typeof(Border));
        var rs = new RelativeSource(RelativeSourceMode.TemplatedParent);
        bd.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = rs });
        var sp = new FrameworkElementFactory(typeof(StackPanel));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetBinding(ContentPresenter.ContentProperty, new Binding("Header") { RelativeSource = rs });
        cp.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("HeaderTemplate") { RelativeSource = rs });
        cp.SetValue(ContentPresenter.MarginProperty, new Thickness(14, 7, 14, 7));
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        var pop = new FrameworkElementFactory(typeof(Popup));
        pop.Name = "pop";
        pop.SetBinding(Popup.IsOpenProperty, new Binding("IsSubmenuOpen") { RelativeSource = rs });
        pop.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        pop.SetValue(Popup.AllowsTransparencyProperty, true);
        var pbd = new FrameworkElementFactory(typeof(Border));
        pbd.SetValue(Border.BackgroundProperty, Ctl);   // exakt dieselbe Brush wie die Zeilen
        pbd.SetValue(Border.EffectProperty, new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 4, Opacity = 0.33 });
        pbd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        pbd.SetValue(Border.PaddingProperty, new Thickness(3));   // Mini-Puffer, damit eckige Zeilen nicht in den Radius stossen
        pbd.SetValue(Border.MarginProperty, new Thickness(8));
        var host = new FrameworkElementFactory(typeof(StackPanel));
        host.SetValue(StackPanel.IsItemsHostProperty, true);
        pbd.AppendChild(host); pop.AppendChild(pbd);
        var igrid = new FrameworkElementFactory(typeof(Grid));
        var cda = new FrameworkElementFactory(typeof(ColumnDefinition));
        cda.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        igrid.AppendChild(cda); igrid.AppendChild(new FrameworkElementFactory(typeof(ColumnDefinition)));
        var tick = new FrameworkElementFactory(typeof(TextBlock));
        tick.SetValue(TextBlock.TextProperty, "");   // leer wenn aus, Haekchen wenn an
        tick.SetValue(TextBlock.ForegroundProperty, Acc);
        tick.SetValue(TextBlock.VisibilityProperty, Visibility.Collapsed);   // nur bei checkbaren Items
        tick.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        tick.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        tick.SetValue(TextBlock.MarginProperty, new Thickness(4, 0, 0, 0));
        tick.SetValue(TextBlock.WidthProperty, 16.0);
        tick.Name = "tick";
        igrid.AppendChild(tick);
        cp.SetValue(Grid.ColumnProperty, 1);
        igrid.AppendChild(cp);
        sp.AppendChild(igrid); sp.AppendChild(pop);
        var arw = new FrameworkElementFactory(typeof(TextBlock));
        arw.Name = "arw";
        arw.SetValue(TextBlock.TextProperty, "\uF054");
        arw.SetValue(TextBlock.FontFamilyProperty, fa);
        arw.SetValue(TextBlock.FontSizeProperty, 9.0);
        arw.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = rs });
        arw.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 10, 0));
        arw.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        arw.SetValue(TextBlock.VisibilityProperty, Visibility.Collapsed);   // nur bei Untermenue
        var dpk = new FrameworkElementFactory(typeof(DockPanel));
        arw.SetValue(DockPanel.DockProperty, Dock.Right);
        dpk.AppendChild(arw); dpk.AppendChild(sp);
        bd.AppendChild(dpk);
        tpl.VisualTree = bd;
        var chkable = new Trigger { Property = MenuItem.IsCheckableProperty, Value = true };
        chkable.Setters.Add(new Setter(TextBlock.VisibilityProperty, Visibility.Visible, "tick"));
        tpl.Triggers.Add(chkable);
        var chk = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
        chk.Setters.Add(new Setter(TextBlock.TextProperty, "✓", "tick"));
        tpl.Triggers.Add(chk);
        var hl = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hl.Setters.Add(new Setter(MenuItem.BackgroundProperty, NewBrush(darkOn ? Color.FromRgb(60, 60, 65) : Color.FromRgb(226, 226, 226))));
        tpl.Triggers.Add(hl);
        var op = new Trigger { Property = MenuItem.IsSubmenuOpenProperty, Value = true };
        op.Setters.Add(new Setter(MenuItem.BackgroundProperty, NewBrush(darkOn ? Color.FromRgb(60, 60, 65) : Color.FromRgb(226, 226, 226))));
        tpl.Triggers.Add(op);
        var sub = new Trigger { Property = MenuItem.RoleProperty, Value = MenuItemRole.SubmenuHeader };
        sub.Setters.Add(new Setter(Popup.PlacementProperty, PlacementMode.Right, "pop"));   // Level-2 seitlich statt drueber
        sub.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "arw"));   // Pfeil rechts
        tpl.Triggers.Add(sub);
        st.Setters.Add(new Setter(MenuItem.TemplateProperty, tpl));
        return st;
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

    void SetVol(double f) {
        f = Math.Max(0, Math.Min(1, f));
        volFrac = f;
        vFill.Width = f * volBar.ActualWidth;
        vThumb.Margin = new Thickness(Math.Max(0, f * volBar.ActualWidth - 5), 0, 0, 0);
        wmp.settings.volume = (int)(f * 100);
    }
    void VolAt(double x) { if (volBar.ActualWidth > 0) SetVol(x / volBar.ActualWidth); }

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
        var col = h.Column.Header.ToString().TrimEnd(' ', '▴', '▾');
        var prop = props[colOrder.IndexOf(h.Column)] ?? "Num";
        int tc = Environment.TickCount;
        if (lastSort == prop && tc - lastClickTick < 500) return;   // Doppelklick ignoriert 2. Klick
        lastClickTick = tc;
        int state = lastSort == prop && lastDir == 1 ? 2 : 1;   // nur auf/ab hin-und-her
        lastSort = prop; lastDir = state;
        foreach (var c in gv.Columns) c.Header = c.Header.ToString().TrimEnd(' ', '▴', '▾');   // alte Pfeile weg
        if (state != 0) h.Column.Header = h.Column.Header.ToString() + (state == 1 ? " ▴" : " ▾");
        // Zeilen dimmen + alles sperren, Sortierung laeuft im Hintergrund-Tick
        IsSorting = true; lv.IsHitTestVisible = false;   // blockt Klicks ohne Disabled-Grauflash
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            try {
                using (view.DeferRefresh()) {
                    view.SortDescriptions.Clear();
                    view.GroupDescriptions.Clear();
                    if (state != 0) {
                        var dir = state == 1 ? ListSortDirection.Ascending : ListSortDirection.Descending;
                        view.SortDescriptions.Add(new SortDescription(prop, dir));
                        if (prop != "Album") view.SortDescriptions.Add(new SortDescription("Album", ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription("NumInt", ListSortDirection.Ascending));   // 2. Kriterium: Titelnummer
                        if (prop != "Title") view.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));   // Fallback: Titelname
                        if (prop == "Album") view.GroupDescriptions.Add(new PropertyGroupDescription("AlbumKey"));
                        else if (prop == "Artist") view.GroupDescriptions.Add(new PropertyGroupDescription("Artist"));
                    }
                }
            } finally {
                IsSorting = false; lv.IsHitTestVisible = true;
                var sel = lv.SelectedItem;
                if (sel != null) {   // selektierten Track an den Top scrollen
                    var lst = view.Cast<Track>().ToList();
                    if (lst.Count > 0) lv.ScrollIntoView(lst[lst.Count - 1]);
                    lv.ScrollIntoView(sel);
                }
            }
        }));
    }
    string lastSort; int lastDir, lastClickTick;
    Window infoWin;
    public static readonly DependencyProperty IsSortingProperty =
        DependencyProperty.Register("IsSorting", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsSorting { get { return (bool)GetValue(IsSortingProperty); } set { SetValue(IsSortingProperty, value); } }

    // ---------- Scan/Cache ----------
    Track Parse(string fullName, bool cached) {
        string dur = null; long len = 0, mt = 0, ct = 0;
        if (cached) {   // Cache-Zeile: pfad|len|modTicks|creTicks|dur
            var p = fullName.Split('|');
            fullName = p[0];
            if (p.Length > 4) {
                long.TryParse(p[1], out len); long.TryParse(p[2], out mt); long.TryParse(p[3], out ct);
                dur = p[4].Length > 0 ? p[4] : null;
            }
        }
        var rel = fullName.Substring(root.Length).TrimStart('\\').Split('\\');
        var title = System.IO.Path.GetFileNameWithoutExtension(fullName); var num = "";
        var m = System.Text.RegularExpressions.Regex.Match(title, @"^(\d+)\s+(.+)$");
        if (m.Success) { num = m.Groups[1].Value; title = m.Groups[2].Value; }
        var t = new Track { Num = num, Title = title, Artist = rel[0], Album = rel.Length >= 3 ? rel[1] : "", Path = fullName, Cached = cached, File = System.IO.Path.GetFileName(fullName), Len = len, Dur = dur, DurSec = DurToSec(dur) };
        if (cached) { if (mt > 0) t.Modified = new DateTime(mt); if (ct > 0) t.Created = new DateTime(ct); }
        else {   // Datei-Meta nur im Scan-Pfad, Cache-Start bleibt instant
            try { var fi = new FileInfo(fullName); t.Len = fi.Length / 1024; t.Modified = fi.LastWriteTime; t.Created = fi.CreationTime; } catch { }
        }
        return t;
    }

    static double DurToSec(string d) {
        if (string.IsNullOrEmpty(d)) return 0;
        double s = 0;
        foreach (var x in d.Split(':')) { double v; if (double.TryParse(x, out v)) s = s * 60 + v; }
        return s;
    }

    void WriteCache() {
        try { File.WriteAllLines(Cache, items.Select(t => t.Path + "|" + t.Len + "|" + t.Modified.Ticks + "|" + t.Created.Ticks + "|" + t.Dur)); } catch { }
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
                if (doScan) foreach (var f in EnumFiles(root)) queue.Enqueue(Parse(f, false));
            } finally { scanDone = true; }
        });
        scanTimer.Start();
    }
    bool hadCache, dragOk;
    Point dragStart;
    string[] dragPaths;
    Window dragBadge;
    object pendingItem;

    static T Up<T>(DependencyObject d) where T : class {
        while (d != null && !(d is T)) d = VisualTreeHelper.GetParent(d);
        return d as T;
    }

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
        statusLbl.Text = scanning ? items.Count + S(" Tracks (scanne... ", " tracks (scanning... ") + scanned.Count + ")" : items.Count + " " + S("Tracks", "tracks");
        if (scanDone && queue.IsEmpty) {
            scanTimer.Stop();
            if (scanned.Count > 0) {
                if (hadCache) MergeDiff();
                WriteCache();
            }
            scanning = false;
            statusLbl.Text = items.Count + " " + S("Tracks", "tracks");
            if (gv.Columns.Contains(colOrder[4])) FillDurations();   // Meta-Spalten lazy nachfuellen
            if (gv.Columns.Contains(colOrder[5]) || gv.Columns.Contains(colOrder[6]) || gv.Columns.Contains(colOrder[7])) FillMeta();
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
    void RevealPlaying() {
        if (playing == null || !items.Contains(playing)) return;
        lv.SelectedItem = playing;
        // zuverlaessig: erst ans Ende (Ziel liegt dann UEBER dem Viewport), dann zum Track -> oberste Zeile
        var list = view.Cast<Track>().ToList();
        int idx = list.IndexOf(playing);
        lv.ScrollIntoView(list[list.Count - 1]);
        lv.ScrollIntoView(list[idx]);
    }

    void PlayItem(Track t, bool record = true) {
        if (t == null) return;
        if (playing != null) playing.IsPlaying = false;
        playing = t; t.IsPlaying = true;
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
        var ref_ = playing ?? lv.SelectedItem as Track;
        int cur = ref_ != null ? ordered.IndexOf(ref_) : -1;
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
        var dest = lv.SelectedItem != null ? Path.GetDirectoryName(((Track)lv.SelectedItem).Path) : root;
        // immer kopieren, nie verschieben
        foreach (var src in (string[])cdo.GetData(System.Windows.DataFormats.FileDrop)) {
            var target = Path.Combine(dest, Path.GetFileName(src));
            try { if (!File.Exists(target)) File.Copy(src, target); } catch { }
        }
        StartJob(false, true);
    }
    void SearchTracks() {
        var q = Microsoft.VisualBasic.Interaction.InputBox(S("Suchen nach (Titel, Interpret, Album):", "Search for (title, artist, album):"), S("Medien durchsuchen", "Search media"));
        if (string.IsNullOrEmpty(q)) return;
        var q2 = q.ToLowerInvariant();
        var hit = items.FirstOrDefault(t => (t.Title + " " + t.Artist + " " + t.Album).ToLowerInvariant().Contains(q2));
        if (hit != null) { lv.SelectedItem = hit; lv.ScrollIntoView(hit); }
        else System.Windows.MessageBox.Show("Kein Treffer für '" + q + "'", "Medien durchsuchen");
    }

    // ---------- Darkmode ----------
    void SetDark(bool on) {
        darkOn = on;
        if (theme == 2) {   // Desert: warm
            BgC = on ? Color.FromRgb(30, 24, 19) : Color.FromRgb(252, 248, 242);
            FgC = on ? Color.FromRgb(225, 215, 205) : Color.FromRgb(60, 45, 35);
            CtlC = on ? Color.FromRgb(42, 34, 27) : Color.FromRgb(246, 238, 226);
            SepC = on ? Color.FromRgb(66, 55, 44) : Color.FromRgb(226, 214, 196);
            AccC = Color.FromRgb(220, 140, 50);
            DimC = on ? Color.FromRgb(170, 155, 140) : Color.FromRgb(150, 130, 115);
        } else {
            BgC = on ? (theme == 1 ? Color.FromRgb(7, 6, 10) : Color.FromRgb(30, 30, 30)) : Colors.White;
            FgC = on ? Color.FromRgb(220, 220, 220) : Colors.Black;
            CtlC = on ? (theme == 1 ? Color.FromRgb(13, 11, 17) : Color.FromRgb(45, 45, 48)) : Color.FromRgb(240, 240, 240);
            SepC = on ? (theme == 1 ? Color.FromRgb(26, 23, 32) : Color.FromRgb(62, 62, 66)) : Color.FromRgb(224, 224, 224);
            AccC = theme == 1 ? Color.FromRgb(150, 100, 230) : (on ? Color.FromRgb(96, 150, 255) : Color.FromRgb(0, 120, 215));
            DimC = on ? (theme == 1 ? Color.FromRgb(165, 155, 180) : Color.FromRgb(160, 160, 160)) : Colors.Gray;
        }
        Bg = NewBrush(BgC); Fg = NewBrush(FgC); Ctl = NewBrush(CtlC); Sep = NewBrush(SepC); Acc = NewBrush(AccC); Dim = NewBrush(DimC);

        Background = Ctl; Foreground = Fg;
        lv.Background = Bg; lv.Foreground = Fg;
        menu.Background = Ctl; menu.Foreground = Fg;
        foreach (MenuItem top in menu.Items) SetItemColors(top, Ctl, Fg);
        topBar.Background = Ctl; statusLbl.Foreground = Fg; statusLbl.Opacity = 0.5;
        bar.Background = Ctl; bar.BorderBrush = Sep;
        seekTrack.Background = NewBrush(on ? Color.FromRgb(62, 62, 66) : Color.FromRgb(214, 214, 214));
        seekFill.Background = Acc; seekThumb.Fill = Acc;
        vTrack.Background = seekTrack.Background;
        vFill.Background = muted ? Brushes.Gray : Acc; vThumb.Fill = Acc;
        nowLbl.Foreground = Dim; timeLbl.Foreground = Dim;
        statusLbl.Foreground = Fg;
        foreach (var b in new[] { btnPrev, btnPlay, btnNext }) { b.Foreground = Fg; b.Template = FlatBtnTemplate(); }
        // ListView: Zeilen, Selektion, Header und Gruppentitel einfaerben
        lv.ItemContainerStyle = RowStyle();
        lv.Resources[typeof(ScrollBar)] = ScrollBarStyle();
        var hstyle = new Style(typeof(GridViewColumnHeader));
        hstyle.Setters.Add(new Setter(Control.BackgroundProperty, Ctl));
        hstyle.Setters.Add(new Setter(Control.ForegroundProperty, Fg));
        hstyle.Setters.Add(new Setter(Control.BorderBrushProperty, Sep));
        hstyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 7, 8, 7)));
        var htpl = new ControlTemplate(typeof(GridViewColumnHeader));   // flach statt Aero-Chrome
        var hbd = new FrameworkElementFactory(typeof(Border));
        hbd.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        hbd.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        hbd.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
        var hcp = new FrameworkElementFactory(typeof(ContentPresenter));
        hcp.SetBinding(ContentPresenter.MarginProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var hgrid = new FrameworkElementFactory(typeof(Grid));
        hgrid.AppendChild(hcp);
        var hthumb = new FrameworkElementFactory(typeof(Thumb));   // Resize-Grip am Spaltenrand
        hthumb.SetValue(Thumb.WidthProperty, 8.0);
        hthumb.SetValue(Thumb.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        hthumb.SetValue(Thumb.BackgroundProperty, Brushes.Transparent);
        hthumb.SetValue(Thumb.CursorProperty, Cursors.SizeWE);
        var ttpl = new ControlTemplate(typeof(Thumb));              // ohne Chrome, sonst weisser Balken sichtbar
        var tbd = new FrameworkElementFactory(typeof(Border));
        tbd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        ttpl.VisualTree = tbd;
        hthumb.SetValue(Thumb.TemplateProperty, ttpl);
        hgrid.AppendChild(hthumb);
        hbd.AppendChild(hgrid); htpl.VisualTree = hbd;
        hstyle.Setters.Add(new Setter(GridViewColumnHeader.TemplateProperty, htpl));
        var hhov = new Trigger { Property = GridViewColumnHeader.IsMouseOverProperty, Value = true };
        hhov.Setters.Add(new Setter(Control.BackgroundProperty, NewBrush(darkOn ? Color.FromRgb(62, 62, 66) : Color.FromRgb(226, 226, 226))));
        hstyle.Triggers.Add(hhov);
        hstyle.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, colMenu));   // Rechtsklick -> Spalten togglen
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

    Style ScrollBarStyle() {   // minimalistisch: nur Pill-Thumb, keine Buttons (XAML zur Laufzeit)
        string bg = darkOn ? "#FF55555F" : "#FFB9B9C3";
        string hv = darkOn ? "#FF7D7D8C" : "#FF8C8CA0";
        string xaml =
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>" +
            "<Setter Property='Background' Value='Transparent'/>" +
            "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>" +
            "<Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True'>" +
            "<Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>" +
            "<Track.Thumb><Thumb Cursor='Hand'><Thumb.Template>" +
            "<ControlTemplate TargetType='{x:Type Thumb}'><Border x:Name='b' Background='" + bg + "'/>" +
            "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'>" +
            "<Setter TargetName='b' Property='Background' Value='" + hv + "'/>" +
            "</Trigger></ControlTemplate.Triggers></ControlTemplate>" +
            "</Thumb.Template></Thumb></Track.Thumb>" +
            "<Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>" +
            "</Track>" +
            "</ControlTemplate></Setter.Value></Setter></Style>";
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    Style RowStyle() {
        var st = new Style(typeof(ListViewItem));
        st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 5, 6, 5)));
        st.Setters.Add(new Setter(Control.ForegroundProperty, Fg));
        st.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        var tpl = new ControlTemplate(typeof(ListViewItem));   // flach, ohne Aero-Selektion
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        bd.AppendChild(new FrameworkElementFactory(typeof(GridViewRowPresenter)));
        tpl.VisualTree = bd;
        st.Setters.Add(new Setter(ListViewItem.TemplateProperty, tpl));
        var hov = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hov.Setters.Add(new Setter(Control.BackgroundProperty, NewBrush(Color.FromArgb(darkOn ? (byte)45 : (byte)30, AccC.R, AccC.G, AccC.B))));
        st.Triggers.Add(hov);
        var pl = new DataTrigger { Binding = new Binding("IsPlaying"), Value = true };
        pl.Setters.Add(new Setter(Control.BackgroundProperty, NewBrush(Color.FromArgb(24, AccC.R, AccC.G, AccC.B))));
        pl.Setters.Add(new Setter(Control.ForegroundProperty, Acc));   // Glyph+Text in Akzentfarbe
        st.Triggers.Add(pl);
        var sel = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };   // letzter Trigger gewinnt -> Selektion schlaegt Playing
        sel.Setters.Add(new Setter(Control.BackgroundProperty, NewBrush(Color.FromArgb(darkOn ? (byte)90 : (byte)45, AccC.R, AccC.G, AccC.B))));
        st.Triggers.Add(sel);
        var dim = new DataTrigger {
            Binding = new Binding("IsSorting") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1) }, Value = true };
        dim.Setters.Add(new Setter(Control.ForegroundProperty, NewBrush(Color.FromArgb((byte)(darkOn ? 60 : 110), FgC.R, FgC.G, FgC.B))));
        st.Triggers.Add(dim);
        return st;
    }

    void SetLang(int l) {   // Sprache merken + Neustart (Labels werden im Ctor gebaut)
        lang = l; SaveBounds();
        try { System.Diagnostics.Process.Start(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
        Application.Current.Shutdown();
    }

    void PickRoot() {   // Quellordner waehlen -> merken + neu scannen
        var d = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = root, Description = "Musik-Ordner wählen" };
        if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        root = d.SelectedPath;
        try { File.WriteAllText(RootFile, root); } catch { }
        items.Clear(); view.Refresh();
        StartJob(false, true);
    }

    void ShowInfo(Track t) {   // nur ein Fenster: vorhandenes wird aktualisiert
        if (infoWin == null) {
            infoWin = new Window {
                Title = S("Informationen", "Info"), Width = 440, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            infoWin.Closed += (s, e) => infoWin = null;
        }
        infoWin.Background = Bg; infoWin.Foreground = Fg;
        var g = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new string[][] {
            new[] { S("Titel", "Title"), t.Title }, new[] { S("Interpret", "Artist"), t.Artist }, new[] { "Album", t.Album },
            new[] { S("Nummer", "Number"), t.Num }, new[] { S("Dateiname", "Filename"), t.File },
            new[] { S("Dauer", "Length"), t.Dur ?? "" },
            new[] { S("Größe", "Size"), t.Len > 0 ? (t.Len / 1024.0).ToString("N1") + " MB" : "" },
            new[] { S("Geändert", "Modified"), t.Modified.Year > 2000 ? t.Modified.ToString("dd.MM.yyyy HH:mm") : "" },
            new[] { S("Erstellt", "Created"), t.Created.Year > 2000 ? t.Created.ToString("dd.MM.yyyy HH:mm") : "" },
            new[] { S("Pfad", "Path"), t.Path }
        };
        for (int i = 0; i < rows.Length; i++) {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = rows[i][0], Foreground = Dim, Margin = new Thickness(0, 4, 16, 4) };
            var v = new TextBox { Text = rows[i][1], IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Fg, Padding = new Thickness(0), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(l, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v);
        }
        infoWin.Content = g;
        if (!infoWin.IsVisible) infoWin.Show();
        infoWin.Activate();
    }

    // ---------- Fenster-Persistenz ----------
    void SaveBounds() {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        try { File.WriteAllText(WinFile, r.X + "," + r.Y + "," + r.Width + "," + r.Height + (WindowState == WindowState.Maximized ? ",max" : "") + (darkOn ? ",dark" : "") + (theme == 1 ? ",alchemist" : theme == 2 ? ",desert" : "") + (lang == 1 ? ",en" : "")); } catch { }
        try {   // Spaltenzustand sichern
            var sb2 = "";
            for (int i = 0; i < colOrder.Count; i++) {
                if (i > 0) sb2 += "|";
                sb2 += (gv.Columns.Contains(colOrder[i]) ? "1" : "0") + "," + colOrder[i].Width;
            }
            File.WriteAllText(ColsFile, sb2);
        } catch { }
    }
    void LoadBounds() {
        try {
            var p = File.ReadAllText(WinFile).Split(',');
            var r = new Rect(double.Parse(p[0]), double.Parse(p[1]), double.Parse(p[2]), double.Parse(p[3]));
            if (SystemParameters.VirtualScreenWidth > 0) { Left = r.X; Top = r.Y; Width = r.Width; Height = r.Height; }
            if (p.Length > 4 && p[4] == "max") WindowState = WindowState.Maximized;
            wantDark = Array.IndexOf(p, "dark") >= 0;
            theme = Array.IndexOf(p, "alchemist") >= 0 ? 1 : Array.IndexOf(p, "desert") >= 0 ? 2 : 0;
            lang = Array.IndexOf(p, "en") >= 0 ? 1 : 0;
        } catch { }
    }
}
