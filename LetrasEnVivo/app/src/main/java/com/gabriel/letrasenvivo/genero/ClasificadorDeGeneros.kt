package com.gabriel.letrasenvivo.genero

/**
 * Género y subgénero listos para mostrar.
 * @param fuente de dónde salió (Beatport, MusicBrainz…), para depurar.
 * @param deArtista true si no se encontró para la canción y es el estilo general del artista.
 */
data class Genero(val genero: String, val subgenero: String?, val fuente: String = "", val deArtista: Boolean = false)

/**
 * Convierte los géneros votados en MusicBrainz ("alternative rock" 36, "pop" 15…) en un género amplio
 * y un subgénero: el más votado decide la familia ("rock") y él mismo, o el siguiente de esa familia, es el subgénero.
 */
object ClasificadorDeGeneros {

    /** Familias amplias, con su nombre para mostrar. */
    private val familias = mapOf(
        "rock" to "Rock", "pop" to "Pop", "electronic" to "Electrónica", "dance" to "Dance", "hip hop" to "Hip hop",
        "r&b" to "R&B", "soul" to "Soul", "funk" to "Funk", "jazz" to "Jazz", "blues" to "Blues", "classical" to "Clásica",
        "country" to "Country", "folk" to "Folk", "latin" to "Música latina", "metal" to "Metal", "punk" to "Punk",
        "reggae" to "Reggae", "ambient" to "Ambient", "gospel" to "Gospel", "soundtrack" to "Banda sonora",
    )

    /** Palabras que delatan la familia aunque no la nombren ("progressive trance" -> electronic). */
    private val pistas = mapOf(
        "house" to "electronic", "techno" to "electronic", "trance" to "electronic", "edm" to "electronic",
        "dubstep" to "electronic", "drum and bass" to "electronic", "drum & bass" to "electronic", "dnb" to "electronic", "electro" to "electronic", "electronica" to "electronic",
        "hardstyle" to "electronic", "big room" to "electronic", "idm" to "electronic", "eurodance" to "dance", "disco" to "dance",
        "trap" to "hip hop", "rap" to "hip hop", "drill" to "hip hop", "grime" to "hip hop", "boom bap" to "hip hop",
        "reggaeton" to "latin", "trap latino" to "latin", "dembow" to "latin", "bachata" to "latin", "salsa" to "latin",
        "cumbia" to "latin", "merengue" to "latin", "corrido" to "latin", "corridos tumbados" to "latin",
        "regional mexicano" to "latin", "banda" to "latin", "bolero" to "latin", "bossa nova" to "latin", "tango" to "latin",
        "grunge" to "rock", "britpop" to "rock", "post britpop" to "rock", "shoegaze" to "rock", "emo" to "rock",
        "new wave" to "rock", "indie" to "rock", "metalcore" to "metal", "deathcore" to "metal", "djent" to "metal",
        "singer songwriter" to "folk", "new age" to "ambient",
        "phonk" to "hip hop", "lo fi" to "hip hop", "garage" to "electronic", "breakbeat" to "electronic",
        "breaks" to "electronic", "jungle" to "electronic", "liquid funk" to "electronic", "neurofunk" to "electronic",
        "future bass" to "electronic", "bass music" to "electronic", "synthwave" to "electronic", "vaporwave" to "electronic",
        "downtempo" to "electronic", "chillout" to "electronic", "chillstep" to "electronic", "hardcore" to "electronic",
        "witch house" to "electronic", "hyperpop" to "pop",
    )

    /** Nombres que no basta con poner en mayúscula. */
    private val nombres = mapOf(
        "reggaeton" to "Reguetón", "trap latino" to "Trap latino", "latin pop" to "Pop latino", "latin rock" to "Rock latino",
        "electronic" to "Electrónica", "electronica" to "Electrónica", "classical" to "Clásica", "soundtrack" to "Banda sonora",
        "singer songwriter" to "Cantautor", "regional mexicano" to "Regional mexicano", "new wave" to "New wave",
        "drum and bass" to "Drum and bass", "hip hop" to "Hip hop", "lo fi" to "Lo-fi", "uk garage" to "UK garage", "r&b" to "R&B", "edm" to "EDM", "idm" to "IDM",
        "contemporary r&b" to "R&B contemporáneo", "pop rap" to "Pop rap", "dance pop" to "Dance pop", "synth pop" to "Synth pop",
    )

    /** "alternative rock" -> "Rock alternativo". */
    private val adjetivos = mapOf(
        "alternative" to "alternativo", "progressive" to "progresivo", "psychedelic" to "psicodélico", "classic" to "clásico",
        "contemporary" to "contemporáneo", "experimental" to "experimental", "gothic" to "gótico", "symphonic" to "sinfónico",
        "melodic" to "melódico", "acoustic" to "acústico", "instrumental" to "instrumental", "latin" to "latino",
        "uplifting" to "uplifting", "vocal" to "vocal", "soft" to "suave",
    )

    private val todasLasClaves = (familias.keys.map { it to it } + pistas.toList())

    /** @param etiquetas nombre y votos, en cualquier orden. null si ninguna es un género reconocible. */
    fun clasificar(etiquetas: List<Pair<String, Int>>): Genero? {
        val conFamilia = etiquetas
            .sortedByDescending { it.second }
            .map { normalizar(it.first) }
            .distinct()
            .mapNotNull { nombre -> familiaDe(nombre)?.let { nombre to it } }
        val (primera, familia) = conFamilia.firstOrNull() ?: return null
        val sub = if (primera != familia) primera else conFamilia.firstOrNull { it.second == familia && it.first != familia }?.first
        return Genero(familias.getValue(familia), sub?.let { nombreEnEspanol(it) })
    }

    /** La familia de un género: la palabra clave que aparece más a la derecha ("dance pop" -> pop, "pop rock" -> rock). */
    fun familiaDe(nombre: String): String? {
        val n = normalizar(nombre)
        var mejor: Triple<Int, Int, String>? = null // dónde acaba la clave, su largo, la familia
        for ((clave, familia) in todasLasClaves) {
            val fin = Regex("""(?<!\S)${Regex.escape(clave)}(?!\S)""").findAll(n).lastOrNull()?.range?.last ?: continue
            // A igual final gana la clave más larga ("post britpop" antes que "britpop").
            if (mejor == null || fin > mejor.first || (fin == mejor.first && clave.length > mejor.second)) mejor = Triple(fin, clave.length, familia)
        }
        return mejor?.third
    }

    /** "Electro House" -> "Electrónica"; null si no se reconoce la familia. */
    fun familiaParaMostrar(nombre: String): String? = familiaDe(nombre)?.let { familias.getValue(it) }

    fun nombreEnEspanol(nombre: String): String {
        val n = normalizar(nombre)
        nombres[n]?.let { return it }
        val palabras = n.split(' ')
        val adjetivo = adjetivos[palabras.first()]
        if (adjetivo != null && palabras.size > 1) {
            val resto = palabras.drop(1).joinToString(" ")
            return "${nombres[resto] ?: familias[resto] ?: mayuscula(resto)} $adjetivo"
        }
        return mayuscula(n)
    }

    private fun normalizar(s: String) = s.lowercase().replace('-', ' ').replace(Regex("""\s+"""), " ").trim()

    private fun mayuscula(s: String) = s.replaceFirstChar { it.uppercase() }
}
