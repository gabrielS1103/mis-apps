package com.gabriel.letrasenvivo.genero

import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.red.ClienteHttp
import com.gabriel.letrasenvivo.red.ClienteMusicBrainz
import com.gabriel.letrasenvivo.red.NavegadorOculto
import kotlinx.coroutines.CancellationException
import java.io.File

/**
 * Género y subgénero de la canción que suena (no del artista en general), de lo más concreto a lo más amplio:
 * Beatport (estilo oficial de cada pista de electrónica), MusicBrainz (canción, álbum y, si no, artista) y Deezer.
 */
class ProveedorDeGeneros(http: ClienteHttp, mb: ClienteMusicBrainz, navegador: NavegadorOculto, depuracion: File? = null) {

    private val beatport = FuenteBeatport(navegador, depuracion)
    private val musicBrainz = FuenteMusicBrainz(mb)
    private val deezer = FuenteDeezer(http)

    /** Por canción; también se recuerdan los "no encontrado" para no repetir la consulta. */
    private val memoria = HashMap<String, Genero?>()

    suspend fun buscar(cancion: CancionDetectada): Genero? {
        if (cancion.artista.isBlank()) return null
        synchronized(memoria) { if (cancion.clave in memoria) return memoria[cancion.clave] }
        var huboError = false
        suspend fun probar(nombre: String, fuente: suspend () -> Genero?): Genero? = try {
            fuente()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            huboError = true
            Registro.aviso(Registro.GENERO, "$nombre: ${e.message}")
            null
        }
        val deBeatport = probar("Beatport") { beatport.buscar(cancion) }
        val porCancion = if (deBeatport != null && !FuenteBeatport.esGenerico(deBeatport)) deBeatport else {
            // Sin Beatport, o con una categoría genérica: lo que digan de esta canción en MusicBrainz es más concreto.
            val deMusicBrainz = probar("MusicBrainz") { musicBrainz.buscar(cancion) }
            when {
                deMusicBrainz != null && !deMusicBrainz.deArtista -> deMusicBrainz
                else -> deBeatport ?: deMusicBrainz
            }
        }
        // Deezer solo da la categoría amplia: se usa cuando no hay nada más.
        val g = porCancion ?: probar("Deezer") { deezer.buscar(cancion) }
        Registro.info(Registro.GENERO, "\"${cancion.titulo}\" de ${cancion.artista}: ${g ?: "sin género"}")
        // Sin red o con una fuente caída no se recuerda, para reintentar la próxima vez que suene.
        if (g != null || !huboError) synchronized(memoria) { memoria[cancion.clave] = g }
        return g
    }
}
