package com.gabriel.subtitulosenvivo.traduccion

import com.gabriel.subtitulosenvivo.Registro
import com.google.android.gms.tasks.Task
import com.google.mlkit.common.model.DownloadConditions
import com.google.mlkit.common.model.RemoteModelManager
import com.google.mlkit.nl.translate.TranslateLanguage
import com.google.mlkit.nl.translate.TranslateRemoteModel
import com.google.mlkit.nl.translate.Translation
import com.google.mlkit.nl.translate.TranslatorOptions
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import java.io.Closeable
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * Traducción inglés → español en el teléfono con ML Kit (~30 MB, sin internet una vez descargado).
 * Un solo cliente abierto mientras dura la captura: crear uno por frase añadiría ~100 ms de desfase.
 */
class Traductor : Closeable {
    private val cliente = Translation.getClient(OPCIONES)

    /** Devuelve null si falla (por ejemplo, si el modelo no está descargado). */
    suspend fun traducir(texto: String): String? = try {
        cliente.translate(texto).esperar()
    } catch (e: Exception) {
        Registro.error(Registro.TRADUCCION, "No se pudo traducir", e)
        null
    }

    override fun close() = cliente.close()

    enum class Estado { DESCONOCIDO, FALTA, DESCARGANDO, ERROR, LISTO }

    companion object {
        private val OPCIONES = TranslatorOptions.Builder()
            .setSourceLanguage(TranslateLanguage.ENGLISH)
            .setTargetLanguage(TranslateLanguage.SPANISH)
            .build()

        private val _estado = MutableStateFlow(Estado.DESCONOCIDO)
        val estado: StateFlow<Estado> = _estado

        private val alcance = CoroutineScope(SupervisorJob() + Dispatchers.Main)
        private var descarga: Job? = null

        /** Consulta si el modelo inglés→español ya está en el teléfono. */
        fun refrescar() {
            if (descarga?.isActive == true) return
            alcance.launch {
                _estado.value = try {
                    val modelos = RemoteModelManager.getInstance().getDownloadedModels(TranslateRemoteModel::class.java).esperar()
                    val codigos = modelos.map { it.language }
                    if (TranslateLanguage.ENGLISH in codigos && TranslateLanguage.SPANISH in codigos) Estado.LISTO else Estado.FALTA
                } catch (e: Exception) {
                    Registro.error(Registro.TRADUCCION, "No se pudo consultar el modelo", e)
                    Estado.FALTA
                }
            }
        }

        fun descargar() {
            if (descarga?.isActive == true || _estado.value == Estado.LISTO) return
            descarga = alcance.launch {
                _estado.value = Estado.DESCARGANDO
                val cliente = Translation.getClient(OPCIONES)
                _estado.value = try {
                    // Sin requireWifi: el usuario lo pide a mano desde la pantalla principal.
                    cliente.downloadModelIfNeeded(DownloadConditions.Builder().build()).esperar()
                    Registro.info(Registro.TRADUCCION, "Modelo de traducción descargado")
                    Estado.LISTO
                } catch (e: Exception) {
                    Registro.error(Registro.TRADUCCION, "Falló la descarga del traductor", e)
                    Estado.ERROR
                } finally {
                    cliente.close()
                }
            }
        }

        private suspend fun <T> Task<T>.esperar(): T = suspendCancellableCoroutine { c ->
            addOnSuccessListener { if (c.isActive) c.resume(it) }
            addOnFailureListener { if (c.isActive) c.resumeWithException(it) }
            addOnCanceledListener { c.cancel() }
        }
    }
}
