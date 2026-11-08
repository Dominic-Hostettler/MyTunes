# MyTunes - iTunes-ähnliche Liste für "iTunes Media\Music"
# Artist/Album/Titel kommen aus der Ordnerstruktur (Interpret\Album\NN Titel.ext)

if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    powershell -STA -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    exit
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
Add-Type -TypeDefinition @'
using System;
using System.Collections;
using System.Drawing;
using System.Windows.Forms;
public class DarkTable : ProfessionalColorTable {
    static Color B = Color.FromArgb(45,45,48), S = Color.FromArgb(62,62,66);
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
public class TrackSorter : IComparer {
    public int Col = 0; public bool Asc = true;
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
'@ -ReferencedAssemblies System.Windows.Forms, System.Drawing

$root = 'G:\Meine Ablage\Synchronized\iTunes reduced Copy\iTunes Media\Music'

$form = New-Object Windows.Forms.Form -Property @{
    Text = 'MyTunes'; Width = 1100; Height = 750; StartPosition = 'CenterScreen'
    Font = New-Object Drawing.Font('Segoe UI', 9); KeyPreview = $true
}
try { $form.Icon = [Drawing.Icon]::ExtractAssociatedIcon("$env:WINDIR\System32\wmplayer.exe") } catch { }
$lv = New-Object Windows.Forms.ListView -Property @{
    Dock = 'Fill'; View = 'Details'; FullRowSelect = $true; HideSelection = $false
}
$lv.Columns.Add('#', 40) | Out-Null
$lv.Columns.Add('Titel', 420) | Out-Null
$lv.Columns.Add('Interpret', 260) | Out-Null
$lv.Columns.Add('Album', 260) | Out-Null
$form.Controls.Add($lv)

# --- Player-Leiste unten ---
$iconFont = New-Object Drawing.Font('Segoe MDL2 Assets', 11)
function New-Btn([int]$c) {
    $b = New-Object Windows.Forms.Button -Property @{
        Width = 44; Height = 32; Font = $iconFont; FlatStyle = 'Flat'; Text = [string][char]$c
    }
    $b.FlatAppearance.BorderSize = 0
    $b
}
$btnPrev = New-Btn 0xE76B   # previous
$btnPlay = New-Btn 0xE768   # play
$btnNext = New-Btn 0xE76C   # next
$nowLbl  = New-Object Windows.Forms.Label -Property @{ AutoSize = $true; Padding = '14,9,0,0'; ForeColor = 'DimGray' }
$volIco  = New-Object Windows.Forms.Label -Property @{ Text = [string][char]0xE767; Font = $iconFont; AutoSize = $true; Padding = '14,8,0,0' }
$vol     = New-Object Windows.Forms.TrackBar -Property @{ TickStyle = 'None'; Width = 110 }
$vol.AutoSize = $false; $vol.Height = 30; $vol.Maximum = 100; $vol.Value = 80
$timeLbl = New-Object Windows.Forms.Label -Property @{ AutoSize = $true; Padding = '14,9,0,0'; ForeColor = 'DimGray' }
$flow = New-Object Windows.Forms.FlowLayoutPanel -Property @{ Dock = 'Fill'; Padding = '8,3,0,0'; WrapContents = $false }
$flow.Controls.AddRange(@($btnPrev, $btnPlay, $btnNext, $nowLbl, $volIco, $vol, $timeLbl))
$seek = New-Object Windows.Forms.Panel -Property @{ Dock = 'Top'; Height = 24; Cursor = 'Hand' }
$seek.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($seek, $true)
$bar  = New-Object Windows.Forms.Panel -Property @{ Dock = 'Bottom'; Height = 76 }
$bar.Controls.Add($flow)
$bar.Controls.Add($seek)
$form.Controls.Add($bar)

$status = New-Object Windows.Forms.StatusStrip
$statusLbl = New-Object Windows.Forms.ToolStripStatusLabel
$status.Items.Add($statusLbl) | Out-Null
$form.Controls.Add($status)

$menu = New-Object Windows.Forms.MenuStrip
$mDatei = New-Object Windows.Forms.ToolStripMenuItem 'Datei'
$mScan = New-Object Windows.Forms.ToolStripMenuItem 'Medien neu scannen', $null, { Start-Job $false $true }, 'F5'
$mExit = New-Object Windows.Forms.ToolStripMenuItem 'Beenden', $null, { $form.Close() }
$mDatei.DropDownItems.AddRange(@($mScan, $mExit))
$mBearb = New-Object Windows.Forms.ToolStripMenuItem 'Bearbeiten'
$mUndo   = New-Object Windows.Forms.ToolStripMenuItem 'Rückgängig', $null, { Undo }, 'Control, Z'
$mRedo   = New-Object Windows.Forms.ToolStripMenuItem 'Wiederherstellen', $null, { Redo }, 'Control, Y'
$mCut    = New-Object Windows.Forms.ToolStripMenuItem 'Ausschneiden', $null, { Copy-Tracks $true }, 'Control, X'
$mCopy   = New-Object Windows.Forms.ToolStripMenuItem 'Kopieren', $null, { Copy-Tracks $false }, 'Control, C'
$mPaste  = New-Object Windows.Forms.ToolStripMenuItem 'Einfügen', $null, { Paste-Tracks }, 'Control, V'
$mSelAll = New-Object Windows.Forms.ToolStripMenuItem 'Alles auswählen', $null, { Select-All }, 'Control, A'
$mFind   = New-Object Windows.Forms.ToolStripMenuItem 'Seite durchsuchen...', $null, { Search-Tracks }, 'Control, F'
$mBearb.DropDownItems.AddRange(@(
    $mUndo, $mRedo, (New-Object Windows.Forms.ToolStripSeparator),
    $mCut, $mCopy, $mPaste, (New-Object Windows.Forms.ToolStripSeparator),
    $mSelAll, (New-Object Windows.Forms.ToolStripSeparator), $mFind))

$mAnsicht = New-Object Windows.Forms.ToolStripMenuItem 'Ansicht'
$mDark = New-Object Windows.Forms.ToolStripMenuItem 'Dunkelmodus'
$mDark.CheckOnClick = $true
$mDark.Add_Click({ Set-Dark $mDark.Checked })
$mAnsicht.DropDownItems.Add($mDark) | Out-Null

$menu.Items.AddRange(@($mDatei, $mBearb, $mAnsicht))
$form.MainMenuStrip = $menu
$form.Controls.Add($menu)

$cache = Join-Path $PSScriptRoot 'tracks.txt'   # ein Pfad pro Zeile - schnell lesbar
Remove-Item (Join-Path $PSScriptRoot 'tracks.clixml') -ErrorAction Ignore   # altes Cache-Format
$groups = @{}

function New-TrackItem($t) {
    $gkey = "$($t.Artist) - $($t.Album)"
    if (-not $groups[$gkey]) {
        $groups[$gkey] = New-Object Windows.Forms.ListViewGroup $gkey
        $lv.Groups.Add($groups[$gkey]) | Out-Null
    }
    $it = New-Object Windows.Forms.ListViewItem $t.Num
    $it.SubItems.Add($t.Title)  | Out-Null
    $it.SubItems.Add($t.Artist) | Out-Null
    $it.SubItems.Add($t.Album)  | Out-Null
    $it.Tag = $t.Path
    $it.Group = $groups[$gkey]
    $it
}

function Build-List($tracks) {
    $lv.BeginUpdate()
    $lv.Items.Clear(); $lv.Groups.Clear(); $groups.Clear()
    $lv.Items.AddRange([Windows.Forms.ListViewItem[]]($tracks | ForEach-Object { New-TrackItem $_ }))
    $lv.EndUpdate()
    $lv.Sort()
}

# Scan + Cache-Lesen in separatem Runspace -> UI friert nie ein
$hadCache = Test-Path $cache
$sb = {
    param($root, $cache, $sync, $doCache, $doScan)
    function Push-Track($fullName, [bool]$cached) {
        $rel = $fullName.Substring($root.Length).TrimStart('\') -split '\\'
        $num = ''; $title = [IO.Path]::GetFileNameWithoutExtension($fullName)
        if ($title -match '^\d+\s+(.+)$') { $num = $Matches[0] -replace '\s.*'; $title = $Matches[1] }
        $sync.Q.Enqueue([pscustomobject]@{
            Num    = $num
            Title  = $title
            Artist = $rel[0]
            Album  = if ($rel.Count -ge 3) { $rel[1] } else { '' }
            Path   = $fullName
            Cached = $cached
        })
    }
    try {
        if ($doCache -and (Test-Path $cache)) { Get-Content $cache | ForEach-Object { Push-Track $_ $true } }
        if ($doScan) {
            Get-ChildItem $root -Recurse -File -Include *.mp3,*.m4a,*.aac,*.m4p,*.wav,*.flac,*.wma,*.aif,*.aiff |
                ForEach-Object { Push-Track $_.FullName $false }
        }
    } finally { $sync.Done = $true }
}

$tracks = New-Object Collections.Generic.List[object]
$pending = New-Object Collections.Generic.List[object]
$timer = New-Object Windows.Forms.Timer -Property @{ Interval = 200 }
$timer.Add_Tick({
    $t = $null
    while ($sync.Q.TryDequeue([ref]$t)) {
        if ($t.Cached -or -not $hadCache) { $pending.Add($t) }
        if (-not $t.Cached) { $tracks.Add($t) }
    }
    $limit = if ($lv.Items.Count) { 2000 } else { 400 }   # erster Chunk frueh sichtbar
    if ($pending.Count -ge $limit -or ($sync.Done -and $pending.Count)) {
        $lv.BeginUpdate()
        $lv.Items.AddRange([Windows.Forms.ListViewItem[]]($pending | ForEach-Object { New-TrackItem $_ }))
        $pending.Clear()
        $lv.EndUpdate()   # ein Repaint pro Chunk
    }
    $statusLbl.Text = "$($lv.Items.Count) Tracks"
    $form.Text = if ($hadCache -and $script:scanning) { "MyTunes - $($lv.Items.Count) Tracks (scanne... $($tracks.Count))" }
                 elseif ($hadCache) { "MyTunes - $($lv.Items.Count) Tracks" }
                 else { "MyTunes - scanne... $($lv.Items.Count)" }
    if ($sync.Done -and $sync.Q.IsEmpty) {
        $timer.Stop()
        try { $ps.EndInvoke($handle) } catch { }
        if ($tracks.Count) {
            if ($hadCache) {
                # Differenz zum sichtbaren Stand mergen: neue rein, geloeschte raus
                $scanPaths = @{}; foreach ($t in $tracks) { $scanPaths[$t.Path] = $true }
                $havePaths = @{}; foreach ($it in $lv.Items) { $havePaths[$it.Tag] = $true }
                $lv.BeginUpdate()
                foreach ($it in @($lv.Items)) { if (-not $scanPaths[$it.Tag]) { $lv.Items.Remove($it) } }
                foreach ($t in $tracks) { if (-not $havePaths[$t.Path]) { $lv.Items.Add((New-TrackItem $t)) | Out-Null } }
                $lv.EndUpdate()
                $lv.Sort()
            }
            ($tracks | ForEach-Object Path) | Set-Content $cache -Encoding UTF8
        }
        $script:scanning = $false
        $form.Text = "MyTunes - $($lv.Items.Count) Tracks"
        $ps.Dispose(); $rs.Close()
    }
})

$script:scanning = $false
function Start-Job([bool]$doCache, [bool]$doScan) {
    if ($script:scanning) { return }
    $script:scanning = $true
    if ($doScan) { $script:tracks.Clear() }
    $script:sync = [hashtable]::Synchronized(@{ Q = [Collections.Concurrent.ConcurrentQueue[object]]::new(); Done = $false })
    $script:rs = [runspacefactory]::CreateRunspace(); $script:rs.Open()
    $script:ps = [powershell]::Create(); $script:ps.Runspace = $script:rs
    [void]$script:ps.AddScript($sb).AddParameter('root', $root).AddParameter('cache', $cache).AddParameter('sync', $script:sync).AddParameter('doCache', $doCache).AddParameter('doScan', $doScan)
    $script:handle = $script:ps.BeginInvoke()
    $timer.Start()
}
# Cache sofort laden; Hintergrund-Scan laeuft immer und merged Aenderungen am Ende rein
Start-Job $hadCache $true

$wmp = New-Object -ComObject WMPlayer.OCX
$wmp.settings.volume = 80

$history = New-Object Collections.Generic.List[object]
$histPos = -1
function Play-Item($it, [bool]$record = $true) {
    if (-not $it) { return }
    foreach ($i in $lv.SelectedItems) { $i.Selected = $false }
    $it.Selected = $true; $it.Focused = $true; $it.EnsureVisible()
    $wmp.URL = $it.Tag
    $script:seekFrac = 0.0
    $nowLbl.Text = "$($it.SubItems[2].Text) - $($it.SubItems[1].Text)"
    $form.Text = "MyTunes - $($nowLbl.Text)"
    if ($record) {
        $script:histPos++
        if ($histPos -lt $history.Count) { $history.RemoveRange($histPos, $history.Count - $histPos) }
        $history.Add($it)
    }
}

function Undo {
    if ($histPos -gt 0 -and $history[$histPos - 1].ListView) { $script:histPos--; Play-Item $history[$histPos] $false }
}
function Redo {
    if ($histPos -lt $history.Count - 1 -and $history[$histPos + 1].ListView) { $script:histPos++; Play-Item $history[$histPos] $false }
}

function Prev-OrRestart {
    if ($wmp.controls.currentPosition -gt 0.5) { $wmp.controls.currentPosition = 0 }
    else { Step -1 }
}

function Toggle-Play {
    if ($wmp.playState -eq 3) { $wmp.controls.pause() }
    elseif ($wmp.currentMedia) { $wmp.controls.play() }
    elseif ($lv.SelectedItems.Count) { Play-Item $lv.SelectedItems[0] }
}

function Copy-Tracks([bool]$cut) {
    $paths = @($lv.SelectedItems | ForEach-Object Tag)
    if (-not $paths.Count) { return }
    $col = New-Object Collections.Specialized.StringCollection
    $col.AddRange([string[]]$paths)
    $do = New-Object Windows.Forms.DataObject
    $do.SetFileDropList($col)
    $ms = New-Object IO.MemoryStream
    $ms.WriteByte([byte]($(if ($cut) { 2 } else { 5 })))   # Preferred DropEffect: 2=Move, 5=Copy
    $do.SetData('Preferred DropEffect', $ms)
    [Windows.Forms.Clipboard]::SetDataObject($do)
}

function Paste-Tracks {
    $cdo = [Windows.Forms.Clipboard]::GetDataObject()
    if (-not $cdo.GetDataPresent([Windows.Forms.DataFormats]::FileDrop)) { return }
    $dest = if ($lv.SelectedItems.Count) { [IO.Path]::GetDirectoryName($lv.SelectedItems[0].Tag) } else { $root }
    $move = $false
    try { $move = ($cdo.GetData('Preferred DropEffect').ReadByte() -band 2) -ne 0 } catch { }
    foreach ($src in $cdo.GetData([Windows.Forms.DataFormats]::FileDrop)) {
        $target = Join-Path $dest ([IO.Path]::GetFileName($src))
        try {
            if (-not (Test-Path $target)) {
                if ($move) { [IO.File]::Move($src, $target) } else { [IO.File]::Copy($src, $target) }
            }
        } catch { }
    }
    Start-Job $false $true
}

function Select-All {
    $lv.BeginUpdate()
    foreach ($it in $lv.Items) { $it.Selected = $true }
    $lv.EndUpdate()
}

Add-Type -AssemblyName Microsoft.VisualBasic
function Search-Tracks {
    $q = [Microsoft.VisualBasic.Interaction]::InputBox('Suchen nach (Titel, Interpret, Album):', 'Medien durchsuchen')
    if (-not $q) { return }
    $q2 = $q.ToLower()
    foreach ($it in $lv.Items) {
        $hay = "$($it.SubItems[1].Text) $($it.SubItems[2].Text) $($it.SubItems[3].Text)".ToLower()
        if ($hay.Contains($q2)) {
            foreach ($i in $lv.SelectedItems) { $i.Selected = $false }
            $it.Selected = $true; $it.Focused = $true; $it.EnsureVisible()
            return
        }
    }
    [Windows.Forms.MessageBox]::Show("Kein Treffer für '$q'", 'Medien durchsuchen')
}

$darkOn = $false
function Set-Dark([bool]$on) {
    $script:darkOn = $on
    $bg  = if ($on) { [Drawing.Color]::FromArgb(30,30,30) }    else { [Drawing.SystemColors]::Window }
    $fg  = if ($on) { [Drawing.Color]::FromArgb(220,220,220) } else { [Drawing.SystemColors]::WindowText }
    $ctl = if ($on) { [Drawing.Color]::FromArgb(45,45,48) }    else { [Drawing.SystemColors]::Control }
    $form.BackColor = $ctl
    $lv.BackColor = $bg; $lv.ForeColor = $fg
    $bar.BackColor = $ctl; $flow.BackColor = $ctl; $seek.BackColor = $ctl; $vol.BackColor = $ctl
    $nowLbl.ForeColor = $fg; $timeLbl.ForeColor = $fg; $volIco.ForeColor = $fg
    $status.BackColor = $ctl; $statusLbl.BackColor = $ctl; $statusLbl.ForeColor = $fg
    $menu.BackColor = $ctl; $menu.ForeColor = $fg
    foreach ($mi in @($mDatei, $mBearb, $mAnsicht)) {
        $mi.BackColor = $ctl; $mi.ForeColor = $fg
        foreach ($d in $mi.DropDownItems) { $d.BackColor = $ctl; $d.ForeColor = $fg }
    }
    foreach ($b in @($btnPrev, $btnPlay, $btnNext)) { $b.BackColor = $ctl; $b.ForeColor = $fg }
    $r = if ($on) { New-Object Windows.Forms.ToolStripProfessionalRenderer((New-Object DarkTable)) }
         else { New-Object Windows.Forms.ToolStripProfessionalRenderer }
    $menu.Renderer = $r; $status.Renderer = $r
    $seek.Invalidate()
}

function Step([int]$d) {
    # sichtbare Reihenfolge: bei Gruppierung Gruppen der Reihe nach, sonst Items
    $ordered = New-Object Collections.Generic.List[Windows.Forms.ListViewItem]
    if ($lv.ShowGroups -and $lv.Groups.Count) {
        foreach ($g in $lv.Groups) { foreach ($it in $g.Items) { $ordered.Add($it) } }
    } else {
        foreach ($it in $lv.Items) { $ordered.Add($it) }
    }
    $cur = if ($lv.SelectedItems.Count) { $ordered.IndexOf($lv.SelectedItems[0]) } else { -1 }
    $n = if ($cur -ge 0) { $cur + $d } else { 0 }
    if ($n -ge 0 -and $n -lt $ordered.Count) { Play-Item $ordered[$n] }
}

$sorter = New-Object TrackSorter
$lv.ListViewItemSorter = $sorter

$lv.Add_ColumnClick({
    if ($sorter.Col -eq $_.Column) { $sorter.Asc = -not $sorter.Asc }
    else { $sorter.Col = $_.Column; $sorter.Asc = $true }
    $lv.ShowGroups = ($_.Column -eq 3)   # Gruppierung nur bei Album-Sortierung
    $lv.Sort()
})

$form.Add_KeyDown({
    $e = $_   # $_ wird im switch vom KeyCode ueberschrieben
    switch ($e.KeyCode) {
        'Right' { Step 1; $e.SuppressKeyPress = $true }
        'Left'  { Prev-OrRestart; $e.SuppressKeyPress = $true }
        'Enter' { if ($lv.SelectedItems.Count) { Play-Item $lv.SelectedItems[0] }; $e.SuppressKeyPress = $true }
        'Space' { Toggle-Play; $e.SuppressKeyPress = $true }
    }
})

$btnPrev.Add_Click({ Prev-OrRestart; $lv.Focus() })
$btnNext.Add_Click({ Step 1; $lv.Focus() })
$btnPlay.Add_Click({ Toggle-Play; $lv.Focus() })
$vol.Add_ValueChanged({ $wmp.settings.volume = $vol.Value })

$seeking = $false
$seekFrac = 0.0
$seek.Add_Paint({
    $g = $_.Graphics; $w = $seek.Width; $y = [int]($seek.Height / 2) - 2
    $g.FillRectangle([Drawing.SystemBrushes]::ControlLight, 0, $y, $w, 4)
    $fill = [int]($w * $script:seekFrac)
    $g.FillRectangle([Drawing.SystemBrushes]::Highlight, 0, $y, $fill, 4)
    $g.FillEllipse([Drawing.SystemBrushes]::Highlight, [math]::Max(0, $fill - 5), $y - 3, 10, 10)
})
function Seek-To([int]$x) {
    $script:seekFrac = [math]::Max(0.0, [math]::Min(1.0, $x / $seek.Width))
    $seek.Invalidate()
    try {
        if ($wmp.currentMedia -and ($d = $wmp.currentMedia.duration)) {
            $wmp.controls.currentPosition = $d * $script:seekFrac
        }
    } catch { }
}
$seek.Add_MouseDown({ $script:seeking = $true; Seek-To $_.X })
$seek.Add_MouseMove({ if ($seeking) { Seek-To $_.X } })
$seek.Add_MouseUp({ $script:seeking = $false; Seek-To $_.X })

$uiTimer = New-Object Windows.Forms.Timer -Property @{ Interval = 500 }
$uiTimer.Add_Tick({
    $btnPlay.Text = [string][char]($(if ($wmp.playState -eq 3) { 0xE769 } else { 0xE768 }))
    $d = 0.0; try { if ($wmp.currentMedia) { $d = $wmp.currentMedia.duration } } catch { }
    if ($d -gt 0) {
        $p = $wmp.controls.currentPosition
        if (-not $seeking) { $script:seekFrac = $p / $d; $seek.Invalidate() }
        $timeLbl.Text = '{0:mm\:ss} / {1:mm\:ss}' -f [timespan]::FromSeconds($p), [timespan]::FromSeconds($d)
    }
})
$uiTimer.Start()

$lv.Add_DoubleClick({ if ($lv.SelectedItems.Count) { Play-Item $lv.SelectedItems[0] } })

$lv.Add_ItemDrag({
    $paths = @($lv.SelectedItems | ForEach-Object { $_.Tag })
    if (-not $paths.Count -and $_.Item) { $paths = @($_.Item.Tag) }
    $col = New-Object Collections.Specialized.StringCollection
    $col.AddRange([string[]]$paths)
    $do = New-Object Windows.Forms.DataObject
    $do.SetFileDropList($col)
    $lv.DoDragDrop($do, 'Copy')   # nur Copy erlaubt -> Explorer kopiert, verschiebt nie
})

$form.Add_FormClosed({ $timer.Stop(); $uiTimer.Stop(); $wmp.close() })
[Windows.Forms.Application]::Run($form)
