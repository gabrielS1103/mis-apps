package com.gabriel.letrasenvivo.letras

import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.genero.BusquedaGrabacionesMb
import com.gabriel.letrasenvivo.genero.GrabacionMb
import com.gabriel.letrasenvivo.genero.ObraMb
import com.gabriel.letrasenvivo.red.ClienteMusicBrainz
import kotlinx.serialization.json.Json
import java.net.URLEncoder

/**
 * Para un remix o un cover sin letra propia: busca en MusicBrainz cuál es la grabación original, por los
 * vínculos que pone la gente ("remix de", o la obra compuesta de la que esta es una interpretación).
 * Así la letra de respaldo es de la misma canción con seguridad, no de otra que se llame igual.
 */
class BuscadorDeOriginal(private val mb: ClienteMusicBrainz) {

    data class Original(val titulo: String, val artista: String)

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    suspend fun buscar(cancion: CancionDetectada): Original? {
        val consulta = URLEncoder.encode("recording:\"${cancion.tituloBase}\" AND artist:\"${cancion.artistaPrincipal}\"", "UTF-8")
        val r = mb.obtener("recording?query=$consulta&fmt=json&limit=10") ?: return null
        val grabaciones = json.decodeFromString<BusquedaGrabacionesMb>(r).recordings
        val esta = Emparejador.mejor(grabaciones) { g ->
            Emparejador.puntuar(g.title, g.creditos.joinToString(", ") { it.name }, g.length?.let { it / 1000.0 }, cancion)?.takeIf { it >= 0.8 }
        }?.first ?: return null
        val ficha = mb.obtener("recording/${esta.id}?inc=work-rels+recording-rels+artist-credits&fmt=json")
            ?.let { json.decodeFromString<GrabacionMb>(it) } ?: return null

        // 1) Remix: MusicBrainz lo enlaza con la grabación de la que sale.
        val deRemix = ficha.relations.firstOrNull { it.type == "remix" && it.direction == "backward" && it.recording != null }?.recording
        if (deRemix != null) {
            describir(deRemix.id)?.let { return it.also { o -> Registro.info(Registro.APP, "Original (remix de): $o") } }
        }
        // 2) Cover u otra versión: la obra que interpreta y, de ella, la grabación original más antigua.
        val obra = ficha.relations.firstOrNull { it.type == "performance" && it.work != null }?.work ?: return null
        val datosObra = mb.obtener("work/${obra.id}?inc=recording-rels&fmt=json")?.let { json.decodeFromString<ObraMb>(it) } ?: return null
        val originales = datosObra.relations
            .filter { it.type == "performance" && it.recording != null && "cover" !in it.attributes && "live" !in it.attributes && it.recording.id != esta.id }
            .mapNotNull { it.recording }
            .sortedBy { it.primeraFecha?.ifBlank { null } ?: "9999" }
        for (g in originales.take(3)) {
            val o = describir(g.id) ?: continue
            // Si la "original" es del mismo artista y título, no aporta nada nuevo.
            if (NormalizadorTexto.clave(o.artista) == NormalizadorTexto.clave(cancion.artista) && NormalizadorTexto.clave(o.titulo) == NormalizadorTexto.clave(cancion.tituloBase)) continue
            Registro.info(Registro.APP, "Original (obra \"${obra.title}\"): $o")
            return o
        }
        return null
    }

    private suspend fun describir(id: String): Original? {
        val g = mb.obtener("recording/$id?inc=artist-credits&fmt=json")?.let { json.decodeFromString<GrabacionMb>(it) } ?: return null
        val artista = g.creditos.joinToString(", ") { it.name }.ifBlank { return null }
        return Original(g.title, artista)
    }
}
