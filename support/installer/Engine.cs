// 삼국지 14 · 2D 패치 설치기 (새로 만든 판, 2026-10-20)
// 옛 beta3 설치기(InstallerEngine)와 같은 기록 형식·같은 순서·같은 안전장치를 그대로 지키고, 느린 부분만 바꿈:
//  - 안 바뀐 그림은 다시 풀지 않고 기존 파일을 새 설치로 연결(하드링크, 안 되면 복사)
//  - 기존 파일 검사: 크기·수정 시각이 지난 검사와 같으면 저장해 둔 해시 사용(.San14_2D-hashcache.json)
//  - 압축 묶음 전체 해시 단계 생략(파일마다 해시는 그대로 확인), 파일마다 디스크 강제 기록 생략
//  - 여러 코어로 동시에 풀기
//  - assets.zip 이 이 폴더에 없으면 옆 정식판 폴더(..\SAN14_2D_Unified_beta3)의 것을 읽기만 함(시험판용)
// 기록(.installed.json / 작업 기록 / 백업 폴더) 형식은 옛 설치기와 같아서, 옛 설치기로 되돌려도 그대로 이어받음.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

public class PayloadFile { public string path; public string sha256; public string archive; public string blob; public long length; }
public class ArchiveInfo { public string sha256; public long length; }
public class PayloadManifest { public string release; public string executable; public string executableSha256; public List<PayloadFile> files; public Dictionary<string, ArchiveInfo> archives; }
public class InstalledState { public string format = "San14_2D_v1"; public string id; public string release; public string previousBackup; public List<PayloadFile> files; public bool removing; }
public class Transaction { public string id; public string newDllSha; public string restoreSource; public string restoreStateId; public string restorePreviousBackup; public bool hadPrevious; public bool committed; public bool cleanupBackup; }
public class HashCache { public string format = "San14_2D_hc1"; public Dictionary<string, string> entries = new Dictionary<string, string>(); }

public sealed class FileRec { public string Sha; public long Len; public long Ticks; }

public sealed class InstallerEngine {
    public Action<string> Checkpoint = delegate { };
    public Action<string, int> Progress = delegate { };
    public Func<bool> Cancelled = delegate { return false; };
    public Func<string, long, string, string> FetchArchive;   // (이름, 크기, sha) → 받은 파일 경로 (깃허브 설치기)
    public bool TestMode, RequirePrevious, KeepPreviousBackup;
    public string TestLocalAppData;
    public int Threads = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
    public bool NoHardLinks;              // 시험용
    public readonly Stats Stat = new Stats();
    readonly string package, manifestHash;
    static readonly MiniJson Json = new MiniJson();
    static string GameRoot;               // Root() 가 정한 게임 폴더(옛 설치기 패치: Safe 의 d3d11_chain 판단용)
    public const string SettingsPath = "San14_2D/San14_2D.ini";
    public const string Official = "SAN14_2D_Unified_beta3";
    const string ALLOW = "653aaaea26842224533ec31d590562712ede9785ed2fe418be2ae85fb5974c8f|5749cdadb1622398b7d200c82b257676a9c282a3e3ffa89af97c14189996e998|25a5ee0820a6029e04868d5f0744b08d638472aa7a5c2a152aa39fb294683dec";   // 통합패치 R43 로더 2개 + 삼갤 AI 모드 dinput8

    public sealed class Stats { public int Linked, Copied, Extracted, Hashed, CacheHits; public long ExtractedBytes; public Stopwatch Clock = new Stopwatch(); }

    public InstallerEngine(string pkg, string manifestSha) { package = Path.GetFullPath(pkg); manifestHash = manifestSha; }

