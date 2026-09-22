#"Prueba: usar # no dificulta nada
#'Casos limite para confirmar que Python real NUNCA se rompe y Hekatan no mal-renderiza.
import numpy as np

# comentario normal (debe ser INVISIBLE en Hekatan, ignorado en Python)
x = 10          # comentario al final de linea (trailing)
y = 20  # otro trailing
z = x + y
print("z =", z)

# comentario que empieza con palabra reservada -> revisar si se cuela:
# show esto es solo un comentario normal en ingles
# hidden feature (empieza con 'hid' no 'hide')
# cpu y memoria (empieza con 'cp' -> OJO posible mal-render)

s = "# esto NO es comentario, es texto dentro de un string"
print(s)

def f(n):
    #'Comentario-directiva DENTRO de una funcion (debe verse en Hekatan):
    total = 0
    for i in range(n):
        total += i   # acumulador
    return total

print("f(5) =", f(5))

#'Un `#` dentro de codigo inline: el color `#ff0000` va en un string, no rompe.
color = "#ff0000"
print("color =", color)

#'Fin: si Python imprimio z=30, f(5)=10 y los strings, entonces # no dificulta nada.
