# -*- coding: utf-8 -*-
"""
Traduce los BOTONES de la ventana: la ventana viene de Hekatan Lab, así que sus botones
insertan MATLAB. Aquí se cambian, uno a uno, por su equivalente REAL de Hekatan Python3.

"Real" quiere decir: solo funciones que el motor reconoce de verdad
(Symbolic.Wpf/PythonBuiltins.g.cs, generado leyendo Symbolic.Core/Python). Nada inventado.

    python Tools/botones_matlab_a_python.py            # traduce
    python Tools/botones_matlab_a_python.py --revisar  # solo dice qué quedaría sin traducir

Convenciones de Hekatan Python3 que se respetan:
  · `#"` encabezado de la hoja · `#'` texto markdown · `#%%` celda plegable
  · una variable sola en su línea = se hace ECO en la hoja (no hace falta print)
  · numpy embebido: `import numpy as np`
  · gráficas del motor: mesh_viewer / mesh3d_viewer / solid3d_viewer
  · matplotlib SOLO con Python real (menú Python → entorno); por eso va aparte
  · `§` en un Tag = salto de línea (lo parte InsertLines)
"""
import pathlib, re, sys

XAML = pathlib.Path(__file__).resolve().parent.parent / "Symbolic.Wpf" / "MainWindow.xaml"