    // ---------------- 기본 도구 (옛 설치기와 같음) ----------------
    public static string Sha(string p) {
        using (var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
        using (var h = SHA256.Create()) return Hex(h.ComputeHash(f));
    }
    static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
    static T ReadJson<T>(string p) {
        if (new FileInfo(p).Length > 16777216) throw new IOException("메타데이터가 너무 큽니다.");
        return Json.Deserialize<T>(File.ReadAllText(p, Encoding.UTF8));
    }
    static void WriteJson(string p, object o) {
        string tmp = Path.Combine(Path.GetDirectoryName(p), ".w-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
        try {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false))) { w.Write(Json.Serialize(o)); w.Flush(); fs.Flush(true); }
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
        } finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public static void NoLinks(string path) {
        string p = Path.GetFullPath(path);
        while (!String.IsNullOrEmpty(p)) {
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("링크 또는 연결 경로에는 설치하지 않습니다: " + p);
            string d = Path.GetDirectoryName(p);
            if (d == p) return;
            p = d;
        }
    }
    static bool BadPart(string s) {
        return String.IsNullOrEmpty(s) || s == "." || s == ".." || s.TrimEnd(' ', '.') != s || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }
    public static string Safe(string root, string rel) {
        if (String.IsNullOrEmpty(rel) || rel.Contains("\\") || rel.Contains(":") || Path.IsPathRooted(rel) || rel.Split('/').Any(BadPart))
            throw new IOException("잘못된 설치 경로: " + rel);
        if (rel != "d3d11.dll" && !rel.StartsWith("San14_2D/", StringComparison.OrdinalIgnoreCase)) throw new IOException("허용되지 않은 파일: " + rel);
        string r2 = rel;
        // 계절 맵(SeasonSync.dll)이 있으면 게임 폴더의 2D 실행부는 d3d11_chain.dll (옛 설치기 패치와 같음)
        if (rel == "d3d11.dll" && GameRoot != null && root == GameRoot && File.Exists(Path.Combine(root, "SeasonSync.dll"))) r2 = "d3d11_chain.dll";
        string full = Path.GetFullPath(Path.Combine(root, r2.Replace('/', Path.DirectorySeparatorChar)));
        string sep = Path.DirectorySeparatorChar.ToString();
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + sep, StringComparison.OrdinalIgnoreCase)) throw new IOException("게임 폴더를 벗어난 경로입니다.");
        NoLinks(full);
        return full;
    }
    static bool IsHex(string s, int n) { return s != null && s.Length == n && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')); }
    public static bool IsSha(string s) { return IsHex(s, 64); }
    static bool ValidId(string s) { return IsHex(s, 32); }
    void ValidateFiles(string root, List<PayloadFile> files) {
        if (files == null || files.Count < 1 || files.Count > 20000) throw new IOException("잘못된 파일 목록입니다.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) {
            Safe(root, f.path);
            if (f.path == "San14_2D/.installed.json" || !seen.Add(f.path) || f.length < 0 || (f.length == 0 && f.path != SettingsPath) || f.length > 134217728 || !IsSha(f.sha256))
                throw new IOException("잘못된 파일 정보: " + f.path);
        }
        if (!seen.Contains("d3d11.dll")) throw new IOException("2D 실행부가 없습니다.");
    }
    PayloadManifest Manifest(string root) {
        string p = Path.Combine(package, "manifest.json");
        if (!IsSha(manifestHash) || Sha(p) != manifestHash) throw new IOException("배포 목록 해시가 다릅니다. 배포 파일을 다시 받아 주세요.");
        var m = ReadJson<PayloadManifest>(p);
        ValidateFiles(root, m.files);
        if (m.executable != "SAN14CE_KO.exe" || !IsSha(m.executableSha256) || m.archives == null) throw new IOException("잘못된 버전 정보입니다.");
        foreach (var a in m.archives)
            if ((a.Key != "assets.zip" && a.Key != "core.zip") || a.Value.length < 1 || !IsSha(a.Value.sha256)) throw new IOException("잘못된 압축 정보입니다.");
        foreach (var f in m.files)
            if (!m.archives.ContainsKey(f.archive) || f.blob != f.sha256) throw new IOException("잘못된 압축 파일 참조입니다.");
        return m;
    }
    public static string StatePath(string root) { return Path.Combine(root, "San14_2D", ".installed.json"); }
    InstalledState State(string root) {
        NoLinks(StatePath(root));
        var s = ReadJson<InstalledState>(StatePath(root));
        if (s == null || s.format != "San14_2D_v1" || !ValidId(s.id)) throw new IOException("설치 기록을 확인할 수 없습니다.");
        ValidateFiles(root, s.files);
        return s;
    }
    static string Backup(string root, string id) {
        if (!ValidId(id)) throw new IOException("잘못된 백업 번호입니다.");
        string p = Path.Combine(root, ".San14_2D-backups", id); NoLinks(p); return p;
    }
    static string Stage(string root, string id) {
        if (!ValidId(id)) throw new IOException("잘못된 작업 번호입니다.");
        string p = Path.Combine(root, ".San14_2D-stage-" + id); NoLinks(p); return p;
    }
    static string Journal(string root) { string p = Path.Combine(root, ".San14_2D-transaction.json"); NoLinks(p); return p; }
    public static string Root(string folder) {
        string r = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        if (r.Length > 140) throw new IOException("설치 경로가 너무 깁니다. 140자 이하의 게임 폴더를 선택하세요.");
        if (!Directory.Exists(r) || r == Path.GetPathRoot(r).TrimEnd('\\')) throw new IOException("게임 설치 폴더를 선택하세요.");
        NoLinks(r);
        GameRoot = r;
        return r;
    }
    sealed class Unlock : IDisposable { readonly Mutex m; public Unlock(Mutex x) { m = x; } public void Dispose() { if (m != null) { m.ReleaseMutex(); m.Dispose(); } } }
    IDisposable Lock(string root) {
        if (TestMode) return new Unlock(null);
        string h; using (var s = SHA256.Create()) h = BitConverter.ToString(s.ComputeHash(Encoding.UTF8.GetBytes(root.ToLowerInvariant()))).Replace("-", "");
        var mx = new Mutex(false, "Local\\San14_2D_" + h);
        try { if (!mx.WaitOne(0)) throw new IOException("이 폴더에 다른 설치 작업이 진행 중입니다."); }
        catch (AbandonedMutexException) { }
        catch { mx.Dispose(); throw; }
        return new Unlock(mx);
    }
    static bool IsGameProcess(string n) {
        if (String.Equals(n, "SAN14_Launcher", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var a in new[] { "SAN14", "SAN14CE", "SAN14PK" })
            foreach (var b in new[] { "", "_KO", "_EN", "_SC", "_TC", "_JP" })
                if (String.Equals(n, a + b, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    void GameClosed() {
        if (TestMode) return;
        foreach (var p in Process.GetProcesses()) {
            try { if (IsGameProcess(p.ProcessName)) throw new IOException("삼국지 14와 런처를 종료한 뒤 다시 실행하세요. 강제로 종료하지 않습니다."); }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
    }
    void ThrowIfCancelled() { if (Cancelled()) throw new OperationCanceledException("설치가 취소되었습니다."); }

    // ---------------- 빠른 해시: 크기·수정 시각이 지난번과 같으면 저장해 둔 값 ----------------
    HashCache cache; string cacheRoot; bool cacheDirty;
    static string CachePath(string root) { return Path.Combine(root, ".San14_2D-hashcache.json"); }
    void LoadCache(string root) {
        if (cache != null && cacheRoot == root) return;
        cacheRoot = root; cache = null; cacheDirty = false;
        try { string p = CachePath(root); if (File.Exists(p)) { NoLinks(p); var c = ReadJson<HashCache>(p); if (c != null && c.format == "San14_2D_hc1" && c.entries != null) cache = c; } } catch { cache = null; }
        if (cache == null) cache = new HashCache();
        cache.entries = new Dictionary<string, string>(cache.entries, StringComparer.OrdinalIgnoreCase);
    }
    void SaveCache() {
        if (cache == null || !cacheDirty) return;
        try { WriteJson(CachePath(cacheRoot), cache); cacheDirty = false; } catch { }
    }
    FileRec Rec(string full, string rel, bool forceHash) {
        var fi = new FileInfo(full); long len = fi.Length, t = fi.LastWriteTimeUtc.Ticks; string key = rel.ToLowerInvariant();
        string v;
        if (!forceHash && cache != null && cache.entries.TryGetValue(key, out v)) {
            string[] p = v.Split('|');
            long l2, t2;
            if (p.Length == 3 && long.TryParse(p[0], out l2) && long.TryParse(p[1], out t2) && l2 == len && t2 == t && IsSha(p[2])) { Stat.CacheHits++; return new FileRec { Sha = p[2], Len = len, Ticks = t }; }
        }
        string h = Sha(full); Stat.Hashed++;
        if (cache != null) { cache.entries[key] = len + "|" + t + "|" + h; cacheDirty = true; }
        return new FileRec { Sha = h, Len = len, Ticks = t };
    }
    void Remember(string full, string rel, string sha) {
        if (cache == null) return;
        var fi = new FileInfo(full);
        cache.entries[rel.ToLowerInvariant()] = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + sha; cacheDirty = true;
    }

    // 같이 써도 되는 우리 쪽 모드(빌드마다 해시가 바뀜): SAN14CE 맵 확장 도우미 dinput8.dll — 파일 안 표시 글자로 알아봄
    static readonly string[] CompanionMarks = { "SAN14CE map expansion", "MapExpand_RealCell" };
    static bool Companion(string p) {
        var fi = new FileInfo(p);
        if (fi.Length < 4096 || fi.Length > (32 << 20)) return false;
        string s = Encoding.ASCII.GetString(File.ReadAllBytes(p));
        foreach (var m in CompanionMarks) if (s.IndexOf(m, StringComparison.Ordinal) >= 0) return true;
        return false;
    }
    // ---------------- 검사 ----------------
    void CheckExisting(string root) {
        if (File.Exists(Path.Combine(root, "FieldLeader3D_Managed20", "FieldLeader3D_Runtime.exe")))
            throw new IOException("통합패치 11번(필드 부대 이미지 3D 조형)이 설치되어 있습니다. 필드 부대 그림을 바꾸는 패치라 2D 패치와 같이 쓸 수 없습니다. 통합패치 설치 창에서 11번 체크를 빼고 [적용]한 뒤 다시 설치해 주세요. 기존 파일은 변경하지 않았습니다.");
        foreach (var n in new[] { "DINPUT8.dll", "dxgi.dll" }) {
            string p = Path.Combine(root, n);
            if (!File.Exists(p)) continue;
            string sh = Sha(p);
            if (!ALLOW.Contains(sh) && !Companion(p))
                throw new IOException(n + " 기반 모드가 있습니다. 호환성이 확인되지 않아 설치하지 않습니다. 기존 파일은 변경하지 않았습니다. (" + n + " " + sh.Substring(0, 12) + ")");
        }
        string dll = Safe(root, "d3d11.dll");
        if (File.Exists(dll) || File.Exists(Path.Combine(root, "San14_2D", "San14Officers.bin"))) {
            if (!File.Exists(StatePath(root)))
                throw new IOException("이전 2D 패치 파일이 설치 기록 없이 남아 있습니다(d3d11.dll / d3d11_chain.dll 또는 San14_2D\\San14Officers.bin). 해당 파일을 지운 뒤 다시 설치해 주세요. 다른 파일은 변경하지 않았습니다.");
            var st = State(root);
            var d = st.files.Single(f => f.path == "d3d11.dll");
            if (!File.Exists(dll) || Sha(dll) != d.sha256) throw new IOException("설치 기록과 현재 d3d11.dll이 다릅니다. 자동 교체하지 않습니다.");
        }
    }
    // 기존 설치 파일(기록된 것 + San14_2D 안의 개인 파일) → 해시
    Dictionary<string, FileRec> Snapshot(string root) {
        var r = new Dictionary<string, FileRec>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(StatePath(root))) return r;
        var st = State(root); int n = 0;
        foreach (var f in st.files) {
            ThrowIfCancelled();
            string p = Safe(root, f.path);
            if (!File.Exists(p)) throw new IOException("기존 파일이 누락되었습니다: " + f.path);
            var rec = Rec(p, f.path, false);
            if (f.path != SettingsPath && rec.Sha != f.sha256) {
                rec = Rec(p, f.path, true);          // 시각만 같고 내용이 다른 드문 경우 끝까지 다시 읽어 확인
                if (rec.Sha != f.sha256) throw new IOException("사용자가 변경한 파일은 덮어쓰지 않습니다: " + f.path);
            }
            r[f.path] = rec;
            if ((++n & 63) == 0 || n == st.files.Count) Progress("기존 설치 검사 " + n + "/" + st.files.Count, 0);
        }
        var stack = new Stack<string>(); stack.Push(Path.Combine(root, "San14_2D"));
        while (stack.Count > 0) {
            foreach (var e in Directory.EnumerateFileSystemEntries(stack.Pop())) {
                NoLinks(e); ThrowIfCancelled();
                if (Directory.Exists(e)) { stack.Push(e); continue; }
                string rel = e.Substring(root.Length + 1).Replace('\\', '/');
                if (rel == "San14_2D/.installed.json" || r.ContainsKey(rel)) continue;
                r[rel] = Rec(Safe(root, rel), rel, false);
            }
        }
        return r;
    }
    void CheckSnapshot(string root, Dictionary<string, FileRec> before) {
        var after = Snapshot(root);
        if (before.Count != after.Count || before.Any(kv => { FileRec a; return !after.TryGetValue(kv.Key, out a) || a.Sha != kv.Value.Sha; }))
            throw new IOException("준비 중 기존 설치 파일이 변경되어 갱신을 중단했습니다. 다시 실행하세요.");
    }
    // 개인 파일·개인 설정을 새 설치로 옮겨 담기 (옛 설치기 Preserve 와 같음)
    void Preserve(string root, string stage, PayloadManifest m, Dictionary<string, FileRec> snap) {
        if (snap.Count == 0) return;
        var tracked = new HashSet<string>(State(root).files.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
        foreach (var kv in snap) {
            if (kv.Key != SettingsPath && tracked.Contains(kv.Key)) continue;
            string src = Safe(root, kv.Key), dst = Safe(stage, kv.Key);
            if (kv.Key != SettingsPath && File.Exists(dst)) throw new IOException("추가한 개인 파일과 업데이트 경로가 겹칩니다: " + kv.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, kv.Key == SettingsPath);
            if (Sha(dst) != kv.Value.Sha) throw new IOException("복사 중 개인 파일이 변경되었습니다: " + kv.Key);
            if (kv.Key == SettingsPath) { var f = m.files.SingleOrDefault(x => x.path == SettingsPath); if (f != null) { f.sha256 = kv.Value.Sha; f.length = new FileInfo(dst).Length; } }
        }
        string rel = State(root).release;
        if (rel != null && rel.StartsWith("2d-only-beta1")) {
            string la = TestMode ? TestLocalAppData : Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (String.IsNullOrEmpty(la)) return;
            ulong h = 14695981039346656037UL;
            foreach (char c0 in Path.Combine(root, "San14_2D") + "\\") { char c = c0 == '/' ? '\\' : c0; if (c >= 'A' && c <= 'Z') c = (char)(c + 32); h ^= c; h *= 1099511628211UL; }
            string marker = Path.Combine(la, "San14_2D", "beta2-" + h.ToString("x16"), "San14_2D.ini"), old = Path.Combine(la, "San14_2D", "San14_2D.ini");
            if (File.Exists(marker) || !File.Exists(old)) return;
            NoLinks(old);
            string sh = Sha(old), dst = Safe(stage, SettingsPath);
            File.Copy(old, dst, true);
            if (Sha(dst) != sh || Sha(old) != sh) throw new IOException("이전 개인 설정이 복사 중 변경되었습니다.");
            var f = m.files.Single(x => x.path == SettingsPath); f.sha256 = sh; f.length = new FileInfo(dst).Length;
        }
    }

    // ---------------- 풀기 (안 바뀐 파일은 연결, 바뀐 것만 여러 스레드로) ----------------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr sa);
    bool TryLink(string dst, string src) {
        if (NoHardLinks || KeepPreviousBackup) return false;
        try { return CreateHardLink(dst, src, IntPtr.Zero); } catch (DllNotFoundException) { NoHardLinks = true; } catch (EntryPointNotFoundException) { NoHardLinks = true; }
        return false;
    }
    public string ArchivePath(string key) { return ArchivePath(key, -1); }
    // 이 폴더 → 옆 beta3 폴더 → 옆의 다른 폴더들(최근 것부터) 중 크기가 같은 것. 다른 폴더 것은 읽기만 함
    public string ArchivePath(string key, long len) {
        string p = Path.Combine(package, key);
        if (File.Exists(p)) return p;
        string up = Path.GetFullPath(Path.Combine(package, "..", Official, key));
        if (File.Exists(up) && (len < 0 || new FileInfo(up).Length == len)) return up;
        if (len > 0) {
            try {
                string parent = Path.GetFullPath(Path.Combine(package, ".."));
                string me = Path.GetFullPath(package).TrimEnd(Path.DirectorySeparatorChar);
                foreach (var d in Directory.GetDirectories(parent).OrderByDescending(x => Directory.GetLastWriteTimeUtc(x))) {
                    if (string.Equals(Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar), me, StringComparison.OrdinalIgnoreCase)) continue;
                    string c = Path.Combine(d, key);
                    if (File.Exists(c) && new FileInfo(c).Length == len) return c;
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return p;
    }
    void Extract(string root, string stage, PayloadManifest m, Dictionary<string, FileRec> snap) {
        long total = Math.Max(1, m.files.Sum(f => f.length)), done = 0;
        var todo = new List<PayloadFile>();
        // 1) 기존 설치에 같은 파일(같은 해시·크기)이 있으면 연결/복사
        foreach (var f in m.files) {
            ThrowIfCancelled();
            FileRec r;
            if (f.path != "d3d11.dll" && f.path != SettingsPath && snap.TryGetValue(f.path, out r) && r.Sha == f.sha256 && r.Len == f.length) {
                string src = Safe(root, f.path), dst = Safe(stage, f.path);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (TryLink(dst, src)) Stat.Linked++;
                else { File.Copy(src, dst, false); Stat.Copied++; }
                done += f.length;
                if ((Stat.Linked + Stat.Copied) % 200 == 0) Progress("안 바뀐 파일 재사용 " + (Stat.Linked + Stat.Copied), (int)(done * 95 / total));
            } else todo.Add(f);
        }
        // 2) 바뀐/새 파일만 압축에서 풀기
        foreach (var g in todo.GroupBy(f => f.archive)) {
            ThrowIfCancelled();
            var info = m.archives[g.Key];
            string ap = ArchivePath(g.Key, info.length);
            if ((!File.Exists(ap) || new FileInfo(ap).Length != info.length) && FetchArchive != null) ap = FetchArchive(g.Key, info.length, info.sha256) ?? ap;   // 깃허브 설치기: 없으면 받아 옴
            if (!File.Exists(ap) || new FileInfo(ap).Length != info.length) throw new IOException(g.Key + "가 없거나 다른 버전입니다.");
            if (g.Key == "core.zip" && Sha(ap) != info.sha256) throw new IOException(g.Key + "가 손상되었거나 다른 버전입니다.");
            var list = g.ToList();
            using (var fs0 = new FileStream(ap, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var z0 = new ZipArchive(fs0, ZipArchiveMode.Read)) {
                if (z0.Entries.Count > 20000 || z0.Entries.Select(e => e.FullName).Distinct().Count() != z0.Entries.Count) throw new IOException("압축 파일의 항목 목록이 잘못되었습니다.");
            }
            int nt = list.Count >= 64 ? Threads : 1; Exception err = null; long[] doneBytes = new long[nt];
            var ths = new List<Thread>();
            for (int t = 0; t < nt; t++) {
                int ti = t;
                var th = new Thread(() => {
                    try {
                        using (var fs = new FileStream(ap, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                        using (var z = new ZipArchive(fs, ZipArchiveMode.Read)) {
                            byte[] buf = new byte[1 << 20];
                            for (int i = ti; i < list.Count; i += nt) {
                                if (err != null || Cancelled()) return;
                                var f = list[i];
                                var e = z.GetEntry(f.blob);
                                if (e == null || e.Length != f.length) throw new IOException("누락된 압축 데이터: " + f.path);
                                string dst = Safe(stage, f.path);
                                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                                using (var s = e.Open())
                                using (var o = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                                using (var h = SHA256.Create()) {
                                    long n = 0; int k;
                                    while ((k = s.Read(buf, 0, buf.Length)) > 0) {
                                        n += k; if (n > f.length) throw new IOException("압축 해제 크기가 일치하지 않습니다.");
                                        o.Write(buf, 0, k); h.TransformBlock(buf, 0, k, buf, 0);
                                    }
                                    h.TransformFinalBlock(buf, 0, 0);
                                    if (n != f.length || Hex(h.Hash) != f.sha256) throw new IOException("스프라이트 해시 불일치: " + f.path);
                                }
                                Interlocked.Add(ref doneBytes[ti], f.length);
                            }
                        }
                    } catch (Exception ex) { if (err == null) err = ex; }
                });
                th.IsBackground = true; th.Start(); ths.Add(th);
            }
            while (ths.Any(x => x.IsAlive)) {
                Thread.Sleep(100);
                long d = done + doneBytes.Sum();
                Progress(g.Key + " 풀기", (int)(d * 95 / total));
            }
            if (err != null) throw err is IOException ? err : new IOException(err.Message, err);
            ThrowIfCancelled();
            done += doneBytes.Sum(); Stat.Extracted += list.Count; Stat.ExtractedBytes += doneBytes.Sum();
        }
    }

    // ---------------- 작업 기록·바꿔 끼우기 (옛 설치기와 같음) ----------------
    Transaction Begin(string root, string newDll, string restoreSource) {
        if (File.Exists(Journal(root))) throw new IOException("중단된 설치가 있습니다. 먼저 '중단 복구'를 실행하세요.");
        var tx = new Transaction { id = Guid.NewGuid().ToString("N"), newDllSha = newDll, hadPrevious = File.Exists(StatePath(root)), restoreSource = restoreSource };
        if (!String.IsNullOrEmpty(restoreSource)) { var s = State(Backup(root, restoreSource)); tx.restoreStateId = s.id; tx.restorePreviousBackup = s.previousBackup; }
        // 설치 기록 없이 설정만 남은 San14_2D 폴더는 백업으로 옮기고 진행 (옛 설치기 패치)
        if (!tx.hadPrevious && Directory.Exists(Path.Combine(root, "San14_2D"))) {
            string b = Backup(root, tx.id); Directory.CreateDirectory(b);
            Directory.Move(Path.Combine(root, "San14_2D"), Path.Combine(b, "San14_2D"));
        }
        string st = Stage(root, tx.id);
        Directory.CreateDirectory(st);
        File.WriteAllText(Path.Combine(st, ".owner"), tx.id);
        WriteJson(Journal(root), tx);
        return tx;
    }
    void Commit(string root, Transaction tx, InstalledState ns) {
        GameClosed(); ThrowIfCancelled();
        using (new FileStream(Path.Combine(root, "SAN14CE_KO.exe"), FileMode.Open, FileAccess.Read, FileShare.Read)) {
            string st = Stage(root, tx.id), bk = Backup(root, tx.id), dll = Safe(root, "d3d11.dll"), dir = Path.Combine(root, "San14_2D");
            ns.id = tx.id; ns.previousBackup = tx.hadPrevious ? tx.id : null;
            WriteJson(StatePath(st), ns);
            Directory.CreateDirectory(bk);
            Checkpoint("before-swap");
            if (tx.hadPrevious) { File.Move(dll, Path.Combine(bk, "d3d11.dll")); Directory.Move(dir, Path.Combine(bk, "San14_2D")); }
            Checkpoint("after-backup");
            Directory.Move(Path.Combine(st, "San14_2D"), dir);
            Checkpoint("after-assets");
            File.Move(Path.Combine(st, "d3d11.dll"), dll);
            Checkpoint("after-dll");
            tx.committed = true; WriteJson(Journal(root), tx);
            Checkpoint("after-commit");
            FinishCommitted(root, tx);
        }
    }
    void FinishCommitted(string root, Transaction tx) {
        var s = State(root);
        if (s.id != tx.id || Sha(Safe(root, "d3d11.dll")) != tx.newDllSha) throw new IOException("갱신 완료 상태가 다른 작업에서 변경되었습니다. 파일을 보존했습니다.");
        if (tx.cleanupBackup && tx.hadPrevious) {
            string b = Backup(root, tx.id);
            if (File.Exists(StatePath(b))) {
                var old = State(b); int n = 0;
                foreach (var f in old.files) {
                    string p = Safe(b, f.path);
                    if (File.Exists(p) && FastEq(p, f)) File.Delete(p);
                    if ((++n & 127) == 0 || n == old.files.Count) Progress("이전 대용량 파일 정리 " + n + "/" + old.files.Count, 99);
                    Checkpoint("during-cleanup");
                }
            }
            s.previousBackup = null; WriteJson(StatePath(root), s);
        }
        if (!tx.hadPrevious) {      // 설치 기록 없이 옮겨 둔 개인 설정 되살리기 (옛 설치기 패치)
            string ini = Path.Combine(Backup(root, tx.id), "San14_2D", "San14_2D.ini");
            if (File.Exists(ini)) File.Copy(ini, Path.Combine(root, "San14_2D", "San14_2D.ini"), true);
        }
        DeleteOwnedStage(Stage(root, tx.id), tx.id);
        File.Delete(Journal(root));
        // 다음 갱신이 빠르도록 지금 설치된 파일의 크기·시각·해시를 기억
        try { LoadCache(root); foreach (var f in s.files) { string p = Safe(root, f.path); if (File.Exists(p) && f.path != SettingsPath) Remember(p, f.path, f.sha256); } SaveCache(); } catch { }
        Progress("업데이트 완료", 100);
    }
    // 정리 단계: 백업 쪽 파일이 기록과 같은지(크기·시각이 기억과 같으면 해시 생략)
    Dictionary<string, FileRec> lastSnap;
    bool FastEq(string p, PayloadFile f) {
        var fi = new FileInfo(p);
        FileRec r;
        if (lastSnap != null && lastSnap.TryGetValue(f.path, out r) && r.Len == fi.Length && r.Ticks == fi.LastWriteTimeUtc.Ticks) return r.Sha == f.sha256;
        return fi.Length == f.length && Sha(p) == f.sha256;
    }
    static void DeleteOwnedStage(string st, string id) {
        if (!Directory.Exists(st)) return;
        NoLinks(st);
        if (Path.GetFileName(st) != ".San14_2D-stage-" + id || File.ReadAllText(Path.Combine(st, ".owner")) != id) throw new IOException("임시 폴더 소유권을 확인할 수 없습니다.");
        var stack = new Stack<string>(); stack.Push(st);
        while (stack.Count > 0) foreach (var e in Directory.EnumerateFileSystemEntries(stack.Pop())) { NoLinks(e); if (Directory.Exists(e)) stack.Push(e); }
        Directory.Delete(st, true);
    }

    // ---------------- 작업 ----------------
    public void Install(string folder) {
        Stat.Clock.Restart();
        string root = Root(folder);
        using (Lock(root)) {
            GameClosed();
            if (File.Exists(Journal(root))) throw new IOException("중단된 설치가 있습니다. 먼저 '중단 복구'를 실행하세요.");
            var m = Manifest(root);
            string exe = Path.Combine(root, m.executable); NoLinks(exe);
            if (!File.Exists(exe) || Sha(exe) != m.executableSha256) throw new IOException("지원하는 실행 파일과 다릅니다. SAN14CE_KO.exe의 정확한 SHA-256이 일치해야 합니다. 원본 파일은 변경하지 않았습니다.");
            CheckExisting(root);
            if (RequirePrevious && !File.Exists(StatePath(root))) throw new IOException("이 파일은 기존 2D 독립형 배포판의 업데이트 전용입니다. 이전 배포판이 설치된 게임 폴더를 선택하세요.");
            if (File.Exists(StatePath(root)) && !TestMode) {
                string rel = State(root).release;
                if (rel == null || !(rel.StartsWith("2d-only-beta1") || rel.StartsWith("2d-only-beta2") || rel.StartsWith("2d-update-beta3") || rel.StartsWith("2d-unified-beta3")))
                    throw new IOException("지원하는 이전 2D 배포판의 설치 기록이 아닙니다.");
            }
            Progress("기존 파일과 개인 설정 검사", 0);
            LoadCache(root);
            var snap = Snapshot(root); lastSnap = snap;
            // 필요한 여유 공간: 새로 풀 파일 + 여유 512MB (연결로 재사용하는 파일은 공간이 거의 안 듦)
            long need = m.files.Where(f => { FileRec r; return !(snap.TryGetValue(f.path, out r) && r.Sha == f.sha256 && r.Len == f.length) || KeepPreviousBackup; }).Sum(f => f.length) + 536870912L;
            if (!TestMode && new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace < need) throw new IOException("설치에 필요한 여유 공간: " + Math.Ceiling(need / 1073741824.0) + " GiB");
            Transaction tx = null;
            try {
                tx = Begin(root, m.files.Single(f => f.path == "d3d11.dll").sha256, null);
                tx.cleanupBackup = !KeepPreviousBackup;
                WriteJson(Journal(root), tx);
                string st = Stage(root, tx.id);
                Extract(root, st, m, snap);
                Checkpoint("after-extract");
                Preserve(root, st, m, snap);
                CheckSnapshot(root, snap);
                SaveCache();
                Commit(root, tx, new InstalledState { release = m.release, files = m.files });
            } catch {
                if (File.Exists(Journal(root))) RecoverInternal(root);
                throw;
            }
        }
        Stat.Clock.Stop();
    }
    void RecoverInternal(string root) {
        var tx = ReadJson<Transaction>(Journal(root));
        if (tx == null || !ValidId(tx.id) || !IsSha(tx.newDllSha) || (!String.IsNullOrEmpty(tx.restoreSource) && !ValidId(tx.restoreSource))) throw new IOException("복구 기록을 검증할 수 없습니다.");
        if (tx.committed) { FinishCommitted(root, tx); return; }
        string st = Stage(root, tx.id), bk = Backup(root, tx.id), dll = Safe(root, "d3d11.dll"), dir = Path.Combine(root, "San14_2D"), bdll = Path.Combine(bk, "d3d11.dll"), bdir = Path.Combine(bk, "San14_2D");
        bool ours = File.Exists(StatePath(root)) && State(root).id == tx.id;
        if (Directory.Exists(bdir) || !tx.hadPrevious) {
            if (Directory.Exists(dir)) {
                if (ours) Directory.Move(dir, Path.Combine(st, "San14_2D"));
                else if (tx.hadPrevious) throw new IOException("설치 중 다른 파일이 변경되어 자동 복구를 멈췄습니다.");
            }
            if (Directory.Exists(bdir) && !Directory.Exists(dir)) Directory.Move(bdir, dir);
        }
        if (File.Exists(bdll) || !tx.hadPrevious) {
            if (File.Exists(dll)) {
                if (Sha(dll) != tx.newDllSha) throw new IOException("d3d11.dll이 다른 프로그램에서 변경되어 보존했습니다.");
                File.Move(dll, Path.Combine(st, "d3d11.dll"));
            }
            if (File.Exists(bdll)) File.Move(bdll, dll);
        }
        if (!String.IsNullOrEmpty(tx.restoreSource)) {
            string rs = Backup(root, tx.restoreSource);
            if (Directory.Exists(Path.Combine(st, "San14_2D"))) {
                if (!ValidId(tx.restoreStateId) || (!String.IsNullOrEmpty(tx.restorePreviousBackup) && !ValidId(tx.restorePreviousBackup))) throw new IOException("이전 버전 복구 기록이 잘못되었습니다.");
                var s = State(st); s.id = tx.restoreStateId; s.previousBackup = tx.restorePreviousBackup;
                WriteJson(StatePath(st), s);
                Directory.Move(Path.Combine(st, "San14_2D"), Path.Combine(rs, "San14_2D"));
            }
            if (File.Exists(Path.Combine(st, "d3d11.dll"))) File.Move(Path.Combine(st, "d3d11.dll"), Path.Combine(rs, "d3d11.dll"));
        }
        File.Delete(Journal(root));
        DeleteOwnedStage(st, tx.id);
        Progress("중단 전 상태로 복구됨", 100);
    }
    public void Recover(string folder) {
        string root = Root(folder);
        using (Lock(root)) { GameClosed(); if (!File.Exists(Journal(root))) throw new IOException("중단된 설치가 없습니다."); RecoverInternal(root); }
    }
    void VerifyState(string root, InstalledState s, bool skipMissing, bool full) {
        int n = 0;
        foreach (var f in s.files) {
            ThrowIfCancelled();
            if ((++n & 63) == 0) Progress("파일 검사 " + n + "/" + s.files.Count, n * 99 / s.files.Count);
            if (f.path == SettingsPath) continue;
            string p = Safe(root, f.path);
            if (skipMissing && !File.Exists(p)) continue;
            if (!File.Exists(p) || new FileInfo(p).Length != f.length || (full ? Sha(p) : Rec(p, f.path, false).Sha) != f.sha256) throw new IOException("변경되거나 누락된 파일: " + f.path);
        }
    }
    public void Verify(string folder) {
        string root = Root(folder);
        using (Lock(root)) {
            LoadCache(root);
            var s = State(root);
            VerifyState(root, s, false, true);              // 파일 검사는 언제나 끝까지 읽어서 확인
            foreach (var f in s.files) if (f.path != SettingsPath) Remember(Safe(root, f.path), f.path, f.sha256);
            SaveCache();
            Progress("설치 파일 전체 검사 통과", 100);
        }
    }
    public void Rollback(string folder) {
        string root = Root(folder);
        using (Lock(root)) {
            GameClosed();
            var s = State(root);
            if (String.IsNullOrEmpty(s.previousBackup)) throw new IOException("복구 가능한 이전 버전 백업이 없습니다. 전체 백업 유지 없이 업데이트한 경우 이전 텍스처는 정리됩니다.");
            string b = Backup(root, s.previousBackup);
            var bs = State(b);
            VerifyState(b, bs, false, true);
            var tx = Begin(root, bs.files.Single(f => f.path == "d3d11.dll").sha256, s.previousBackup);
            try {
                string st = Stage(root, tx.id);
                Directory.Move(Path.Combine(b, "San14_2D"), Path.Combine(st, "San14_2D"));
                File.Move(Path.Combine(b, "d3d11.dll"), Path.Combine(st, "d3d11.dll"));
                Commit(root, tx, bs);
            } catch {
                if (File.Exists(Journal(root))) RecoverInternal(root);
                throw;
            }
        }
    }
    public void Remove(string folder) {
        string root = Root(folder);
        using (Lock(root)) {
            GameClosed();
            if (File.Exists(Journal(root))) throw new IOException("먼저 중단된 설치를 복구하세요.");
            var s = State(root);
            LoadCache(root);
            VerifyState(root, s, s.removing, false);
            using (new FileStream(Path.Combine(root, "SAN14CE_KO.exe"), FileMode.Open, FileAccess.Read, FileShare.Read)) {
                s.removing = true; WriteJson(StatePath(root), s);
                File.Delete(Safe(root, "d3d11.dll"));
                int n = 0;
                foreach (var f in s.files.Where(f => f.path != "d3d11.dll" && f.path != SettingsPath)) {
                    File.Delete(Safe(root, f.path));
                    if ((++n & 63) == 0) Progress(f.path, n * 99 / s.files.Count);
                }
                File.Delete(StatePath(root));
                var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string top = Path.Combine(root, "San14_2D");
                foreach (var f in s.files.Where(f => f.path != "d3d11.dll"))
                    for (string d = Path.GetDirectoryName(Safe(root, f.path)); d.Length > top.Length; d = Path.GetDirectoryName(d)) dirs.Add(d);
                foreach (var d in dirs.OrderByDescending(x => x.Length))
                    if (Directory.Exists(d)) { NoLinks(d); if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); }
                if (!Directory.EnumerateFileSystemEntries(top).Any()) Directory.Delete(top);
            }
            try { File.Delete(CachePath(root)); } catch { }
            Progress("2D 패치 제거 완료. 개인 설정과 이전 버전 백업은 보존했습니다.", 100);
        }
    }
    public bool Installed(string folder) { try { return File.Exists(StatePath(Path.GetFullPath(folder))); } catch { return false; } }
}
