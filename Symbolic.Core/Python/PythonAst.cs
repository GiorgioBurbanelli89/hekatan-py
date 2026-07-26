// =============================================================================
// Hekatan Python3 — Python Abstract Syntax Tree (AST nodes)
// =============================================================================
//   AST de un statement Python. Evaluado por PythonEvaluator.
//   Render HTML por PythonHtmlWriter. Ningún nodo referencia tipos de Calcpad.
//   Paralelo a Calcpad.Core.Matlab pero con semántica Python (indentación,
//   bloques por ':' + INDENT/DEDENT, slicing, f-strings, etc.).
// =============================================================================
using System.Collections.Generic;

namespace Calcpad.Core.Python
{
    public abstract class PyNode
    {
        public int Line;
        public int Column;
    }

    // ─── Expresiones ────────────────────────────────────────────────────────
    public sealed class NumberLit : PyNode { public double Value; public bool IsInt; public string OrigText; }
    public sealed class StringLit : PyNode { public string Value; }
    /// <summary>f-string: lista de segmentos. Cada segmento es texto literal (Text != null)
    /// o una expresión embebida (Expr != null) con un format-spec opcional.</summary>
    public sealed class FStringLit : PyNode
    {
        public List<FStringPart> Parts = new();
    }
    public sealed class FStringPart
    {
        public string Text;        // segmento literal (si no es expr)
        public PyNode Expr;        // expresión embebida {expr}
        public string FormatSpec;  // texto tras ':' dentro de {} (ej. ".3f")
        public bool Repr;          // {expr!r}
    }
    public sealed class BoolLit : PyNode { public bool Value; }
    public sealed class NoneLit : PyNode { }
    public sealed class NameRef : PyNode { public string Name; }
    public sealed class UnaryOp : PyNode { public string Op; public PyNode Operand; } // "-", "+", "not", "~"
    public sealed class BinaryOp : PyNode
    {
        // "+", "-", "*", "/", "//", "%", "**", "@",
        // "&", "|", "^", "<<", ">>", "and", "or"
        public string Op;
        public PyNode Left;
        public PyNode Right;
    }
    /// <summary>Comparación encadenada estilo Python: a &lt; b &lt;= c.
    /// Ops.Count == Comparators.Count, Left es el primer operando.</summary>
    public sealed class CompareOp : PyNode
    {
        public PyNode Left;
        public List<string> Ops = new();          // "==","!=","<",">","<=",">=","in","not in","is","is not"
        public List<PyNode> Comparators = new();
    }
    public sealed class CallExpr : PyNode
    {
        public PyNode Func;
        public List<PyNode> Args = new();
        public List<(string Name, PyNode Value)> Kwargs = new();
        public PyNode StarArgs;     // *args (opcional)
        public PyNode DoubleStar;   // **kwargs (opcional)
    }
    public sealed class IndexExpr : PyNode { public PyNode Target; public PyNode Index; }
    public sealed class SliceExpr : PyNode { public PyNode Lower; public PyNode Upper; public PyNode Step; }
    public sealed class AttributeExpr : PyNode { public PyNode Target; public string Name; }
    public sealed class ListLit : PyNode { public List<PyNode> Elements = new(); }
    public sealed class TupleLit : PyNode { public List<PyNode> Elements = new(); }
    public sealed class SetLit : PyNode { public List<PyNode> Elements = new(); }
    public sealed class DictLit : PyNode { public List<(PyNode Key, PyNode Value)> Pairs = new(); }
    /// <summary>Comprensión: [expr for t in iter if cond ...]. Kind: list/set/dict/gen.</summary>
    public sealed class Comprehension : PyNode
    {
        public string Kind;            // "list", "set", "dict", "gen"
        public PyNode Element;         // para dict: la key
        public PyNode ValueElement;    // sólo dict: el value
        public List<CompClause> Clauses = new();
    }
    public sealed class CompClause
    {
        public PyNode Target;          // variable(s) de iteración
        public PyNode Iter;
        public List<PyNode> Conditions = new();
    }
    public sealed class LambdaExpr : PyNode
    {
        public List<Param> Params = new();
        public PyNode Body;
    }
    /// <summary>Operador ternario: TrueExpr if Cond else FalseExpr.</summary>
    public sealed class IfExp : PyNode { public PyNode Cond; public PyNode TrueExpr; public PyNode FalseExpr; }
    /// <summary>El `:` aislado en un slice vacío, o `*x` en unpacking.</summary>
    public sealed class StarExpr : PyNode { public PyNode Value; }

