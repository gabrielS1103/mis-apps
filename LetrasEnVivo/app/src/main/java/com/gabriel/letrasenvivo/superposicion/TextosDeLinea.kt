package com.gabriel.letrasenvivo.superposicion

import com.gabriel.letrasenvivo.ajustes.ModoVisualizacion
import com.gabriel.letrasenvivo.letras.modelos.LineaLetra

/**
 * Decide qué textos se muestran para una línea según el modo elegido: arriba (blanco) la traducción,
 * abajo (amarillo) el original. null = no mostrar ese renglón.
 */
object TextosDeLinea {
    fun para(l: LineaLetra, modo: ModoVisualizacion): Pair<String?, String?> {
        if (l.original.isBlank()) return "♪" to null
        return when (modo) {
            ModoVisualizacion.AMBOS -> l.traducida to l.original
            ModoVisualizacion.SOLO_ORIGINAL -> null to l.original
            // Sin traducción se cae al original para no dejar la línea vacía.
            ModoVisualizacion.SOLO_ESPANOL -> if (l.traducida != null) l.traducida to null else null to l.original
        }
    }

    /** El renglón que representa a la línea en una sola fila (la vista previa de la siguiente). */
    fun principal(l: LineaLetra, modo: ModoVisualizacion): String? = para(l, modo).let { it.first ?: it.second }
}
