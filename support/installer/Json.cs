// 작은 JSON 읽기/쓰기 (옛 설치기의 JavaScriptSerializer 와 같은 모양: 공개 필드 이름 그대로)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

public sealed class MiniJson {
    public int MaxJsonLength = 16777216, RecursionLimit = 30;
    // ---- 쓰기
    public string Serialize(object o) { var b = new StringBuilder(); W(b, o, 0); return b.ToString(); }
    void W(StringBuilder b, object o, int depth) {
        if (depth > RecursionLimit) throw new InvalidOperationException("JSON 깊이 초과");
        if (o == null) { b.Append("null"); return; }
        var t = o.GetType();
        if (o is string) { Str(b, (string)o); return; }
        if (o is bool) { b.Append((bool)o ? "true" : "false"); return; }
        if (o is int || o is long || o is short || o is uint || o is ulong) { b.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); return; }
        if (o is double || o is float) { b.Append(Convert.ToDouble(o).ToString("R", CultureInfo.InvariantCulture)); return; }
        if (o is IDictionary) {
            b.Append('{'); bool first = true;
            foreach (DictionaryEntry e in (IDictionary)o) { if (!first) b.Append(','); first = false; Str(b, Convert.ToString(e.Key, CultureInfo.InvariantCulture)); b.Append(':'); W(b, e.Value, depth + 1); }
            b.Append('}'); return;
        }
        if (o is IEnumerable) {
            b.Append('['); bool first = true;
            foreach (var x in (IEnumerable)o) { if (!first) b.Append(','); first = false; W(b, x, depth + 1); }
            b.Append(']'); return;
        }
        b.Append('{'); bool f1 = true;
        foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            if (!f1) b.Append(','); f1 = false;
            Str(b, fi.Name); b.Append(':'); W(b, fi.GetValue(o), depth + 1);
        }
        b.Append('}');
    }
    static void Str(StringBuilder b, string s) {
        b.Append('"');
        foreach (char c in s) {
            switch (c) {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default: if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); break;
            }
        }
        b.Append('"');
    }
    // ---- 읽기
    string s; int p;
    public T Deserialize<T>(string text) {
        if (text.Length > MaxJsonLength) throw new ArgumentException("JSON 이 너무 큽니다.");
        s = text; p = 0;
        Ws(); object v = V(0); Ws();
        if (p != s.Length) throw new ArgumentException("JSON 끝이 이상합니다.");
        return (T)Map(v, typeof(T));
    }
    void Ws() { while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n' || s[p] == '﻿')) p++; }
    object V(int depth) {
        if (depth > RecursionLimit) throw new ArgumentException("JSON 깊이 초과");
        Ws(); if (p >= s.Length) throw new ArgumentException("JSON 이 끊겼습니다.");
        char c = s[p];
        if (c == '{') {
            p++; var d = new Dictionary<string, object>(StringComparer.Ordinal); Ws();
            if (p < s.Length && s[p] == '}') { p++; return d; }
            for (;;) { Ws(); string k = S(); Ws(); Exp(':'); d[k] = V(depth + 1); Ws(); if (s[p] == ',') { p++; continue; } Exp('}'); return d; }
        }
        if (c == '[') {
            p++; var l = new List<object>(); Ws();
            if (p < s.Length && s[p] == ']') { p++; return l; }
            for (;;) { l.Add(V(depth + 1)); Ws(); if (s[p] == ',') { p++; continue; } Exp(']'); return l; }
        }
        if (c == '"') return S();
        if (s.Length - p >= 4 && String.CompareOrdinal(s, p, "true", 0, 4) == 0) { p += 4; return true; }
        if (s.Length - p >= 5 && String.CompareOrdinal(s, p, "false", 0, 5) == 0) { p += 5; return false; }
        if (s.Length - p >= 4 && String.CompareOrdinal(s, p, "null", 0, 4) == 0) { p += 4; return null; }
        int st = p;
        while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0) p++;
        if (st == p) throw new ArgumentException("JSON 값이 이상합니다.");
        string num = s.Substring(st, p - st);
        long lv; if (long.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out lv)) return lv;
        return double.Parse(num, CultureInfo.InvariantCulture);
    }
    void Exp(char c) { if (p >= s.Length || s[p] != c) throw new ArgumentException("JSON 모양이 이상합니다."); p++; }
    string S() {
        Exp('"'); var b = new StringBuilder();
        for (;;) {
            if (p >= s.Length) throw new ArgumentException("JSON 글자가 끊겼습니다.");
            char c = s[p++];
            if (c == '"') return b.ToString();
            if (c != '\\') { b.Append(c); continue; }
            char e = s[p++];
            switch (e) {
                case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break;
                case 'u': b.Append((char)Convert.ToInt32(s.Substring(p, 4), 16)); p += 4; break;
                default: b.Append(e); break;
            }
        }
    }
    static object Map(object v, Type t) {
        if (v == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
        if (t == typeof(object)) return v;
        if (t == typeof(string)) return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        if (t == typeof(bool)) return v is bool ? (bool)v : Convert.ToBoolean(v, CultureInfo.InvariantCulture);
        if (t == typeof(long)) return Convert.ToInt64(v, CultureInfo.InvariantCulture);
        if (t == typeof(int)) return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) {
            var et = t.GetGenericArguments()[0]; var l = (IList)Activator.CreateInstance(t);
            foreach (var x in (List<object>)v) l.Add(Map(x, et));
            return l;
        }
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
            var vt = t.GetGenericArguments()[1]; var d = (IDictionary)Activator.CreateInstance(t);
            foreach (var kv in (Dictionary<string, object>)v) d[kv.Key] = Map(kv.Value, vt);
            return d;
        }
        var o = Activator.CreateInstance(t); var src = (Dictionary<string, object>)v;
        foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            foreach (var kv in src) if (String.Equals(kv.Key, fi.Name, StringComparison.OrdinalIgnoreCase)) { fi.SetValue(o, Map(kv.Value, fi.FieldType)); break; }
        }
        return o;
    }
}
