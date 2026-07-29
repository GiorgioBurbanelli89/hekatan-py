// =============================================================================
// Hekatan Python3 — Python Parser (recursive descent + precedence climbing)
// =============================================================================
//   Convierte List<PyToken> en una lista de statements (PyNode). Maneja suites
//   por INDENT/DEDENT, comparaciones encadenadas, comprensiones, f-strings,
//   lambdas y el operador ternario.
// =============================================================================
using System;
using System.Collections.Generic;

namespace Calcpad.Core.Python
{
    public sealed class PythonParseException : Exception
    {
        public int Line;
        public PythonParseException(string msg, int line) : base(msg) { Line = line; }
    }

    public sealed class PythonParser
    {
        private readonly List<PyToken> _t;
        private int _p;

        public PythonParser(List<PyToken> tokens) { _t = tokens; _p = 0; }

        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "if","elif","else","for","while","def","class","return","break","continue",
            "pass","import","from","as","in","not","and","or","None","True","False",
            "lambda","del","global","nonlocal","try","except","finally","with","yield",
            "raise","assert","is","async","await",
        };

        // ── Helpers de cursor ──
        private PyToken Cur => _t[_p];
        private PyToken Peek(int k = 0) => _p + k < _t.Count ? _t[_p + k] : _t[^1];
        private PyToken Next() => _t[_p++];
        private bool IsName(string s) => Cur.Type == PyTok.Name && Cur.Text == s;
        private bool IsOp(string s) => Cur.Type == PyTok.Op && Cur.Text == s;
        private bool AtEnd => Cur.Type == PyTok.EndMarker;

        private bool MatchOp(string s) { if (IsOp(s)) { _p++; return true; } return false; }
        private bool MatchName(string s) { if (IsName(s)) { _p++; return true; } return false; }
        private void ExpectOp(string s)
        {
            if (!MatchOp(s)) throw new PythonParseException($"Se esperaba '{s}' pero se encontró '{Cur.Text}'", Cur.Line);
        }
        private void ExpectName(string s)
        {
            if (!MatchName(s)) throw new PythonParseException($"Se esperaba '{s}' pero se encontró '{Cur.Text}'", Cur.Line);
        }
        private void SkipNewlines()
        {
            while (Cur.Type == PyTok.Newline) _p++;
        }

        // ===================================================================
        //  ENTRADA
        // ===================================================================
        public List<PyNode> ParseModule()
        {
            var stmts = new List<PyNode>();
            ParseBlock(stmts, topLevel: true);
            return stmts;
        }

        private void ParseBlock(List<PyNode> outList, bool topLevel)
        {
            while (true)
            {
                SkipNewlines();
                if (AtEnd) break;
                if (!topLevel && Cur.Type == PyTok.Dedent) break;
                if (Cur.Type == PyTok.Indent) { _p++; continue; } // tolerante
                ParseLine(outList);
            }
        }

        // Una línea lógica: compound stmt, o uno/varios simple stmts separados por ';'.
        private void ParseLine(List<PyNode> outList)
        {
            if (Cur.Type == PyTok.Comment)
            {
                var ctxt = Next().Text;
                outList.Add(new CommentStmt { Text = ctxt, IsHeading = IsHeadingComment(ctxt), Line = Peek(-1).Line });
                if (Cur.Type == PyTok.Newline) _p++;
                return;
            }

            if (Cur.Type == PyTok.Name && IsCompoundKeyword(Cur.Text))
            {
                outList.Add(ParseCompound());
                return;
            }

            // Decoradores
            if (IsOp("@"))
            {
                ParseDecorated(outList);
                return;
            }

            // Simple statements separados por ';'
            ParseSimpleStatements(outList);
        }

        private static bool IsHeadingComment(string text)
        {
            // "# %%", "##", "# ==="
            var t = text.TrimStart('#').TrimStart();
            return text.StartsWith("##") || t.StartsWith("%%") || (t.Length > 3 && (t.StartsWith("===") || t.StartsWith("---")));
        }

        private bool IsCompoundKeyword(string s) =>
            s is "if" or "for" or "while" or "def" or "class" or "try" or "with";

