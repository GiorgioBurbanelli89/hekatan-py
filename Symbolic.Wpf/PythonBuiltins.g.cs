// GENERADO por Tools/gen_python_builtins.py — no editar a mano.
// Origen: los nombres REALES que registra el motor en Symbolic.Core/Python/*.cs
//   Reg("x") · Fn("x") · .Attrs["x"] · new PyBuiltin("x")
// Regenerar cuando el motor gane funciones.

using System.Collections.Generic;

namespace Calcpad.Wpf;

internal static class PythonBuiltins
{
    /// <summary>Las 47 funciones que el motor ofrece SIN importar nada (print, len, range...).</summary>
    internal static readonly string[] Globales =
    {
        "Counter", "OrderedDict", "abs", "all", "any", "bin", "bool", "chr", "defaultdict", "dict", "divmod",
        "eig_sym", "enumerate", "filter", "float", "format", "frozenset", "hex", "input", "int", "isinstance",
        "len", "list", "map", "max", "mesh3d", "mesh3d_viewer", "mesh_viewer", "min", "oct", "opensees", "ord",
        "pow", "print", "range", "repr", "reversed", "round", "set", "solid3d_viewer", "solve", "sorted",
        "str", "sum", "tuple", "type", "zip",
    };

    /// <summary>Lo que expone <c>numpy</c> (95 nombres).</summary>
    private static readonly string[] _numpy =
    {
        "abs", "absolute", "amax", "amin", "angle", "arange", "argmax", "argmin", "around", "array", "asarray",
        "astype", "ceil", "clip", "column_stack", "concatenate", "conj", "copy", "cos", "cumsum", "deg2rad",
        "degrees", "det", "diag", "diff", "dot", "e", "eig", "eigh", "eigvals", "eigvalsh", "exp", "eye",
        "fft", "flatten", "float64", "float_", "floor", "full", "genfromtxt", "hstack", "identity", "imag",
        "inf", "int64", "int_", "interp", "inv", "isnan", "ix_", "linalg", "linspace", "loadtxt", "log",
        "matmul", "max", "maximum", "mean", "meshgrid", "min", "minimum", "nan", "nanmax", "nanmin", "newaxis",
        "norm", "ones", "ones_like", "outer", "pi", "power", "prod", "rad2deg", "radians", "ravel", "real",
        "reshape", "round", "savetxt", "setdiff1d", "sign", "sin", "solve", "spsolve", "sqrt", "sum", "tan",
        "tolist", "trace", "transpose", "unique", "unravel_index", "vstack", "zeros", "zeros_like",
    };

    /// <summary>Lo que expone <c>math</c> (43 nombres).</summary>
    private static readonly string[] _math =
    {
        "acos", "acosh", "asin", "asinh", "atan", "atan2", "atanh", "ceil", "copysign", "cos", "cosh",
        "degrees", "dist", "e", "exp", "expm1", "fabs", "factorial", "floor", "fmod", "gcd", "hypot", "inf",
        "isfinite", "isinf", "isnan", "lcm", "log", "log10", "log1p", "log2", "nan", "pi", "pow", "prod",
        "radians", "sin", "sinh", "sqrt", "tan", "tanh", "tau", "trunc",
    };

    /// <summary>Lo que expone <c>scipy</c> (93 nombres).</summary>
    private static readonly string[] _scipy =
    {
        "CubicSpline", "bisect", "blackman", "brentq", "butter", "cdf", "cdist", "cholesky", "cityblock",
        "comb", "convolve", "coo_matrix", "correlate", "csc_matrix", "csr_matrix", "cumtrapz",
        "cumulative_trapezoid", "curve_fit", "det", "diags", "distance", "dot", "eig", "eigh", "eigvalsh",
        "erf", "erfc", "erfinv", "euclidean", "expm", "eye", "factorial", "factorized", "fft", "fftfreq",
        "filtfilt", "find_peaks", "fsolve", "gamma", "gammainc", "gammaln", "hamming", "hann", "identity",
        "ifft", "integrate", "interp1d", "interpolate", "inv", "io", "irfft", "least_squares", "lfilter",
        "lil_matrix", "linalg", "loadmat", "lstsq", "lu", "minimize", "minimize_scalar", "newton", "norm",
        "odeint", "optimize", "pdf", "pdist", "pinv", "ppf", "qr", "quad", "rfft", "rfftfreq", "root",
        "savemat", "sf", "signal", "simpson", "solve", "solve_ivp", "solve_triangular", "sparse", "spatial",
        "special", "splu", "spsolve", "stats", "svd", "tmean", "toarray", "transpose", "trapezoid", "trapz",
        "windows",
    };

    /// <summary>Lo que expone <c>os</c> (18 nombres).</summary>
    private static readonly string[] _os =
    {
        "abspath", "basename", "dirname", "exists", "getcwd", "isdir", "isfile", "join", "linesep", "listdir",
        "makedirs", "mkdir", "name", "path", "realpath", "sep", "split", "splitext",
    };

