// =============================================================================
// Hekatan Python3 — Métodos builtin de tipos nativos (str, list, dict, set)
// =============================================================================
//   GetMethod(obj, name) devuelve un PyBuiltin con Self ligado, o null.
//   Por convención args[0] == self (CallCallable antepone Self).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Calcpad.Core.Python
{
    public static class PythonBuiltinMethods
    {
        public static PyBuiltin GetMethod(object obj, string name, PythonEvaluator ev)
        {
            PyBuiltin Make(Func<object[], PyDict, object> fn) => new PyBuiltin(name, fn) { Self = obj };

            switch (obj)
            {
                case string: { var fn = StrMethod(name, ev); return fn == null ? null : Make(fn); }
                case PyList: { var fn = ListMethod(name, ev); return fn == null ? null : Make(fn); }
                case PyDict: { var fn = DictMethod(name, ev); return fn == null ? null : Make(fn); }
                case PySet: { var fn = SetMethod(name, ev); return fn == null ? null : Make(fn); }
                case PyTuple: { var fn = TupleMethod(name, ev); return fn == null ? null : Make(fn); }
            }
            return null;
        }

        // ── STRING ──
        private static Func<object[], PyDict, object> StrMethod(string name, PythonEvaluator ev)
        {
            string S(object[] a) => (string)a[0];
            switch (name)
            {
                case "upper": return (a, kw) => S(a).ToUpperInvariant();
                case "lower": return (a, kw) => S(a).ToLowerInvariant();
                case "strip": return (a, kw) => a.Length > 1 ? S(a).Trim(((string)a[1]).ToCharArray()) : S(a).Trim();
                case "lstrip": return (a, kw) => a.Length > 1 ? S(a).TrimStart(((string)a[1]).ToCharArray()) : S(a).TrimStart();
                case "rstrip": return (a, kw) => a.Length > 1 ? S(a).TrimEnd(((string)a[1]).ToCharArray()) : S(a).TrimEnd();
                case "capitalize": return (a, kw) => { var s = S(a); return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant(); };
                case "title": return (a, kw) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(S(a).ToLowerInvariant());
                case "swapcase": return (a, kw) => SwapCase(S(a));
                case "split": return (a, kw) => Split(S(a), a, false);
                case "rsplit": return (a, kw) => Split(S(a), a, true);
                case "splitlines": return (a, kw) => { var r = new PyList(); foreach (var ln in S(a).Split('\n')) r.Items.Add(ln.TrimEnd('\r')); if (S(a).EndsWith("\n")) r.Items.RemoveAt(r.Count - 1); return r; };
                case "join": return (a, kw) =>
                {
                    var sb = new StringBuilder(); bool first = true;
                    foreach (var x in ev.Iterate(a[1])) { if (!first) sb.Append(S(a)); sb.Append(PyOps.Str(x)); first = false; }
                    return sb.ToString();
                };
                case "replace": return (a, kw) => a.Length >= 4 ? ReplaceN(S(a), (string)a[1], (string)a[2], (int)PyOps.ToLong(a[3])) : S(a).Replace((string)a[1], (string)a[2]);
                case "startswith": return (a, kw) => a[1] is PyTuple t ? StartsAny(S(a), t, true) : S(a).StartsWith((string)a[1], StringComparison.Ordinal);
                case "endswith": return (a, kw) => a[1] is PyTuple t ? StartsAny(S(a), t, false) : S(a).EndsWith((string)a[1], StringComparison.Ordinal);
                case "find": return (a, kw) => (long)S(a).IndexOf((string)a[1], StringComparison.Ordinal);
                case "rfind": return (a, kw) => (long)S(a).LastIndexOf((string)a[1], StringComparison.Ordinal);
                case "index": return (a, kw) => { int i = S(a).IndexOf((string)a[1], StringComparison.Ordinal); if (i < 0) throw new PyRuntimeError("ValueError", "substring not found"); return (long)i; };
                case "count": return (a, kw) => (long)CountSub(S(a), (string)a[1]);
                case "zfill": return (a, kw) => Zfill(S(a), (int)PyOps.ToLong(a[1]));
                case "ljust": return (a, kw) => S(a).PadRight((int)PyOps.ToLong(a[1]), a.Length >= 3 ? ((string)a[2])[0] : ' ');
                case "rjust": return (a, kw) => S(a).PadLeft((int)PyOps.ToLong(a[1]), a.Length >= 3 ? ((string)a[2])[0] : ' ');
                case "center": return (a, kw) => Center(S(a), (int)PyOps.ToLong(a[1]), a.Length >= 3 ? ((string)a[2])[0] : ' ');
                case "isdigit": return (a, kw) => S(a).Length > 0 && AllChars(S(a), char.IsDigit);
                case "isalpha": return (a, kw) => S(a).Length > 0 && AllChars(S(a), char.IsLetter);
                case "isalnum": return (a, kw) => S(a).Length > 0 && AllChars(S(a), char.IsLetterOrDigit);
                case "isspace": return (a, kw) => S(a).Length > 0 && AllChars(S(a), char.IsWhiteSpace);
                case "isupper": return (a, kw) => S(a) == S(a).ToUpperInvariant() && S(a) != S(a).ToLowerInvariant();
                case "islower": return (a, kw) => S(a) == S(a).ToLowerInvariant() && S(a) != S(a).ToUpperInvariant();
                case "format": return (a, kw) => StrFormat(S(a), a, kw);
                case "encode": return (a, kw) => S(a); // MVP: tratamos bytes como str
                case "removeprefix": return (a, kw) => { var s = S(a); var p = (string)a[1]; return s.StartsWith(p, StringComparison.Ordinal) ? s.Substring(p.Length) : s; };
                case "removesuffix": return (a, kw) => { var s = S(a); var p = (string)a[1]; return s.EndsWith(p, StringComparison.Ordinal) ? s.Substring(0, s.Length - p.Length) : s; };
            }
            return null;
        }

        // ── LIST ──
        private static Func<object[], PyDict, object> ListMethod(string name, PythonEvaluator ev)
        {
            List<object> L(object[] a) => ((PyList)a[0]).Items;
            switch (name)
            {
                case "append": return (a, kw) => { L(a).Add(a[1]); return null; };
                case "extend": return (a, kw) => { foreach (var x in ev.Iterate(a[1])) L(a).Add(x); return null; };
                case "insert": return (a, kw) => { int i = (int)PyOps.ToLong(a[1]); if (i < 0) i += L(a).Count; if (i < 0) i = 0; if (i > L(a).Count) i = L(a).Count; L(a).Insert(i, a[2]); return null; };
                case "pop": return (a, kw) => { var l = L(a); int i = a.Length >= 2 ? PythonEvaluator.NormIndex(PyOps.ToLong(a[1]), l.Count) : l.Count - 1; if (l.Count == 0) throw new PyRuntimeError("IndexError", "pop from empty list"); var v = l[i]; l.RemoveAt(i); return v; };
                case "remove": return (a, kw) => { var l = L(a); for (int i = 0; i < l.Count; i++) if (PyOps.Equal(l[i], a[1])) { l.RemoveAt(i); return null; } throw new PyRuntimeError("ValueError", "list.remove(x): x not in list"); };
                case "index": return (a, kw) => { var l = L(a); for (int i = 0; i < l.Count; i++) if (PyOps.Equal(l[i], a[1])) return (long)i; throw new PyRuntimeError("ValueError", $"{PyOps.Repr(a[1])} is not in list"); };
                case "count": return (a, kw) => { long c = 0; foreach (var x in L(a)) if (PyOps.Equal(x, a[1])) c++; return c; };
                case "reverse": return (a, kw) => { L(a).Reverse(); return null; };
                case "clear": return (a, kw) => { L(a).Clear(); return null; };
                case "copy": return (a, kw) => new PyList(L(a));
                case "sort": return (a, kw) =>
                {
                    var l = L(a);
                    bool rev = kw != null && kw.TryGet("reverse", out var rv) && PyOps.Truthy(rv);
                    object keyFn = null; kw?.TryGet("key", out keyFn);
                    l.Sort((x, y) =>
                    {
                        object kx = keyFn != null ? ev.CallCallable(keyFn, new[] { x }, null) : x;
                        object ky = keyFn != null ? ev.CallCallable(keyFn, new[] { y }, null) : y;
                        return PyOps.Compare(kx, ky);
                    });
                    if (rev) l.Reverse();
                    return null;
                };
            }
            return null;
        }

        // ── DICT ──
        private static Func<object[], PyDict, object> DictMethod(string name, PythonEvaluator ev)
        {
            PyDict D(object[] a) => (PyDict)a[0];
            switch (name)
            {
                case "keys": return (a, kw) => new PyList(D(a).Keys);
                case "values": return (a, kw) => new PyList(D(a).Values);
                case "items": return (a, kw) => { var r = new PyList(); var d = D(a); for (int i = 0; i < d.Keys.Count; i++) r.Items.Add(new PyTuple(new List<object> { d.Keys[i], d.Values[i] })); return r; };
                case "get": return (a, kw) => D(a).TryGet(a[1], out var v) ? v : (a.Length >= 3 ? a[2] : null);
                case "pop": return (a, kw) => { var d = D(a); if (d.TryGet(a[1], out var v)) { d.Remove(a[1]); return v; } if (a.Length >= 3) return a[2]; throw new PyRuntimeError("KeyError", PyOps.Repr(a[1])); };
                case "setdefault": return (a, kw) => { var d = D(a); if (d.TryGet(a[1], out var v)) return v; var def = a.Length >= 3 ? a[2] : null; d.Set(a[1], def); return def; };
                case "update": return (a, kw) => { var d = D(a); if (a.Length >= 2 && a[1] is PyDict src) for (int i = 0; i < src.Keys.Count; i++) d.Set(src.Keys[i], src.Values[i]); if (kw != null) for (int i = 0; i < kw.Keys.Count; i++) d.Set(kw.Keys[i], kw.Values[i]); return null; };
                case "clear": return (a, kw) => { var d = D(a); d.Keys.Clear(); d.Values.Clear(); return null; };
                case "copy": return (a, kw) => { var d = D(a); var r = new PyDict(); for (int i = 0; i < d.Keys.Count; i++) r.Set(d.Keys[i], d.Values[i]); return r; };
            }
            return null;
        }

        // ── SET ──
        private static Func<object[], PyDict, object> SetMethod(string name, PythonEvaluator ev)
        {
            PySet St(object[] a) => (PySet)a[0];
            switch (name)
            {
                case "add": return (a, kw) => { St(a).Add(a[1]); return null; };
                case "discard": return (a, kw) => { var s = St(a); for (int i = 0; i < s.Items.Count; i++) if (PyOps.Equal(s.Items[i], a[1])) { s.Items.RemoveAt(i); break; } return null; };
                case "remove": return (a, kw) => { var s = St(a); for (int i = 0; i < s.Items.Count; i++) if (PyOps.Equal(s.Items[i], a[1])) { s.Items.RemoveAt(i); return null; } throw new PyRuntimeError("KeyError", PyOps.Repr(a[1])); };
                case "clear": return (a, kw) => { St(a).Items.Clear(); return null; };
                case "copy": return (a, kw) => { var r = new PySet(); foreach (var x in St(a).Items) r.Add(x); return r; };
                case "union": return (a, kw) => { var r = new PySet(); foreach (var x in St(a).Items) r.Add(x); for (int i = 1; i < a.Length; i++) foreach (var x in ev.Iterate(a[i])) r.Add(x); return r; };
                case "intersection": return (a, kw) => { var r = new PySet(); foreach (var x in St(a).Items) { bool all = true; for (int i = 1; i < a.Length; i++) { bool found = false; foreach (var y in ev.Iterate(a[i])) if (PyOps.Equal(x, y)) { found = true; break; } if (!found) { all = false; break; } } if (all) r.Add(x); } return r; };
                case "difference": return (a, kw) => { var r = new PySet(); foreach (var x in St(a).Items) { bool inOther = false; for (int i = 1; i < a.Length; i++) foreach (var y in ev.Iterate(a[i])) if (PyOps.Equal(x, y)) { inOther = true; break; } if (!inOther) r.Add(x); } return r; };
                case "pop": return (a, kw) => { var s = St(a); if (s.Count == 0) throw new PyRuntimeError("KeyError", "pop from an empty set"); var v = s.Items[0]; s.Items.RemoveAt(0); return v; };
                case "update": return (a, kw) => { var s = St(a); for (int i = 1; i < a.Length; i++) foreach (var x in ev.Iterate(a[i])) s.Add(x); return null; };
            }
            return null;
        }

        // ── TUPLE ──
        private static Func<object[], PyDict, object> TupleMethod(string name, PythonEvaluator ev)
        {
            List<object> T(object[] a) => ((PyTuple)a[0]).Items;
            switch (name)
            {
                case "count": return (a, kw) => { long c = 0; foreach (var x in T(a)) if (PyOps.Equal(x, a[1])) c++; return c; };
                case "index": return (a, kw) => { var t = T(a); for (int i = 0; i < t.Count; i++) if (PyOps.Equal(t[i], a[1])) return (long)i; throw new PyRuntimeError("ValueError", "tuple.index(x): x not in tuple"); };
            }
            return null;
        }

        // ── Helpers ──
        private static bool AllChars(string s, Func<char, bool> pred) { foreach (var c in s) if (!pred(c)) return false; return true; }
        private static string SwapCase(string s) { var sb = new StringBuilder(); foreach (var c in s) sb.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)); return sb.ToString(); }
        private static int CountSub(string s, string sub)
        {
            if (sub.Length == 0) return s.Length + 1;
            int c = 0, i = 0;
            while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { c++; i += sub.Length; }
            return c;
        }
        private static string ReplaceN(string s, string from, string to, int n)
        {
            if (n < 0) return s.Replace(from, to);
            var sb = new StringBuilder(); int i = 0, done = 0;
            while (done < n)
            {
                int j = s.IndexOf(from, i, StringComparison.Ordinal);
                if (j < 0) break;
                sb.Append(s, i, j - i).Append(to);
                i = j + from.Length; done++;
            }
            sb.Append(s.Substring(i));
            return sb.ToString();
        }
        private static string Zfill(string s, int width)
        {
            bool neg = s.StartsWith("-") || s.StartsWith("+");
            string sign = neg ? s.Substring(0, 1) : "";
            string body = neg ? s.Substring(1) : s;
            int pad = width - s.Length;
            if (pad <= 0) return s;
            return sign + new string('0', pad) + body;
        }
        private static string Center(string s, int width, char fill)
        {
            int pad = width - s.Length;
            if (pad <= 0) return s;
            int left = pad / 2;
            return new string(fill, left) + s + new string(fill, pad - left);
        }
        private static PyList Split(string s, object[] a, bool right)
        {
            var r = new PyList();
            string sep = a.Length >= 2 && a[1] != null ? (string)a[1] : null;
            int maxsplit = a.Length >= 3 ? (int)PyOps.ToLong(a[2]) : -1;
            if (sep == null)
            {
                var parts = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts) r.Items.Add(p);
                return r;
            }
            var arr = s.Split(new[] { sep }, StringSplitOptions.None);
            foreach (var p in arr) r.Items.Add(p);
            return r;
        }
        private static bool StartsAny(string s, PyTuple t, bool start)
        {
            foreach (var x in t.Items)
                if (start ? s.StartsWith((string)x, StringComparison.Ordinal) : s.EndsWith((string)x, StringComparison.Ordinal)) return true;
            return false;
        }

        // str.format con campos {}, {0}, {name}, {:spec}
        private static string StrFormat(string fmt, object[] a, PyDict kw)
        {
            var sb = new StringBuilder();
            int auto = 0;
            for (int i = 0; i < fmt.Length; i++)
            {
                char c = fmt[i];
                if (c == '{')
                {
                    if (i + 1 < fmt.Length && fmt[i + 1] == '{') { sb.Append('{'); i++; continue; }
                    int j = fmt.IndexOf('}', i);
                    if (j < 0) { sb.Append(fmt.Substring(i)); break; }
                    string field = fmt.Substring(i + 1, j - i - 1);
                    string spec = "";
                    int colon = field.IndexOf(':');
                    if (colon >= 0) { spec = field.Substring(colon + 1); field = field.Substring(0, colon); }
                    object val;
                    if (field.Length == 0) val = a[1 + auto++];
                    else if (int.TryParse(field, out int idx)) val = a[1 + idx];
                    else if (kw != null && kw.TryGet(field, out var kv)) val = kv;
                    else val = null;
                    sb.Append(string.IsNullOrEmpty(spec) ? PyOps.Str(val) : PyStringFormat.FormatSpec(val, spec));
                    i = j;
                }
                else if (c == '}')
                {
                    if (i + 1 < fmt.Length && fmt[i + 1] == '}') { sb.Append('}'); i++; continue; }
                    sb.Append('}');
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
