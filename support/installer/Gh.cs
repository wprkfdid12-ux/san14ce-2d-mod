// 깃허브 판 목록·내려받기 (화면 없는 부분 — 시험에서 그대로 부름)
//  저장소: wprkfdid12-ux/san14ce-2d-mod
//   updates/index.json            판 목록(새 판이 앞)·장수 그림 묶음 주소
//   updates/<판>/manifest.json, core.zip   판마다 프로그램 묶음(되돌리기용으로 옛 판도 남김)
//   장수 그림 묶음 assets.zip(1.4GB) = 저장소 '배포(Release)' assets-N 첨부 (저장소 파일은 100MB 한도라)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public class GhNote { public string t; public string d; }
public class GhRelease {
    public string tag, date, title, game, stamp;          // stamp = 게임 실행 파일 빌드 시각(16진)
    public List<GhNote> notes;
    public string manifest, manifestSha, core, coreSha;
    public long manifestSize, coreSize;
}
public class GhAssets { public string url, sha256; public long size; }
public class GhIndex { public int format; public int installer; public List<GhRelease> releases; public GhAssets assets; }
public class IpRelease { public string tag; }
public class IpIndex { public List<IpRelease> releases; }

public static class Gh {
    public const string Repo = "wprkfdid12-ux/san14ce-2d-mod";
    public const string Page = "https://github.com/" + Repo;
    public const string Raw = "https://raw.githubusercontent.com/" + Repo + "/main/";
    public const string IpRaw = "https://raw.githubusercontent.com/wprkfdid12-ux/san14ce-integrated-patch/main/updates/index.json";
    public const int InstallerVersion = 1;

