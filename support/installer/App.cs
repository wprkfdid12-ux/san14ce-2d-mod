// 삼국지 14 · 2D 모드 설치기 (깃허브판, 2026-11-11 D안 화면)
//  위: 포스터 디자인(제목·판 도장·호환 칩·기마 그림)  왼쪽: 내 판 → 최신 판 / 큰 단추 하나(설치·업데이트·최신) / 파일 검사·되돌리기 / 필수 띠 / 진행
//  오른쪽: 판별 업데이트 내역(누르면 펼침, 「↺ 이 판으로」 = 그 판으로 바꾸기)
//  화면은 고정 배치(96dpi 기준 좌표 × 화면 배율)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Fx {
    public static float K = 1f;                              // 화면 배율
    public static int S(float v) { return (int)Math.Round(v * K); }
    public static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
    public static readonly Color Bg = C(0x141110), Card = C(0x1d1916), Line = C(0x4a3d2c), Ink = C(0xefe6d2), Dim = C(0xa99f8c),
        Gold = C(0xe8c07a), Red = C(0xc8432f), Lab = C(0x9c8f77), Box = C(0x151210), BoxLine = C(0x33291f), Green = C(0x8fd08a), Orange = C(0xffb15a), Blue = C(0x7a9ad0);
    static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
    public static string SansName = "Malgun Gothic", SerifName = "Batang";
    public static Font F(float px, bool bold = false, bool serif = false) { return Make(serif ? SerifName : SansName, px, bold); }
    public static Font Sym(float px) { return Make("Segoe UI Symbol", px, false); }
    static Font Make(string name, float px, bool bold) {
        string k = name + "|" + px + "|" + bold + "|" + K;
        Font f; if (cache.TryGetValue(k, out f)) return f;
        try { f = new Font(name, px * K, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
        catch (ArgumentException) { try { f = new Font(name, px * K, FontStyle.Regular, GraphicsUnit.Pixel); } catch (ArgumentException) { f = new Font(SystemFonts.DefaultFont.FontFamily, px * K, FontStyle.Regular, GraphicsUnit.Pixel); } }
        cache[k] = f; return f;
    }
    // 맑은 고딕에 없는 기호(✔✖⟳↺)는 맨 앞 한 글자만 Segoe UI Symbol 로
    const string Syms = "✔✖⟳↺";
    static bool Lead(string s) { return s != null && s.Length > 0 && Syms.IndexOf(s[0]) >= 0; }
    public static void Hq(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }
    static float SymW(Graphics g, string s, Font f) { return g.MeasureString(s.Substring(0, 1), Sym(f.Size / K), 10000, StringFormat.GenericTypographic).Width; }
    public static void Text(Graphics g, string s, Font f, Color c, float x, float y) {
        using (var b = new SolidBrush(c)) {
            if (Lead(s)) { var sf = Sym(f.Size / K); g.DrawString(s.Substring(0, 1), sf, b, x, y + (f.Height - sf.Height) / 2f, StringFormat.GenericTypographic); x += SymW(g, s, f); s = s.Substring(1); }
            g.DrawString(s, f, b, x, y, StringFormat.GenericTypographic);
        }
    }
    public static SizeF Measure(Graphics g, string s, Font f) {
        if (Lead(s)) { var r = g.MeasureString(s.Substring(1), f, 10000, StringFormat.GenericTypographic); return new SizeF(r.Width + SymW(g, s, f), Math.Max(r.Height, f.Height)); }
        return g.MeasureString(s, f, 10000, StringFormat.GenericTypographic);
    }
    // 글자 사이 띄우기(제목 위 작은 줄)
    public static void Spaced(Graphics g, string s, Font f, Color c, float x, float y, float gap) {
        using (var b = new SolidBrush(c)) foreach (char ch in s) { string t = ch.ToString(); g.DrawString(t, f, b, x, y, StringFormat.GenericTypographic); x += (ch == ' ' ? f.Size * 0.3f : g.MeasureString(t, f, 1000, StringFormat.GenericTypographic).Width) + gap; }
    }
    public static void TextIn(Graphics g, string s, Font f, Color c, RectangleF r, bool center = false, bool ellipsis = true) {
        if (Lead(s)) { var sz = Measure(g, s, f); Text(g, s, f, c, center ? r.X + (r.Width - sz.Width) / 2 : r.X, r.Y + (r.Height - sz.Height) / 2); return; }
        using (var b = new SolidBrush(c)) using (var sf = new StringFormat(StringFormat.GenericTypographic)) {
            sf.LineAlignment = StringAlignment.Center; sf.Alignment = center ? StringAlignment.Center : StringAlignment.Near;
            if (ellipsis) { sf.Trimming = StringTrimming.EllipsisCharacter; sf.FormatFlags |= StringFormatFlags.NoWrap; }
            g.DrawString(s, f, b, r, sf);
        }
    }
    public static void Rect(Graphics g, Rectangle r, Color fill, Color line, float w = 1) {
        using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
        using (var p = new Pen(line, Math.Max(1, w * K))) { p.Alignment = PenAlignment.Inset; g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1); }
    }
}

// 직접 그리는 바탕 컨트롤
class Pane : Control {
    public Action<Graphics, Pane> Draw;
    public Pane() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); BackColor = Fx.Bg; }
    protected override void OnPaint(PaintEventArgs e) { Fx.Hq(e.Graphics); if (Draw != null) Draw(e.Graphics, this); }
}

