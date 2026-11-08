// MyTunes - iTunes-aehnliche Liste fuer "iTunes Media\Music"
// Artist/Album/Titel kommen aus der Ordnerstruktur (Interpret\Album\NN Titel.ext)
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Program {
    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class Track {
    public string Num, Title, Artist, Album, Path;
    public bool Cached;
}

class TrackSorter : IComparer {
    public int Col; public bool Asc = true;
    static string S(ListViewItem i, int c) { return c == 0 ? i.Text : i.SubItems[c].Text; }
    static int Cmp(ListViewItem x, ListViewItem y, int c, bool num) {
        if (num) {
            int xi, yi;
            bool ox = int.TryParse(S(x, c), out xi), oy = int.TryParse(S(y, c), out yi);
            if (ox && oy) return xi.CompareTo(yi);
            if (ox != oy) return ox ? -1 : 1;
        }
        return string.Compare(S(x, c), S(y, c), StringComparison.CurrentCultureIgnoreCase);
    }
    public int Compare(object a, object b) {
        var x = (ListViewItem)a; var y = (ListViewItem)b;
        int c = Cmp(x, y, Col, Col == 0);
        if (c == 0 && Col != 3) c = Cmp(x, y, 3, false); // Tie-Break: Album
        if (c == 0 && Col != 0) c = Cmp(x, y, 0, true);  // dann Tracknr
        return Asc ? c : -c;
    }
}

class DarkTable : ProfessionalColorTable {
    static readonly Color B = Color.FromArgb(45,45,48), S = Color.FromArgb(62,62,66);
    public override Color ToolStripDropDownBackground { get { return B; } }
    public override Color ImageMarginGradientBegin { get { return B; } }
    public override Color ImageMarginGradientMiddle { get { return B; } }
    public override Color ImageMarginGradientEnd { get { return B; } }
    public override Color MenuItemSelected { get { return S; } }
    public override Color MenuItemBorder { get { return S; } }
    public override Color MenuBorder { get { return S; } }
    public override Color MenuItemSelectedGradientBegin { get { return S; } }
    public override Color MenuItemSelectedGradientEnd { get { return S; } }
    public override Color MenuItemPressedGradientBegin { get { return S; } }
    public override Color MenuItemPressedGradientEnd { get { return S; } }
    public override Color SeparatorDark { get { return S; } }
    public override Color ToolStripBorder { get { return S; } }
    public override Color StatusStripGradientBegin { get { return B; } }
    public override Color StatusStripGradientEnd { get { return B; } }
}

class SeekBar : Panel {
    public double Frac;
    public event Action<double> Seeked;
    public SeekBar() { DoubleBuffered = true; Cursor = Cursors.Hand; }
    void Set(int x) {
        Frac = Math.Max(0.0, Math.Min(1.0, x / (double)Width));
        Invalidate();
        if (Seeked != null) Seeked(Frac);
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e); Capture = true; Set(e.X);
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left) Set(e.X);
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; int y = Height / 2 - 2;
        g.FillRectangle(SystemBrushes.ControlLight, 0, y, Width, 4);
        int fill = (int)(Width * Frac);
        g.FillRectangle(SystemBrushes.Highlight, 0, y, fill, 4);
        g.FillEllipse(SystemBrushes.Highlight, Math.Max(0, fill - 5), y - 3, 10, 10);
    }
}

class MainForm : Form {
    const string Root = @"G:\Meine Ablage\Synchronized\iTunes reduced Copy\iTunes Media\Music";
    static readonly string[] Exts = { ".mp3", ".m4a", ".aac", ".m4p", ".wav", ".flac", ".wma", ".aif", ".aiff" };
    readonly string Cache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tracks.txt");

    ListView lv;
    dynamic wmp;
    Button btnPrev, btnPlay, btnNext;
    Label nowLbl, timeLbl, volIco;
    ToolStripStatusLabel statusLbl;
    TrackBar vol;
    SeekBar seek;
    MenuStrip menu;
    StatusStrip status;
    ToolStripMenuItem mDark;
    Panel bar;
    FlowLayoutPanel flow;

    readonly Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
    readonly TrackSorter sorter = new TrackSorter();
    readonly List<ListViewItem> history = new List<ListViewItem>();
    int histPos = -1;