    public static string CacheRoot() {
        string b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(b, "San14_2D", "배포");
    }
    static HttpWebRequest Req(string url) {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
        var q = (HttpWebRequest)WebRequest.Create(url);
        q.UserAgent = "SAN14-2D-Installer/" + InstallerVersion; q.Timeout = 20000; q.ReadWriteTimeout = 60000; q.AllowAutoRedirect = true;
        return q;
    }
    public static string GetText(string url) {
        var q = Req(url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks);   // 캐시 피하기
        using (var r = q.GetResponse()) using (var s = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return s.ReadToEnd();
    }
    public static GhIndex ParseIndex(string json) {
        var x = new MiniJson().Deserialize<GhIndex>(json);
        if (x == null || x.releases == null || x.releases.Count == 0) throw new IOException("판 목록(index.json) 모양이 이상합니다.");
        foreach (var r in x.releases) {
            if (!Regex.IsMatch(r.tag ?? "", @"^v\d+[a-z]?$")) throw new IOException("판 번호가 이상합니다: " + r.tag);
            foreach (var f in new[] { r.manifest, r.core })
                if (f == null || !f.StartsWith("updates/") || f.Contains("..") || f.Contains("\\")) throw new IOException("판 파일 경로가 이상합니다: " + f);
            if (!InstallerEngine.IsSha(r.manifestSha) || !InstallerEngine.IsSha(r.coreSha)) throw new IOException("판 지문(sha)이 이상합니다: " + r.tag);
            if (r.notes == null) r.notes = new List<GhNote>();
        }
        return x;
    }
    public static GhIndex FetchIndex() { return ParseIndex(GetText(Raw + "updates/index.json")); }
    // 받은 저장소 ZIP 안에서 실행됐으면 그 폴더(updates/index.json 이 있는 곳). 없으면 null
    public static string LocalRepo(string exeDir) {
        try {
            var d = new DirectoryInfo(exeDir);
            for (int i = 0; i < 4 && d != null; i++, d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "updates", "index.json"))) return d.FullName;
        } catch { }
        return null;
    }
    public static GhIndex LocalIndex(string repo) {
        if (repo == null) return null;
        try { return ParseIndex(File.ReadAllText(Path.Combine(repo, "updates", "index.json"), Encoding.UTF8)); } catch { return null; }
    }
    // 통합패치 최신 판 번호(R43.30 같은). 못 읽으면 null
    public static string IpLatest(string json) {
        var x = new MiniJson().Deserialize<IpIndex>(json);
        if (x == null || x.releases == null) return null;
        string best = null; int b1 = -1, b2 = -1;
        foreach (var r in x.releases) {
            var m = Regex.Match(r.tag ?? "", @"^R(\d+)\.(\d+)$"); if (!m.Success) continue;
            int a1 = Int32.Parse(m.Groups[1].Value), a2 = Int32.Parse(m.Groups[2].Value);
            if (a1 > b1 || (a1 == b1 && a2 > b2)) { b1 = a1; b2 = a2; best = r.tag; }
        }
        return best;
    }
    public static string FetchIpLatest() { try { return IpLatest(GetText(IpRaw)); } catch { return null; } }

    // 깔린 판: .installed.json 의 release "…-v31d" → "v31d". 옛 1.0.1 판은 "v27(옛 1.0.1판)"
    public static string TagOfRelease(string release) {
        if (String.IsNullOrEmpty(release)) return null;
        var m = Regex.Match(release, @"-(v\d+[a-z]?)$"); if (m.Success) return m.Groups[1].Value;
        if (release.EndsWith("-ce101")) return "v27";
        return "옛 판";
    }
    public static string InstalledRelease(string root) {
        try {
            string p = InstallerEngine.StatePath(root); if (!File.Exists(p)) return null;
            var s = new MiniJson().Deserialize<InstalledState>(File.ReadAllText(p, Encoding.UTF8));
            return s == null ? null : (s.release ?? "");
        } catch { return ""; }
    }
    // 게임 실행 파일 빌드 시각(PE 머리). 못 읽으면 0
    public static uint ExeStamp(string root) {
        try {
            string p = Path.Combine(root, "SAN14CE_KO.exe"); if (!File.Exists(p)) return 0;
            byte[] b = new byte[4096]; int got = 0;
            using (var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { int k; while (got < b.Length && (k = f.Read(b, got, b.Length - got)) > 0) got += k; }
            if (got < 0x200 || b[0] != 'M' || b[1] != 'Z') return 0;
            int pe = BitConverter.ToInt32(b, 0x3c); if (pe < 0x40 || pe + 12 > got || BitConverter.ToUInt32(b, pe) != 0x4550) return 0;
            return BitConverter.ToUInt32(b, pe + 8);
        } catch { return 0; }
    }
    public static bool GameMatches(GhRelease r, uint stamp) {
        uint st; if (r == null || !UInt32.TryParse(r.stamp ?? "", System.Globalization.NumberStyles.HexNumber, null, out st)) return true;
        return stamp == 0 || st == stamp;
    }
    public static int Order(GhIndex ix, string tag) { if (ix == null || tag == null) return -1; for (int i = 0; i < ix.releases.Count; i++) if (ix.releases[i].tag == tag) return i; return -1; }

    public static string Sha(string p) { return InstallerEngine.Sha(p); }
    // 받기(이어받기 지원). say(받은 바이트, 전체)
    public static void Download(string url, string path, long size, Action<long, long> say, Func<bool> cancelled) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string part = path + ".part";
        for (int attempt = 1; ; attempt++) {
            try {
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (size > 0 && have > size) { File.Delete(part); have = 0; }
                if (size > 0 && have == size) break;
                var q = Req(url); q.Timeout = 60000; q.ReadWriteTimeout = 120000;
                if (have > 0) q.AddRange(have);
                using (var r = (HttpWebResponse)q.GetResponse()) {
                    bool append = have > 0 && r.StatusCode == HttpStatusCode.PartialContent;
                    if (!append) have = 0;
                    using (var s = r.GetResponseStream())
                    using (var f = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)) {
                        byte[] buf = new byte[1 << 20]; int k; long got = have; var last = DateTime.MinValue;
                        while ((k = s.Read(buf, 0, buf.Length)) > 0) {
                            if (cancelled != null && cancelled()) throw new OperationCanceledException();
                            f.Write(buf, 0, k); got += k;
                            if ((DateTime.UtcNow - last).TotalMilliseconds > 200) { last = DateTime.UtcNow; say(got, size); }
                        }
                    }
                }
                if (size <= 0 || new FileInfo(part).Length == size) break;
                if (attempt >= 6) throw new IOException("받은 크기가 다릅니다: " + Path.GetFileName(path));
            } catch (OperationCanceledException) { throw; }
            catch (Exception) { if (attempt >= 6) throw; System.Threading.Thread.Sleep(1500 * attempt); }
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(part, path);
    }
    // 판 묶음(manifest.json·core.zip)을 캐시에 받아 두고 폴더 경로를 돌려줌
    public static string EnsurePackage(GhRelease r, Action<string, int> progress, Func<bool> cancelled) { return EnsurePackage(r, progress, cancelled, null); }
    public static string EnsurePackage(GhRelease r, Action<string, int> progress, Func<bool> cancelled, string localRepo) {
        // 받은 저장소 ZIP 에 같은 판 묶음이 있으면 그대로(지문 확인)
        if (localRepo != null) {
            try {
                string lm = Path.Combine(localRepo, r.manifest.Replace('/', Path.DirectorySeparatorChar)), lc = Path.Combine(localRepo, r.core.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(lm) && File.Exists(lc) && Path.GetDirectoryName(lm) == Path.GetDirectoryName(lc) && Path.GetFileName(lm) == "manifest.json" && Path.GetFileName(lc) == "core.zip") {
                    progress(r.tag + " 묶음 확인 중", 0);
                    if (Sha(lm) == r.manifestSha && Sha(lc) == r.coreSha) return Path.GetDirectoryName(lm);
                }
            } catch { }
        }
        string dir = Path.Combine(CacheRoot(), r.tag);
        Directory.CreateDirectory(dir);
        foreach (var it in new[] { new { f = r.manifest, sha = r.manifestSha, size = r.manifestSize, name = "manifest.json" }, new { f = r.core, sha = r.coreSha, size = r.coreSize, name = "core.zip" } }) {
            string p = Path.Combine(dir, it.name);
            if (File.Exists(p) && Sha(p) == it.sha) continue;
            if (File.Exists(p)) File.Delete(p);
            string nm = it.name;
            Download(Raw + it.f, p, it.size, (g, t) => progress(r.tag + " " + nm + " 받는 중 " + Mb(g) + (t > 0 ? " / " + Mb(t) : ""), t > 0 ? (int)(g * 100 / t) : 0), cancelled);
            if (Sha(p) != it.sha) { File.Delete(p); throw new IOException(r.tag + " " + it.name + " 지문이 다릅니다. 다시 눌러 주세요."); }
        }
        return dir;
    }
    public static string Mb(long b) { return b >= 1073741824 ? (b / 1073741824.0).ToString("0.00") + "GB" : (b / 1048576.0).ToString("0.0") + "MB"; }
    // 장수 그림 묶음 찾기: 설치기 옆 → 설치기 위 폴더의 다른 폴더들 → 캐시. 크기가 같은 것
    // 설치기 폴더에서 위로 4단계까지: 그 폴더와 바로 아래 폴더들의 assets.zip
    public static string FindAssets(string exeDir, long size) {
        var c = new List<string>();
        try {
            var d = new DirectoryInfo(exeDir.TrimEnd(Path.DirectorySeparatorChar));
            for (int i = 0; i < 4 && d != null; i++, d = d.Parent) {
                c.Add(Path.Combine(d.FullName, "assets.zip"));
                if (d.Parent == null) break;              // 드라이브 뿌리는 아래 폴더까지 뒤지지 않음
                try { foreach (var s2 in Directory.GetDirectories(d.FullName)) c.Add(Path.Combine(s2, "assets.zip")); } catch { }
            }
        } catch { }
        c.Add(Path.Combine(CacheRoot(), "assets", "assets.zip"));
        foreach (var p in c) try { if (File.Exists(p) && new FileInfo(p).Length == size) return p; } catch { }
        return null;
    }
}
