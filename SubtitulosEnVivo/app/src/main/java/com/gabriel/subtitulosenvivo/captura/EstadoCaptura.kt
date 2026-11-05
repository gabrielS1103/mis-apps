package com.gabriel.subtitulosenvivo.captura

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/** Estado compartido entre el servicio y la pantalla principal. */
object EstadoCaptura {

    data class Nivel(
        /** Nivel del último bloque de 100 ms, en dBFS (−90 = silencio). */
        val actualDb: Float = NivelAudio.SILENCIO_DB,
        val maximoDb: Float = NivelAudio.SILENCIO_DB,
        /** Segundos acumulados con sonido por encima del umbral desde que empezó la captura. */
        val segundosConSonido: Float = 0f,
    )

    internal val _activa = MutableStateFlow(false)
    val activa: StateFlow<Boolean> = _activa

    internal val _nivel = MutableStateFlow(Nivel())
    val nivel: StateFlow<Nivel> = _nivel

    /** Qué se está capturando: "Facebook" o "todas las apps". */
    internal val _origen = MutableStateFlow<String?>(null)
    val origen: StateFlow<String?> = _origen

    /** Últimos 10 s capturados, para exportarlos y escucharlos. */
    val muestra = MuestraCircular(CapturadorAudio.FRECUENCIA * 10)

    data class Frase(
        val ingles: String,
        /** null si la traducción falló (por ejemplo, sin el modelo de ML Kit). */
        val espanol: String?,
        /** Duración del audio de la frase. */
        val audioS: Float,
        /** Cuánto tardó Whisper en transcribirla. */
        val whisperS: Float,
    )

    /** Últimas frases reconocidas, la más reciente al final. */
    internal val _frases = MutableStateFlow<List<Frase>>(emptyList())
    val frases: StateFlow<List<Frase>> = _frases

    internal fun agregarFrase(f: Frase) {
        _frases.value = (_frases.value + f).takeLast(MAXIMO_FRASES)
    }

    private const val MAXIMO_FRASES = 30
}
