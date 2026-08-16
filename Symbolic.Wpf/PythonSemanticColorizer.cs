using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Calcpad.Core.Python;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Calcpad.Wpf;

/// <summary>
/// Pinta lo que el usuario declaro EN SU ARCHIVO: sus variables con color propio, y sus
/// funciones y clases en negrita. El <c>Python.xshd</c> no puede hacerlo: solo conoce listas
/// fijas de palabras (keywords, constantes, los builtins del motor). Esto es lo que el editor
/// clasico hacia con <c>HighLighter</c> + <c>UserDefined</c>.
///
/// Se aplica DESPUES del resaltado de sintaxis, asi que solo toca los nombres que el .xshd
/// dejo sin clasificar: si la palabra es keyword, constante o funcion del motor, no se toca.
///
/// Quien lee los simbolos es el MOTOR (<see cref="PythonSymbols"/>), no esta clase: la piel
/// solo elige colores.
/// </summary>
internal sealed class PythonSemanticColorizer : DocumentColorizingTransformer
{
    /// <summary>Lo que el .xshd ya pinta. Se arma una vez y como conjunto: mirarlo por cada
    /// palabra de cada linea tiene que ser barato.</summary>
    private static readonly HashSet<string> YaPintadas = new(
        System.Linq.Enumerable.Concat(
            System.Linq.Enumerable.Concat(PythonBuiltins.All, PythonBuiltins.Keywords),
            PythonBuiltins.Constants),
        StringComparer.Ordinal);

    private Dictionary<string, SimboloTipo> _mapa = new(StringComparer.Ordinal);
    private HashSet<int> _lineasDeCadena = new();
    private Brush _variable, _funcion, _parametro, _modulo;

    internal PythonSemanticColorizer() => AplicarTema(false);

    /// <summary>Mismos colores que usa el popup del autocompletado, para que lo que ves en la
    /// lista y lo que ves en el codigo sean lo mismo.</summary>
    internal void AplicarTema(bool oscuro)
    {
        _variable = Pincel(oscuro ? "#98C379" : "#3F7A2E");
        _funcion = Pincel(oscuro ? "#E5E5E5" : "#1A1A1A");
        _parametro = Pincel(oscuro ? "#D19A66" : "#8A5A00");
        _modulo = Pincel(oscuro ? "#56B6C2" : "#0B7285");
    }

    /// <summary>Cambia la tabla de nombres. La ultima declaracion gana (una variable
    /// reasignada sigue siendo variable). <paramref name="lineasDeCadena"/> son las lineas de
    /// dentro de un docstring: ahi no se pinta nada, es texto.</summary>
    internal void Actualizar(IReadOnlyList<Simbolo> simbolos, HashSet<int> lineasDeCadena)
    {
        var mapa = new Dictionary<string, SimboloTipo>(StringComparer.Ordinal);
        if (simbolos is not null)
            foreach (var s in simbolos)
            {
                // Una funcion o clase manda sobre una variable del mismo nombre: es lo que se llama.
                if (mapa.TryGetValue(s.Nombre, out var previo) &&
                    previo is SimboloTipo.Funcion or SimboloTipo.Clase) continue;
                mapa[s.Nombre] = s.Tipo;
            }
        _mapa = mapa;
        _lineasDeCadena = lineasDeCadena ?? new HashSet<int>();
    }

    protected override void ColorizeLine(DocumentLine linea)
    {
        if (_mapa.Count == 0 || linea.Length == 0) return;
        if (_lineasDeCadena.Contains(linea.LineNumber)) return;   // dentro de un docstring

        var texto = CurrentContext.Document.GetText(linea);
        foreach (var p in PythonSymbols.Identificadores(texto))
        {
            if (!_mapa.TryGetValue(p.Nombre, out var tipo)) continue;
            if (YaPintadas.Contains(p.Nombre)) continue;   // ya lo pinto el .xshd

            var (color, negrita) = tipo switch
            {
                SimboloTipo.Funcion => (_funcion, true),
                SimboloTipo.Clase => (_funcion, true),
                SimboloTipo.Parametro => (_parametro, false),
                SimboloTipo.Modulo => (_modulo, false),
                _ => (_variable, false),
            };

            ChangeLinePart(linea.Offset + p.Inicio, linea.Offset + p.Inicio + p.Largo, e =>
            {
                e.TextRunProperties.SetForegroundBrush(color);
                if (negrita)
                    e.TextRunProperties.SetTypeface(new Typeface(
                        e.TextRunProperties.Typeface.FontFamily, FontStyles.Normal,
                        FontWeights.Bold, FontStretches.Normal));
            });
        }
    }

    private static Brush Pincel(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
