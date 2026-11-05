package com.gabriel.subtitulosenvivo.voz

import com.gabriel.subtitulosenvivo.Registro
import kotlinx.coroutines.asCoroutineDispatcher
import kotlinx.coroutines.withContext
import java.io.Closeable
import java.io.File
import java.util.concurrent.Executors

/**
 * Envoltorio de un contexto de whisper.cpp. Todas las llamadas nativas van por un único hilo:
 * whisper no admite dos transcripciones a la vez sobre el mismo contexto, y así liberar
 * nunca coincide con una transcripción en curso.
 */
class Reconocedor private constructor(private val ctx: Long) : Closeable {
    private val ejecutor = Executors.newSingleThreadExecutor { Thread(it, "whisper") }
    private val hilo = ejecutor.asCoroutineDispatcher()

    suspend fun transcribir(audio: FloatArray, prompt: String?): String? = withContext(hilo) {
        WhisperNativo.transcribir(ctx, audio, prompt, HILOS)
    }

    override fun close() {
        ejecutor.execute { WhisperNativo.liberar(ctx) }
        ejecutor.shutdown()
    }

    companion object {
        /** Los 4 núcleos rápidos del S21; más hilos usarían los lentos y no ayuda. */
        private const val HILOS = 4

        /** Tarda ~1 s: llamar fuera del hilo principal. */
        fun cargar(modelo: File): Reconocedor? {
            val ctx = WhisperNativo.iniciar(modelo.path)
            if (ctx == 0L) {
                Registro.error(Registro.VOZ, "whisper no pudo cargar ${modelo.name}")
                return null
            }
            Registro.info(Registro.VOZ, "Modelo cargado. ${WhisperNativo.infoSistema().trim()}")
            return Reconocedor(ctx)
        }
    }
}
