#"Parser de Calcpad dentro de Hekatan Python
#'Este archivo es **Python real** (corre con `python`) y a la vez una **hoja Hekatan**.
#'Las lineas `#'` `#"` `#cp` son comentarios en Python, pero markup Calcpad en Hekatan.
import numpy as np
import math

#"1. Texto Markdown con #'
#'Soporta **negrita**, *cursiva*, listas y tablas:
#'- energia cinetica  E = ½·m·v²
#'- se ignora en Python real, se ve en Hekatan
#'
#'| simbolo | significado |
#'|---------|-------------|
#'| σ       | tension     |
#'| φ       | friccion    |

#"2. Formula simbolica con #cp (AngouriMath)
#'La hipotenusa (renderizada con raiz y potencias):
#cp c = sqrt(a^2 + b^2)

# --- computo en Python real ---
a, b = 3.0, 4.0
c = math.sqrt(a**2 + b**2)
print("Hipotenusa c =", c)

#"3. Bloque Calcpad completo con #cp[ ... #cp]
#'Serie de Fourier de una onda cuadrada, evaluada por el motor Calcpad:
#cp[
#'Suma parcial (9 terminos) en x = pi/2:
#f(x) = $Sum{sin((2*k - 1)*x)/(2*k - 1) @ k = 1 : 9}
#f(1.5707963)
#cp]

#"4. El mismo calculo en Python (numpy) para comparar
x = math.pi / 2
f_py = sum(math.sin((2*k - 1)*x) / (2*k - 1) for k in range(1, 10))
print("Serie en Python  =", round(f_py, 6))
print("Valor exacto pi/4 =", round(math.pi/4, 6))

#'**Conclusion:** un solo archivo, dos vistas — codigo que corre y reporte que se lee.