    // Hintergrund-Scan
    ConcurrentQueue<Track> queue = new ConcurrentQueue<Track>();
    readonly List<Track> scanned = new List<Track>();
    readonly List<Track> pending = new List<Track>();
    volatile bool scanDone, scanning;
    bool hadCache;
    readonly Timer scanTimer = new Timer { Interval = 200 };
    readonly Timer uiTimer = new Timer { Interval = 500 };
    bool seeking;

    static readonly Font IconFont = new Font("Segoe MDL2 Assets", 11f);

    public MainForm() {
        Text = "MyTunes"; Width = 1100; Height = 750; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f); KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wmplayer.exe")); } catch { }

        lv = new ListView {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
            ListViewItemSorter = sorter
        };
        lv.Columns.Add("#", 40); lv.Columns.Add("Titel", 420); lv.Columns.Add("Interpret", 260); lv.Columns.Add("Album", 260);
        Controls.Add(lv);

        // --- Player-Leiste unten ---
        btnPrev = NewBtn('\uE76B');
        btnPlay = NewBtn('\uE768');
        btnNext = NewBtn('\uE76C');
        nowLbl = new Label { AutoSize = true, Padding = new Padding(14, 9, 0, 0), ForeColor = Color.DimGray };
        volIco = new Label { Text = "\uE767", Font = IconFont, AutoSize = true, Padding = new Padding(14, 8, 0, 0) };
        vol = new TrackBar { TickStyle = TickStyle.None, Width = 110 };
        vol.AutoSize = false; vol.Height = 30; vol.Maximum = 100; vol.Value = 80;
        timeLbl = new Label { AutoSize = true, Padding = new Padding(14, 9, 0, 0), ForeColor = Color.DimGray };
        flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 3, 0, 0), WrapContents = false };
        flow.Controls.AddRange(new Control[] { btnPrev, btnPlay, btnNext, nowLbl, volIco, vol, timeLbl });
        seek = new SeekBar { Dock = DockStyle.Top, Height = 24 };
        bar = new Panel { Dock = DockStyle.Bottom, Height = 76 };
        bar.Controls.Add(flow);
        bar.Controls.Add(seek);
        Controls.Add(bar);

        status = new StatusStrip();
        statusLbl = new ToolStripStatusLabel();
        status.Items.Add(statusLbl);
        Controls.Add(status);

        menu = new MenuStrip();
        var mDatei = new ToolStripMenuItem("Datei");
        mDatei.DropDownItems.Add(new ToolStripMenuItem("Medien neu scannen", null, (s, e) => StartJob(false, true), Keys.F5));
        mDatei.DropDownItems.Add(new ToolStripMenuItem("Beenden", null, (s, e) => Close()));
        var mBearb = new ToolStripMenuItem("Bearbeiten");
        mBearb.DropDownItems.AddRange(new ToolStripItem[] {
            new ToolStripMenuItem("Rückgängig", null, (s, e) => Undo(), Keys.Control | Keys.Z),
            new ToolStripMenuItem("Wiederherstellen", null, (s, e) => Redo(), Keys.Control | Keys.Y),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Ausschneiden", null, (s, e) => CopyTracks(true), Keys.Control | Keys.X),
            new ToolStripMenuItem("Kopieren", null, (s, e) => CopyTracks(false), Keys.Control | Keys.C),
            new ToolStripMenuItem("Einfügen", null, (s, e) => PasteTracks(), Keys.Control | Keys.V),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Alles auswählen", null, (s, e) => SelectAll(), Keys.Control | Keys.A),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Seite durchsuchen...", null, (s, e) => SearchTracks(), Keys.Control | Keys.F)
        });
        var mAnsicht = new ToolStripMenuItem("Ansicht");
        mDark = new ToolStripMenuItem("Dunkelmodus") { CheckOnClick = true };
        mDark.Click += (s, e) => SetDark(mDark.Checked);
        mAnsicht.DropDownItems.Add(mDark);
        menu.Items.AddRange(new ToolStripItem[] { mDatei, mBearb, mAnsicht });
        MainMenuStrip = menu;
        Controls.Add(menu);

        wmp = Activator.CreateInstance(Type.GetTypeFromProgID("WMPlayer.OCX"));
        wmp.settings.volume = 80;

        // --- Events ---
        KeyDown += (s, e) => {
            switch (e.KeyCode) {
                case Keys.Right: Step(1); e.SuppressKeyPress = true; break;
                case Keys.Left: PrevOrRestart(); e.SuppressKeyPress = true; break;
                case Keys.Enter: if (lv.SelectedItems.Count > 0) PlayItem(lv.SelectedItems[0]); e.SuppressKeyPress = true; break;
                case Keys.Space: TogglePlay(); e.SuppressKeyPress = true; break;
            }
        };
        btnPrev.Click += (s, e) => { PrevOrRestart(); lv.Focus(); };
        btnNext.Click += (s, e) => { Step(1); lv.Focus(); };
        btnPlay.Click += (s, e) => { TogglePlay(); lv.Focus(); };
        vol.ValueChanged += (s, e) => wmp.settings.volume = vol.Value;
        seek.Seeked += f => {
            try {
                if (wmp.currentMedia != null) {
                    double d = wmp.currentMedia.duration;
                    if (d > 0) wmp.controls.currentPosition = d * f;
                }
            } catch { }
        };
        seek.MouseDown += (s, e) => seeking = true;
        seek.MouseUp += (s, e) => seeking = false;
        lv.DoubleClick += (s, e) => { if (lv.SelectedItems.Count > 0) PlayItem(lv.SelectedItems[0]); };
        lv.ColumnClick += (s, e) => {
            if (sorter.Col == e.Column) sorter.Asc = !sorter.Asc;
            else { sorter.Col = e.Column; sorter.Asc = true; }
            lv.ShowGroups = e.Column == 3;   // Gruppierung nur bei Album-Sortierung
            lv.Sort();
        };
        lv.ItemDrag += (s, e) => {
            var paths = lv.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToArray();
            if (paths.Length == 0) { var it = e.Item as ListViewItem; if (it != null) paths = new[] { (string)it.Tag }; }
            var col = new StringCollection(); col.AddRange(paths);
            var d = new DataObject(); d.SetFileDropList(col);
            lv.DoDragDrop(d, DragDropEffects.Copy);   // nur Copy -> Explorer verschiebt nie
        };

        uiTimer.Tick += (s, e) => {
            btnPlay.Text = ((char)((int)wmp.playState == 3 ? 0xE769 : 0xE768)).ToString();
            double d = 0;
            try { if (wmp.currentMedia != null) d = wmp.currentMedia.duration; } catch { }
            if (d > 0) {
                double p = wmp.controls.currentPosition;
                if (!seeking) { seek.Frac = p / d; seek.Invalidate(); }
                timeLbl.Text = string.Format("{0:mm\\:ss} / {1:mm\\:ss}",
                    TimeSpan.FromSeconds(p), TimeSpan.FromSeconds(d));
            }
        };
        uiTimer.Start();

        scanTimer.Tick += ScanTick;
        FormClosed += (s, e) => { scanTimer.Stop(); uiTimer.Stop(); try { wmp.close(); } catch { } };

        hadCache = File.Exists(Cache);
        StartJob(hadCache, true);   // Cache sofort, Scan immer im Hintergrund
    }

    Button NewBtn(char c) {
        return new Button { Width = 44, Height = 32, Font = IconFont, Text = c.ToString(), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 } };
    }

    // ---------- Tracks/Parsing ----------
    static Track Parse(string fullName, bool cached) {
        var rel = fullName.Substring(Root.Length).TrimStart('\\').Split('\\');
        var title = Path.GetFileNameWithoutExtension(fullName); var num = "";
        var m = System.Text.RegularExpressions.Regex.Match(title, @"^(\d+)\s+(.+)$");
        if (m.Success) { num = m.Groups[1].Value; title = m.Groups[2].Value; }
        return new Track {
            Num = num, Title = title, Artist = rel[0],
            Album = rel.Length >= 3 ? rel[1] : "", Path = fullName, Cached = cached
        };
    }

    static IEnumerable<string> EnumFiles(string root) {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0) {
            var dir = stack.Pop();
            string[] files; try { files = Directory.GetFiles(dir); } catch { continue; }
            foreach (var f in files)
                if (Exts.Contains(Path.GetExtension(f).ToLowerInvariant())) yield return f;
            string[] subs; try { subs = Directory.GetDirectories(dir); } catch { continue; }
            foreach (var sd in subs) stack.Push(sd);
        }
    }

    ListViewItem NewItem(Track t) {
        var key = t.Artist + " - " + t.Album;
        ListViewGroup g;
        if (!groups.TryGetValue(key, out g)) { g = new ListViewGroup(key); lv.Groups.Add(g); groups[key] = g; }
        var it = new ListViewItem(t.Num);
        it.SubItems.Add(t.Title); it.SubItems.Add(t.Artist); it.SubItems.Add(t.Album);
        it.Tag = t.Path; it.Group = g;
        return it;
    }

    void BuildList(List<Track> tracks) {
        lv.BeginUpdate();
        lv.Items.Clear(); lv.Groups.Clear(); groups.Clear();
        lv.Items.AddRange(tracks.Select(NewItem).ToArray());
        lv.EndUpdate();
        lv.Sort();
    }

    // ---------- Hintergrund-Scan ----------
    void StartJob(bool doCache, bool doScan) {
        if (scanning) return;
        scanning = true; scanDone = false;
        if (doScan) scanned.Clear();
        queue = new ConcurrentQueue<Track>();
        Task.Run(() => {
            try {
                if (doCache && File.Exists(Cache))
                    foreach (var line in File.ReadLines(Cache)) queue.Enqueue(Parse(line, true));
                if (doScan)
                    foreach (var f in EnumFiles(Root)) queue.Enqueue(Parse(f, false));
            } finally { scanDone = true; }
        });
        scanTimer.Start();
    }

    void ScanTick(object s, EventArgs e) {
        Track t;
        while (queue.TryDequeue(out t)) {
            if (t.Cached || !hadCache) pending.Add(t);
            if (!t.Cached) scanned.Add(t);
        }
        int limit = lv.Items.Count > 0 ? 2000 : 400;   // erster Chunk frueh sichtbar
        if (pending.Count >= limit || (scanDone && pending.Count > 0)) {
            lv.BeginUpdate();
            lv.Items.AddRange(pending.Select(NewItem).ToArray());
            pending.Clear();
            lv.EndUpdate();
        }
        statusLbl.Text = lv.Items.Count + " Tracks";
        Text = hadCache && scanning ? "MyTunes - " + lv.Items.Count + " Tracks (scanne... " + scanned.Count + ")"
            : hadCache ? "MyTunes - " + lv.Items.Count + " Tracks"
            : "MyTunes - scanne... " + lv.Items.Count;
        if (scanDone && queue.IsEmpty) {
            scanTimer.Stop();
            if (scanned.Count > 0) {
                if (hadCache) MergeDiff();
                File.WriteAllLines(Cache, scanned.Select(x => x.Path));
            }
            scanning = false;
            Text = "MyTunes - " + lv.Items.Count + " Tracks";
        }
    }

    void MergeDiff() {
        var scanPaths = new HashSet<string>(scanned.Select(x => x.Path));
        var havePaths = new HashSet<string>(lv.Items.Cast<ListViewItem>().Select(i => (string)i.Tag));
        lv.BeginUpdate();
        foreach (var it in lv.Items.Cast<ListViewItem>().ToArray())
            if (!scanPaths.Contains((string)it.Tag)) lv.Items.Remove(it);
        foreach (var t in scanned)
            if (!havePaths.Contains(t.Path)) lv.Items.Add(NewItem(t));
        lv.EndUpdate();
        lv.Sort();
    }

    // ---------- Wiedergabe ----------
    void PlayItem(ListViewItem it, bool record = true) {
        if (it == null) return;
        foreach (ListViewItem i in lv.SelectedItems) i.Selected = false;
        it.Selected = true; it.Focused = true; it.EnsureVisible();
        wmp.URL = (string)it.Tag;
        seek.Frac = 0;
        nowLbl.Text = it.SubItems[2].Text + " - " + it.SubItems[1].Text;
        Text = "MyTunes - " + nowLbl.Text;
        if (record) {
            histPos++;
            if (histPos < history.Count) history.RemoveRange(histPos, history.Count - histPos);
            history.Add(it);
        }
    }

    void Step(int d) {
        // sichtbare Reihenfolge: bei Gruppierung Gruppen der Reihe nach, sonst Items
        var ordered = new List<ListViewItem>();
        if (lv.ShowGroups && lv.Groups.Count > 0)
            foreach (ListViewGroup g in lv.Groups) foreach (ListViewItem it in g.Items) ordered.Add(it);
        else
            foreach (ListViewItem it in lv.Items) ordered.Add(it);
        int cur = lv.SelectedItems.Count > 0 ? ordered.IndexOf(lv.SelectedItems[0]) : -1;
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
        else if (lv.SelectedItems.Count > 0) PlayItem(lv.SelectedItems[0]);
    }

    void Undo() {
        if (histPos > 0 && history[histPos - 1].ListView != null) { histPos--; PlayItem(history[histPos], false); }
    }
    void Redo() {
        if (histPos < history.Count - 1 && history[histPos + 1].ListView != null) { histPos++; PlayItem(history[histPos], false); }
    }

    // ---------- Bearbeiten ----------
    void CopyTracks(bool cut) {
        var paths = lv.SelectedItems.Cast<ListViewItem>().Select(i => (string)i.Tag).ToArray();
        if (paths.Length == 0) return;
        var col = new StringCollection(); col.AddRange(paths);
        var d = new DataObject(); d.SetFileDropList(col);
        var ms = new MemoryStream(); ms.WriteByte((byte)(cut ? 2 : 5));   // Preferred DropEffect: 2=Move, 5=Copy
        d.SetData("Preferred DropEffect", ms);
        Clipboard.SetDataObject(d);
    }

    void PasteTracks() {
        var cdo = Clipboard.GetDataObject();
        if (!cdo.GetDataPresent(DataFormats.FileDrop)) return;
        var dest = lv.SelectedItems.Count > 0 ? Path.GetDirectoryName((string)lv.SelectedItems[0].Tag) : Root;
        bool move = false;
        try { move = (((MemoryStream)cdo.GetData("Preferred DropEffect")).ReadByte() & 2) != 0; } catch { }
        foreach (var src in (string[])cdo.GetData(DataFormats.FileDrop)) {
            var target = Path.Combine(dest, Path.GetFileName(src));
            try {
                if (!File.Exists(target)) {
                    if (move) File.Move(src, target); else File.Copy(src, target);
                }
            } catch { }
        }
        StartJob(false, true);
    }

    void SelectAll() {
        lv.BeginUpdate();
        foreach (ListViewItem it in lv.Items) it.Selected = true;
        lv.EndUpdate();
    }

    void SearchTracks() {
        var q = Microsoft.VisualBasic.Interaction.InputBox(
            "Suchen nach (Titel, Interpret, Album):", "Medien durchsuchen");
        if (string.IsNullOrEmpty(q)) return;
        var q2 = q.ToLowerInvariant();
        foreach (ListViewItem it in lv.Items) {
            var hay = (it.SubItems[1].Text + " " + it.SubItems[2].Text + " " + it.SubItems[3].Text).ToLowerInvariant();
            if (hay.Contains(q2)) {
                foreach (ListViewItem i in lv.SelectedItems) i.Selected = false;
                it.Selected = true; it.Focused = true; it.EnsureVisible();
                return;
            }
        }
        MessageBox.Show("Kein Treffer für '" + q + "'", "Medien durchsuchen");
    }

    // ---------- Ansicht ----------
    bool darkOn;
    void SetDark(bool on) {
        darkOn = on;
        var bg = on ? Color.FromArgb(30, 30, 30) : SystemColors.Window;
        var fg = on ? Color.FromArgb(220, 220, 220) : SystemColors.WindowText;
        var ctl = on ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
        BackColor = ctl;
        lv.BackColor = bg; lv.ForeColor = fg;
        bar.BackColor = ctl; flow.BackColor = ctl; seek.BackColor = ctl; vol.BackColor = ctl;
        nowLbl.ForeColor = fg; timeLbl.ForeColor = fg; volIco.ForeColor = fg;
        status.BackColor = ctl; statusLbl.BackColor = ctl; statusLbl.ForeColor = fg;
        menu.BackColor = ctl; menu.ForeColor = fg;
        foreach (ToolStripMenuItem mi in menu.Items) {
            mi.BackColor = ctl; mi.ForeColor = fg;
            foreach (ToolStripItem d in mi.DropDownItems) { d.BackColor = ctl; d.ForeColor = fg; }
        }
        foreach (var b in new[] { btnPrev, btnPlay, btnNext }) { b.BackColor = ctl; b.ForeColor = fg; }
        var r = on ? (ToolStripRenderer)new ToolStripProfessionalRenderer(new DarkTable())
                   : new ToolStripProfessionalRenderer();
        menu.Renderer = r; status.Renderer = r;
        seek.Invalidate();
    }
}
