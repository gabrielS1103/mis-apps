package com.gabriel.letrasenvivo.genero

import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.letras.Emparejador
import com.gabriel.letrasenvivo.red.ClienteMusicBrainz
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.net.URLEncoder

@Serializable
data class EtiquetaMb(val name: String = "", val count: Int = 0)

@Serializable
data class CreditoMb(val name: String = "", val artist: ArtistaMb = ArtistaMb())

@Serializable
data class ArtistaMb(val id: String = "", val name: String = "", val genres: List<EtiquetaMb> = emptyList())

@Serializable
data class RelacionMb(
    val type: String = "",
    val direction: String = "",
    val attributes: List<String> = emptyList(),
    val recording: GrabacionMb? = null,
    val work: ObraMb? = null,
)

@Serializable
data class ObraMb(val id: String = "", val title: String = "", val relations: List<RelacionMb> = emptyList())

@Serializable
data class GrupoMb(val id: String = "", val genres: List<EtiquetaMb> = emptyList())

@Serializable
data class LanzamientoMb(@SerialName("release-group") val grupo: GrupoMb? = null)

@Serializable
data class GrabacionMb(
    val id: String = "",
    val title: String = "",
    val score: Int = 0,
    val length: Long? = null,
    @SerialName("artist-credit") val creditos: List<CreditoMb> = emptyList(),
    val releases: List<LanzamientoMb> = emptyList(),
    val tags: List<EtiquetaMb> = emptyList(),
    val genres: List<EtiquetaMb> = emptyList(),
    @SerialName("first-release-date") val primeraFecha: String? = null,
    val relations: List<RelacionMb> = emptyList(),
)

@Serializable
data class BusquedaGrabacionesMb(val recordings: List<GrabacionMb> = emptyList())

/**
 * Géneros votados en MusicBrainz, de lo más concreto a lo más general: la grabación, su álbum y,
 * si nada de eso tiene géneros, el artista (marcado como tal). Sin clave de API.
 */
class FuenteMusicBrainz(private val mb: ClienteMusicBrainz) {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    suspend fun buscar(cancion: CancionDetectada): Genero? {
        val consulta = URLEncoder.encode("recording:\"${cancion.tituloBase}\" AND artist:\"${cancion.artistaPrincipal}\"", "UTF-8")
        val r = mb.obtener("recording?query=$consulta&fmt=json&limit=10") ?: return null
        val grabaciones = json.decodeFromString<BusquedaGrabacionesMb>(r).recordings
        val mejor = Emparejador.mejor(grabaciones) { g ->
            Emparejador.puntuar(g.title, g.creditos.joinToString(", ") { it.name }, g.length?.let { it / 1000.0 }, cancion)?.takeIf { it >= 0.8 }
        }?.first
        Registro.info(Registro.GENERO, "MusicBrainz: ${grabaciones.size} grabaciones; elegida=${mejor?.let { "${it.title} ${it.id}" }}")
        if (mejor == null) return null

        // 1) La canción: etiquetas de la búsqueda + géneros de la ficha.
        val ficha = mb.obtener("recording/${mejor.id}?inc=genres+tags&fmt=json")?.let { json.decodeFromString<GrabacionMb>(it) }
        clasificar(mejor.tags + ficha?.tags.orEmpty() + ficha?.genres.orEmpty())?.let { return it }
        // 2) El álbum o single en el que salió.
        mejor.releases.firstNotNullOfOrNull { it.grupo?.id }?.let { id ->
            val grupo = mb.obtener("release-group/$id?inc=genres&fmt=json")?.let { json.decodeFromString<GrupoMb>(it) }
            clasificar(grupo?.genres.orEmpty())?.let { return it }
        }
        // 3) El artista: es su estilo general, no necesariamente el de esta canción. En un remix
        // sería el estilo del autor original, que puede no tener nada que ver: mejor no decir nada.
        if (cancion.version != null) return null
        val idArtista = mejor.creditos.firstOrNull()?.artist?.id ?: return null
        val artista = mb.obtener("artist/$idArtista?inc=genres&fmt=json")?.let { json.decodeFromString<ArtistaMb>(it) }
        return clasificar(artista?.genres.orEmpty())?.copy(deArtista = true)
    }

    private fun clasificar(etiquetas: List<EtiquetaMb>): Genero? =
        ClasificadorDeGeneros.clasificar(etiquetas.map { it.name to it.count })?.copy(fuente = "MusicBrainz")
}
