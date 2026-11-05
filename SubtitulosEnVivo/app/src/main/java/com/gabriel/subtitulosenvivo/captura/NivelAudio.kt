package com.gabriel.subtitulosenvivo.captura

import kotlin.math.log10
import kotlin.math.sqrt

/** Cálculo del volumen de un bloque de muestras PCM de 16 bits. */
object NivelAudio {
    const val SILENCIO_DB = -90f
    /** Por encima de esto se considera que hay sonido (voz o música a volumen normal). */
    const val UMBRAL_SONIDO_DB = -50f

    /** Nivel RMS en dBFS: 0 = máximo posible, [SILENCIO_DB] = silencio digital. */
    fun rmsDb(muestras: ShortArray, cantidad: Int = muestras.size): Float {
        if (cantidad <= 0) return SILENCIO_DB
        var suma = 0.0
        for (i in 0 until cantidad) {
            val v = muestras[i] / 32768.0
            suma += v * v
        }
        val rms = sqrt(suma / cantidad)
        if (rms <= 0.0) return SILENCIO_DB
        return (20 * log10(rms)).toFloat().coerceAtLeast(SILENCIO_DB)
    }
}
