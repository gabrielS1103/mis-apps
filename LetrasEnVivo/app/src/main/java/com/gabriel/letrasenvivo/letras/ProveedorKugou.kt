package com.gabriel.letrasenvivo.letras

import android.util.Base64
import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.red.ClienteHttp
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import java.net.URLEncoder

/**
 * Letras con tiempos de Kugou, sin clave. Tres pasos: buscar la canción (hash y duración),
 * pedir las letras candidatas para ese hash y descargar la elegida (viene en base64).
 */
class ProveedorKugou(private val http: ClienteHttp, private val decodificarBase64: (String) -> ByteArray = { Base64.decode(it, Base64.DEFAULT) }) : ProveedorSincronizadas {

    override val nombre = "Kugou"

    data class Cancion(val hash: String, val titulo: String, val artista: String, val duracionSeg: Double)
    data class CandidataLetra(val id: String, val accesskey: String, val duracionSeg: Double?)

    override suspend fun buscar(cancion: CancionDetectada): LetraEncontrada? {
        val consulta = "${cancion.artistaPrincipal} ${cancion.tituloSinColaboradores}"
        val r = http.obtener("https://songsearch.kugou.com/song_search_v2?keyword=${URLEncoder.encode(consulta, "UTF-8")}&page=1&pagesize=10&platform=WebFilter")
        if (r.fallaServidor) throw FuenteNoResponde(nombre, r.codigo)
        if (!r.correcta) { Registro.aviso(Registro.KUGOU, "búsqueda -> ${r.codigo}"); return null }
        val canciones = analizarBusqueda(r.cuerpo)
        val mejor = Emparejador.mejor(canciones) { Emparejador.puntuar(it.titulo, it.artista, it.duracionSeg, cancion) }
        Registro.info(Registro.KUGOU, "\"$consulta\": ${canciones.size} canciones; mejor=${mejor?.let { "%s %.2f dur=%s".format(it.first.hash.take(8), it.second, it.first.duracionSeg) } ?: "ninguna aceptable"}")
        val c = mejor?.first ?: return null

        val rc = http.obtener("https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&duration=${(c.duracionSeg * 1000).toLong()}&hash=${c.hash}")
        if (rc.fallaServidor) throw FuenteNoResponde(nombre, rc.codigo)
        if (!rc.correcta) { Registro.aviso(Registro.KUGOU, "candidatas -> ${rc.codigo}"); return null }
        val candidata = analizarCandidatas(rc.cuerpo).firstOrNull() ?: run { Registro.info(Registro.KUGOU, "sin letras para ${c.hash.take(8)}"); return null }

        val rd = http.obtener("https://lyrics.kugou.com/download?ver=1&client=pc&id=${candidata.id}&accesskey=${candidata.accesskey}&fmt=lrc&charset=utf8")
        if (rd.fallaServidor) throw FuenteNoResponde(nombre, rd.codigo)
        if (!rd.correcta) { Registro.aviso(Registro.KUGOU, "descarga -> ${rd.codigo}"); return null }
        val lrc = analizarDescarga(rd.cuerpo, decodificarBase64) ?: return null
        return LetraEncontrada(nombre, candidata.id, c.titulo, c.artista, c.duracionSeg, lrc)
    }

    companion object {
        private val json = Json { ignoreUnknownKeys = true; isLenient = true }

        fun analizarBusqueda(cuerpo: String): List<Cancion> = try {
            val raiz = json.parseToJsonElement(cuerpo).jsonObject
            val lista = raiz["data"]?.jsonObject?.get("lists")?.jsonArray ?: return emptyList()
            lista.mapNotNull { e ->
                val o = e.jsonObject
                val hash = o["FileHash"]?.jsonPrimitive?.content ?: return@mapNotNull null
                val titulo = o["SongName"]?.jsonPrimitive?.content ?: return@mapNotNull null
                val artista = o["SingerName"]?.jsonPrimitive?.content.orEmpty().replace("、", ", ")
                val duracion = o["Duration"]?.jsonPrimitive?.longOrNull ?: 0L
                Cancion(hash, titulo, artista, duracion.toDouble())
            }
        } catch (e: Exception) { emptyList() }

        fun analizarCandidatas(cuerpo: String): List<CandidataLetra> = try {
            val raiz = json.parseToJsonElement(cuerpo).jsonObject
            raiz["candidates"]?.jsonArray?.mapNotNull { e ->
                val o = e.jsonObject
                val id = o["id"]?.jsonPrimitive?.content ?: return@mapNotNull null
                val clave = o["accesskey"]?.jsonPrimitive?.content ?: return@mapNotNull null
                CandidataLetra(id, clave, o["duration"]?.jsonPrimitive?.longOrNull?.let { it / 1000.0 })
            }.orEmpty()
        } catch (e: Exception) { emptyList() }

        fun analizarDescarga(cuerpo: String, decodificar: (String) -> ByteArray): String? = try {
            val raiz = json.parseToJsonElement(cuerpo).jsonObject
            val contenido = raiz["content"]?.jsonPrimitive?.content ?: return null
            String(decodificar(contenido), Charsets.UTF_8).takeIf { Regex("\\[\\d+:\\d+").containsMatchIn(it) }
        } catch (e: Exception) { null }
    }
}