    // ─── Parámetros de función ──────────────────────────────────────────────
    public sealed class Param
    {
        public string Name;
        public PyNode Default;     // null si no tiene default
        public bool IsStar;        // *args
        public bool IsDoubleStar;  // **kwargs
    }

    // ─── Statements ─────────────────────────────────────────────────────────
    public sealed class ExprStmt : PyNode { public PyNode Expr; }
    public sealed class AssignStmt : PyNode
    {
        public List<PyNode> Targets = new();  // a = b = value → 2 targets
        public PyNode Value;
    }
    public sealed class AugAssignStmt : PyNode
    {
        public PyNode Target;
        public string Op;          // "+", "-", "*", "/", "//", "%", "**", etc.
        public PyNode Value;
    }
    public sealed class CommentStmt : PyNode
    {
        public string Text;
        public bool IsHeading;     // (legacy) "##" → encabezado; ahora heading real = #"
        public bool IsInline;      // comentario al final de una línea con código (directiva: #show/#hide/#noc/#val)
    }
    public sealed class PassStmt : PyNode { }
    public sealed class BreakStmt : PyNode { }
    public sealed class ContinueStmt : PyNode { }
    public sealed class ReturnStmt : PyNode { public PyNode Value; }
    public sealed class DelStmt : PyNode { public List<PyNode> Targets = new(); }
    public sealed class GlobalStmt : PyNode { public List<string> Names = new(); }
    public sealed class NonlocalStmt : PyNode { public List<string> Names = new(); }
    public sealed class AssertStmt : PyNode { public PyNode Test; public PyNode Message; }
    public sealed class RaiseStmt : PyNode { public PyNode Exc; }

    public sealed class ImportStmt : PyNode
    {
        // import a.b as c, d
        public List<(string Module, string Alias)> Names = new();
    }
    public sealed class ImportFromStmt : PyNode
    {
        public string Module;
        public List<(string Name, string Alias)> Names = new();  // from m import x as y
        public bool ImportStar;
    }

    // ─── Control flow ───────────────────────────────────────────────────────
    public sealed class IfStmt : PyNode
    {
        // Branches: (cond, body). El else final tiene Cond = null.
        public List<(PyNode Cond, List<PyNode> Body)> Branches = new();
    }
    public sealed class ForStmt : PyNode
    {
        public PyNode Target;            // variable(s): x  o  (i, v)
        public PyNode Iter;
        public List<PyNode> Body = new();
        public List<PyNode> ElseBody;    // for ... else (raro, opcional)
    }
    public sealed class WhileStmt : PyNode
    {
        public PyNode Cond;
        public List<PyNode> Body = new();
        public List<PyNode> ElseBody;
    }
    public sealed class TryStmt : PyNode
    {
        public List<PyNode> Body = new();
        public List<ExceptClause> Handlers = new();
        public List<PyNode> ElseBody;
        public List<PyNode> FinallyBody;
    }
    public sealed class ExceptClause
    {
        public PyNode ExcType;    // opcional
        public string Name;       // except E as name
        public List<PyNode> Body = new();
    }
    public sealed class WithStmt : PyNode
    {
        public List<(PyNode Ctx, PyNode Var)> Items = new();
        public List<PyNode> Body = new();
    }

    // ─── Definiciones ───────────────────────────────────────────────────────
    public sealed class FuncDef : PyNode
    {
        public string Name;
        public List<Param> Params = new();
        public List<PyNode> Body = new();
        public List<PyNode> Decorators = new();
    }
    public sealed class ClassDef : PyNode
    {
        public string Name;
        public List<PyNode> Bases = new();
        public List<PyNode> Body = new();
        public List<PyNode> Decorators = new();
    }
}
