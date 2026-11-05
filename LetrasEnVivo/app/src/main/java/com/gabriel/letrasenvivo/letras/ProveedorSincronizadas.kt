package com.gabriel.letrasenvivo.letras

import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.deteccion.Versiones
import kotlin.math.abs

/** Letra encontrada en una fuente de letras con tiempos (LRCLIB, NetEase, Kugou…). */
data class LetraEncontrada(
    val fuente: String,
    val id: String,
    val titulo: String,
    val artista: String,
    val duracionSeg: Double?,
    /** Texto en formato LRC, o null si la fuente solo tiene letra plana. */
    val lrc: String?,
    val plano: String? = null,
    val instrumental: Boolean = false,
) {
    val sincronizada: Boolean get() = !lrc.isNullOrBlank()

    /**
     * false si la "letra" es relleno: Kugou devuelve a veces una sola línea "ah ah oh" para un remix casi
     * instrumental, y quedaba en pantalla durante minutos sin que sonara ninguna voz.
     */
    val tieneTextoReal: Boolean by lazy {
        val texto = lrc?.let { AnalizadorLrc.parsear(it).map { l -> l.texto } } ?: plano?.lines() ?: return@lazy false
        val lineas = texto.map { NormalizadorTexto.normalizar(it) }.filter { it.isNotBlank() }
        val palabras = lineas.flatMap { it.split(' ') }.filter { it !in VOCALIZACIONES }
        lineas.distinct().size >= 3 && palabras.size >= 8
    }

    /**
     * true si los tiempos no son creíbles: muchas líneas pegadas (a menos de 0,6 s) que nadie podría cantar.
     * "Empty Streets" de Kaskade en LRCLIB tiene 17 de 36 así; los subtítulos se atrasaban y luego corrían.
     */
    val sincronizacionDudosa: Boolean by lazy {
        val tiempos = lrc?.let { AnalizadorLrc.parsear(it).filter { l -> l.texto.isNotBlank() }.map { l -> l.tiempoMs } } ?: return@lazy false
        if (tiempos.size < 6) return@lazy false
        val pegadas = tiempos.zipWithNext().count { (a, b) -> b - a < 600 }
        pegadas * 4 > tiempos.size
    }

    /**
     * Kugou y NetEase marcan las canciones instrumentales con un aviso en chino como única "letra"
     * ("纯音乐，请欣赏": música instrumental, disfrútela). Se convierte en instrumental de verdad.
     */
    fun normalizada(): LetraEncontrada {
        val texto = (lrc ?: "") + (plano ?: "")
        return if (AVISO_INSTRUMENTAL.containsMatchIn(texto) && !tieneTextoReal) copy(lrc = null, plano = null, instrumental = true) else this
    }

    /** Segundo de la última línea con tiempo (null sin LRC). */
    val ultimoTiempoSeg: Double? by lazy {
        lrc?.let { Regex("""\[(\d+):(\d+(?:\.\d+)?)]""").findAll(it).lastOrNull() }
            ?.let { it.groupValues[1].toInt() * 60 + it.groupValues[2].toDouble() }
    }

    /** Diferencia de duración con la canción que suena, en segundos (null si alguna no se conoce). */
    fun diferenciaDuracion(cancion: CancionDetectada): Double? =
        if (duracionSeg == null || cancion.duracionMs <= 0) null else abs(duracionSeg - cancion.duracionMs / 1000.0)
}

/** "纯音乐" = música instrumental; "请欣赏" = disfrútela; "没有填词" = sin letra. */
private val AVISO_INSTRUMENTAL = Regex("纯音乐|没有填词|請欣賞|请欣赏")

/** Sonidos sin significado que no cuentan como letra. */
private val VOCALIZACIONES = setOf("ah", "ahh", "oh", "ohh", "ooh", "oo", "ooo", "uh", "na", "la", "da", "yeah", "hey", "woah", "whoa", "mm", "mmm", "hmm", "eh", "ya", "yo", "ay")

/** La fuente respondió con un error del servidor: no se sabe si tiene la letra. */
class FuenteNoResponde(fuente: String, codigo: Int) : Exception("$fuente respondió $codigo")

/** Fuente de letras con tiempos. */
interface ProveedorSincronizadas {
    val nombre: String
    suspend fun buscar(cancion: CancionDetectada): LetraEncontrada?
}

