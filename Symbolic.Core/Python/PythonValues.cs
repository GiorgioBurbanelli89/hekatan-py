// =============================================================================
// Hekatan Python3 — Modelos de valor del runtime Python nativo
// =============================================================================
//   Los valores Python se representan con tipos .NET:
//     int   → long          float → double        bool → bool
//     str   → string        None  → null
//     list  → PyList        tuple → PyTuple       set  → PySet
//     dict  → PyDict        function → PyFunction / PyBuiltin
//     range → PyRange       module → PyModule     class → PyClass / PyInstance
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;

namespace Calcpad.Core.Python
{
    /// <summary>Excepción para constructos Python aún no soportados por el motor
    /// nativo. El PythonPipeline la captura y cae al intérprete python real.</summary>
    public sealed class PythonNotSupported : Exception
    {
        public PythonNotSupported(string msg) : base(msg) { }
    }

    /// <summary>Error de runtime Python (equivalente a una excepción Python).</summary>
    public sealed class PyRuntimeError : Exception
    {
        public string PyType;   // "ZeroDivisionError", "TypeError", ...
        public PyRuntimeError(string type, string msg) : base(msg) { PyType = type; }
        public PyRuntimeError(string msg) : base(msg) { PyType = "Exception"; }
    }

    public sealed class PyList : IEnumerable<object>
    {
        public readonly List<object> Items;
        public PyList() { Items = new(); }
        public PyList(IEnumerable<object> src) { Items = new(src); }
        public int Count => Items.Count;
        public object this[int i] { get => Items[i]; set => Items[i] = value; }
        public IEnumerator<object> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
    }

    public sealed class PyTuple : IEnumerable<object>
    {
        public readonly List<object> Items;
        public PyTuple() { Items = new(); }
        public PyTuple(IEnumerable<object> src) { Items = new(src); }
        public int Count => Items.Count;
        public object this[int i] => Items[i];
        public IEnumerator<object> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
    }

    public sealed class PySet : IEnumerable<object>
    {
        public readonly List<object> Items = new();  // orden de inserción, igualdad por valor
        public bool Contains(object v)
        {
            foreach (var it in Items) if (PyOps.Equal(it, v)) return true;
            return false;
        }
        public void Add(object v) { if (!Contains(v)) Items.Add(v); }
        public int Count => Items.Count;
        public IEnumerator<object> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();
    }

    /// <summary>Dict con orden de inserción y comparación de claves por valor.</summary>
    public sealed class PyDict
    {
        public readonly List<object> Keys = new();
        public readonly List<object> Values = new();
        public bool DefaultZero;   // collections.Counter: clave faltante -> 0 (para d[k]+=1)
        public int Count => Keys.Count;
        public bool TryGet(object key, out object value)
        {
            for (int i = 0; i < Keys.Count; i++)
                if (PyOps.Equal(Keys[i], key)) { value = Values[i]; return true; }
            value = null; return false;
        }
        public void Set(object key, object value)
        {
            for (int i = 0; i < Keys.Count; i++)
                if (PyOps.Equal(Keys[i], key)) { Values[i] = value; return; }
            Keys.Add(key); Values.Add(value);
        }
        public bool Remove(object key)
        {
            for (int i = 0; i < Keys.Count; i++)
                if (PyOps.Equal(Keys[i], key)) { Keys.RemoveAt(i); Values.RemoveAt(i); return true; }
            return false;
        }
        public bool ContainsKey(object key) => TryGet(key, out _);
    }

    /// <summary>Objeto range (lazy): start, stop, step.</summary>
    public sealed class PyRange : IEnumerable<object>
    {
        public long Start, Stop, Step;
        public PyRange(long start, long stop, long step) { Start = start; Stop = stop; Step = step; }
        public long Length
        {
            get
            {
                if (Step == 0) return 0;
                long len = Step > 0 ? (Stop - Start + Step - 1) / Step : (Start - Stop - Step - 1) / (-Step);
                return len < 0 ? 0 : len;
            }
        }
        public IEnumerator<object> GetEnumerator()
        {
            if (Step > 0) for (long v = Start; v < Stop; v += Step) yield return v;
            else if (Step < 0) for (long v = Start; v > Stop; v += Step) yield return v;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Función definida por el usuario (def / lambda) con su closure.</summary>
    public sealed class PyFunction
    {
        public string Name;
        public List<Param> Params;
        public List<PyNode> Body;     // null si es lambda
        public PyNode Expr;           // cuerpo de lambda
        public PyScope Closure;
        public PyClass OwnerClass;    // si es método
    }

    /// <summary>Función nativa (builtin) implementada en C#.</summary>
    public sealed class PyBuiltin
    {
        public string Name;
        public Func<object[], PyDict, object> Invoke;  // (args, kwargs) → result
        public object Self;                            // método ligado (lista, str, dict, ...)
        public PyBuiltin(string name, Func<object[], PyDict, object> fn) { Name = name; Invoke = fn; }
    }

    /// <summary>Módulo nativo (math, etc.): namespace de atributos.</summary>
    public sealed class PyModule
    {
        public string Name;
        public readonly Dictionary<string, object> Attrs = new(StringComparer.Ordinal);
        public PyModule(string name) { Name = name; }
    }

    public sealed class PyClass
    {
        public string Name;
        public List<PyClass> Bases = new();
        public readonly Dictionary<string, object> Attrs = new(StringComparer.Ordinal);
        public bool TryLookup(string name, out object val)
        {
            if (Attrs.TryGetValue(name, out val)) return true;
            foreach (var b in Bases) if (b.TryLookup(name, out val)) return true;
            val = null; return false;
        }
    }

    public sealed class PyInstance
    {
        public PyClass Class;
        public readonly Dictionary<string, object> Attrs = new(StringComparer.Ordinal);
    }

    /// <summary>Scope de variables con cadena de padres (closures / globals).</summary>
    public sealed class PyScope
    {
        public readonly Dictionary<string, object> Vars = new(StringComparer.Ordinal);
        public readonly PyScope Parent;
        public readonly PyScope Globals;   // referencia directa al módulo global
        public HashSet<string> GlobalNames;  // nombres declarados `global`

        public PyScope(PyScope parent, PyScope globals)
        {
            Parent = parent;
            Globals = globals ?? this;
        }

        public bool TryGet(string name, out object value)
        {
            var s = this;
            while (s != null)
            {
                if (s.Vars.TryGetValue(name, out value)) return true;
                s = s.Parent;
            }
            value = null;
            return false;
        }

        public void Set(string name, object value)
        {
            if (GlobalNames != null && GlobalNames.Contains(name))
            { Globals.Vars[name] = value; return; }
            Vars[name] = value;
        }
    }
}
