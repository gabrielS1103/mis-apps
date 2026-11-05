package com.gabriel.subtitulosenvivo.voz

/** Funciones de whisper.cpp expuestas por libvoz.so (ver cpp/whisper_jni.cpp). */
internal object WhisperNativo {
    init { System.loadLibrary("voz") }

    /** Carga el modelo; devuelve 0 si falla. */
    external fun iniciar(ruta: String): Long
    external fun liberar(ctx: Long)
    /** [audio]: PCM float 16 kHz mono. Devuelve null si whisper falla. */
    external fun transcribir(ctx: Long, audio: FloatArray, prompt: String?, hilos: Int): String?
    external fun infoSistema(): String

    /** Detector de voz Silero; devuelve 0 si falla. */
    external fun vadIniciar(ruta: String): Long
    external fun vadLiberar(vctx: Long)
    /** Probabilidad de voz por cada 512 muestras; [audio] en orden y en múltiplos de 512. Null si falla. */
    external fun vadProbabilidades(vctx: Long, audio: FloatArray): FloatArray?
}