# ── Tag de MATLAB  →  Tag de Python ──────────────────────────────────────────────
TAGS = {
    # comentarios y markup de la hoja
    "% '": "#'",
    "%% ": "#%% ",
    "% #noc ": "#noc ",
    "% #md§§% #endmd": "#' **negrita**, _cursiva_, listas y tablas (la linea #' es markdown)",
    "% #plain§§% #render": "#hide§§#show",
    "% ": "# ",
    "%-- ": "#'-----",
    "%{§§%}": "'''§§'''",

    # vectores y matrices  →  numpy
    "zeros(m, n)": "np.zeros((m, n))",
    "ones(m, n)": "np.ones((m, n))",
    "eye(n)": "np.eye(n)",
    "rand(m, n)": "np.random.rand(m, n)",
    "randn(m, n)": "np.random.randn(m, n)",
    "linspace(a, b, n)": "np.linspace(a, b, n)",
    "size(A)": "A.shape",
    "length(v)": "len(v)",
    "numel(A)": "A.size",
    "A(end)": "A[-1]",
    "inv(A)": "np.linalg.inv(A)",
    "det(A)": "np.linalg.det(A)",
    "norm(v)": "np.linalg.norm(v)",
    "dot(a, b)": "np.dot(a, b)",
    "cross(a, b)": "np.cross(a, b)",
    "eig(A)": "np.linalg.eigvals(A)",
    "[V, D] = eig(A)": "w, V = np.linalg.eig(A)",
    "lu(A)": "from scipy.linalg import lu§P, L, U = lu(A)",
    "chol(A)": "from scipy.linalg import cholesky§L = cholesky(A, lower=True)",
    "qr(A)": "from scipy.linalg import qr§Q, R = qr(A)",
    "svd(A)": "from scipy.linalg import svd§U, s, Vt = svd(A)",
    "sum(v)": "np.sum(v)",
    "prod(v)": "np.prod(v)",
    "cumsum(v)": "np.cumsum(v)",
    "diff(v)": "np.diff(v)",
    "max(v)": "np.max(v)",
    "min(v)": "np.min(v)",
    "sort(v)": "np.sort(v)",
    "find(v)": "np.nonzero(v)",
    "interp1(x, y, xq)": "np.interp(xq, x, y)",
    "min(x, y)": "min(x, y)",
    "max(x, y)": "max(x, y)",
    "trapz(x, y)": "np.trapz(y, x)",
    "integral(@(x) x, a, b)": "from scipy.integrate import quad§I, err = quad(lambda x: x, a, b)",
    "fzero(@(x) x, x0)": "from scipy.optimize import fsolve§r = fsolve(lambda x: x, x0)",

    # bloques
    "for k = 1:n§    §end": "for k in range(n):§    ",
    "for k = 1:n&#10;    &#10;end": "for k in range(n):&#10;    ",
    "while cond§    §end": "while cond:§    ",
    "if cond§    §end": "if cond:§    ",
    "if cond§    §else§    §end": "if cond:§    §else:§    ",
    "if cond§    §elseif cond§    §else§    §end": "if cond:§    §elif cond:§    §else:§    ",
    "switch expr§    case a§        §    otherwise§        §end":
        "if expr == a:§    §elif expr == b:§    §else:§    ",
    "try§    §catch err§    §end": "try:§    §except Exception as err:§    ",
    "function y = f(x)§    §end": "def f(x):§    §    return y",

    # salida
    "disp(x)": "print(x)",
    "fprintf('%.4f\\n', x)": 'print(f"{x:.4f}")',
    "sprintf('%.4f', x)": 's = f"{x:.4f}"',
    "num2str(x)": "str(x)",
    "tic": "t0 = time.perf_counter()",
    "toc": "dt = time.perf_counter() - t0",
    "disp('==== TÍTULO ====')": '#" TITULO',
    "disp('=== Título ===')": "#\" Titulo",
    "disp('== Título ==')": "#' **Titulo**",
    "disp('= Título =')": "#' _Titulo_",
    "disp('texto')": "print('texto')",
    "disp('----------------------------------------')": "#'-----",
    "disp(' ')": "print()",

    # plantillas de salida con formato
    "x = 5;§fprintf('%s = %.4g\\n', 'x', x)": 'x = 5§print(f"x = {x:.4g}")',
    "L = 3.50;§fprintf('%s = %.2f %s\\n', 'L', L, 'm')": 'L = 3.50§print(f"L = {L:.2f} m")',
    "x = 5;§disp(['Resultado: ', num2str(x)])": "x = 5§print('Resultado:', x)",
    "x = 5;§s = sprintf('%s = %.4g', 'x', x)": 'x = 5§s = f"x = {x:.4g}"§s',
    "x = 3.14159;§fprintf('%.4f\\n', x)": 'x = 3.14159§print(f"{x:.4f}")',
    "valor = 12.3456;§fprintf('%-12s %10.3f\\n', 'nombre', valor)":
        'valor = 12.3456§print(f"{\'nombre\':<12s} {valor:10.3f}")',

    # gráficas: las del MOTOR (sin Python externo) y las de matplotlib (con Python real)
    "plot(x, y)": "import matplotlib.pyplot as plt§plt.plot(x, y)§plt.show()",
    "plot(x, y, '-o')": "plt.plot(x, y, '-o')",
    "hold on": "#' (matplotlib dibuja encima solo: no hace falta hold on)",
    "hold off": "plt.show()",
    "xlabel('x')": "plt.xlabel('x')",
    "ylabel('y')": "plt.ylabel('y')",
    "title('t')": "plt.title('t')",
    "legend('a', 'b')": "plt.legend(['a', 'b'])",
    "grid on": "plt.grid(True)",
    "axis equal": "plt.axis('equal')",
    "subplot(m, n, p)": "plt.subplot(m, n, p)",
    "scatter(x, y)": "plt.scatter(x, y)",
    "bar(y)": "plt.bar(range(len(y)), y)",
    "contourf(X, Y, Z)": "plt.contourf(X, Y, Z)§plt.colorbar()",
    "surf(X, Y, Z)": "mesh3d_viewer(X, Y, Z)",
    "mesh(X, Y, Z)": "mesh3d_viewer(X, Y, Z)",
    "colorbar": "plt.colorbar()",
    "xlabel('x')§ylabel('y')§title('Título')": "plt.xlabel('x')§plt.ylabel('y')§plt.title('Titulo')",
    "legend('curva 1', 'curva 2')": "plt.legend(['curva 1', 'curva 2'])",

    "x = linspace(0, 10, 100);§y = sin(x);§plot(x, y)§grid on":
        "import numpy as np§import matplotlib.pyplot as plt§x = np.linspace(0, 10, 100)§y = np.sin(x)§"
        "plt.plot(x, y)§plt.grid(True)§plt.show()",
    "x = 0:10;§y = x.^2;§plot(x, y, '-o')§grid on":
        "import numpy as np§import matplotlib.pyplot as plt§x = np.arange(0, 11)§y = x**2§"
        "plt.plot(x, y, '-o')§plt.grid(True)§plt.show()",
    "x = linspace(0, 2*pi, 100);§plot(x, sin(x))§hold on§plot(x, cos(x))§hold off§legend('sin', 'cos')":
        "import numpy as np§import matplotlib.pyplot as plt§x = np.linspace(0, 2*np.pi, 100)§"
        "plt.plot(x, np.sin(x))§plt.plot(x, np.cos(x))§plt.legend(['sin', 'cos'])§plt.show()",
    "x = rand(50, 1);§y = rand(50, 1);§scatter(x, y)":
        "import numpy as np§import matplotlib.pyplot as plt§x = np.random.rand(50)§y = np.random.rand(50)§"
        "plt.scatter(x, y)§plt.show()",
    "y = [3 7 2 5 4];§bar(y)":
        "import matplotlib.pyplot as plt§y = [3, 7, 2, 5, 4]§plt.bar(range(len(y)), y)§plt.show()",
    "datos = randn(1000, 1);§histogram(datos)":
        "import numpy as np§import matplotlib.pyplot as plt§datos = np.random.randn(1000)§"
        "plt.hist(datos, bins=30)§plt.show()",
    "n = 0:20;§stem(n, sin(n/3))":
        "import numpy as np§import matplotlib.pyplot as plt§n = np.arange(0, 21)§"
        "plt.stem(n, np.sin(n/3))§plt.show()",
    "[X, Y] = meshgrid(-3:0.2:3);§Z = X.^2 - Y.^2;§surf(X, Y, Z)§colorbar":
        "import numpy as np§X, Y = np.meshgrid(np.arange(-3, 3.01, 0.2), np.arange(-3, 3.01, 0.2))§"
        "Z = X**2 - Y**2§mesh3d_viewer(X, Y, Z)",
    "[X, Y] = meshgrid(-3:0.2:3);§Z = sin(sqrt(X.^2 + Y.^2));§mesh(X, Y, Z)":
        "import numpy as np§X, Y = np.meshgrid(np.arange(-3, 3.01, 0.2), np.arange(-3, 3.01, 0.2))§"
        "Z = np.sin(np.sqrt(X**2 + Y**2))§mesh3d_viewer(X, Y, Z)",
    "[X, Y] = meshgrid(-3:0.2:3);§Z = X.^2 - Y.^2;§contourf(X, Y, Z)§colorbar":
        "import numpy as np§import matplotlib.pyplot as plt§"
        "X, Y = np.meshgrid(np.arange(-3, 3.01, 0.2), np.arange(-3, 3.01, 0.2))§Z = X**2 - Y**2§"
        "plt.contourf(X, Y, Z)§plt.colorbar()§plt.show()",
    "t = linspace(0, 10*pi, 200);§plot3(sin(t), cos(t), t)§grid on":
        "import numpy as np§import matplotlib.pyplot as plt§t = np.linspace(0, 10*np.pi, 200)§"
        "ax = plt.figure().add_subplot(projection='3d')§ax.plot(np.sin(t), np.cos(t), t)§plt.show()",
    "[X, Y] = meshgrid(-2:0.5:2);§quiver(X, Y, -Y, X)":
        "import numpy as np§import matplotlib.pyplot as plt§"
        "X, Y = np.meshgrid(np.arange(-2, 2.01, 0.5), np.arange(-2, 2.01, 0.5))§"
        "plt.quiver(X, Y, -Y, X)§plt.show()",

    # bucles y control, con ejemplo
    "for k = 1:5§    disp(k)§end": "for k in range(1, 6):§    print(k)",
    "s = 0;§for k = 1:10§    s = s + k;§end§s": "s = 0§for k in range(1, 11):§    s += k§s",
    "for i = 1:3§    for j = 1:3§        disp([i, j])§    end§end":
        "for i in range(1, 4):§    for j in range(1, 4):§        print(i, j)",
    "k = 1;§while k &lt;= 5§    disp(k)§    k = k + 1;§end": "k = 1§while k &lt;= 5:§    print(k)§    k += 1",
    "x = 5;§if x &gt; 0§    disp('positivo')§end": "x = 5§if x &gt; 0:§    print('positivo')",
    "x = -2;§if x &gt; 0§    disp('positivo')§else§    disp('negativo')§end":
        "x = -2§if x &gt; 0:§    print('positivo')§else:§    print('negativo')",
    "x = 0;§if x &gt; 0§    disp('positivo')§elseif x &lt; 0§    disp('negativo')§else§    disp('cero')§end":
        "x = 0§if x &gt; 0:§    print('positivo')§elif x &lt; 0:§    print('negativo')§else:§    print('cero')",
    "n = 2;§switch n§    case 1§        disp('uno')§    case 2§        disp('dos')§    otherwise§        disp('otro')§end":
        "n = 2§if n == 1:§    print('uno')§elif n == 2:§    print('dos')§else:§    print('otro')",
    "try§    error('algo falló')§catch err§    disp(err.message)§end":
        "try:§    raise ValueError('algo fallo')§except Exception as err:§    print(err)",
    "for k = 1:10§    if k &gt; 5§        break§    end§    disp(k)§end":
        "for k in range(1, 11):§    if k &gt; 5:§        break§    print(k)",
    "function y = mifun(x)§    y = x^2;§end": "def mifun(x):§    return x**2",
    "function [s, p] = sumprod(a, b)§    s = a + b;§    p = a * b;§end":
        "def sumprod(a, b):§    return a + b, a * b",
    "classdef Punto§    properties§        x§        y§    end§    methods§        function obj = Punto(x, y)§"
    "            obj.x = x;§            obj.y = y;§        end§        function d = norma(obj)§"
    "            d = sqrt(obj.x^2 + obj.y^2);§        end§    end§end":
        "import math§class Punto:§    def __init__(self, x, y):§        self.x = x§        self.y = y§§"
        "    def norma(self):§        return math.sqrt(self.x**2 + self.y**2)",

    # cálculo numérico
    "f = @(x) x.^2;§a = 0; b = 1;§I = integral(f, a, b)":
        "from scipy.integrate import quad§f = lambda x: x**2§a, b = 0, 1§I, err = quad(f, a, b)§I",
    "x = linspace(0, 1, 100);§y = x.^2;§area = trapz(x, y)":
        "import numpy as np§x = np.linspace(0, 1, 100)§y = x**2§area = np.trapz(y, x)§area",
    "a = 0; b = 1; n = 100;§h = (b - a)/n;§s = 0;§for k = 1:n§    x0 = a + (k-1)*h;§    x1 = a + k*h;§"
    "    s = s + 0.5*h*(x0^2 + x1^2);§end§s":
        "a, b, n = 0, 1, 100§h = (b - a)/n§s = 0§for k in range(1, n+1):§    x0 = a + (k-1)*h§"
        "    x1 = a + k*h§    s += 0.5*h*(x0**2 + x1**2)§s",
    "syms x§I_g = gaussint(x^2, x, 0, 1)": "#cp[§#' $Int{x^2 @ x = 0 : 1}§#cp]",
    "s = sum((1:100).^2)": "import numpy as np§s = np.sum(np.arange(1, 101)**2)§s",
    "s = 0;§for k = 1:100§    s = s + k^2;§end§s": "s = 0§for k in range(1, 101):§    s += k**2§s",
    "p = prod(1:5)": "import numpy as np§p = np.prod(np.arange(1, 6))§p",
    "x = linspace(0, 2*pi, 100);§y = sin(x);§dydx = gradient(y, x);§plot(x, dydx)":
        "import numpy as np§import matplotlib.pyplot as plt§x = np.linspace(0, 2*np.pi, 100)§y = np.sin(x)§"
        "dydx = np.gradient(y, x)§plt.plot(x, dydx)§plt.show()",
    "v = [1 4 9 16];§d = diff(v)": "import numpy as np§v = np.array([1, 4, 9, 16])§d = np.diff(v)§d",
    "f = @(x) x^2 - 2;§x0 = 1;§r = fzero(f, x0)":
        "from scipy.optimize import brentq§f = lambda x: x**2 - 2§r = brentq(f, 0, 2)§r",
    "df__dx = diff(f, x)": "df_dx = np.gradient(f, x)",

    # simbólico: en Hekatan Python lo hace el motor de Calcpad dentro de un bloque #cp[
    "%' Declarar x como variable simbólica:§syms x": "#' Formula simbolica inline:§#cp x^2 + 2*x",
    "%' Derivada de f(x) = x³ + 2x respecto a x:§syms x§f(x) = x^3 + 2*x§df = diff(f(x), x)":
        "#' Derivada de f(x) = x^3 + 2x:§#cp[§#f(x) = x^3 + 2*x§#df = $Derivative{f(x) @ x = 2}§#cp]",
    "%' Integral indefinida de f(x) = x²:§syms x§f(x) = x^2§F = int(f(x), x)":
        "#' Integral de f(x) = x^2 entre 0 y 1:§#cp[§#f(x) = x^2§#I = $Int{f(x) @ x = 0 : 1}§#cp]",
    "%' Integral definida de f(x) = x² entre 0 y 1:§syms x§f(x) = x^2§I = int(f(x), x, 0, 1)":
        "#cp[§#f(x) = x^2§#I = $Int{f(x) @ x = 0 : 1}§#cp]",
    "%' Límite de f(x) = sin(x)/x cuando x → 0:§syms x§f(x) = sin(x)/x§L = limit(f(x), x, 0)":
        "#' Limite numerico de sin(x)/x cuando x tiende a 0:§import math§x = 1e-9§L = math.sin(x)/x§L",
    "%' Serie de Taylor de f(x) = eˣ alrededor de x = 0:§syms x§f(x) = exp(x)§T = taylor(f(x), x, 0)":
        "#' Serie de Taylor de e^x (5 terminos):§import math§x = 0.5§"
        "T = sum(x**k/math.factorial(k) for k in range(5))§T",
    "%' Simplificar f(x) = (x²−1)/(x−1):§syms x§f(x) = (x^2 - 1)/(x - 1)§s = simplify(f(x))":
        "#cp[§#f(x) = (x^2 - 1)/(x - 1)§#f(3)§#cp]",
    "%' Expandir f(x) = (x + 1)³:§syms x§f(x) = (x + 1)^3§e = expand(f(x))":
        "#cp[§#f(x) = (x + 1)^3§#f(2)§#cp]",
    "%' Factorizar f(x) = x²−1:§syms x§f(x) = x^2 - 1§fa = factor(f(x))":
        "import numpy as np§raices = np.roots([1, 0, -1])§raices",
    "%' Resolver f(x) = 0 con f(x) = x² − 4:§syms x§f(x) = x^2 - 4§sol = solve(f(x) == 0, x)":
        "from scipy.optimize import brentq§f = lambda x: x**2 - 4§r = brentq(f, 0, 5)§r",
    "%' Sustituir x = 3 en f(x) = x²:§syms x§f(x) = x^2§v = subs(f(x), x, 3)":
        "def f(x):§    return x**2§v = f(3)§v",
    "%' Jacobiano de F = [x·y, x + y] respecto a [x, y]:§syms x y§F = [x*y, x + y]§J = jacobian(F, [x, y])":
        "import numpy as np§#' Jacobiano numerico de F = [x*y, x + y] en (x, y):§x, y = 2.0, 3.0§"
        "J = np.array([[y, x], [1.0, 1.0]])§J",
    "%' Exportar f(x) = x² + 1 a código LaTeX:§syms x§f(x) = x^2 + 1§L = latex(f(x))":
        "#' La hoja ya sale en LaTeX: escribe la formula en un bloque #cp[§#cp[§#f(x) = x^2 + 1§#cp]",

    # marcado de la hoja: %' → #'
    "%'&lt;div&gt;§%'&lt;/div&gt;": "#'&lt;div&gt;§#'&lt;/div&gt;",
    "%&quot; Titulo de seccion": "#&quot; Titulo de seccion",
    "%&quot;{negro} Titulo en negro": "#&quot;{negro} Titulo en negro",
    "%'-----": "#'-----",
    "%'&lt; texto a la izquierda": "#'&lt; texto a la izquierda",
    "%'| texto centrado": "#'| texto centrado",
    "%'&gt; texto a la derecha": "#'&gt; texto a la derecha",
    "%'* texto en negrita": "#'* texto en negrita",
    "%'/ texto en italica": "#'/ texto en italica",
    "%'_ texto subrayado": "#'_ texto subrayado",
    "%'{center,azul,negrita} texto centrado azul y negrita": "#'{center,azul,negrita} texto centrado azul y negrita",
    "  %' ms": "  #' ms",
    "%'\\&gt; muestra el signo &gt; literal": "#'\\&gt; muestra el signo &gt; literal",
}

