package com.gabriel.subtitulosenvivo

import android.util.Log
import java.io.File
import java.text.SimpleDateFormat
import java.util.ArrayDeque
import java.util.Date
import java.util.Locale

/** Log de la app: escribe en logcat, guarda las últimas líneas para mostrarlas en pantalla y las copia
 *  a files/registro.txt (en el S21 de pruebas logcat no muestra los logs de las apps; el archivo se lee con
 *  `adb shell run-as com.gabriel.subtitulosenvivo cat files/registro.txt`). */
object Registro {
    const val APP = "SubtitulosEnVivo"
    const val CAPTURA = "SEV/Captura"
    const val VOZ = "SEV/Voz"
    const val TRADUCCION = "SEV/Traduccion"
    const val OVERLAY = "SEV/Overlay"

    private const val MAXIMO = 200
    private val lineas = ArrayDeque<String>(MAXIMO)
    private val formato = SimpleDateFormat("HH:mm:ss.SSS", Locale.US)

    @Volatile var alCambiar: (() -> Unit)? = null
    @Volatile private var archivo: File? = null

    /** Se llama al iniciar la app; empieza un archivo nuevo si el anterior pasó de 1 MB. */
    fun usarArchivo(carpeta: File) {
        val a = File(carpeta, "registro.txt")
        if (a.length() > 1_000_000) a.delete()
        archivo = a
    }

    fun info(etiqueta: String, mensaje: String) { Log.i(etiqueta, mensaje); guardar("I", etiqueta, mensaje) }
    fun aviso(etiqueta: String, mensaje: String) { Log.w(etiqueta, mensaje); guardar("W", etiqueta, mensaje) }
    fun error(etiqueta: String, mensaje: String, e: Throwable? = null) {
        Log.e(etiqueta, mensaje, e)
        guardar("E", etiqueta, if (e != null) "$mensaje: ${e.javaClass.simpleName}: ${e.message}" else mensaje)
    }

    private fun guardar(nivel: String, etiqueta: String, mensaje: String) {
        val linea = "${formato.format(Date())} $nivel/${etiqueta.removePrefix("SEV/")}: $mensaje"
        synchronized(lineas) {
            if (lineas.size >= MAXIMO) lineas.removeFirst()
            lineas.addLast(linea)
            try { archivo?.appendText(linea + "\n") } catch (_: Exception) {}
        }
        alCambiar?.invoke()
    }

    fun volcar(): String = synchronized(lineas) { lineas.joinToString("\n") }
}