/** Puntúa candidatos de cualquier fuente contra la canción que suena. */
object Emparejador {
    /** Más de esta diferencia de duración se considera otra versión (otro arreglo) y se descarta. */
    const val TOLERANCIA_DURACION_SEG = 15.0
    /** Hasta esta diferencia se considera la misma grabación: los tiempos valen sin ajustar. */
    const val MISMA_VERSION_SEG = 3.0
    const val MINIMO = 0.6
    /** Parecido mínimo del artista: por debajo es otra canción con el mismo título. */
    const val ARTISTA_MINIMO = 0.7

    /**
     * Devuelve la puntuación (0..1) o null si el candidato se descarta.
     * @param duracionSeg duración del candidato, si la fuente la conoce.
     */
    fun puntuar(titulo: String, artista: String, duracionSeg: Double?, cancion: CancionDetectada): Double? {
        // Otra versión (el original cuando suena un remix, otro remix, el directo…) no es esta canción.
        if (!Versiones.compatibles(Versiones.clave(titulo), cancion.version)) return null
        val simTitulo = cancion.variantesTitulo.maxOf { Similitud.similitud(titulo, it) }
        val artistaNorm = NormalizadorTexto.normalizar(artista)
        val principalNorm = NormalizadorTexto.normalizar(cancion.artistaPrincipal)
        val simArtista = when {
            principalNorm.isNotEmpty() && artistaNorm.contains(principalNorm) -> 1.0
            else -> maxOf(Similitud.similitud(artista, cancion.artista), Similitud.similitud(artista, cancion.artistaPrincipal))
        }
        if (simArtista < ARTISTA_MINIMO) return null
        var puntos = 0.6 * simTitulo + 0.4 * simArtista
        if (duracionSeg != null && cancion.duracionMs > 0) {
            val dif = abs(duracionSeg - cancion.duracionMs / 1000.0)
            if (cancion.esVideo) {
                // Vídeo: se busca la pista de audio, que suele ser más corta (el vídeo añade intro o final).
                // Se acepta cualquier duración razonable y solo se penaliza un poco que sea más larga que el vídeo.
                if (duracionSeg > cancion.duracionMs / 1000.0 + 10) puntos -= 0.15
                puntos -= 0.002 * dif
            } else {
                if (dif > TOLERANCIA_DURACION_SEG) return null
                puntos -= 0.015 * dif
            }
        }
        return if (puntos >= MINIMO) puntos else null
    }

    /** Una letra que acaba esta cantidad antes que otra de la misma canción se da por cortada. */
    const val MARGEN_CORTADA_SEG = 30.0

    /**
     * Muchas letras con tiempos solo traen la primera vuelta de la canción, sin los estribillos repetidos
     * ("Black Room Boy": 32 líneas hasta el 2:34 en una canción de 6:10). Si entre las aceptables hay una que
     * sigue bastante más, se prefiere esa. No se exige que llegue al final: en electrónica la voz acaba mucho antes.
     */
    fun preferirCompleta(elegida: LetraEncontrada, aceptables: List<LetraEncontrada>, cancion: CancionDetectada): LetraEncontrada {
        val limite = if (cancion.duracionMs > 0) cancion.duracionMs / 1000.0 + 10 else Double.MAX_VALUE
        val validas = aceptables.filter { it.sincronizada && (it.ultimoTiempoSeg ?: 0.0) <= limite }
        // Antes que nada, tiempos creíbles: una bien sincronizada gana a una dudosa aunque sea de otra fuente.
        val base = if (elegida.sincronizacionDudosa) validas.firstOrNull { !it.sincronizacionDudosa } ?: elegida else elegida
        val fin = base.ultimoTiempoSeg ?: return base
        val mas = validas.filter { !it.sincronizacionDudosa || base.sincronizacionDudosa }.maxByOrNull { it.ultimoTiempoSeg ?: 0.0 } ?: return base
        return if ((mas.ultimoTiempoSeg ?: 0.0) > fin + MARGEN_CORTADA_SEG) mas else base
    }

    /** true si la letra acaba antes de la mitad de la canción: vale la pena buscar otra versión más completa. */
    fun pareceCortada(e: LetraEncontrada, cancion: CancionDetectada): Boolean {
        val fin = e.ultimoTiempoSeg ?: return false
        return cancion.duracionMs > 0 && fin < cancion.duracionMs / 1000.0 * 0.5
    }

    /** Elige el mejor candidato de una lista ya puntuada; null si ninguno pasa. */
    fun <T> mejor(candidatos: List<T>, puntuacion: (T) -> Double?): Pair<T, Double>? =
        candidatos.mapNotNull { c -> puntuacion(c)?.let { c to it } }.maxByOrNull { it.second }
}