# lo que queda: cualquier otro Tag que empiece por %' o %" es marcado de hoja → # equivalente
GENERICOS = [(re.compile(r'^%\''), "#'"), (re.compile(r'^%&quot;'), "#&quot;")]


def traducir(s):
    cambios = 0
    for viejo, nuevo in sorted(TAGS.items(), key=lambda kv: -len(kv[0])):
        marca = 'Tag="%s"' % viejo
        if marca in s:
            s = s.replace(marca, 'Tag="%s"' % nuevo)
            cambios += 1
    # los marcados de hoja que quedan (%' … / %" …) van a # de una pasada
    def sub(m):
        t = m.group(1)
        for rx, rep in GENERICOS:
            if rx.match(t):
                return 'Tag="%s"' % rx.sub(rep, t).replace("§%'", "§#'")
        return m.group(0)
    s = re.sub(r'Tag="([^"]*)"', sub, s)
    return s, cambios


def pendientes(s):
    malos = []
    for m in re.finditer(r'Tag="([^"]*)"', s):
        t = m.group(1)
        if re.match(r'^\s*%', t) or re.search(r'\b(disp|fprintf|sprintf|num2str|elseif|otherwise|syms|endfunction)\s*\(?', t) \
           or re.search(r'§\s*end\b|^end\b', t):
            malos.append(t)
    return malos


def main():
    s = XAML.read_text(encoding="utf-8-sig")
    if "--revisar" in sys.argv:
        for t in pendientes(s):
            print("  PENDIENTE:", t.encode("ascii", "replace").decode()[:110])
        return
    s2, n = traducir(s)
    XAML.write_text(s2, encoding="utf-8-sig")
    print(f"{n} botones traducidos a Python")
    resto = pendientes(s2)
    print(f"quedan {len(resto)} tags con pinta de MATLAB:")
    for t in resto:
        print("   ", t.encode("ascii", "replace").decode()[:110])


if __name__ == "__main__":
    main()
