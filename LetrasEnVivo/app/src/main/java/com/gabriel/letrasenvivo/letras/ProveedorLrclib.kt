package com.gabriel.letrasenvivo.letras

import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.red.ClienteHttp
import kotlinx.coroutines.delay
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.net.URLEncoder
import kotlin.math.roundToLong

@Serializable
data class RespuestaLrclib(
    val id: Long = 0,
    val trackName: String? = null,
    val artistName: String? = null,
    val albumName: String? = null,
    val duration: Double? = null,
    val instrumental: Boolean = false,
    val plainLyrics: String? = null,
    val syncedLyrics: String? = null,
)

/** Letras sincronizadas de LRCLIB (https://lrclib.net), sin clave de API. */
class ProveedorLrclib(private val http: ClienteHttp) : ProveedorSincronizadas {

    override val nombre = "LRCLIB"
    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    /**
     * LRCLIB va a ratos saturado ("The server is busy", 503) aunque la consulta siguiente funcione. Una consulta
     * que falla no tira la búsqueda: se sigue con las demás y solo se da la fuente por caída si no salió nada.
     */
    override suspend fun buscar(cancion: CancionDetectada): LetraEncontrada? {
        var fallo: FuenteNoResponde? = null
        suspend fun <T> intentar(consulta: suspend () -> T, siFalla: T): T =
            try { consulta() } catch (e: FuenteNoResponde) { fallo = e; siFalla }
        val r = buscarCon(cancion, { a, t, al, d -> intentar({ obtenerExacta(a, t, al, d) }, null) }, { t, a -> intentar({ buscarLibre(t, a) }, emptyList()) })
        if (r == null) fallo?.let { throw it }
        return r
    }

    private suspend fun buscarCon(
        cancion: CancionDetectada,
        obtenerExacta: suspend (String, String, String?, Long) -> RespuestaLrclib?,
        buscarLibre: suspend (String, String) -> List<RespuestaLrclib>,
    ): LetraEncontrada? {
        val duracionSeg = if (cancion.duracionMs > 0) (cancion.duracionMs / 1000.0).roundToLong() else null
        // 1) Búsqueda exacta (LRCLIB ya comprueba la duración con ±2 s).
        var exacta: LetraEncontrada? = null
        if (duracionSeg != null) {
            for (titulo in cancion.variantesTitulo) {
                val directa = obtenerExacta(cancion.artista, titulo, cancion.album, duracionSeg) ?: continue
                exacta = convertir(directa)
                if (!Emparejador.pareceCortada(exacta, cancion) && !exacta.sincronizacionDudosa) return exacta
                Registro.info(Registro.LRCLIB, "get id=${directa.id} acaba en ${exacta.ultimoTiempoSeg} s: se buscan versiones más completas")
                break
            }
        }
        // 2) Búsqueda libre por variantes del título y elección por parecido y duración.
        val consultas = buildList {
            for (t in cancion.variantesTitulo) add(t to cancion.artista)
            if (cancion.artistaPrincipal != cancion.artista) add(cancion.tituloBase to cancion.artistaPrincipal)
        }.distinct().take(4)
        for ((titulo, artista) in consultas) {
            val candidatas = buscarLibre(titulo, artista)
            val elegida = elegir(candidatas, cancion)?.let { convertir(it) } ?: continue
            val aceptables = candidatas.filter { Emparejador.puntuar(it.trackName.orEmpty(), it.artistName.orEmpty(), it.duration, cancion) != null }.map { convertir(it) }
            return Emparejador.preferirCompleta(exacta ?: elegida, aceptables + elegida, cancion)
        }
        return exacta
    }

    private fun convertir(r: RespuestaLrclib) = LetraEncontrada(
        fuente = nombre, id = r.id.toString(), titulo = r.trackName.orEmpty(), artista = r.artistName.orEmpty(),
        duracionSeg = r.duration, lrc = r.syncedLyrics?.takeIf { it.isNotBlank() }, plano = r.plainLyrics, instrumental = r.instrumental,
    )

    private suspend fun obtenerExacta(artista: String, titulo: String, album: String?, duracionSeg: Long): RespuestaLrclib? {
        val url = StringBuilder("https://lrclib.net/api/get?artist_name=").append(codificar(artista))
            .append("&track_name=").append(codificar(titulo))
            .append("&duration=").append(duracionSeg)
        if (!album.isNullOrBlank()) url.append("&album_name=").append(codificar(album))
        val r = pedir(url.toString())
        if (r.codigo == 404) return null
        if (r.fallaServidor) throw FuenteNoResponde(nombre, r.codigo)
        if (!r.correcta) { Registro.aviso(Registro.LRCLIB, "get -> ${r.codigo}"); return null }
        return try { json.decodeFromString<RespuestaLrclib>(r.cuerpo).also { Registro.info(Registro.LRCLIB, "get \"$titulo\": id=${it.id} sincronizada=${it.syncedLyrics != null}") } }
        catch (e: Exception) { Registro.error(Registro.LRCLIB, "JSON inválido en get", e); null }
    }

    private suspend fun buscarLibre(titulo: String, artista: String): List<RespuestaLrclib> {
        val url = "https://lrclib.net/api/search?track_name=${codificar(titulo)}&artist_name=${codificar(artista)}"
        val r = pedir(url)
        if (r.fallaServidor) throw FuenteNoResponde(nombre, r.codigo)
        if (!r.correcta) { Registro.aviso(Registro.LRCLIB, "search -> ${r.codigo}"); return emptyList() }
        return try { json.decodeFromString<List<RespuestaLrclib>>(r.cuerpo) }
        catch (e: Exception) { Registro.aviso(Registro.LRCLIB, "search \"$titulo\": respuesta no es una lista"); emptyList() }
    }

    private fun elegir(candidatas: List<RespuestaLrclib>, cancion: CancionDetectada): RespuestaLrclib? {
        if (candidatas.isEmpty()) return null
        // Se prefieren las sincronizadas: pequeño extra en la puntuación.
        val mejor = Emparejador.mejor(candidatas) { c ->
            Emparejador.puntuar(c.trackName.orEmpty(), c.artistName.orEmpty(), c.duration, cancion)?.plus(if (c.syncedLyrics != null) 0.1 else 0.0)
        }
        Registro.info(Registro.LRCLIB, "search: ${candidatas.size} candidatas; mejor=${mejor?.let { "id=${it.first.id} %.2f sinc=${it.first.syncedLyrics != null} dur=${it.first.duration}".format(it.second) } ?: "ninguna aceptable"}")
        return mejor?.first
    }

    /** Un 503 de LRCLIB suele durar un instante: se repite una vez antes de rendirse. */
    private suspend fun pedir(url: String): ClienteHttp.Respuesta {
        val r = http.obtener(url)
        if (!r.fallaServidor) return r
        delay(700)
        return http.obtener(url)
    }

    private fun codificar(s: String) = URLEncoder.encode(s, "UTF-8")
}