        private void ParseSimpleStatements(List<PyNode> outList)
        {
            while (true)
            {
                var stmt = ParseSimpleStatement();
                if (stmt != null) outList.Add(stmt);
                if (IsOp(";")) { _p++; if (Cur.Type == PyTok.Newline || AtEnd) break; continue; }
                break;
            }
            // trailing inline comment
            if (Cur.Type == PyTok.Comment)
            {
                var ctxt = Next().Text;
                outList.Add(new CommentStmt { Text = ctxt, IsHeading = false, IsInline = true, Line = Peek(-1).Line });
            }
            if (Cur.Type == PyTok.Newline) _p++;
        }

        private PyNode ParseSimpleStatement()
        {
            int line = Cur.Line;
            if (Cur.Type == PyTok.Name)
            {
                switch (Cur.Text)
                {
                    case "pass": _p++; return new PassStmt { Line = line };
                    case "break": _p++; return new BreakStmt { Line = line };
                    case "continue": _p++; return new ContinueStmt { Line = line };
                    case "return":
                        _p++;
                        PyNode rv = (Cur.Type == PyTok.Newline || IsOp(";") || AtEnd || Cur.Type == PyTok.Comment) ? null : ParseTestListStar();
                        return new ReturnStmt { Value = rv, Line = line };
                    case "import": return ParseImport();
                    case "from": return ParseFromImport();
                    case "del":
                        _p++;
                        var del = new DelStmt { Line = line };
                        del.Targets.Add(ParseExpr());
                        while (MatchOp(",")) del.Targets.Add(ParseExpr());
                        return del;
                    case "global":
                        _p++;
                        var g = new GlobalStmt { Line = line };
                        g.Names.Add(Next().Text);
                        while (MatchOp(",")) g.Names.Add(Next().Text);
                        return g;
                    case "nonlocal":
                        _p++;
                        var nl = new NonlocalStmt { Line = line };
                        nl.Names.Add(Next().Text);
                        while (MatchOp(",")) nl.Names.Add(Next().Text);
                        return nl;
                    case "assert":
                        _p++;
                        var test = ParseExpr();
                        PyNode msg = MatchOp(",") ? ParseExpr() : null;
                        return new AssertStmt { Test = test, Message = msg, Line = line };
                    case "raise":
                        _p++;
                        PyNode exc = (Cur.Type == PyTok.Newline || IsOp(";") || AtEnd) ? null : ParseExpr();
                        return new RaiseStmt { Exc = exc, Line = line };
                    case "yield":
                        // tratado como expresión simple
                        break;
                }
            }
            return ParseExprOrAssign();
        }

        private PyNode ParseExprOrAssign()
        {
            int line = Cur.Line;
            var left = ParseTestListStar();

            // Augmented assignment
            if (Cur.Type == PyTok.Op && IsAugAssign(Cur.Text))
            {
                var op = Next().Text;
                op = op.Substring(0, op.Length - 1); // quitar '='
                var val = ParseTestListStar();
                return new AugAssignStmt { Target = left, Op = op, Value = val, Line = line };
            }

            // Assignment encadenado
            if (IsOp("="))
            {
                var targets = new List<PyNode> { left };
                while (MatchOp("="))
                    targets.Add(ParseTestListStar());
                var value = targets[^1];
                targets.RemoveAt(targets.Count - 1);
                return new AssignStmt { Targets = targets, Value = value, Line = line };
            }

            return new ExprStmt { Expr = left, Line = line };
        }

        private static bool IsAugAssign(string op) =>
            op is "+=" or "-=" or "*=" or "/=" or "//=" or "%=" or "**=" or
                  "&=" or "|=" or "^=" or ">>=" or "<<=" or "@=";

        // testlist con soporte *star: usado en targets/return/asignaciones
        private PyNode ParseTestListStar()
        {
            var first = ParseStarOrExpr();
            if (!IsOp(",")) return first;
            var tup = new TupleLit { Line = first.Line };
            tup.Elements.Add(first);
            while (MatchOp(","))
            {
                if (Cur.Type == PyTok.Newline || IsOp("=") || IsOp(")") || IsOp("]") || IsOp(":") || AtEnd || IsOp(";"))
                    break;
                tup.Elements.Add(ParseStarOrExpr());
            }
            return tup;
        }

        private PyNode ParseStarOrExpr()
        {
            if (IsOp("*"))
            {
                int l = Cur.Line; _p++;
                return new StarExpr { Value = ParseExpr(), Line = l };
            }
            return ParseExpr();
        }

