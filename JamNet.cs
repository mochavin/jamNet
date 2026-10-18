// JamNet - widget jam + kecepatan internet, selalu di atas, minimalis.
// Build: jalankan build.bat (memakai csc.exe bawaan .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

class JamNet : Form
{
    [STAThread]
    static void Main()
    {
        bool created;
        var mutex = new System.Threading.Mutex(true, "JamNet_SingleInstance", out created);
        if (!created) return;
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.Run(new JamNet());
        GC.KeepAlive(mutex);
    }

    const string AppName = "JamNet";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Pengaturan
    int bgAlpha = 170;
    float userScale = 1.0f;
    bool onTop = true;
    int mode = 0; // 0 = jam + network, 1 = jam saja, 2 = network saja

    bool ShowClock { get { return mode != 2; } }
    bool ShowNet { get { return mode != 1; } }

    // Status
    float dpi = 1f;
    bool focused;
    int lastSecond = -1;
    long downBps, upBps;
    Dictionary<string, long[]> lastCounters = new Dictionary<string, long[]>();
    Stopwatch sw = new Stopwatch();
    Timer timer = new Timer();
    ContextMenuStrip menu = new ContextMenuStrip();
    ToolStripMenuItem miTop, miStartup;

    // Layout
    Font clockFont, netFont;
    int winW, winH;
    float padX, padY, rowGap, colGap, contentW;
    RectangleF clockInk, netInk;

