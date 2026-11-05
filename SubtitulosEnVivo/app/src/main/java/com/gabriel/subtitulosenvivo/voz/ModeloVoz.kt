package com.gabriel.subtitulosenvivo.voz

import android.content.Context
import com.gabriel.subtitulosenvivo.Registro
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/**
 * Modelo de Whisper solo inglés, "base" cuantizado a 5 bits: buena calidad y casi en tiempo real en un S21.
 * Si resulta lento, la alternativa es ggml-tiny.en-q5_1.bin (32 MB, SHA-256 c77c5766…7c2b).
 */
object ModeloVoz {
    private const val NOMBRE = "ggml-base.en-q5_1.bin"
    private const val URL_DESCARGA = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$NOMBRE"
    const val TAMANO = 59_721_011L
    private const val SHA256 = "4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f"

    sealed interface Estado {
        data object Falta : Estado
        data class Descargando(val bytes: Long) : Estado
        data class Error(val mensaje: String) : Estado
        data object Listo : Estado
    }

    private val _estado = MutableStateFlow<Estado>(Estado.Falta)
    val estado: StateFlow<Estado> = _estado

    private val alcance = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var descarga: Job? = null

    fun archivo(context: Context) = File(context.filesDir, "modelos/$NOMBRE")

    /** Solo comprueba el tamaño: el SHA-256 se verificó al descargar. */
    fun disponible(context: Context) = archivo(context).length() == TAMANO

    fun refrescar(context: Context) {
        if (descarga?.isActive == true) return
        _estado.value = if (disponible(context)) Estado.Listo else Estado.Falta
    }

    fun descargar(context: Context) {
        if (descarga?.isActive == true || disponible(context)) return
        val destino = archivo(context)
        descarga = alcance.launch {
            _estado.value = Estado.Descargando(0)
            val temporal = File(destino.parentFile, "$NOMBRE.descargando")
            try {
                destino.parentFile?.mkdirs()
                val sha = MessageDigest.getInstance("SHA-256")
                val conexion = URL(URL_DESCARGA).openConnection() as HttpURLConnection
                conexion.connectTimeout = 15_000
                conexion.readTimeout = 30_000
                if (conexion.responseCode != 200) throw IllegalStateException("HTTP ${conexion.responseCode}")
                var total = 0L
                conexion.inputStream.use { entrada ->
                    temporal.outputStream().use { salida ->
                        val buf = ByteArray(64 * 1024)
                        while (true) {
                            ensureActive()
                            val n = entrada.read(buf)
                            if (n < 0) break
                            salida.write(buf, 0, n)
                            sha.update(buf, 0, n)
                            total += n
                            _estado.value = Estado.Descargando(total)
                        }
                    }
                }
                val hash = sha.digest().joinToString("") { "%02x".format(it) }
                if (total != TAMANO || hash != SHA256) throw IllegalStateException("archivo dañado ($total bytes)")
                if (!temporal.renameTo(destino)) throw IllegalStateException("no se pudo guardar")
                Registro.info(Registro.VOZ, "Modelo de voz descargado")
                _estado.value = Estado.Listo
            } catch (e: Exception) {
                temporal.delete()
                Registro.error(Registro.VOZ, "Falló la descarga del modelo", e)
                _estado.value = Estado.Error(e.message ?: e.javaClass.simpleName)
            }
        }
    }
}