        // ===================================================================
        //  IMPORTS
        // ===================================================================
        private PyNode ParseImport()
        {
            int line = Cur.Line; _p++; // import
            var st = new ImportStmt { Line = line };
            do
            {
                string mod = ParseDottedName();
                string alias = null;
                if (MatchName("as")) alias = Next().Text;
                st.Names.Add((mod, alias));
            } while (MatchOp(","));
            return st;
        }

        private PyNode ParseFromImport()
        {
            int line = Cur.Line; _p++; // from
            // soporta niveles relativos (.) — los acumulamos en el nombre
            string dots = "";
            while (IsOp(".") || IsOp("...")) { dots += Next().Text; }
            string mod = (Cur.Type == PyTok.Name && !IsName("import")) ? ParseDottedName() : "";
            mod = dots + mod;
            ExpectName("import");
            var st = new ImportFromStmt { Module = mod, Line = line };
            if (MatchOp("*")) { st.ImportStar = true; return st; }
            bool paren = MatchOp("(");
            do
            {
                if (paren && IsOp(")")) break;
                string name = Next().Text;
                string alias = null;
                if (MatchName("as")) alias = Next().Text;
                st.Names.Add((name, alias));
            } while (MatchOp(","));
            if (paren) ExpectOp(")");
            return st;
        }

        private string ParseDottedName()
        {
            var sb = new System.Text.StringBuilder(Next().Text);
            while (IsOp("."))
            {
                _p++;
                sb.Append('.').Append(Next().Text);
            }
            return sb.ToString();
        }

        // ===================================================================
        //  COMPOUND STATEMENTS
        // ===================================================================
        private PyNode ParseCompound()
        {
            switch (Cur.Text)
            {
                case "if": return ParseIf();
                case "for": return ParseFor();
                case "while": return ParseWhile();
                case "def": return ParseDef();
                case "class": return ParseClass();
                case "try": return ParseTry();
                case "with": return ParseWith();
            }
            throw new PythonParseException($"Keyword compuesta no soportada: {Cur.Text}", Cur.Line);
        }

        private List<PyNode> ParseSuite()
        {
            ExpectOp(":");
            var body = new List<PyNode>();
            // Comentario INLINE tras ':' (def f():  # nota / if x:  # nota) -> preservar y continuar al bloque.
            while (Cur.Type == PyTok.Comment)
            {
                body.Add(new CommentStmt { Text = Cur.Text, IsInline = true, Line = Cur.Line });
                _p++;
            }
            if (Cur.Type == PyTok.Newline)
            {
                _p++; // newline
                SkipNewlines();
                // Líneas SÓLO-comentario al inicio del bloque: el tokenizer las emite
                // ANTES del INDENT (no afectan la indentación). Las preservamos como
                // CommentStmt y seguimos buscando el bloque indentado.
                while (Cur.Type == PyTok.Comment)
                {
                    var ctxt = Cur.Text;
                    body.Add(new CommentStmt { Text = ctxt, IsHeading = IsHeadingComment(ctxt), Line = Cur.Line });
                    _p++;
                    SkipNewlines();
                }
                if (Cur.Type != PyTok.Indent)
                    throw new PythonParseException("Se esperaba un bloque indentado", Cur.Line);
                _p++; // indent
                ParseBlock(body, topLevel: false);
                if (Cur.Type == PyTok.Dedent) _p++;
            }
            else
            {
                // suite en la misma línea: simple stmts separados por ';'
                ParseSimpleStatements(body);
            }
            return body;
        }

        private PyNode ParseIf()
        {
            int line = Cur.Line;
            var node = new IfStmt { Line = line };
            _p++; // if
            var cond = ParseNamedTest();
            var body = ParseSuite();
            node.Branches.Add((cond, body));
            while (true)
            {
                SkipNewlines();
                if (IsName("elif"))
                {
                    _p++;
                    var c = ParseNamedTest();
                    var b = ParseSuite();
                    node.Branches.Add((c, b));
                }
                else if (IsName("else"))
                {
                    _p++;
                    var b = ParseSuite();
                    node.Branches.Add((null, b));
                    break;
                }
                else break;
            }
            return node;
        }

        private PyNode ParseFor()
        {
            int line = Cur.Line; _p++; // for
            var target = ParseTargetList();
            ExpectName("in");
            var iter = ParseTestListStar();
            var body = ParseSuite();
            List<PyNode> elseBody = null;
            SkipNewlines();
            if (IsName("else")) { _p++; elseBody = ParseSuite(); }
            return new ForStmt { Target = target, Iter = iter, Body = body, ElseBody = elseBody, Line = line };
        }

