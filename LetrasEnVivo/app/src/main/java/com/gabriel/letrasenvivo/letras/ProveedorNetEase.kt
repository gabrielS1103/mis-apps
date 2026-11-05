package com.gabriel.letrasenvivo.letras

import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.red.ClienteHttp
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull

/** Letras con tiempos de NetEase Cloud Music (music.163.com), sin clave. Tiene muchas canciones occidentales. */
class ProveedorNetEase(private val http: ClienteHttp) : ProveedorSincronizadas {

    override val nombre = "NetEase"

    data class Candidato(val id: Long, val titulo: String, val artista: String, val duracionSeg: Double)

    override suspend fun buscar(cancion: CancionDetectada): LetraEncontrada? {
        val consulta = "${cancion.artistaPrincipal} ${cancion.tituloSinColaboradores}"
        val r = http.enviarFormulario("https://music.163.com/api/cloudsearch/pc", mapOf("s" to consulta, "type" to "1", "limit" to "10", "offset" to "0"))
        if (r.fallaServidor) throw FuenteNoResponde(nombre, r.codigo)
        if (!r.correcta) { Registro.aviso(Registro.NETEASE, "búsqueda -> ${r.codigo}"); return null }
        val candidatos = analizarBusqueda(r.cuerpo)
        val mejor = Emparejador.mejor(candidatos) { Emparejador.puntuar(it.titulo, it.artista, it.duracionSeg, cancion) }
        Registro.info(Registro.NETEASE, "\"$consulta\": ${candidatos.size} candidatos; mejor=${mejor?.let { "id=${it.first.id} %.2f dur=${it.first.duracionSeg}".format(it.second) } ?: "ninguno aceptable"}")
        val c = mejor?.first ?: return null
        val rl = http.obtener("https://music.163.com/api/song/lyric?id=${c.id}&lv=1&tv=-1")
        if (rl.fallaServidor) throw FuenteNoResponde(nombre, rl.codigo)
        if (!rl.correcta) { Registro.aviso(Registro.NETEASE, "letra -> ${rl.codigo}"); return null }
        val lrc = analizarLetra(rl.cuerpo)
        if (lrc.isNullOrBlank()) { Registro.info(Registro.NETEASE, "id=${c.id} sin letra"); return null }
        return LetraEncontrada(nombre, c.id.toString(), c.titulo, c.artista, c.duracionSeg, lrc)
    }

    companion object {
        private val json = Json { ignoreUnknownKeys = true; isLenient = true }

        fun analizarBusqueda(cuerpo: String): List<Candidato> = try {
            val raiz = json.parseToJsonElement(cuerpo).jsonObject
            val canciones = raiz["result"]?.jsonObject?.get("songs")?.jsonArray ?: return emptyList()
            canciones.mapNotNull { e ->
                val o = e.jsonObject
                val id = o["id"]?.jsonPrimitive?.longOrNull ?: return@mapNotNull null
                val titulo = o["name"]?.jsonPrimitive?.content ?: return@mapNotNull null
                val artistas = o["ar"]?.jsonArray?.mapNotNull { it.jsonObject["name"]?.jsonPrimitive?.content }.orEmpty()
                val duracionMs = o["dt"]?.jsonPrimitive?.longOrNull ?: 0L
                Candidato(id, titulo, artistas.joinToString(", "), duracionMs / 1000.0)
            }
        } catch (e: Exception) { emptyList() }

        /** Devuelve el LRC o null si la canción no tiene letra con tiempos. */
        fun analizarLetra(cuerpo: String): String? = try {
            val raiz = json.parseToJsonElement(cuerpo).jsonObject
            val lrc = raiz["lrc"]?.jsonObject?.get("lyric")?.jsonPrimitive?.content
            // Si no hay ninguna marca de tiempo, NetEase devuelve texto plano: no sirve como sincronizada.
            lrc?.takeIf { Regex("\\[\\d+:\\d+").containsMatchIn(it) }
        } catch (e: Exception) { null }
    }
}