    /// <summary>Lo que expone <c>sys</c> (10 nombres).</summary>
    private static readonly string[] _sys =
    {
        "argv", "executable", "exit", "maxsize", "modules", "path", "platform", "stderr", "stdout", "version",
    };

    /// <summary>Lo que expone <c>time</c> (4 nombres).</summary>
    private static readonly string[] _time =
    {
        "monotonic", "perf_counter", "sleep", "time",
    };

    /// <summary>Lo que expone <c>collections</c> (3 nombres).</summary>
    private static readonly string[] _collections =
    {
        "Counter", "OrderedDict", "defaultdict",
    };

    /// <summary>Modulo → lo que expone. Para autocompletar despues del punto (<c>np.zer</c> → <c>zeros</c>).</summary>
    internal static readonly Dictionary<string, string[]> Modulos = new()
    {
        ["numpy"] = _numpy,
        ["math"] = _math,
        ["scipy"] = _scipy,
        ["os"] = _os,
        ["sys"] = _sys,
        ["time"] = _time,
        ["collections"] = _collections,
    };

    /// <summary>Todos los nombres del motor (271), sin repetir: es lo que colorea el editor.</summary>
    internal static readonly string[] All =
    {
        "Counter", "CubicSpline", "OrderedDict", "abs", "absolute", "abspath", "acos", "acosh", "all", "amax",
        "amin", "angle", "any", "arange", "argmax", "argmin", "argv", "around", "array", "asarray", "asin",
        "asinh", "astype", "atan", "atan2", "atanh", "basename", "bin", "bisect", "blackman", "bool", "brentq",
        "butter", "cdf", "cdist", "ceil", "cholesky", "chr", "cityblock", "clip", "column_stack", "comb",
        "concatenate", "conj", "convolve", "coo_matrix", "copy", "copysign", "correlate", "cos", "cosh",
        "csc_matrix", "csr_matrix", "cumsum", "cumtrapz", "cumulative_trapezoid", "curve_fit", "defaultdict",
        "deg2rad", "degrees", "det", "diag", "diags", "dict", "diff", "dirname", "dist", "distance", "divmod",
        "dot", "e", "eig", "eig_sym", "eigh", "eigvals", "eigvalsh", "enumerate", "erf", "erfc", "erfinv",
        "euclidean", "executable", "exists", "exit", "exp", "expm", "expm1", "eye", "fabs", "factorial",
        "factorized", "fft", "fftfreq", "filter", "filtfilt", "find_peaks", "flatten", "float", "float64",
        "float_", "floor", "fmod", "format", "frozenset", "fsolve", "full", "gamma", "gammainc", "gammaln",
        "gcd", "genfromtxt", "getcwd", "hamming", "hann", "hex", "hstack", "hypot", "identity", "ifft", "imag",
        "inf", "input", "int", "int64", "int_", "integrate", "interp", "interp1d", "interpolate", "inv", "io",
        "irfft", "isdir", "isfile", "isfinite", "isinf", "isinstance", "isnan", "ix_", "join", "lcm",
        "least_squares", "len", "lfilter", "lil_matrix", "linalg", "linesep", "linspace", "list", "listdir",
        "loadmat", "loadtxt", "log", "log10", "log1p", "log2", "lstsq", "lu", "makedirs", "map", "matmul",
        "max", "maximum", "maxsize", "mean", "mesh3d", "mesh3d_viewer", "mesh_viewer", "meshgrid", "min",
        "minimize", "minimize_scalar", "minimum", "mkdir", "modules", "monotonic", "name", "nan", "nanmax",
        "nanmin", "newaxis", "newton", "norm", "oct", "odeint", "ones", "ones_like", "opensees", "optimize",
        "ord", "outer", "path", "pdf", "pdist", "perf_counter", "pi", "pinv", "platform", "pow", "power",
        "ppf", "print", "prod", "qr", "quad", "rad2deg", "radians", "range", "ravel", "real", "realpath",
        "repr", "reshape", "reversed", "rfft", "rfftfreq", "root", "round", "savemat", "savetxt", "sep", "set",
        "setdiff1d", "sf", "sign", "signal", "simpson", "sin", "sinh", "sleep", "solid3d_viewer", "solve",
        "solve_ivp", "solve_triangular", "sorted", "sparse", "spatial", "special", "split", "splitext", "splu",
        "spsolve", "sqrt", "stats", "stderr", "stdout", "str", "sum", "svd", "tan", "tanh", "tau", "time",
        "tmean", "toarray", "tolist", "trace", "transpose", "trapezoid", "trapz", "trunc", "tuple", "type",
        "unique", "unravel_index", "version", "vstack", "windows", "zeros", "zeros_like", "zip",
    };

    internal static readonly string[] Keywords =
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else",
        "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not",
        "or", "pass", "raise", "return", "try", "while", "with", "yield",
    };

    internal static readonly string[] Constants =
    {
        "True", "False", "None", "NotImplemented", "Ellipsis", "self",
    };
}
