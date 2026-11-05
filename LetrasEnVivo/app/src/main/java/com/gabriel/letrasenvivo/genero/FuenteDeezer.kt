package com.gabriel.letrasenvivo.genero

import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.letras.Emparejador
import com.gabriel.letrasenvivo.red.ClienteHttp
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.net.URLEncoder

@Serializable
data class ArtistaDz(val name: String = "")

@Serializable
data class AlbumDz(val id: Long = 0)

@Serializable
data class PistaDz(val title: String = "", val duration: Double? = null, val artist: ArtistaDz = ArtistaDz(), val album: AlbumDz = AlbumDz())

@Serializable
data class BusquedaDz(val data: List<PistaDz> = emptyList())

@Serializable
data class NombreDz(val name: String = "")

@Serializable
data class ListaGenerosDz(val data: List<NombreDz> = emptyList())

@Serializable
data class FichaAlbumDz(val genres: ListaGenerosDz = ListaGenerosDz())

/** Último recurso: Deezer solo da categorías amplias del álbum ("Electro", "Rap/Hip Hop"), sin subgénero. */
class FuenteDeezer(private val http: ClienteHttp) {

    private val json = Json { ignoreUnknownKeys = true; isLenient = true }

    private val nombres = mapOf(
        "Electro" to "Electrónica", "Dance" to "Dance", "Pop" to "Pop", "Rap/Hip Hop" to "Hip hop", "Rock" to "Rock",
        "Alternative" to "Alternativa", "R&B" to "R&B", "Latin Music" to "Música latina", "Reggaeton" to "Reguetón",
        "Reggae" to "Reggae", "Jazz" to "Jazz", "Classical" to "Clásica", "Metal" to "Metal", "Soul & Funk" to "Soul y funk",
        "Country" to "Country", "Blues" to "Blues", "Folk" to "Folk", "Films/Games" to "Banda sonora", "Indie Pop" to "Indie pop",
    )

    suspend fun buscar(cancion: CancionDetectada): Genero? {
        val consulta = URLEncoder.encode("${cancion.artistaPrincipal} ${cancion.tituloBase}", "UTF-8")
        val r = http.obtener("https://api.deezer.com/search?q=$consulta&limit=10")
        if (!r.correcta) return null
        val pistas = json.decodeFromString<BusquedaDz>(r.cuerpo).data
        val p = Emparejador.mejor(pistas) { Emparejador.puntuar(it.title, it.artist.name, it.duration, cancion)?.takeIf { s -> s >= 0.8 } }?.first ?: return null
        val ra = http.obtener("https://api.deezer.com/album/${p.album.id}")
        if (!ra.correcta) return null
        val generos = json.decodeFromString<FichaAlbumDz>(ra.cuerpo).genres.data.map { it.name }
        Registro.info(Registro.GENERO, "Deezer: \"${p.title}\" álbum ${p.album.id} -> $generos")
        val g = generos.firstOrNull() ?: return null
        return Genero(nombres[g] ?: g, null, "Deezer")
    }
}