        private PyNode ParseWhile()
        {
            int line = Cur.Line; _p++; // while
            var cond = ParseNamedTest();
            var body = ParseSuite();
            List<PyNode> elseBody = null;
            SkipNewlines();
            if (IsName("else")) { _p++; elseBody = ParseSuite(); }
            return new WhileStmt { Cond = cond, Body = body, ElseBody = elseBody, Line = line };
        }

        private PyNode ParseDef()
        {
            int line = Cur.Line; _p++; // def
            string name = Next().Text;
            ExpectOp("(");
            var prms = ParseParamList();
            ExpectOp(")");
            // anotación de retorno -> tipo
            if (MatchOp("->")) ParseExpr();
            var body = ParseSuite();
            return new FuncDef { Name = name, Params = prms, Body = body, Line = line };
        }

        private List<Param> ParseParamList()
        {
            var list = new List<Param>();
            while (!IsOp(")") && !AtEnd)
            {
                if (IsOp("/")) { _p++; if (MatchOp(",")) continue; else break; } // positional-only marker
                var p = new Param();
                if (MatchOp("**")) p.IsDoubleStar = true;
                else if (MatchOp("*")) p.IsStar = true;
                if (Cur.Type == PyTok.Name) p.Name = Next().Text;
                // anotación de tipo
                if (MatchOp(":")) ParseExpr();
                if (MatchOp("=")) p.Default = ParseExpr();
                list.Add(p);
                if (!MatchOp(",")) break;
            }
            return list;
        }

        private PyNode ParseClass()
        {
            int line = Cur.Line; _p++; // class
            string name = Next().Text;
            var node = new ClassDef { Name = name, Line = line };
            if (MatchOp("("))
            {
                while (!IsOp(")") && !AtEnd)
                {
                    // ignorar kwargs tipo metaclass=...
                    if (Cur.Type == PyTok.Name && Peek(1).Type == PyTok.Op && Peek(1).Text == "=")
                    { _p += 2; ParseExpr(); }
                    else node.Bases.Add(ParseExpr());
                    if (!MatchOp(",")) break;
                }
                ExpectOp(")");
            }
            node.Body = ParseSuite();
            return node;
        }

        private PyNode ParseTry()
        {
            int line = Cur.Line; _p++; // try
            var node = new TryStmt { Line = line };
            node.Body = ParseSuite();
            while (true)
            {
                SkipNewlines();
                if (IsName("except"))
                {
                    _p++;
                    var h = new ExceptClause();
                    if (!IsOp(":"))
                    {
                        h.ExcType = ParseExpr();
                        if (MatchName("as")) h.Name = Next().Text;
                    }
                    h.Body = ParseSuite();
                    node.Handlers.Add(h);
                }
                else if (IsName("else")) { _p++; node.ElseBody = ParseSuite(); }
                else if (IsName("finally")) { _p++; node.FinallyBody = ParseSuite(); break; }
                else break;
            }
            return node;
        }

        private PyNode ParseWith()
        {
            int line = Cur.Line; _p++; // with
            var node = new WithStmt { Line = line };
            do
            {
                var ctx = ParseExpr();
                PyNode var = null;
                if (MatchName("as")) var = ParseExpr();
                node.Items.Add((ctx, var));
            } while (MatchOp(","));
            node.Body = ParseSuite();
            return node;
        }

        private void ParseDecorated(List<PyNode> outList)
        {
            var decorators = new List<PyNode>();
            while (IsOp("@"))
            {
                _p++;
                decorators.Add(ParseExpr());
                if (Cur.Type == PyTok.Newline) _p++;
                SkipNewlines();
            }
            var stmt = ParseCompound();
            if (stmt is FuncDef fd) fd.Decorators = decorators;
            else if (stmt is ClassDef cd) cd.Decorators = decorators;
            outList.Add(stmt);
        }

        // target list para for / with (sin convertirlo en assignment)
        private PyNode ParseTargetList()
        {
            var first = ParsePostfixExpr();
            if (!IsOp(",")) return first;
            var tup = new TupleLit { Line = first.Line };
            tup.Elements.Add(first);
            while (MatchOp(","))
            {
                if (IsName("in") || IsOp("=") || Cur.Type == PyTok.Newline) break;
                tup.Elements.Add(ParsePostfixExpr());
            }
            return tup;
        }

