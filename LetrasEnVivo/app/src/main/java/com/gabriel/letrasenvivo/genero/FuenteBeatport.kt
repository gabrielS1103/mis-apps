package com.gabriel.letrasenvivo.genero

import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.letras.Emparejador
import com.gabriel.letrasenvivo.red.NavegadorOculto
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import java.io.File
import java.net.URLEncoder

/**
 * Beatport pone a cada pista su género y subgénero oficiales ("Drum & Bass" / "Liquid"), también a las
 * de sellos pequeños: es la mejor fuente para electrónica underground. Está tras Cloudflare, así que la
 * búsqueda se carga con el navegador oculto y se lee el JSON de Next.js que trae la página.
 */
class FuenteBeatport(private val navegador: NavegadorOculto, private val depuracion: File? = null) {

    /** @param fecha "2010-05-11T00:00:00": las fechas ISO se ordenan bien como texto. */
    data class Pista(val titulo: String, val mezcla: String?, val artistas: String, val genero: String?, val subgenero: String?, val fecha: String? = null)

    suspend fun buscar(cancion: CancionDetectada): Genero? {
        // En un remix se busca también al remixer, para que su versión salga entre los resultados.
        val consulta = listOfNotNull(cancion.artistaPrincipal, cancion.tituloBase, cancion.version).joinToString(" ")
        val html = navegador.obtener("https://www.beatport.com/search/tracks?q=${URLEncoder.encode(consulta, "UTF-8")}", 25_000) ?: return null
        try { depuracion?.writeText(html) } catch (_: Exception) {}
        val pistas = analizar(html)
        // Sin duración: Beatport suele tener la versión extendida y el género es el mismo en todas las mezclas.
        val iguales = pistas.filter { p ->
            // El nombre de la mezcla dice qué versión es ("Alesso Remix", "Original Mix").
            val conMezcla = sinFeat(p.titulo) + (p.mezcla?.let { " ($it)" } ?: "")
            p.genero != null && (Emparejador.puntuar(conMezcla, p.artistas, null, cancion) ?: 0.0) >= 0.8
        }
        // La misma canción suele estar varias veces (reediciones, recopilatorios) y no siempre con el mismo género:
        // manda el lanzamiento original, que es el que clasificó su sello ("Say It's Over": Progressive House en 2010,
        // "Dance / Pop" en una reedición).
        val p = iguales.minByOrNull { it.fecha ?: "9999" }
        Registro.info(Registro.GENERO, "Beatport \"$consulta\": ${pistas.size} pistas, ${iguales.size} de esta canción; elegida=$p")
        if (p == null) return null
        // El género de Beatport ya es un estilo concreto ("Electro House"): va como subgénero, con su
        // subgénero detrás si lo tiene ("Drum & Bass › Liquid"); la familia sale de él (todo Beatport es electrónica).
        val estilo = listOfNotNull(p.genero, p.subgenero?.takeIf { !it.equals(p.genero, ignoreCase = true) }).joinToString(" › ")
        return Genero(ClasificadorDeGeneros.familiaParaMostrar(p.genero!!) ?: "Electrónica", estilo, "Beatport")
    }

    companion object {
        /** Categorías cajón de sastre de Beatport: dicen poco del estilo ("Justify" de No Mana es "Dance / Pop"; es electro house). */
        private val genericos = setOf("dance / pop", "mainstage", "electronica", "pop", "dance")

        /** true si Beatport solo dio una categoría genérica sin subgénero: vale la pena buscar algo más concreto. */
        fun esGenerico(g: Genero) = g.fuente == "Beatport" && g.subgenero?.lowercase() in genericos

        private val json = Json { ignoreUnknownKeys = true; isLenient = true }
        private val datosNext = Regex("""<script id="__NEXT_DATA__"[^>]*>(.*?)</script>""", RegexOption.DOT_MATCHES_ALL)

        /** Pistas que aparezcan en cualquier parte del JSON de la página (la estructura cambia con el tiempo). */
        fun analizar(html: String): List<Pista> {
            val crudo = datosNext.find(html)?.groupValues?.get(1) ?: return emptyList()
            val raiz = try { json.parseToJsonElement(crudo) } catch (e: Exception) { return emptyList() }
            val pistas = ArrayList<Pista>()
            recorrer(raiz) { o ->
                val titulo = texto(o, "track_name") ?: texto(o, "name") ?: return@recorrer
                if ("genre" !in o) return@recorrer
                val artistas = (o["artists"] as? JsonArray)?.mapNotNull { (it as? JsonObject)?.let { a -> texto(a, "artist_name") ?: texto(a, "name") } }.orEmpty()
                if (artistas.isEmpty()) return@recorrer
                val fecha = texto(o, "release_date") ?: texto(o, "publish_date")
                pistas += Pista(titulo, texto(o, "mix_name"), artistas.joinToString(", "), nombreDe(o["genre"]), nombreDe(o["sub_genre"]), fecha)
            }
            return pistas.distinct()
        }

        private val feat = Regex("""(?:\s|[(\[])+(feat\.?|ft\.?|featuring)\s.*$""", RegexOption.IGNORE_CASE)

        /** "Say It's Over feat. Mindy Gledhill" -> "Say It's Over" (YouTube Music lo pone aparte o entre paréntesis). */
        fun sinFeat(titulo: String) = feat.replace(titulo, "").trim().ifEmpty { titulo }

        private fun recorrer(e: JsonElement, alEncontrar: (JsonObject) -> Unit) {
            when (e) {
                is JsonObject -> { alEncontrar(e); e.values.forEach { recorrer(it, alEncontrar) } }
                is JsonArray -> e.forEach { recorrer(it, alEncontrar) }
                else -> {}
            }
        }

        /** El género viene como objeto {name}, como lista [{genre_name}] o como texto, según la página. */
        private fun nombreDe(e: JsonElement?): String? = when (e) {
            is JsonObject -> texto(e, "genre_name") ?: texto(e, "sub_genre_name") ?: texto(e, "name")
            is JsonArray -> e.firstNotNullOfOrNull { nombreDe(it) }
            is JsonPrimitive -> e.contentOrNull?.takeIf { e.isString }
            else -> null
        }?.takeIf { it.isNotBlank() }

        private fun texto(o: JsonObject, clave: String) = (o[clave] as? JsonPrimitive)?.takeIf { it.isString }?.contentOrNull?.takeIf { it.isNotBlank() }
    }
}