    string SettingsPath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppName, "settings.ini");
        }
    }

    float S { get { return dpi * userScale; } }

    JamNet()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = AppName;

        using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f;

        LoadSettings();
        TopMost = onTop;
        BuildMenu();

        timer.Interval = 100;
        timer.Tick += delegate { Tick(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 /* WS_EX_LAYERED */ | 0x80 /* WS_EX_TOOLWINDOW: sembunyi dari Alt+Tab */;
            return cp;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyScale();
        EnsureOnScreen();
        SampleNetwork();
        sw.Start();
        Render();
        timer.Start();
    }

    // ---------- Loop utama ----------

    void Tick()
    {
        int sec = DateTime.Now.Second;
        if (sec == lastSecond) return;
        lastSecond = sec;
        SampleNetwork();
        if (onTop && !menu.Visible)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Render();
    }

    void SampleNetwork()
    {
        double elapsed = sw.Elapsed.TotalSeconds;
        sw.Restart();
        long dDown = 0, dUp = 0;
        var current = new Dictionary<string, long[]>();
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return; }

        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            try
            {
                var st = nic.GetIPStatistics();
                var c = new long[] { st.BytesReceived, st.BytesSent };
                current[nic.Id] = c;
                long[] prev;
                // Hanya hitung adapter yang sudah ada di sampel sebelumnya agar tidak ada lonjakan palsu
                if (lastCounters.TryGetValue(nic.Id, out prev))
                {
                    dDown += Math.Max(0, c[0] - prev[0]);
                    dUp += Math.Max(0, c[1] - prev[1]);
                }
            }
            catch { }
        }
        lastCounters = current;
        if (elapsed > 0.2)
        {
            downBps = (long)(dDown / elapsed);
            upBps = (long)(dUp / elapsed);
        }
    }

    static string FormatSpeed(long bps)
    {
        // Singkat: 512B, 7.5K, 680K, 1.3M
        double v = bps;
        string unit;
        if (v >= 1024 * 1024 * 1024) { v /= 1024 * 1024 * 1024; unit = "G"; }
        else if (v >= 1024 * 1024) { v /= 1024 * 1024; unit = "M"; }
        else if (v >= 1024) { v /= 1024; unit = "K"; }
        else return bps.ToString(Inv) + "B";
        return v.ToString(v < 100 ? "0.0" : "0", Inv) + unit;
    }

    // ---------- Gambar ----------

    // Batas "tinta" teks yang benar-benar terlihat (tanpa ruang kosong bawaan tinggi baris font)
    static RectangleF InkBounds(string text, Font font)
    {
        using (var p = new GraphicsPath())
        {
            p.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, StringFormat.GenericTypographic);
            return p.GetBounds();
        }
    }

    void ApplyScale()
    {
        if (clockFont != null) clockFont.Dispose();
        if (netFont != null) netFont.Dispose();
        clockFont = new Font("Segoe UI Semibold", 21f * S, GraphicsUnit.Pixel);
        netFont = new Font("Segoe UI", 13f * S, GraphicsUnit.Pixel);

        clockInk = InkBounds("00:00:00", clockFont);
        netInk = InkBounds("↓ 888.8K", netFont);
        padX = 12f * S;
        padY = 9f * S;
        rowGap = 5f * S;
        colGap = 10f * S;
        float clockW = ShowClock ? clockInk.Width : 0;
        float netW = ShowNet ? netInk.Width * 2 + colGap : 0;
        float contentH = (ShowClock ? clockInk.Height : 0) + (ShowNet ? netInk.Height : 0)
            + (ShowClock && ShowNet ? rowGap : 0);
        contentW = Math.Max(clockW, netW);
        winW = (int)Math.Ceiling(contentW + padX * 2);
        winH = (int)Math.Ceiling(contentH + padY * 2);
        Size = new Size(winW, winH);
    }

    void Render()
    {
        if (!IsHandleCreated || winW <= 0) return;
        float s = S;
        using (var bmp = new Bitmap(winW, winH, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                using (var path = RoundRect(new RectangleF(0.5f, 0.5f, winW - 1f, winH - 1f), 8f * s))
                {
                    using (var br = new SolidBrush(Color.FromArgb(bgAlpha, 18, 18, 22)))
                        g.FillPath(br, path);
                    if (focused)
                        using (var pen = new Pen(Color.FromArgb(170, 110, 160, 255), Math.Max(1f, 1.2f * s)))
                            g.DrawPath(pen, path);
                }

                var sf = StringFormat.GenericTypographic;
                if (ShowClock)
                    DrawShadowed(g, DateTime.Now.ToString("HH:mm:ss", Inv), clockFont, Color.FromArgb(245, 245, 245),
                        new PointF((winW - clockInk.Width) / 2f - clockInk.X, padY - clockInk.Y), sf, s);

                if (ShowNet)
                {
                    float netY = padY + (ShowClock ? clockInk.Height + rowGap : 0) - netInk.Y;
                    float colW = (contentW - colGap) / 2f;
                    Color netColor = Color.FromArgb(200, 205, 215);
                    DrawShadowed(g, "↓ " + FormatSpeed(downBps), netFont, netColor,
                        new PointF(padX - netInk.X, netY), sf, s);
                    DrawShadowed(g, "↑ " + FormatSpeed(upBps), netFont, netColor,
                        new PointF(padX + colW + colGap - netInk.X, netY), sf, s);
                }
            }
            PushBitmap(bmp);
        }
    }

    static void DrawShadowed(Graphics g, string text, Font font, Color color, PointF pt, StringFormat sf, float s)
    {
        // Bayangan tipis agar tetap terbaca saat latar sangat transparan
        float o = Math.Max(1f, s);
        using (var sh = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            g.DrawString(text, font, sh, new PointF(pt.X + o, pt.Y + o), sf);
        using (var br = new SolidBrush(color))
            g.DrawString(text, font, br, pt, sf);
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void PushBitmap(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBmp);
        try
        {
            var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
            var src = new POINT { x = 0, y = 0 };
            var dst = new POINT { x = Left, y = Top };
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // ---------- Input ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            Activate();
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int dir = e.Delta > 0 ? 1 : -1;
        if ((ModifierKeys & Keys.Control) != 0) ChangeScale(dir * 0.1f);
        else ChangeAlpha(dir * 15);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        bool ctrl = (keyData & Keys.Control) != 0;
        bool shift = (keyData & Keys.Shift) != 0;
        int step = shift ? 10 : 1;

        switch (key)
        {
            case Keys.Left: MoveBy(-step, 0); return true;
            case Keys.Right: MoveBy(step, 0); return true;
            case Keys.Up: MoveBy(0, -step); return true;
            case Keys.Down: MoveBy(0, step); return true;
            case Keys.Oemplus:
            case Keys.Add:
                if (ctrl) ChangeScale(0.1f); else ChangeAlpha(15);
                return true;
            case Keys.OemMinus:
            case Keys.Subtract:
                if (ctrl) ChangeScale(-0.1f); else ChangeAlpha(-15);
                return true;
            case Keys.D0:
            case Keys.NumPad0:
                bgAlpha = 170; userScale = 1f; ApplyScale(); EnsureOnScreen(); Render(); SaveSettings();
                return true;
            case Keys.T: ToggleTop(); return true;
            case Keys.M: SetMode((mode + 1) % 3); return true;
            case Keys.R: ResetPosition(); return true;
            case Keys.F1:
            case Keys.H: ShowHelp(); return true;
            case Keys.Q:
                if (ctrl) { Close(); return true; }
                break;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnActivated(EventArgs e) { base.OnActivated(e); focused = true; Render(); }
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); focused = false; Render(); }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x232 /* WM_EXITSIZEMOVE */) SaveSettings();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        base.OnFormClosing(e);
    }

    // ---------- Aksi ----------

    void MoveBy(int dx, int dy)
    {
        Location = new Point(Left + dx, Top + dy);
        SaveSettings();
    }

    void ChangeAlpha(int delta)
    {
        // Minimal 1 (bukan 0) agar latar tetap bisa diklik & digeser walau tak terlihat
        bgAlpha = Math.Max(1, Math.Min(255, bgAlpha + delta));
        Render();
        SaveSettings();
    }

    void ChangeScale(float delta)
    {
        userScale = (float)Math.Round(Math.Max(0.6f, Math.Min(3f, userScale + delta)), 1);
        ApplyScale();
        EnsureOnScreen();
        Render();
        SaveSettings();
    }

    void ToggleTop()
    {
        onTop = !onTop;
        TopMost = onTop;
        if (!onTop) SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SaveSettings();
    }

    void SetMode(int m)
    {
        mode = m;
        ApplyScale();
        EnsureOnScreen();
        Render();
        SaveSettings();
    }

    void ResetPosition()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        int m = (int)(12 * dpi);
        Location = new Point(wa.Right - winW - m, wa.Top + m);
        SaveSettings();
    }

    void EnsureOnScreen()
    {
        var r = new Rectangle(Left, Top, winW, winH);
        foreach (var scr in Screen.AllScreens)
        {
            Rectangle wa = scr.WorkingArea;
            if (wa.IntersectsWith(r))
            {
                int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - winW));
                int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - winH));
                Location = new Point(x, y);
                return;
            }
        }
        ResetPosition();
    }

    void ShowHelp()
    {
        MessageBox.Show(this,
            "Klik widget dulu agar fokus (muncul garis biru), lalu:\n\n" +
            "Panah\t\tGeser 1 px\n" +
            "Shift + Panah\tGeser 10 px\n" +
            "+  /  -\t\tLatar lebih pekat / transparan\n" +
            "Scroll\t\tLatar lebih pekat / transparan\n" +
            "Ctrl + / -\tPerbesar / perkecil\n" +
            "Ctrl + Scroll\tPerbesar / perkecil\n" +
            "0\t\tReset ukuran & transparansi\n" +
            "M\t\tGanti tampilan: jam + network / jam / network\n" +
            "T\t\tAlways on top on/off\n" +
            "R\t\tPindah ke pojok kanan atas\n" +
            "F1  /  H\t\tBantuan ini\n" +
            "Ctrl + Q\t\tKeluar\n\n" +
            "Klik kiri + tarik: pindahkan.   Klik kanan: menu.",
            AppName + " - Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void BuildMenu()
    {
        miTop = new ToolStripMenuItem("Selalu di atas (T)", null, delegate { ToggleTop(); });
        miStartup = new ToolStripMenuItem("Jalankan saat Windows start", null, delegate { ToggleStartup(); });
        var miMode = new ToolStripMenuItem("Tampilan (M)");
        string[] modeNames = { "Jam + network", "Jam saja", "Network saja" };
        for (int i = 0; i < modeNames.Length; i++)
        {
            int m = i;
            miMode.DropDownItems.Add(new ToolStripMenuItem(modeNames[i], null, delegate { SetMode(m); }));
        }
        menu.Items.Add(miMode);
        menu.Items.Add(miTop);
        menu.Items.Add(miStartup);
        menu.Items.Add(new ToolStripMenuItem("Pindah ke pojok kanan atas (R)", null, delegate { ResetPosition(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Hotkey... (F1)", null, delegate { ShowHelp(); }));
        menu.Items.Add(new ToolStripMenuItem("Keluar (Ctrl+Q)", null, delegate { Close(); }));
        menu.Opening += delegate
        {
            for (int i = 0; i < miMode.DropDownItems.Count; i++)
                ((ToolStripMenuItem)miMode.DropDownItems[i]).Checked = i == mode;
            miTop.Checked = onTop;
            miStartup.Checked = IsStartupEnabled();
        };
    }

    bool IsStartupEnabled()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    void ToggleStartup()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (IsStartupEnabled()) k.DeleteValue(AppName, false);
                else k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Gagal mengubah startup: " + ex.Message, AppName);
        }
    }

    // ---------- Pengaturan ----------

    void LoadSettings()
    {
        bool hasPos = false;
        int x = 0, y = 0;
        try
        {
            if (File.Exists(SettingsPath))
            {
                foreach (var line in File.ReadAllLines(SettingsPath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "x": hasPos = int.TryParse(v, NumberStyles.Integer, Inv, out x); break;
                        case "y": hasPos &= int.TryParse(v, NumberStyles.Integer, Inv, out y); break;
                        case "alpha": int.TryParse(v, NumberStyles.Integer, Inv, out bgAlpha); break;
                        case "scale": float.TryParse(v, NumberStyles.Float, Inv, out userScale); break;
                        case "ontop": onTop = v != "0"; break;
                        case "mode": int.TryParse(v, NumberStyles.Integer, Inv, out mode); break;
                    }
                }
            }
        }
        catch { }
        bgAlpha = Math.Max(1, Math.Min(255, bgAlpha));
        if (mode < 0 || mode > 2) mode = 0;
        if (userScale < 0.6f || userScale > 3f) userScale = 1f;
        if (hasPos) Location = new Point(x, y);
        else
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - (int)(200 * dpi), wa.Top + (int)(12 * dpi));
        }
    }

    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllLines(SettingsPath, new[]
            {
                "x=" + Left.ToString(Inv),
                "y=" + Top.ToString(Inv),
                "alpha=" + bgAlpha.ToString(Inv),
                "scale=" + userScale.ToString("0.0", Inv),
                "ontop=" + (onTop ? "1" : "0"),
                "mode=" + mode.ToString(Inv),
            });
        }
        catch { }
    }

    // ---------- Win32 ----------

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
}