        // ===================================================================
        //  EXPRESIONES
        // ===================================================================
        // ParseNamedTest: como ParseExpr pero permite walrus a:=b a nivel cond.
        private PyNode ParseNamedTest()
        {
            var e = ParseExpr();
            if (IsOp(":="))
            {
                _p++;
                var val = ParseExpr();
                // representado como Assign-expresión simple: reusar BinaryOp ":="
                return new BinaryOp { Op = ":=", Left = e, Right = val, Line = e.Line };
            }
            return e;
        }

        public PyNode ParseExpr() => ParseTernary();

        private PyNode ParseTernary()
        {
            if (IsName("lambda")) return ParseLambda();
            var body = ParseOr();
            if (IsName("if"))
            {
                _p++;
                var cond = ParseOr();
                ExpectName("else");
                var orelse = ParseExpr();
                return new IfExp { Cond = cond, TrueExpr = body, FalseExpr = orelse, Line = body.Line };
            }
            return body;
        }

        private PyNode ParseLambda()
        {
            int line = Cur.Line; _p++; // lambda
            var prms = new List<Param>();
            while (!IsOp(":") && !AtEnd)
            {
                var p = new Param();
                if (MatchOp("**")) p.IsDoubleStar = true;
                else if (MatchOp("*")) p.IsStar = true;
                p.Name = Next().Text;
                if (MatchOp("=")) p.Default = ParseExpr();
                prms.Add(p);
                if (!MatchOp(",")) break;
            }
            ExpectOp(":");
            var bodyExpr = ParseExpr();
            return new LambdaExpr { Params = prms, Body = bodyExpr, Line = line };
        }

        private PyNode ParseOr()
        {
            var left = ParseAnd();
            while (IsName("or"))
            {
                int l = Cur.Line; _p++;
                var right = ParseAnd();
                left = new BinaryOp { Op = "or", Left = left, Right = right, Line = l };
            }
            return left;
        }

        private PyNode ParseAnd()
        {
            var left = ParseNot();
            while (IsName("and"))
            {
                int l = Cur.Line; _p++;
                var right = ParseNot();
                left = new BinaryOp { Op = "and", Left = left, Right = right, Line = l };
            }
            return left;
        }

        private PyNode ParseNot()
        {
            if (IsName("not"))
            {
                int l = Cur.Line; _p++;
                return new UnaryOp { Op = "not", Operand = ParseNot(), Line = l };
            }
            return ParseComparison();
        }

        private PyNode ParseComparison()
        {
            var left = ParseBitOr();
            var ops = new List<string>();
            var comps = new List<PyNode>();
            while (true)
            {
                string op = null;
                if (Cur.Type == PyTok.Op && Cur.Text is "<" or ">" or "<=" or ">=" or "==" or "!=")
                    op = Next().Text;
                else if (IsName("in")) { _p++; op = "in"; }
                else if (IsName("not") && Peek(1).Type == PyTok.Name && Peek(1).Text == "in") { _p += 2; op = "not in"; }
                else if (IsName("is"))
                {
                    _p++;
                    if (MatchName("not")) op = "is not"; else op = "is";
                }
                else break;
                ops.Add(op);
                comps.Add(ParseBitOr());
            }
            if (ops.Count == 0) return left;
            return new CompareOp { Left = left, Ops = ops, Comparators = comps, Line = left.Line };
        }

        private PyNode ParseBitOr()
        {
            var left = ParseBitXor();
            while (IsOp("|")) { int l = Cur.Line; _p++; left = new BinaryOp { Op = "|", Left = left, Right = ParseBitXor(), Line = l }; }
            return left;
        }
        private PyNode ParseBitXor()
        {
            var left = ParseBitAnd();
            while (IsOp("^")) { int l = Cur.Line; _p++; left = new BinaryOp { Op = "^", Left = left, Right = ParseBitAnd(), Line = l }; }
            return left;
        }
        private PyNode ParseBitAnd()
        {
            var left = ParseShift();
            while (IsOp("&")) { int l = Cur.Line; _p++; left = new BinaryOp { Op = "&", Left = left, Right = ParseShift(), Line = l }; }
            return left;
        }
        private PyNode ParseShift()
        {
            var left = ParseArith();
            while (IsOp("<<") || IsOp(">>")) { var o = Next().Text; left = new BinaryOp { Op = o, Left = left, Right = ParseArith(), Line = left.Line }; }
            return left;
        }
        private PyNode ParseArith()
        {
            var left = ParseTerm();
            while (IsOp("+") || IsOp("-")) { var o = Next().Text; left = new BinaryOp { Op = o, Left = left, Right = ParseTerm(), Line = left.Line }; }
            return left;
        }
        private PyNode ParseTerm()
        {
            var left = ParseUnary();
            while (IsOp("*") || IsOp("/") || IsOp("//") || IsOp("%") || IsOp("@"))
            { var o = Next().Text; left = new BinaryOp { Op = o, Left = left, Right = ParseUnary(), Line = left.Line }; }
            return left;
        }
        private PyNode ParseUnary()
        {
            if (IsOp("-") || IsOp("+") || IsOp("~"))
            {
                int l = Cur.Line; var o = Next().Text;
                return new UnaryOp { Op = o, Operand = ParseUnary(), Line = l };
            }
            return ParsePower();
        }
        private PyNode ParsePower()
        {
            var b = ParsePostfixExpr();
            if (IsOp("**"))
            {
                int l = Cur.Line; _p++;
                var exp = ParseUnary(); // right-assoc, permite -
                return new BinaryOp { Op = "**", Left = b, Right = exp, Line = l };
            }
            return b;
        }

