package com.gabriel.letrasenvivo

import android.util.Log
import java.text.SimpleDateFormat
import java.util.ArrayDeque
import java.util.Date
import java.util.Locale

/** Log de la app: escribe en logcat y guarda las últimas líneas para la pantalla de depuración. */
object Registro {
    const val APP = "LetrasEnVivo"
    const val SESION = "LEV/Sesion"
    const val LRCLIB = "LEV/Lrclib"
    const val NETEASE = "LEV/NetEase"
    const val KUGOU = "LEV/Kugou"
    const val LT = "LEV/LT"
    const val ALINEADOR = "LEV/Alineador"
    const val OVERLAY = "LEV/Overlay"
    const val PANEL = "LEV/Panel"
    const val CACHE = "LEV/Cache"
    const val MLKIT = "LEV/MlKit"
    const val RED = "LEV/Red"
    const val GENERO = "LEV/Genero"

    private const val MAXIMO = 400
    private val lineas = ArrayDeque<String>(MAXIMO)
    private val formato = SimpleDateFormat("HH:mm:ss.SSS", Locale.US)

    @Volatile var alCambiar: (() -> Unit)? = null

    fun info(etiqueta: String, mensaje: String) { Log.i(etiqueta, mensaje); guardar("I", etiqueta, mensaje) }
    fun aviso(etiqueta: String, mensaje: String) { Log.w(etiqueta, mensaje); guardar("W", etiqueta, mensaje) }
    fun error(etiqueta: String, mensaje: String, e: Throwable? = null) {
        Log.e(etiqueta, mensaje, e)
        guardar("E", etiqueta, if (e != null) "$mensaje: ${e.javaClass.simpleName}: ${e.message}" else mensaje)
    }

    private fun guardar(nivel: String, etiqueta: String, mensaje: String) {
        synchronized(lineas) {
            if (lineas.size >= MAXIMO) lineas.removeFirst()
            lineas.addLast("${formato.format(Date())} $nivel/${etiqueta.removePrefix("LEV/")}: $mensaje")
        }
        alCambiar?.invoke()
    }

    fun volcar(): String = synchronized(lineas) { lineas.joinToString("\n") }
}
