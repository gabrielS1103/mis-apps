package com.gabriel.letrasenvivo.superposicion

import android.os.SystemClock
import com.gabriel.letrasenvivo.deteccion.EstadoReproduccion
import com.gabriel.letrasenvivo.letras.modelos.Letra
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * Calcula qué línea toca mostrar según la posición de reproducción. Solo emite cuando cambia el índice
 * y solo hace trabajo mientras la música está sonando.
 */
class ReproductorDeLineas(
    private val ambito: CoroutineScope,
    private val letra: StateFlow<Letra?>,
    private val reproduccion: StateFlow<EstadoReproduccion>,
    private val desfaseMs: () -> Long,
) {
    private val _indice = MutableStateFlow(-1)
    val indice: StateFlow<Int> = _indice
    private val _pausa = MutableStateFlow(false)
    /** true si la línea actual ya terminó hace rato y la siguiente no llega: parte instrumental. */
    val pausa: StateFlow<Boolean> = _pausa

    private var bucle: Job? = null

    fun iniciar() {
        ambito.launch {
            combine(letra, reproduccion, EstadoSuperposicion.desfaseCancionMs) { l, r, _ -> l to r }.collect { (l, r) ->
                bucle?.cancel()
                if (l == null || !l.tieneTiempos) { _indice.value = -1; _pausa.value = false; return@collect }
                bucle = launch { seguir(l, r) }
            }
        }
    }

    /** Fuerza el recálculo (p. ej. tras cambiar el desfase). */
    fun recalcular() {
        val l = letra.value ?: return
        val posicion = reproduccion.value.posicionAhora() + desfaseMs()
        _indice.value = indicePara(l, posicion)
        _pausa.value = enPausa(l, _indice.value, posicion)
    }

    /** Ninguna línea se canta más de [LINEA_MAXIMA_MS]: pasado eso, la letra no tiene marcada la pausa que sigue. */
    private fun enPausa(l: Letra, i: Int, posicion: Long): Boolean {
        val inicio = l.lineas.getOrNull(i)?.tiempoMs ?: return false
        return posicion - inicio > LINEA_MAXIMA_MS
    }

    private suspend fun seguir(l: Letra, r: EstadoReproduccion) {
        val tiempos = l.lineas.map { it.tiempoMs ?: Long.MAX_VALUE }
        while (ambito.isActive) {
            val posicion = r.posicionAhora(SystemClock.elapsedRealtime()) + desfaseMs()
            val i = indicePara(l, posicion)
            if (_indice.value != i) _indice.value = i
            val pausa = enPausa(l, i, posicion)
            if (_pausa.value != pausa) _pausa.value = pausa
            if (!r.reproduciendo) return
            val siguiente = tiempos.getOrNull(i + 1) ?: Long.MAX_VALUE
            val espera = if (siguiente == Long.MAX_VALUE) 400L else (siguiente - posicion).coerceIn(30L, 400L)
            delay(espera)
        }
    }

    companion object {
        const val LINEA_MAXIMA_MS = 12_000L
    }

    /** Última línea cuyo tiempo es <= posición (búsqueda binaria); -1 antes de la primera. */
    private fun indicePara(l: Letra, posicionMs: Long): Int {
        val lineas = l.lineas
        var bajo = 0
        var alto = lineas.size - 1
        var resultado = -1
        while (bajo <= alto) {
            val medio = (bajo + alto) / 2
            val t = lineas[medio].tiempoMs ?: Long.MAX_VALUE
            if (t <= posicionMs) { resultado = medio; bajo = medio + 1 } else alto = medio - 1
        }
        return resultado
    }
}