        private PyNode ParsePostfixExpr()
        {
            var e = ParseAtom();
            while (true)
            {
                if (IsOp("("))
                {
                    e = ParseCall(e);
                }
                else if (IsOp("["))
                {
                    _p++;
                    var idx = ParseSubscript();
                    ExpectOp("]");
                    e = new IndexExpr { Target = e, Index = idx, Line = e.Line };
                }
                else if (IsOp("."))
                {
                    _p++;
                    string name = Next().Text;
                    e = new AttributeExpr { Target = e, Name = name, Line = e.Line };
                }
                else break;
            }
            return e;
        }

        private PyNode ParseSubscript()
        {
            // Soporta slicing a:b:c y tuplas de índices a,b
            PyNode ParseOneSub()
            {
                PyNode lower = null, upper = null, step = null;
                bool isSlice = false;
                if (!IsOp(":")) lower = ParseExpr();
                if (IsOp(":"))
                {
                    isSlice = true; _p++;
                    if (!IsOp(":") && !IsOp("]") && !IsOp(",")) upper = ParseExpr();
                    if (IsOp(":"))
                    {
                        _p++;
                        if (!IsOp("]") && !IsOp(",")) step = ParseExpr();
                    }
                }
                if (isSlice) return new SliceExpr { Lower = lower, Upper = upper, Step = step };
                return lower;
            }

            var first = ParseOneSub();
            if (!IsOp(",")) return first;
            var tup = new TupleLit { Line = first.Line };
            tup.Elements.Add(first);
            while (MatchOp(","))
            {
                if (IsOp("]")) break;
                tup.Elements.Add(ParseOneSub());
            }
            return tup;
        }

        private PyNode ParseCall(PyNode func)
        {
            int line = Cur.Line;
            _p++; // (
            var call = new CallExpr { Func = func, Line = line };
            while (!IsOp(")") && !AtEnd)
            {
                if (MatchOp("**")) { call.DoubleStar = ParseExpr(); }
                else if (MatchOp("*")) { call.StarArgs = ParseExpr(); }
                else if (Cur.Type == PyTok.Name && Peek(1).Type == PyTok.Op && Peek(1).Text == "=" )
                {
                    string kw = Next().Text; _p++; // name =
                    call.Kwargs.Add((kw, ParseExpr()));
                }
                else
                {
                    var arg = ParseExpr();
                    // generator expression como único argumento: f(x for x in y)
                    if (IsName("for"))
                    {
                        arg = ParseComprehensionTail(arg, "gen", null);
                    }
                    call.Args.Add(arg);
                }
                if (!MatchOp(",")) break;
            }
            ExpectOp(")");
            return call;
        }