// 큰 단추·중간 단추
class Btn : Pane {
    public string Title = "", Sub = "", Icon = "";
    public Color CTop, CBot, Edge, Fore, SubFore;
    public float TitlePx = 18, SubPx = 12.5f;
    public bool SubRight;   // 중간 단추: 제목 오른쪽에 작은 글
    bool hover, down;
    public Btn() {
        Cursor = Cursors.Hand;
        Draw = (g, c) => {
            var r = ClientRectangle;
            Color t = CTop, b = CBot, ed = Edge, fo = Fore, so = SubFore;
            if (!Enabled) { t = Fx.C(0x2a2622); b = Fx.C(0x221e1b); ed = Fx.C(0x3a3229); fo = Fx.C(0x7a7060); so = Fx.C(0x6a6050); }
            else if (down) { t = Shade(t, -20); b = Shade(b, -20); }
            else if (hover) { t = Shade(t, 14); b = Shade(b, 14); }
            using (var lg = new LinearGradientBrush(r, t, b, 90f)) g.FillRectangle(lg, r);
            using (var p = new Pen(ed, Math.Max(1, Fx.K))) { p.Alignment = PenAlignment.Inset; g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1); }
            var tf = Fx.F(TitlePx, true);
            if (SubRight) {
                var ts = Fx.Measure(g, Title, tf); var sf = Fx.F(SubPx);
                var ss = Sub.Length > 0 ? Fx.Measure(g, Sub, sf) : SizeF.Empty;
                float w = ts.Width + (Sub.Length > 0 ? Fx.S(10) + ss.Width : 0), x = (r.Width - w) / 2;
                Fx.Text(g, Title, tf, fo, x, (r.Height - ts.Height) / 2);
                if (Sub.Length > 0) Fx.Text(g, Sub, sf, so, x + ts.Width + Fx.S(10), (r.Height - ss.Height) / 2 + Fx.S(1));
            } else {
                var ts = Fx.Measure(g, Title, tf);
                float x = Fx.S(26), y = (r.Height - ts.Height) / 2;
                Fx.Text(g, Title, tf, fo, x, y);
                if (Sub.Length > 0) {
                    float lx = x + ts.Width + Fx.S(22);
                    using (var p = new Pen(Color.FromArgb(90, so), Math.Max(1, Fx.K))) g.DrawLine(p, lx, Fx.S(26), lx, r.Height - Fx.S(26));
                    var lines = Sub.Split('\n'); var sf = Fx.F(SubPx); float lh = Fx.S(SubPx * 1.55f), sy = (r.Height - lh * lines.Length) / 2;
                    for (int i = 0; i < lines.Length; i++) Fx.Text(g, lines[i], sf, so, lx + Fx.S(18), sy + lh * i + Fx.S(2));
                }
                if (Icon.Length > 0) { var icf = Fx.F(36); var isz = Fx.Measure(g, Icon, icf); Fx.Text(g, Icon, icf, Color.FromArgb(150, fo), r.Width - isz.Width - Fx.S(26), (r.Height - isz.Height) / 2); }
            }
        };
    }
    static Color Shade(Color c, int d) { Func<int, int> f = v => Math.Max(0, Math.Min(255, v + d)); return Color.FromArgb(c.A, f(c.R), f(c.G), f(c.B)); }
    public void Style(int top, int bottom, int edge, int fore, int sub) { CTop = Fx.C(top); CBot = Fx.C(bottom); Edge = Fx.C(edge); Fore = Fx.C(fore); SubFore = Fx.C(sub); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
}

// 판별 내역 한 줄(누르면 펼침)
class VerRow : Pane {
    public GhRelease R; public bool Open, Selected, Installed, IsNew, Compatible = true;
    public Action<VerRow> Toggle, Pick;
    Rectangle goRect;
    public VerRow() { Cursor = Cursors.Hand; Draw = (g, c) => PaintRow(g); }
    public int Need(Graphics g, int w) {
        int h = Fx.S(52);
        if (Open) { h += Fx.S(8); foreach (var n in R.notes) { h += Fx.S(26); if (!String.IsNullOrEmpty(n.d)) h += Fx.S(20); h += Fx.S(8); } h += Fx.S(6); }
        return h;
    }
    void PaintRow(Graphics g) {
        int w = Width, top = Fx.S(52);
        Color fill = Selected ? Fx.C(0x1a1f2a) : Open ? Fx.C(0x2a2016) : Fx.Box, edge = Selected ? Fx.Blue : Open ? Fx.C(0xc9933f) : Fx.BoxLine;
        Fx.Rect(g, new Rectangle(0, 0, w, Height), Open && !Selected ? Fx.C(0x1a1510) : fill, edge);
        if (Open) using (var b = new SolidBrush(fill)) g.FillRectangle(b, Fx.S(1), Fx.S(1), w - Fx.S(2), top - Fx.S(1));
        int a = Compatible ? 255 : 110;
        Fx.TextIn(g, R.tag, Fx.F(20, true), Color.FromArgb(a, Open || Selected ? Fx.Gold : Fx.C(0xe9dcc0)), new RectangleF(Fx.S(14), 0, Fx.S(70), top));
        Fx.TextIn(g, ShortDate(R.date), Fx.F(14), Color.FromArgb(a, Fx.C(0x8a7a60)), new RectangleF(Fx.S(86), 0, Fx.S(60), top));
        // 오른쪽: 표시·단추
        int right = w - Fx.S(12); goRect = Rectangle.Empty;
        string badge = null; Color bc = Fx.Dim, bl = Fx.C(0x5a4e3d);
        if (Installed) { badge = "깔려 있음"; bc = Fx.Green; bl = Fx.C(0x4d7a48); }
        else if (!Compatible) { badge = "게임판 안 맞음"; }
        else if (IsNew) { badge = "새 판"; bc = Fx.Orange; bl = Fx.C(0xa8622a); }
        if (!Installed && Compatible) {
            string gs = IsNew ? "↑ 이 판으로" : "↺ 이 판으로"; var gf = Fx.F(13.5f); var sz = Fx.Measure(g, gs, gf);
            int bw = (int)sz.Width + Fx.S(22), bh = Fx.S(30);
            goRect = new Rectangle(right - bw, (top - bh) / 2, bw, bh); right = goRect.X - Fx.S(8);
            Fx.Rect(g, goRect, Selected ? Fx.C(0x2f4a78) : Fx.C(0x2a231c), Selected ? Fx.Blue : Fx.C(0x6b5636));
            Fx.TextIn(g, gs, gf, Selected ? Color.White : Fx.C(0xe6d5b2), goRect, true);
        }
        if (badge != null) {
            var bf = Fx.F(13); var sz = Fx.Measure(g, badge, bf); int bw = (int)sz.Width + Fx.S(16), bh = Fx.S(24);
            var br = new Rectangle(right - bw, (top - bh) / 2, bw, bh); right = br.X - Fx.S(8);
            using (var p = new Pen(bl, Math.Max(1, Fx.K))) g.DrawRectangle(p, br.X, br.Y, br.Width - 1, br.Height - 1);
            Fx.TextIn(g, badge, bf, bc, br, true);
        }
        Fx.TextIn(g, R.title ?? "", Fx.F(15.5f), Color.FromArgb(a, Fx.C(0xcfc4ad)), new RectangleF(Fx.S(150), 0, Math.Max(10, right - Fx.S(150)), top));
        if (Open) {
            using (var p = new Pen(Fx.C(0x3a3024), Math.Max(1, Fx.K))) g.DrawLine(p, Fx.S(14), top, w - Fx.S(14), top);
            float y = top + Fx.S(10);
            foreach (var n in R.notes) {
                Fx.Text(g, "◆", Fx.F(11), Fx.Red, Fx.S(16), y + Fx.S(5));
                Fx.TextIn(g, n.t ?? "", Fx.F(16.5f), Fx.C(0xefe3cb), new RectangleF(Fx.S(34), y, w - Fx.S(48), Fx.S(24)));
                y += Fx.S(26);
                if (!String.IsNullOrEmpty(n.d)) { Fx.TextIn(g, n.d, Fx.F(13.5f), Fx.Dim, new RectangleF(Fx.S(34), y, w - Fx.S(48), Fx.S(20))); y += Fx.S(20); }
                y += Fx.S(8);
            }
        }
    }
    static string ShortDate(string d) { DateTime t; return DateTime.TryParse(d, out t) ? t.ToString("MM.dd") : (d ?? ""); }
    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e);
        if (goRect.Contains(e.Location)) { if (Pick != null) Pick(this); return; }
        if (Toggle != null) Toggle(this);
    }
}

