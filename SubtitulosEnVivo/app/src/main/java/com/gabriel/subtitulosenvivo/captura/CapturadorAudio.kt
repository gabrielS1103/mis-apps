package com.gabriel.subtitulosenvivo.captura

import android.annotation.SuppressLint
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.projection.MediaProjection
import com.gabriel.subtitulosenvivo.Registro

/**
 * Lee el audio que reproducen otras apps (no el micrófono) en PCM 16 kHz mono,
 * que es el formato que espera Whisper.
 *
 * Solo llega audio de apps que permiten la captura: si una app la bloquea, se reciben ceros.
 */
class CapturadorAudio(
    private val proyeccion: MediaProjection,
    /** UID de la única app a capturar, o null para todas. */
    private val uidFiltro: Int?,
    private val alRecibir: (ShortArray, Int) -> Unit,
) {
    @Volatile private var corriendo = false
    private var grabador: AudioRecord? = null
    private var hilo: Thread? = null

    /** Devuelve false si Android no dejó crear la grabación. */
    @SuppressLint("MissingPermission") // RECORD_AUDIO se comprueba en la pantalla principal antes de arrancar
    fun iniciar(): Boolean {
        val config = AudioPlaybackCaptureConfiguration.Builder(proyeccion).apply {
            if (uidFiltro != null) addMatchingUid(uidFiltro)
            addMatchingUsage(AudioAttributes.USAGE_MEDIA)
            addMatchingUsage(AudioAttributes.USAGE_GAME)
            addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
        }.build()
        val formato = AudioFormat.Builder()
            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
            .setSampleRate(FRECUENCIA)
            .setChannelMask(AudioFormat.CHANNEL_IN_MONO)
            .build()
        val minimo = AudioRecord.getMinBufferSize(FRECUENCIA, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT)
        val g = try {
            AudioRecord.Builder()
                .setAudioFormat(formato)
                .setBufferSizeInBytes(maxOf(minimo, BLOQUE * 2 * 4))
                .setAudioPlaybackCaptureConfig(config)
                .build()
        } catch (e: Exception) {
            Registro.error(Registro.CAPTURA, "No se pudo crear AudioRecord", e)
            return false
        }
        if (g.state != AudioRecord.STATE_INITIALIZED) {
            Registro.error(Registro.CAPTURA, "AudioRecord no se inicializó (estado ${g.state})")
            g.release()
            return false
        }
        grabador = g
        g.startRecording()
        corriendo = true
        hilo = Thread({ bucle(g) }, "captura-audio").apply { start() }
        Registro.info(Registro.CAPTURA, "Captura iniciada a ${g.sampleRate} Hz, filtro=${uidFiltro ?: "todas"}")
        return true
    }

    private fun bucle(g: AudioRecord) {
        val bloque = ShortArray(BLOQUE)
        while (corriendo) {
            val leidos = g.read(bloque, 0, bloque.size)
            if (leidos < 0) {
                Registro.error(Registro.CAPTURA, "Error al leer audio: $leidos")
                break
            }
            if (leidos > 0) alRecibir(bloque, leidos)
        }
    }

    fun detener() {
        corriendo = false
        grabador?.let { g ->
            try { g.stop() } catch (_: Exception) {}
            hilo?.join(500)
            g.release()
        }
        grabador = null
        hilo = null
    }

    companion object {
        const val FRECUENCIA = 16_000
        /** 100 ms por bloque. */
        const val BLOQUE = FRECUENCIA / 10
    }
}