        private PyNode ParseAtom()
        {
            var t = Cur;
            int line = t.Line;
            switch (t.Type)
            {
                case PyTok.Number:
                    _p++;
                    return new NumberLit { Value = t.Num, IsInt = t.IsInt, OrigText = t.Text, Line = line };
                case PyTok.String:
                    return ParseStringRun();
                case PyTok.Name:
                    if (t.Text == "True") { _p++; return new BoolLit { Value = true, Line = line }; }
                    if (t.Text == "False") { _p++; return new BoolLit { Value = false, Line = line }; }
                    if (t.Text == "None") { _p++; return new NoneLit { Line = line }; }
                    if (t.Text == "lambda") return ParseLambda();
                    if (Keywords.Contains(t.Text) && t.Text is not ("True" or "False" or "None"))
                        throw new PythonParseException($"Token inesperado '{t.Text}'", line);
                    _p++;
                    return new NameRef { Name = t.Text, Line = line };
                case PyTok.Op:
                    if (t.Text == "(") return ParseParenOrTuple();
                    if (t.Text == "[") return ParseListOrComp();
                    if (t.Text == "{") return ParseDictOrSet();
                    if (t.Text == "...") { _p++; return new NameRef { Name = "Ellipsis", Line = line }; }
                    if (t.Text == "*") { _p++; return new StarExpr { Value = ParseExpr(), Line = line }; }
                    break;
            }
            throw new PythonParseException($"Expresión inesperada: '{t.Text}'", line);
        }