public sealed class SetupForm : Form {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    readonly Pane hero = new Pane(), verBox = new Pane(), warn = new Pane(), notes = new Pane(), stline = new Pane(), foot = new Pane();
    readonly Btn main = new Btn(), verify = new Btn(), back = new Btn();
    readonly LinkLabel lnkReinstall = new LinkLabel(), lnkRecover = new LinkLabel(), lnkRemove = new LinkLabel(), lnkFolder = new LinkLabel();
    readonly Panel list = new Panel(), logBox = new Panel();
    readonly TextBox log = new TextBox();
    readonly List<VerRow> rows = new List<VerRow>();
    Image riders;
    // 상태
    string gameRoot = "", installedRel, installedTag, ipTag, indexError, statusText = "준비", localPkg, localRepo;
    uint exeStamp; GhIndex index; VerRow selected; int progress;
    bool busy, stop, checking = true;
    readonly string exeDir = AppDomain.CurrentDomain.BaseDirectory;

    public SetupForm() {
        // 화면 배율 — 작업 영역(작업 표시줄 뺀 화면)에 창이 다 들어가게 줄임
        float k = GetDpi() / 96f;
        try { var wa = Screen.PrimaryScreen.WorkingArea; k = Math.Min(k, Math.Min((wa.Height - 50) / 940f, (wa.Width - 30) / 1240f)); } catch { }
        Fx.K = Math.Max(0.6f, k);
        Text = "삼국지 14 · 2D 모드 설치기";
        BackColor = Fx.Bg; ForeColor = Fx.Ink;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Fx.S(1240), Fx.S(940));
        try { using (var ms = new MemoryStream(Convert.FromBase64String(Art.Riders))) riders = Image.FromStream(ms); } catch { }
        try { var fams = new InstalledFontCollection().Families.Select(f => f.Name).ToList(); if (!fams.Contains("Batang")) Fx.SerifName = fams.Contains("바탕") ? "바탕" : Fx.SansName; if (!fams.Contains("Malgun Gothic") && fams.Contains("맑은 고딕")) Fx.SansName = "맑은 고딕"; } catch { }
        Font = Fx.F(14);
        Build();
        folderFound(FindGame());
        Shown += async delegate { await Refresh(true); };
        FormClosing += (s, e) => { if (busy) { e.Cancel = true; stop = true; Status("작업을 마무리하는 중입니다 — 끝나면 닫힙니다."); } };
    }
    int GetDpi() { try { using (var g = CreateGraphics()) return (int)g.DpiX; } catch { return 96; } }

    void Place(Control c, float x, float y, float w, float h) { c.SetBounds(Fx.S(x), Fx.S(y), Fx.S(w), Fx.S(h)); Controls.Add(c); }
    void Build() {
        hero.Draw = DrawHero; Place(hero, 0, 0, 1240, 270);
        verBox.Draw = DrawVer; Place(verBox, 24, 288, 673, 112);
        lnkFolder.Text = "바꾸기"; StyleLink(lnkFolder, 13); lnkFolder.BackColor = Fx.Box; lnkFolder.TextAlign = ContentAlignment.MiddleRight;
        lnkFolder.SetBounds(Fx.S(560), Fx.S(78), Fx.S(100), Fx.S(20)); verBox.Controls.Add(lnkFolder);
        lnkFolder.LinkClicked += delegate { PickFolder(); };
        main.TitlePx = 38; main.SubPx = 14; main.Icon = "⟳"; Place(main, 24, 414, 673, 104);
        main.Click += async delegate { await MainClick(); };
        verify.SubRight = true; verify.Title = "파일 검사"; verify.Sub = "깨진 파일 찾아 고침"; verify.Style(0x2a231c, 0x231d18, 0x6b5636, 0xe6d5b2, 0xa99f8c); Place(verify, 24, 528, 331, 52);
        verify.Click += async delegate { await Run("verify", null); };
        back.SubRight = true; back.Title = "이전 판으로 되돌리기"; back.Sub = "오른쪽 목록에서 판 고름"; back.Style(0x2a231c, 0x231d18, 0x6b5636, 0xe6d5b2, 0xa99f8c); Place(back, 366, 528, 331, 52);
        back.Click += async delegate { if (selected != null) await Install(selected.R); else MessageBox.Show(this, "오른쪽 목록에서 바꿀 판을 먼저 누르세요.", "2D 모드"); };
        int lx = 697;
        foreach (var l in new[] { lnkRemove, lnkRecover, lnkReinstall }) {
            l.Text = l == lnkRemove ? "제거" : l == lnkRecover ? "중단 복구" : "다시 설치"; StyleLink(l, 13);
            if (l == lnkRemove) l.LinkColor = l.ActiveLinkColor = Fx.C(0xc98373);
            var w = TextRenderer.MeasureText(l.Text, l.Font).Width + Fx.S(4); lx -= (int)(w / Fx.K) + 2;
            l.TextAlign = ContentAlignment.MiddleRight; Place(l, lx, 588, w / Fx.K + 2, 22); lx -= 14;
        }
        lnkRemove.LinkClicked += async delegate { if (MessageBox.Show(this, "2D 모드 파일을 지울까요? 개인 설정과 백업은 남깁니다.", "제거 확인", MessageBoxButtons.YesNo) == DialogResult.Yes) await Run("remove", null); };
        lnkRecover.LinkClicked += async delegate { await Run("recover", null); };
        lnkReinstall.LinkClicked += async delegate { var r = index == null ? null : (Find(installedTag) ?? Latest()); if (r != null) await Install(r, true); else MessageBox.Show(this, "판 목록을 아직 못 읽었습니다.", "2D 모드"); };
        warn.Draw = DrawWarn; Place(warn, 24, 618, 673, 74);
        stline.Draw = DrawStatus; Place(stline, 24, 704, 673, 40);
        logBox.BackColor = Fx.C(0x0b0a09); Place(logBox, 24, 752, 673, 102);
        logBox.Paint += (s, e) => { using (var p = new Pen(Fx.C(0x2b241c), Math.Max(1, Fx.K))) e.Graphics.DrawRectangle(p, 0, 0, logBox.Width - 1, logBox.Height - 1); };
        log.Multiline = true; log.ReadOnly = true; log.BorderStyle = BorderStyle.None; log.ScrollBars = ScrollBars.Vertical; log.BackColor = Fx.C(0x0b0a09); log.ForeColor = Fx.C(0xb3a990);
        log.Font = new Font("Consolas", 12.5f * Fx.K, GraphicsUnit.Pixel); log.SetBounds(Fx.S(10), Fx.S(6), logBox.Width - Fx.S(14), logBox.Height - Fx.S(10)); logBox.Controls.Add(log);
        notes.Draw = DrawNotes; Place(notes, 716, 288, 500, 566);
        list.BackColor = Fx.Card; list.AutoScroll = true; list.SetBounds(Fx.S(16), Fx.S(76), notes.Width - Fx.S(24), notes.Height - Fx.S(90)); notes.Controls.Add(list);
        foot.Draw = DrawFoot; Place(foot, 0, 866, 1240, 74);
        foot.MouseClick += (s, e) => { if (e.X > foot.Width / 2) OpenUrl(Gh.Page); };
        foot.Cursor = Cursors.Default;
        foot.MouseMove += (s, e) => { foot.Cursor = e.X > foot.Width / 2 ? Cursors.Hand : Cursors.Default; };
        UpdateUi();
    }
    void StyleLink(LinkLabel l, float px) { l.Font = Fx.F(px); l.LinkColor = l.ActiveLinkColor = Fx.C(0x9c8f77); l.VisitedLinkColor = l.LinkColor; l.BackColor = Fx.Bg; l.LinkBehavior = LinkBehavior.AlwaysUnderline; }

    // ---------------- 그리기 ----------------
    void DrawHero(Graphics g, Pane p) {
        var r = p.ClientRectangle;
        using (var lg = new LinearGradientBrush(r, Fx.C(0x1a1513), Fx.C(0x141110), 90f)) g.FillRectangle(lg, r);
        using (var path = new GraphicsPath()) {
            path.AddEllipse(r.Width - Fx.S(600), -Fx.S(300), Fx.S(900), Fx.S(560));
            using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(60, Fx.Red), SurroundColors = new[] { Color.FromArgb(0, Fx.Red) } }) g.FillPath(pb, path);
        }
        if (riders != null) {
            int h = r.Height, w = (int)(riders.Width * (h / (float)riders.Height));
            var ir = new Rectangle(r.Width - w, 0, w, h);
            g.DrawImage(riders, ir);
            using (var lg = new LinearGradientBrush(new Rectangle(ir.X - 1, 0, Fx.S(24), h), Fx.Bg, Color.FromArgb(0, Fx.Bg), 0f)) g.FillRectangle(lg, ir.X - 1, 0, Fx.S(24), h);
            using (var lg = new LinearGradientBrush(new Rectangle(ir.X, h - Fx.S(16), w, Fx.S(16)), Color.FromArgb(0, Fx.Bg), Fx.Bg, 90f)) g.FillRectangle(lg, ir.X, h - Fx.S(16), w, Fx.S(16));
        }
        float x = Fx.S(36);
        Fx.Spaced(g, "삼국지 14 · 필드 부대 도트 모드", Fx.F(13), Fx.Gold, x, Fx.S(24), Fx.S(4));
        // 제목(금빛 그라데이션)
        var tf = Fx.F(78, true, true);
        float ty = Fx.S(46);
        var tsz = Fx.Measure(g, "2D 모드", tf);
        using (var path = new GraphicsPath()) {
            path.AddString("2D 모드", tf.FontFamily, (int)tf.Style, tf.Size, new PointF(x, ty), StringFormat.GenericTypographic);
            var bb = path.GetBounds();
            using (var sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) { g.TranslateTransform(0, Fx.S(3)); g.FillPath(sh, path); g.ResetTransform(); }
            using (var lg = new LinearGradientBrush(new RectangleF(bb.X, bb.Y - 1, bb.Width, bb.Height + 2), Fx.C(0xf6dca0), Fx.C(0xc9933f), 90f)) g.FillPath(lg, path);
        }
        float sx = x + tsz.Width + Fx.S(12);
        Fx.Text(g, "설치기", Fx.F(28, true), Fx.C(0xd9cdb5), sx, ty + tsz.Height - Fx.S(44));
        // 판 도장
        string tag = LatestTag() ?? "2D";
        var stf = Fx.F(22, true, true); var ssz = Fx.Measure(g, tag, stf);
        float stx = sx + Fx.Measure(g, "설치기", Fx.F(28, true)).Width + Fx.S(18), sty = ty + Fx.S(6);
        var st = g.Save();
        g.TranslateTransform(stx + ssz.Width / 2 + Fx.S(9), sty + ssz.Height / 2 + Fx.S(6)); g.RotateTransform(4);
        var srect = new RectangleF(-ssz.Width / 2 - Fx.S(9), -ssz.Height / 2 - Fx.S(6), ssz.Width + Fx.S(18), ssz.Height + Fx.S(12));
        using (var pen = new Pen(Fx.Red, Fx.S(3))) g.DrawRectangle(pen, srect.X, srect.Y, srect.Width, srect.Height);
        Fx.Text(g, tag, stf, Fx.Red, srect.X + Fx.S(9), srect.Y + Fx.S(6));
        g.Restore(st);
        float ry = ty + tsz.Height + Fx.S(10);
        using (var b = new SolidBrush(Fx.Red)) g.FillRectangle(b, x, ry, Fx.S(52), Fx.S(3));
        float gy = ry + Fx.S(14);
        Fx.Text(g, "전장의 모든 부대를 ", Fx.F(19), Fx.C(0xe7dcc6), x, gy);
        float gx = x + Fx.Measure(g, "전장의 모든 부대를 ", Fx.F(19)).Width;
        Fx.Text(g, "도트 장수", Fx.F(19, true), Fx.Gold, gx, gy);
        Fx.Text(g, "로.", Fx.F(19), Fx.C(0xe7dcc6), gx + Fx.Measure(g, "도트 장수", Fx.F(19, true)).Width, gy);
        // 칩 — 게임판·통합패치는 깃허브에서 읽은 값
        float cx = x, cy = gy + Fx.S(36);
        var lr = Latest();
        var chips = new List<string>();
        chips.Add((lr != null && !String.IsNullOrEmpty(lr.game) ? lr.game : "11월 5일 업데이트판") + " 전용");
        chips.Add(ipTag != null ? "통합패치 " + ipTag + " 함께" : "통합패치 최신판 함께");
        chips.Add("계절 맵 · AI 모드 · 기색 모드 함께");
        var cf = Fx.F(14);
        foreach (var c in chips) {
            var sz = Fx.Measure(g, c, cf); var rr = new Rectangle((int)cx, (int)cy, (int)sz.Width + Fx.S(22), Fx.S(30));
            Fx.Rect(g, rr, Color.FromArgb(166, 40, 30, 20), Fx.C(0x8a6b3c));
            Fx.TextIn(g, c, cf, Fx.C(0xead6ab), rr, true, false);
            cx += rr.Width + Fx.S(8);
        }
    }
    void DrawVer(Graphics g, Pane p) {
        Fx.Rect(g, p.ClientRectangle, Fx.Box, Fx.BoxLine);
        float x = Fx.S(20);
        Fx.Text(g, "내 2D 모드", Fx.F(14), Fx.Lab, x, Fx.S(14));
        string mine = installedTag ?? (gameRoot.Length == 0 ? "—" : "없음");
        var bf = Fx.F(44, true);
        Fx.Text(g, mine, bf, installedTag != null ? Fx.C(0xf3e7cc) : Fx.Dim, x - Fx.S(2), Fx.S(34));
        string mineSub = installedTag == null ? (gameRoot.Length == 0 ? "게임 폴더를 못 찾음" : "아직 안 깔림") : "깔려 있음";
        Fx.Text(g, mineSub, Fx.F(13), Fx.Dim, x, Fx.S(86));
        float mx = x + Math.Max(Fx.Measure(g, mine, bf).Width, Fx.S(90)) + Fx.S(18);
        var lr = Latest();
        if (lr != null && installedTag != lr.tag) {
            Fx.Text(g, "→", Fx.F(40, true), Fx.Red, mx, Fx.S(36)); mx += Fx.S(58);
        } else mx += Fx.S(10);
        string lt = lr != null ? lr.tag : checking ? "…" : "?";
        bool isNew = lr != null && installedTag != lr.tag;
        using (var b = new SolidBrush(isNew ? Fx.C(0xff8a3a) : Fx.Green)) if (lr != null) g.FillEllipse(b, mx, Fx.S(19), Fx.S(9), Fx.S(9));
        Fx.Text(g, "깃허브 최신", Fx.F(14), Fx.Lab, mx + (lr != null ? Fx.S(15) : 0), Fx.S(14));
        Fx.Text(g, lt, bf, isNew ? Fx.Orange : Fx.C(0xf3e7cc), mx - Fx.S(2), Fx.S(34));
        string ls = lr != null ? ShortDate(lr.date) + " 공개" + (isNew ? "" : " · 최신") : checking ? "판 목록 읽는 중" : (indexError != null ? "깃허브 연결 실패" : "");
        Fx.Text(g, ls, Fx.F(13), Fx.Dim, mx, Fx.S(86));
        // 오른쪽: 게임 폴더
        float rx = p.Width - Fx.S(20);
        string gt = gameRoot.Length > 0 ? "✔ 게임 폴더 찾음" : "✖ 게임 폴더를 골라 주세요";
        var gf = Fx.F(15, true); var gsz = Fx.Measure(g, gt, gf);
        Fx.Text(g, gt, gf, gameRoot.Length > 0 ? Fx.Green : Fx.C(0xe7a08f), rx - gsz.Width, Fx.S(22));
        string path = gameRoot.Length > 0 ? ShortPath(gameRoot) : "Steam 폴더에서 자동으로 못 찾았습니다";
        var pf = Fx.F(13); var psz = Fx.Measure(g, path, pf);
        Fx.Text(g, path, pf, Fx.Dim, rx - Math.Min(psz.Width, Fx.S(300)), Fx.S(50));
    }
    static string ShortPath(string p) { var parts = p.Split('\\'); return parts.Length > 2 ? "…\\" + parts[parts.Length - 2] + "\\" + parts[parts.Length - 1] : p; }
    static string ShortDate(string d) { DateTime t; return DateTime.TryParse(d, out t) ? t.Month + "월 " + t.Day + "일" : (d ?? ""); }
    void DrawWarn(Graphics g, Pane p) {
        var r = p.ClientRectangle;
        using (var lg = new LinearGradientBrush(r, Color.FromArgb(72, Fx.Red), Color.FromArgb(102, 30, 24, 20), 0f)) g.FillRectangle(lg, r);
        using (var pen = new Pen(Fx.Red, Fx.S(2))) { pen.Alignment = PenAlignment.Inset; g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1); }
        var tf = Fx.F(15, true, true); var tsz = Fx.Measure(g, "필수", tf);
        var tr = new Rectangle(Fx.S(16), (r.Height - Fx.S(30)) / 2, (int)tsz.Width + Fx.S(18), Fx.S(30));
        using (var b = new SolidBrush(Fx.Red)) g.FillRectangle(b, tr);
        Fx.TextIn(g, "필수", tf, Color.White, tr, true, false);
        float x = tr.Right + Fx.S(14), y = Fx.S(12);
        var f = Fx.F(17); var fb = Fx.F(17, true);
        Fx.Text(g, "통합패치 ", f, Fx.C(0xf3e2c8), x, y);
        float x2 = x + Fx.Measure(g, "통합패치 ", f).Width;
        Fx.Text(g, "11번(필드 부대 3D 조형)", fb, Fx.C(0xffcf8a), x2, y);
        Fx.Text(g, "과는 같이 못 씁니다.", f, Fx.C(0xf3e2c8), x2 + Fx.Measure(g, "11번(필드 부대 3D 조형)", fb).Width, y);
        Fx.Text(g, "통합패치 설치기에서 11번을 빼고 [적용]한 뒤 2D 모드를 설치하세요.", f, Fx.C(0xf3e2c8), x, y + Fx.S(26));
    }
    void DrawStatus(Graphics g, Pane p) {
        Fx.TextIn(g, statusText, Fx.F(13.5f), Fx.C(0xcfc4ad), new RectangleF(0, 0, p.Width - Fx.S(60), Fx.S(20)));
        var pf = Fx.F(13.5f); string ps = progress + "%"; var psz = Fx.Measure(g, ps, pf);
        Fx.Text(g, ps, pf, Fx.C(0xcfc4ad), p.Width - psz.Width, Fx.S(2));
        var br = new Rectangle(0, Fx.S(24), p.Width, Fx.S(12));
        Fx.Rect(g, br, Fx.C(0x0f0d0c), Fx.C(0x3a3024));
        int w = (int)((br.Width - 2) * Math.Max(0, Math.Min(100, progress)) / 100f);
        if (w > 0) using (var lg = new LinearGradientBrush(new Rectangle(1, br.Y + 1, Math.Max(2, w), br.Height - 2), Fx.C(0xc9933f), Fx.C(0xf6dca0), 0f)) g.FillRectangle(lg, 1, br.Y + 1, w, br.Height - 2);
    }
    void DrawNotes(Graphics g, Pane p) {
        Fx.Rect(g, p.ClientRectangle, Fx.Card, Fx.Line);
        var hf = Fx.F(26, true);
        Fx.Text(g, "판별 ", hf, Fx.C(0xf3e7cc), Fx.S(16), Fx.S(14));
        Fx.Text(g, "업데이트 내역", hf, Fx.Gold, Fx.S(16) + Fx.Measure(g, "판별 ", hf).Width, Fx.S(14));
        Fx.Text(g, "貳", Fx.F(13), Fx.C(0x8a7a60), p.Width - Fx.S(26), Fx.S(12));
        string sub = index != null ? "줄을 누르면 바뀐 점이 펼쳐짐 · 「↺ 이 판으로」 = 그 판으로 바꾸기" : checking ? "깃허브에서 판 목록 읽는 중…" : "판 목록을 못 읽었습니다 — 인터넷 연결 확인 후 큰 단추를 눌러 다시 시도";
        Fx.TextIn(g, sub, Fx.F(13), Fx.C(0x8a7a60), new RectangleF(Fx.S(16), Fx.S(48), p.Width - Fx.S(32), Fx.S(20)));
    }
    void DrawFoot(Graphics g, Pane p) {
        Fx.Text(g, "원작 SAN14 2D Unified beta3 · 새 게임판 호환·확장 · 설치기 " + Gh.InstallerVersion, Fx.F(12), Fx.C(0x6f6450), Fx.S(24), Fx.S(22));
        string r = "깃허브 " + Gh.Repo; var f = Fx.F(12); var sz = Fx.Measure(g, r, f);
        Fx.Text(g, r, f, Fx.C(0xa99566), p.Width - sz.Width - Fx.S(24), Fx.S(22));
        if (index != null && index.installer > Gh.InstallerVersion) {
            string n = "● 새 설치기가 나왔습니다 — 여기를 눌러 깃허브에서 받으세요"; var nf = Fx.F(13, true); var nsz = Fx.Measure(g, n, nf);
            Fx.Text(g, n, nf, Fx.Orange, p.Width - nsz.Width - Fx.S(24), Fx.S(42));
        }
    }

    // ---------------- 상태 ----------------
    GhRelease Latest() {
        if (index == null) return null;
        return index.releases.FirstOrDefault(r => Gh.GameMatches(r, exeStamp)) ?? index.releases[0];
    }
    string LatestTag() { var l = Latest(); return l != null ? l.tag : installedTag; }
    GhRelease Find(string tag) { return index == null || tag == null ? null : index.releases.FirstOrDefault(r => r.tag == tag); }
    void Status(string s) { statusText = s; stline.Invalidate(); }
    void Log(string s) { log.AppendText((log.TextLength > 0 ? "\r\n" : "") + s); }
    void folderFound(string p) {
        gameRoot = p ?? "";
        exeStamp = gameRoot.Length > 0 ? Gh.ExeStamp(gameRoot) : 0;
        installedRel = gameRoot.Length > 0 ? Gh.InstalledRelease(gameRoot) : null;
        installedTag = installedRel == null ? null : Gh.TagOfRelease(installedRel);
    }
    async Task Refresh(bool first) {
        checking = true; indexError = null; UpdateUi();
        if (first) {
            Log(gameRoot.Length > 0 ? "✔ 게임 폴더 찾음 · " + gameRoot : "✖ 게임 폴더를 자동으로 못 찾았습니다 — [바꾸기]로 SAN14CE_KO.exe 가 있는 폴더를 고르세요.");
            if (installedTag != null) Log("✔ 2D 모드 " + installedTag + " 깔려 있음");
        }
        var t1 = Task.Run(() => { try { return Gh.FetchIndex(); } catch (Exception ex) { indexError = ex.Message; return null; } });
        if (localRepo == null) localRepo = Gh.LocalRepo(exeDir);
        var t2 = Task.Run(() => Gh.FetchIpLatest());
        index = await t1; ipTag = await t2;
        checking = false;
        if (index != null) {
            var lr = Latest();
            Log("● 깃허브 최신 " + lr.tag + (installedTag == lr.tag ? " — 이미 최신 판입니다." : " — 큰 단추로 받을 수 있습니다."));
            if (gameRoot.Length > 0 && exeStamp != 0 && !index.releases.Any(r => Gh.GameMatches(r, exeStamp))) Log("✖ 이 게임 실행 파일(" + exeStamp.ToString("x8") + ")에 맞는 2D 판이 아직 없습니다. 게임이 새로 업데이트됐다면 2D 새 판을 기다려 주세요.");
        } else if ((index = Gh.LocalIndex(localRepo)) != null) {
            Log("● 깃허브에 연결이 안 돼, 받아 둔 저장소 폴더의 판 목록을 씁니다 (최신 " + index.releases[0].tag + "). 장수 그림 묶음이 없으면 설치는 인터넷이 필요합니다.");
            indexError = null;
        } else {
            Log("✖ 깃허브 판 목록을 못 읽었습니다: " + indexError);
            Log("   인터넷 연결을 확인하세요. 회사·학교 망이나 백신이 깃허브(raw.githubusercontent.com)를 막으면 이 오류가 납니다.");
            if (File.Exists(Path.Combine(exeDir, "manifest.json")) && File.Exists(Path.Combine(exeDir, "core.zip")) && InstallerEngine.IsSha(BuildInfo.ManifestHash) && BuildInfo.ManifestHash != new string('0', 64)) {
                localPkg = exeDir; Log("● 설치기 옆 묶음으로 설치할 수 있습니다(인터넷 없이).");
            }
        }
        BuildRows(); UpdateUi(); hero.Invalidate();
    }
    void BuildRows() {
        list.SuspendLayout();
        foreach (var r in rows) list.Controls.Remove(r);
        rows.Clear(); selected = null;
        if (index != null) {
            int li = Gh.Order(index, LatestTag());
            for (int i = 0; i < index.releases.Count; i++) {
                var rel = index.releases[i];
                var row = new VerRow { R = rel, Installed = rel.tag == installedTag, IsNew = i == li && rel.tag != installedTag, Compatible = Gh.GameMatches(rel, exeStamp), BackColor = Fx.Card };
                row.Open = i == 0;
                row.Toggle = rr => { bool was = rr.Open; foreach (var x in rows) x.Open = false; rr.Open = !was; Select(rr.Open && !rr.Installed && rr.Compatible ? rr : null); Relayout(); };
                row.Pick = async rr => { Select(rr); Relayout(); await Install(rr.R); };
                rows.Add(row); list.Controls.Add(row);
            }
        }
        list.ResumeLayout(); Relayout();
    }
    void Select(VerRow r) {
        selected = r; foreach (var x in rows) { x.Selected = x == r; x.Invalidate(); }
        if (r != null) { r.Open = true; }
        UpdateUi();
    }
    void Relayout() {
        int y = 0, w = list.ClientSize.Width - Fx.S(6);
        using (var g = list.CreateGraphics()) foreach (var r in rows) { int h = r.Need(g, w); r.SetBounds(0, y - list.VerticalScroll.Value, w, h); r.Invalidate(); y += h + Fx.S(6); }
    }
    void UpdateUi() {
        bool hasGame = gameRoot.Length > 0;
        var lr = Latest();
        bool compat = lr != null && Gh.GameMatches(lr, exeStamp);
        main.Enabled = true;
        if (busy) { main.Title = "작업 중…"; main.Sub = "누르면 취소"; main.Icon = "■"; main.Style(0x5a4a3a, 0x3e3226, 0x8a7458, 0xf3e7cc, 0xd9cdb5); }
        else if (!hasGame) { main.Title = "게임 폴더 고르기"; main.Sub = "SAN14CE_KO.exe 가 있는 폴더\nSteam\\steamapps\\common\\…"; main.Icon = "▶"; StyleGold(); }
        else if (checking) { main.Title = "판 목록 읽는 중…"; main.Sub = "깃허브에서 확인하고 있습니다"; main.Icon = ""; main.Style(0x3a3229, 0x2a241e, 0x5a4e3d, 0xcfc4ad, 0xa99f8c); }
        else if (index == null) {
            if (localPkg != null) { main.Title = "설치기 옆 묶음으로 설치"; main.Sub = "인터넷 없이 · 이 폴더의 판"; main.Icon = "▶"; StyleGold(); }
            else { main.Title = "다시 연결"; main.Sub = "깃허브 판 목록을 못 읽음\n인터넷 확인 후 누르세요"; main.Icon = "⟳"; main.Style(0x3a3229, 0x2a241e, 0x6b5636, 0xe6d5b2, 0xa99f8c); }
        }
        else if (!compat) { main.Title = "게임 판이 다릅니다"; main.Sub = (lr.game ?? "") + " 필요\n새 2D 판을 기다려 주세요"; main.Icon = "!"; main.Style(0x3a2a24, 0x2a1e1a, 0x8a4a3a, 0xf3c8b8, 0xd9a898); }
        else if (installedTag == null) { main.Title = "설치"; main.Sub = "처음 깔기 · " + lr.tag + "\n장수 그림까지 한 번에 받음"; main.Icon = "▶"; StyleGold(); }
        else if (installedTag != lr.tag) { main.Title = lr.tag + " 로 업데이트"; main.Sub = "바뀐 파일만 받음 · 약 " + Gh.Mb(lr.coreSize) + "\n내 설정은 그대로"; main.Icon = "⟳"; main.Style(0xd4532f, 0x9e2f1c, 0xf08a5a, 0xfff3e6, 0xffd9c2); }
        else { main.Title = "✔ 최신 판입니다"; main.Sub = lr.tag + " 깔려 있음\n누르면 파일 검사"; main.Icon = ""; main.Style(0x24331f, 0x1a2617, 0x4d7a48, 0xa8e0a2, 0x8fb88a); }
        main.Invalidate();
        verify.Enabled = !busy && hasGame && installedTag != null;
        back.Enabled = !busy && hasGame && index != null;
        if (selected != null) {
            int si = Gh.Order(index, selected.R.tag), ii = Gh.Order(index, installedTag);
            back.Title = selected.R.tag + (ii >= 0 && si < ii ? " 로 바꾸기" : " 로 되돌리기"); back.Sub = selected.R.title ?? "";
            back.Style(0x1f2735, 0x1a1f2a, 0x7a9ad0, 0xffffff, 0xb8c8e8);
        } else { back.Title = "이전 판으로 되돌리기"; back.Sub = "오른쪽 목록에서 판 고름"; back.Style(0x2a231c, 0x231d18, 0x6b5636, 0xe6d5b2, 0xa99f8c); }
        foreach (var l in new[] { lnkReinstall, lnkRecover, lnkRemove, lnkFolder }) l.Enabled = !busy;
        lnkReinstall.Enabled = !busy && hasGame && index != null;
        lnkRemove.Enabled = !busy && hasGame && installedTag != null;
        verBox.Invalidate(); notes.Invalidate(); foot.Invalidate();
    }
    void StyleGold() { main.Style(0xe8c07a, 0xb8862f, 0xf6dca0, 0x241a0c, 0x4a3616); }

    // ---------------- 동작 ----------------
    void PickFolder() {
        using (var d = new FolderBrowserDialog { Description = "SAN14CE_KO.exe 가 있는 게임 폴더를 고르세요", ShowNewFolderButton = false }) {
            if (gameRoot.Length > 0) d.SelectedPath = gameRoot;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (!File.Exists(Path.Combine(d.SelectedPath, "SAN14CE_KO.exe"))) { MessageBox.Show(this, "이 폴더에는 SAN14CE_KO.exe 가 없습니다.", "2D 모드"); return; }
            folderFound(d.SelectedPath); Log("✔ 게임 폴더 · " + gameRoot + (installedTag != null ? " · 2D " + installedTag + " 깔려 있음" : ""));
            BuildRows(); UpdateUi(); hero.Invalidate();
        }
    }
    async Task MainClick() {
        if (busy) { stop = true; Status("취소 요청됨 — 지금 단계를 마무리하는 중"); return; }
        if (gameRoot.Length == 0) { PickFolder(); return; }
        if (checking) return;
        if (index == null) {
            if (localPkg != null) { await RunLocal(); return; }
            await Refresh(false); return;
        }
        var lr = Latest();
        if (!Gh.GameMatches(lr, exeStamp)) { MessageBox.Show(this, "지금 게임 실행 파일에 맞는 2D 판이 없습니다.\r\n필요한 게임 판: " + lr.game, "2D 모드"); return; }
        if (installedTag == lr.tag) { await Run("verify", null); return; }
        await Install(lr);
    }
    async Task Install(GhRelease r, bool again = false) {
        if (busy || r == null) return;
        if (!Gh.GameMatches(r, exeStamp)) { MessageBox.Show(this, r.tag + " 은 다른 게임 판(" + r.game + ")용이라 지금 게임에는 깔 수 없습니다.", "2D 모드"); return; }
        int ri = Gh.Order(index, r.tag), ii = Gh.Order(index, installedTag);
        if (!again && r.tag == installedTag) { MessageBox.Show(this, r.tag + " 이 이미 깔려 있습니다. 다시 깔려면 [다시 설치]를 누르세요.", "2D 모드"); return; }
        if (!again && ii >= 0 && ri > ii && MessageBox.Show(this, "지금 " + installedTag + " → 옛 판 " + r.tag + " 로 되돌립니다. 개인 설정은 그대로 둡니다.\r\n계속할까요?", "되돌리기 확인", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        await Run("install", r);
    }
    async Task RunLocal() { await Run("install", null, localPkg, BuildInfo.ManifestHash); }
    async Task Run(string op, GhRelease r, string pkgDir = null, string manHash = null) {
        if (busy) return;
        if (gameRoot.Length == 0) { PickFolder(); return; }
        string target;
        try { target = InstallerEngine.Root(gameRoot); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "2D 모드"); return; }
        bool updating = File.Exists(InstallerEngine.StatePath(target));
        busy = true; stop = false; progress = 0; UpdateUi(); Status(op == "install" ? "준비 중" : "검사 중");
        Log("");
        Log("▶ " + (op == "install" ? (r != null ? r.tag + " 설치" : "설치") : op == "verify" ? "파일 검사" : op == "remove" ? "제거" : "중단 복구") + " 시작");
        Action<string, int> prog = (msg, v) => BeginInvoke((Action)(() => { Status(msg); progress = Math.Max(0, Math.Min(100, v)); stline.Invalidate(); }));
        string result; bool ok = false; var eng0 = (InstallerEngine)null;
        try {
            await Task.Run(() => {
                string pkg = pkgDir, hash = manHash;
                if (op == "install" && r != null) { pkg = Gh.EnsurePackage(r, prog, () => stop, localRepo); hash = r.manifestSha; }
                if (pkg == null) { pkg = Path.Combine(Gh.CacheRoot(), "_"); Directory.CreateDirectory(pkg); hash = new string('0', 64); }
                var eng = new InstallerEngine(pkg, hash) { KeepPreviousBackup = false };
                eng0 = eng;
                eng.Cancelled = () => stop;
                eng.Progress = prog;
                eng.FetchArchive = (key, len, sha) => FetchAssets(key, len, sha, prog);
                if (op == "install") eng.Install(target);
                else if (op == "verify") eng.Verify(target);
                else if (op == "remove") eng.Remove(target);
                else eng.Recover(target);
            });
            ok = true;
            result = op == "install" ? "설치 완료" + (r != null ? " — 2D 모드 " + r.tag : "") : op == "verify" ? "파일 검사 통과 — 깨진 파일 없음" : op == "remove" ? "제거 완료" : "중단 복구 완료";
            if (op == "install") {
                result += "\r\n" + (updating ? "개인 설정과 장수별 설정은 그대로 두었습니다." : "처음 설치 기본 설정을 넣었습니다.");
                result += "\r\n설정: 게임 안 정보 화면 「２Ｄ 총변경」 · 편집기능 「２Ｄ 무장 스프라이트 설정」";
                if (eng0 != null) result += "\r\n걸린 시간 " + Math.Round(eng0.Stat.Clock.Elapsed.TotalSeconds, 1) + "초 · 안 바뀐 파일 재사용 " + (eng0.Stat.Linked + eng0.Stat.Copied) + "개 · 새로 푼 파일 " + eng0.Stat.Extracted + "개";
            }
            progress = 100;
        } catch (OperationCanceledException) { result = "취소됨 — 바꾸기 전 상태로 되돌렸습니다."; }
        catch (Exception ex) {
            result = "작업 중단 — " + ex.Message;
            if (op == "verify") result += "\r\n→ [다시 설치]를 누르면 깨진 파일만 새로 받아 고칩니다.";
        }
        busy = false;
        folderFound(gameRoot);
        Status(ok ? "완료" : result.Split('\r')[0]);
        Log((ok ? "✔ " : "✖ ") + result.Replace("\r\n", "\r\n   "));
        BuildRows(); UpdateUi(); hero.Invalidate();
        MessageBox.Show(this, result, "2D 모드", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }
    // 장수 그림 묶음: 옆 폴더에 있으면 그대로, 없으면 깃허브 배포에서 받음(이어받기)
    string FetchAssets(string key, long len, string sha, Action<string, int> prog) {
        if (key != "assets.zip") return null;
        string found = Gh.FindAssets(exeDir, len);
        if (found != null) { BeginInvoke((Action)(() => Log("● 장수 그림 묶음: " + found + " 사용"))); return found; }
        if (index == null || index.assets == null || String.IsNullOrEmpty(index.assets.url)) throw new IOException("장수 그림 묶음(assets.zip)이 없고 받을 주소도 모릅니다.");
        string dst = Path.Combine(Gh.CacheRoot(), "assets", "assets.zip");
        var drive = new DriveInfo(Path.GetPathRoot(dst));
        if (drive.AvailableFreeSpace < len * 2 + 536870912L) throw new IOException("장수 그림 묶음을 받을 공간이 모자랍니다(필요 약 " + Math.Ceiling(len * 2 / 1073741824.0) + "GB, " + drive.Name + ").");
        BeginInvoke((Action)(() => Log("● 장수 그림 묶음 받기 시작 (" + Gh.Mb(len) + ") — 끊겨도 다시 누르면 이어서 받습니다.")));
        var sw = Stopwatch.StartNew();
        Gh.Download(index.assets.url, dst, len, (g, t) => {
            double sp = g / Math.Max(1.0, sw.Elapsed.TotalSeconds);
            string eta = sp > 1 && t > 0 ? " · 남은 시간 약 " + Math.Ceiling((t - g) / sp / 60) + "분" : "";
            prog("장수 그림 묶음 받는 중 " + Gh.Mb(g) + " / " + Gh.Mb(t) + eta, t > 0 ? (int)(g * 100 / t) : 0);
        }, () => stop);
        prog("장수 그림 묶음 확인 중", 100);
        if (!String.IsNullOrEmpty(sha) && Gh.Sha(dst) != sha) { File.Delete(dst); throw new IOException("받은 장수 그림 묶음이 손상됐습니다. 다시 눌러 주세요."); }
        return dst;
    }
    static void OpenUrl(string u) { try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { } }

    public static string FindGame() {
        const string rtk = "Steam\\steamapps\\common\\Romance_of_the_Three_Kingdoms_14";
        foreach (var env in new[] { "ProgramFiles(x86)", "ProgramFiles" }) {
            string b = Environment.GetEnvironmentVariable(env);
            if (String.IsNullOrEmpty(b)) continue;
            string p = Path.Combine(b, rtk);
            if (File.Exists(Path.Combine(p, "SAN14CE_KO.exe"))) return p;
        }
        // Steam 라이브러리 목록(libraryfolders.vdf)
        try {
            foreach (var env in new[] { "ProgramFiles(x86)", "ProgramFiles" }) {
                string b = Environment.GetEnvironmentVariable(env); if (String.IsNullOrEmpty(b)) continue;
                string vdf = Path.Combine(b, "Steam", "steamapps", "libraryfolders.vdf"); if (!File.Exists(vdf)) continue;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) {
                    string lib = m.Groups[1].Value.Replace("\\\\", "\\");
                    string p = Path.Combine(lib, "steamapps", "common", "Romance_of_the_Three_Kingdoms_14");
                    if (File.Exists(Path.Combine(p, "SAN14CE_KO.exe"))) return p;
                }
            }
        } catch { }
        string tail = rtk.Substring(6);
        foreach (var c in new[] { "D:\\SteamLibrary\\" + tail, "E:\\SteamLibrary\\" + tail, "F:\\SteamLibrary\\" + tail, "C:\\SteamLibrary\\" + tail, "D:\\" + rtk, "E:\\" + rtk, "D:\\Games\\" + rtk, "C:\\Games\\" + rtk })
            try { if (File.Exists(Path.Combine(c, "SAN14CE_KO.exe"))) return c; } catch { }
        return "";
    }
    [STAThread]
    public static int Main(string[] args) {
        try { AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false); AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false); } catch { }
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }
}
