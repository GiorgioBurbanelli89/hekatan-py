# %% Variables y aritmética
# Hekatan Py — el motor Python nativo en C# muestra cada asignación
# como una línea de hoja de cálculo: nombre = valor.

# Datos de entrada
a = 12
b = 5

# Operaciones básicas
suma = a + b
resta = a - b
producto = a * b
division = a / b          # división real → float
division_entera = a // b  # floor division
resto = a % b
potencia = a ** 2

# Notación de ingeniería
E = 200000.0   # módulo de Young [MPa]
I = 8.5e7      # inercia [mm^4]
L = 6000.0     # longitud [mm]

# Expresión suelta (se muestra como "expr = valor")
E * I / L**3

# El MISMO archivo corre en Python real, donde no hay hoja de cálculo: ahí lo
# único que se ve es lo que se imprime, así que el resumen va con print().
print("a = %d   b = %d" % (a, b))
print("suma = %d   resta = %d   producto = %d" % (suma, resta, producto))
print("division = %.4g   division_entera = %d   resto = %d   potencia = %d"
      % (division, division_entera, resto, potencia))
print("rigidez E*I/L^3 = %.5f kN/mm" % (E * I / L**3))
