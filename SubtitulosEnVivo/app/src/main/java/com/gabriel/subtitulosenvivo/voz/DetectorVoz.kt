package com.gabriel.subtitulosenvivo.voz

import android.content.Context
import com.gabriel.subtitulosenvivo.Registro
import java.io.Closeable
import java.io.File

/**
 * Detector de voz Silero (viene con whisper.cpp). A diferencia del nivel de volumen, distingue a alguien
 * hablando de la música de fondo, así que la música sola ya no llega a Whisper (que con música inventa frases).
 *
 * No es seguro entre hilos: se usa siempre desde la misma corrutina, en orden.
 */
class DetectorVoz private constructor(private val vctx: Long) : Closeable {
    /** Muestras que sobran de un bloque hasta completar la siguiente ventana de 512. */
    private var pendiente = FloatArray(0)
    private var ultima = 0f

    /** Probabilidad de voz (0–1) de un bloque: la más alta de las ventanas que se completaron con él. */
    fun probabilidad(bloque: ShortArray, cantidad: Int): Float {
        val total = pendiente.size + cantidad
        val usar = total / VENTANA * VENTANA
        val audio = FloatArray(total)
        pendiente.copyInto(audio)
        for (i in 0 until cantidad) audio[pendiente.size + i] = bloque[i] / 32768f
        pendiente = audio.copyOfRange(usar, total)
        if (usar == 0) return ultima
        val probs = WhisperNativo.vadProbabilidades(vctx, audio.copyOf(usar)) ?: return ultima
        ultima = probs.max()
        return ultima
    }

    override fun close() = WhisperNativo.vadLiberar(vctx)

    companion object {
        private const val VENTANA = 512
        private const val ARCHIVO = "ggml-silero-v5.1.2.bin"

        /** El modelo (0,9 MB) viene dentro de la app; whisper.cpp necesita una ruta, así que se copia a filesDir. */
        fun cargar(context: Context): DetectorVoz? {
            val destino = File(context.filesDir, "modelos/$ARCHIVO")
            try {
                if (!destino.exists()) {
                    destino.parentFile?.mkdirs()
                    val temporal = File(destino.path + ".copiando")
                    context.assets.open(ARCHIVO).use { entrada -> temporal.outputStream().use { entrada.copyTo(it) } }
                    temporal.renameTo(destino)
                }
            } catch (e: Exception) {
                Registro.error(Registro.VOZ, "No se pudo preparar el detector de voz", e)
                return null
            }
            val vctx = WhisperNativo.vadIniciar(destino.path)
            if (vctx == 0L) {
                Registro.error(Registro.VOZ, "whisper no pudo cargar el detector de voz")
                return null
            }
            return DetectorVoz(vctx)
        }
    }
}