        // Concatenación de strings adyacentes; si alguno es f-string → FStringLit.
        private PyNode ParseStringRun()
        {
            var parts = new List<PyToken>();
            int line = Cur.Line;
            while (Cur.Type == PyTok.String) parts.Add(Next());
            bool anyF = parts.Exists(p => p.IsFString);
            if (!anyF)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var p in parts) sb.Append(p.Str);
                return new StringLit { Value = sb.ToString(), Line = line };
            }
            var fl = new FStringLit { Line = line };
            foreach (var p in parts)
            {
                if (p.IsFString) ParseFStringInto(fl, p.Text);
                else fl.Parts.Add(new FStringPart { Text = p.Str });
            }
            return fl;
        }

        private void ParseFStringInto(FStringLit fl, string raw)
        {
            int i = 0, n = raw.Length;
            var lit = new System.Text.StringBuilder();
            while (i < n)
            {
                char c = raw[i];
                if (c == '{')
                {
                    if (i + 1 < n && raw[i + 1] == '{') { lit.Append('{'); i += 2; continue; }
                    if (lit.Length > 0) { fl.Parts.Add(new FStringPart { Text = UnescapeFLiteral(lit.ToString()) }); lit.Clear(); }
                    // leer expresión hasta '}' al nivel 0, respetando !r y :spec
                    i++;
                    int depth = 0;
                    var exprSb = new System.Text.StringBuilder();
                    string spec = null; bool repr = false;
                    while (i < n)
                    {
                        char d = raw[i];
                        if (depth == 0 && d == '}') { i++; break; }
                        if (depth == 0 && d == '!' && i + 1 < n && (raw[i + 1] == 'r' || raw[i + 1] == 's' || raw[i + 1] == 'a'))
                        { repr = raw[i + 1] == 'r'; i += 2; continue; }
                        if (depth == 0 && d == ':')
                        {
                            i++;
                            var specSb = new System.Text.StringBuilder();
                            int sd = 0;
                            while (i < n)
                            {
                                char s = raw[i];
                                if (sd == 0 && s == '}') break;
                                if (s == '{') sd++;
                                else if (s == '}') sd--;
                                specSb.Append(s);
                                i++;
                            }
                            spec = specSb.ToString();
                            if (i < n && raw[i] == '}') i++;
                            break;
                        }
                        if (d == '{' || d == '(' || d == '[') depth++;
                        else if (d == '}' || d == ')' || d == ']') depth--;
                        exprSb.Append(d);
                        i++;
                    }
                    string exprSrc = exprSb.ToString().Trim();
                    // soporte "=" debug: {x=} → muestra "x=" + valor
                    bool eqDebug = exprSrc.EndsWith("=") && !exprSrc.EndsWith("==");
                    if (eqDebug) exprSrc = exprSrc.Substring(0, exprSrc.Length - 1).Trim();
                    var exprNode = ParseSubExpression(exprSrc);
                    if (eqDebug) fl.Parts.Add(new FStringPart { Text = exprSrc + "=" });
                    fl.Parts.Add(new FStringPart { Expr = exprNode, FormatSpec = spec, Repr = repr });
                }
                else if (c == '}')
                {
                    if (i + 1 < n && raw[i + 1] == '}') { lit.Append('}'); i += 2; continue; }
                    lit.Append('}'); i++;
                }
                else { lit.Append(c); i++; }
            }
            if (lit.Length > 0) fl.Parts.Add(new FStringPart { Text = UnescapeFLiteral(lit.ToString()) });
        }

        private static string UnescapeFLiteral(string s)
        {
            // El raw de f-strings conserva las secuencias \n etc.; las decodificamos.
            if (s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char e = s[++i];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '\\': sb.Append('\\'); break;
                        case '\'': sb.Append('\''); break;
                        case '"': sb.Append('"'); break;
                        default: sb.Append('\\'); sb.Append(e); break;
                    }
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private PyNode ParseSubExpression(string src)
        {
            var toks = PythonTokenizer.Tokenize(src);
            var sub = new PythonParser(toks);
            return sub.ParseExpr();
        }

        private PyNode ParseParenOrTuple()
        {
            int line = Cur.Line;
            _p++; // (
            if (IsOp(")")) { _p++; return new TupleLit { Line = line }; }
            var first = ParseStarOrExpr();
            // generator expression
            if (IsName("for"))
            {
                var comp = ParseComprehensionTail(first, "gen", null);
                ExpectOp(")");
                return comp;
            }
            if (IsOp(","))
            {
                var tup = new TupleLit { Line = line };
                tup.Elements.Add(first);
                while (MatchOp(","))
                {
                    if (IsOp(")")) break;
                    tup.Elements.Add(ParseStarOrExpr());
                }
                ExpectOp(")");
                return tup;
            }
            ExpectOp(")");
            return first; // grouping
        }

        private PyNode ParseListOrComp()
        {
            int line = Cur.Line;
            _p++; // [
            if (IsOp("]")) { _p++; return new ListLit { Line = line }; }
            var first = ParseStarOrExpr();
            if (IsName("for"))
            {
                var comp = ParseComprehensionTail(first, "list", null);
                ExpectOp("]");
                return comp;
            }
            var lst = new ListLit { Line = line };
            lst.Elements.Add(first);
            while (MatchOp(","))
            {
                if (IsOp("]")) break;
                lst.Elements.Add(ParseStarOrExpr());
            }
            ExpectOp("]");
            return lst;
        }

        private PyNode ParseDictOrSet()
        {
            int line = Cur.Line;
            _p++; // {
            if (IsOp("}")) { _p++; return new DictLit { Line = line }; }
            // **dict unpacking
            if (IsOp("**"))
            {
                _p++;
                var d0 = new DictLit { Line = line };
                d0.Pairs.Add((null, ParseExpr())); // key null = unpack
                while (MatchOp(","))
                {
                    if (IsOp("}")) break;
                    if (MatchOp("**")) d0.Pairs.Add((null, ParseExpr()));
                    else { var k = ParseExpr(); ExpectOp(":"); d0.Pairs.Add((k, ParseExpr())); }
                }
                ExpectOp("}");
                return d0;
            }
            var firstKey = ParseStarOrExpr();
            if (IsOp(":"))
            {
                _p++;
                var firstVal = ParseExpr();
                if (IsName("for"))
                {
                    var comp = ParseComprehensionTail(firstKey, "dict", firstVal);
                    ExpectOp("}");
                    return comp;
                }
                var dict = new DictLit { Line = line };
                dict.Pairs.Add((firstKey, firstVal));
                while (MatchOp(","))
                {
                    if (IsOp("}")) break;
                    if (MatchOp("**")) { dict.Pairs.Add((null, ParseExpr())); continue; }
                    var k = ParseExpr();
                    ExpectOp(":");
                    var v = ParseExpr();
                    dict.Pairs.Add((k, v));
                }
                ExpectOp("}");
                return dict;
            }
            // set o set comp
            if (IsName("for"))
            {
                var comp = ParseComprehensionTail(firstKey, "set", null);
                ExpectOp("}");
                return comp;
            }
            var set = new SetLit { Line = line };
            set.Elements.Add(firstKey);
            while (MatchOp(","))
            {
                if (IsOp("}")) break;
                set.Elements.Add(ParseStarOrExpr());
            }
            ExpectOp("}");
            return set;
        }

        private PyNode ParseComprehensionTail(PyNode element, string kind, PyNode valueElement)
        {
            var comp = new Comprehension { Kind = kind, Element = element, ValueElement = valueElement, Line = element.Line };
            while (IsName("for"))
            {
                _p++;
                var clause = new CompClause();
                clause.Target = ParseTargetList();
                ExpectName("in");
                clause.Iter = ParseOr();
                while (IsName("if"))
                {
                    _p++;
                    clause.Conditions.Add(ParseOr());
                }
                comp.Clauses.Add(clause);
            }
            return comp;
        }
    }
}
