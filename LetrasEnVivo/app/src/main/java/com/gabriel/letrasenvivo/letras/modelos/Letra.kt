package com.gabriel.letrasenvivo.letras.modelos

import kotlinx.serialization.Serializable

/** Una línea de la letra, con su tiempo (si se conoce) y su traducción (si existe). */
@Serializable
data class LineaLetra(
    val tiempoMs: Long?,
    val original: String,
    val traducida: String? = null,
    val traduccionAutomatica: Boolean = false,
)

/** Una línea con tiempo, tal como viene de un archivo LRC. Texto vacío = pausa instrumental. */
data class LineaSincronizada(val tiempoMs: Long, val texto: String)

/** De dónde salió cada parte de la letra; se muestra en la pantalla de depuración. */
@Serializable
data class Fuentes(
    /** Fuente de la letra con tiempos ("LRCLIB", "NetEase", "Kugou") o null si no hubo. */
    val fuenteSincronizada: String? = null,
    val idSincronizada: String? = null,
    /** Diferencia de duración entre la versión encontrada y la que suena, en segundos. */
    val diferenciaDuracionSeg: Double? = null,
    val urlCancionLT: String? = null,
    val urlTraduccionLT: String? = null,
    val mlKit: Boolean = false,
    /** Fracción de líneas con tiempo que recibieron traducción (0..1). */
    val cobertura: Float = 0f,
)

/** La letra completa lista para mostrar. */
@Serializable
data class Letra(
    val lineas: List<LineaLetra>,
    /** true si los tiempos se estimaron repartiendo las líneas a lo largo de la canción. */
    val aproximada: Boolean = false,
    val instrumental: Boolean = false,
    /** Código ISO del idioma original ("en", "es"…) si se pudo determinar. */
    val idiomaOriginal: String? = null,
    val fuentes: Fuentes = Fuentes(),
    /** La letra es de otra grabación (vídeo, edición distinta): los tiempos se ajustaron a ojo. */
    val versionDistinta: Boolean = false,
    /** Desfase propuesto para esa otra grabación (negativo = la voz empieza más tarde que en la letra). */
    val desfaseSugeridoMs: Long = 0,
    /** Si la letra es de la versión original (la de un remix o un cover sin letra propia): "Hurt – Nine Inch Nails". */
    val letraDe: String? = null,
    /** La fuente tenía tiempos, pero imposibles (líneas pegadas): se muestra sin sincronizar. */
    val tiemposDescartados: Boolean = false,
) {
    val tieneTraduccion: Boolean get() = lineas.any { it.traducida != null }
    val tieneTiempos: Boolean get() = lineas.any { it.tiempoMs != null }

    companion object {
        fun instrumental() = Letra(lineas = emptyList(), instrumental = true)
    }
}

/** Resultado del repositorio para una canción. */
sealed class EstadoLetra {
    data object Cargando : EstadoLetra()
    /** Ya hay algo que mostrar (por ejemplo el original) mientras llega el resto. */
    data class Parcial(val letra: Letra) : EstadoLetra()
    data class Lista(val letra: Letra) : EstadoLetra()
    data class SinLetra(val motivo: String) : EstadoLetra()
    data class Error(val mensaje: String) : EstadoLetra()
}
