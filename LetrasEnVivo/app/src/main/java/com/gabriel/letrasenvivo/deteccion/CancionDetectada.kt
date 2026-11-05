package com.gabriel.letrasenvivo.deteccion

import com.gabriel.letrasenvivo.letras.NormalizadorTexto

/** La canción que está sonando según la sesión de medios del reproductor. */
data class CancionDetectada(
    val titulo: String,
    val artista: String,
    val album: String?,
    val duracionMs: Long,
    val paquete: String,
    /** true si suena el vídeo musical (no la pista de audio): su duración y su intro no coinciden con la canción. */
    val esVideo: Boolean = false,
) {
    /** Clave estable para la caché: artista + título normalizados (el vídeo tiene su propia entrada y su propio desfase). */
    val clave: String get() = NormalizadorTexto.clave(artista) + "|" + NormalizadorTexto.clave(titulo) + if (esVideo) "|video" else ""

    /** Primer artista cuando vienen varios: "A, B & C" -> "A". */
    val artistaPrincipal: String
        get() = primerArtista(artista)

    val titulo_limpio: String get() = limpiarTitulo(titulo)

    /** Sin los colaboradores que YouTube Music añade al título: "(con X)", "(feat. X)"… */
    val tituloSinColaboradores: String get() = quitarColaboradores(titulo_limpio)

    /** Además sin sufijos de versión: " - Radio Edit", "(Extended Mix)", "(Remastered 2009)"… */
    val tituloBase: String get() = quitarVersion(tituloSinColaboradores)

    /** Qué versión es ("alesso" para "(Alesso Remix)", "live"…); null si es la original. Ver [Versiones]. */
    val version: String? get() = Versiones.clave(titulo)

    /** Remix, mix o versión extendida: otra canción en cuanto a tiempos. */
    val esRemix: Boolean get() = Regex("""\b(remix|mix|rework|bootleg|vip|extended|dub)\b""", RegexOption.IGNORE_CASE).containsMatchIn(titulo)

    /** Variantes del título de más fiel a más genérica, sin repetidos, para buscar en las fuentes. */
    val variantesTitulo: List<String> get() = listOf(titulo_limpio, tituloSinColaboradores, tituloBase).filter { it.isNotBlank() }.distinct()

    override fun toString(): String = "$artista – $titulo (${duracionMs / 1000} s, $paquete)"

    companion object {
        const val PAQUETE_YT_MUSIC = "com.google.android.apps.youtube.music"
        const val PAQUETE_YOUTUBE = "com.google.android.youtube"

        private val marcaVideo = Regex("""(official\s+(music\s+)?video|video\s+oficial|v[ií]deo\s*clip|music\s+video|\bM/?V\b|official\s+visuali[sz]er)""", RegexOption.IGNORE_CASE)

        /** El título trae una marca de vídeo musical ("(Official Video)", "Video Oficial", "MV"…). */
        fun pareceVideo(tituloCrudo: String) = marcaVideo.containsMatchIn(tituloCrudo)

        /**
         * Las subidas de vídeo en YouTube Music suelen llevar el artista dentro del título:
         * "Calvin Harris - Flashback" o "Calvin Harris Flashback". Se quita si el título empieza por él.
         */
        fun quitarArtistaDelTitulo(titulo: String, artista: String): String {
            // Se prueba con el artista completo y con el primero ("Dash Berlin y Emma Hewitt" -> "Dash Berlin").
            val candidatos = listOf(artista.trim(), primerArtista(artista)).filter { it.length >= 2 }.distinct()
            val a = candidatos.firstOrNull { titulo.startsWith(it, ignoreCase = true) } ?: return titulo
            var resto = titulo.substring(a.length).trim()
            // "Dash Berlin Feat. Emma Hewitt - Waiting": también fuera el "feat. X" hasta el guion.
            val conFeat = Regex("""^(feat\.?|featuring|ft\.?|con|with|&|y|x)\s+[^-–—]*?\s+[-–—]\s+(.+)$""", RegexOption.IGNORE_CASE).find(resto)
            if (conFeat != null) resto = conFeat.groupValues[2]
            resto = resto.trimStart(' ', '-', '–', '—', ':', '|', '·').trim()
            return resto.ifEmpty { titulo }
        }

        private val separadorArtistas = Regex("""\s*(,|&|;|/|\sfeat\.?\s|\sfeaturing\s|\sft\.?\s|\sx\s|\sy\s|\sand\s|\scon\s|\swith\s)\s*""", RegexOption.IGNORE_CASE)

        fun primerArtista(artista: String): String =
            artista.split(separadorArtistas).firstOrNull()?.trim()?.ifEmpty { artista } ?: artista

        private val canal = Regex("""(recordings|records|music|musik|tv|vevo|official|channel|label|audio|entertainment|productions)\b""", RegexOption.IGNORE_CASE)

        /** El "artista" es en realidad un canal o sello ("Black Hole Recordings", "Armada Music TV"). */
        fun esCanal(artista: String) = canal.containsMatchIn(artista)

        /** YouTube Music oficial o variantes (ReVanced: app.revanced.android.apps.youtube.music). */
        fun esYtMusic(paquete: String) = paquete == PAQUETE_YT_MUSIC || paquete.endsWith(".youtube.music")

        /** YouTube oficial o variantes (ReVanced: app.revanced.android.youtube). */
        fun esYouTube(paquete: String) = paquete == PAQUETE_YOUTUBE || (paquete.endsWith(".youtube") && !esYtMusic(paquete))

        private val basura = listOf(
            Regex("\\((official|lyrics?|lyric video|official video|official audio|audio|visualizer|video oficial|letra|hd|4k|remaster(ed)?( \\d{4})?)[^)]*\\)", RegexOption.IGNORE_CASE),
            Regex("\\[(official|lyrics?|lyric video|official video|official audio|audio|visualizer|video oficial|letra|hd|4k)[^]]*]", RegexOption.IGNORE_CASE),
            Regex("\\s*[|•]\\s*.*$"),
            Regex("\\s+-\\s+(official|lyrics?|official video|official audio|audio|video oficial|letra).*$", RegexOption.IGNORE_CASE),
        )

        private val colaboradores = Regex("""\s*[(\[](con|feat\.?|ft\.?|featuring|with|junto a)\s[^)\]]*[)\]]""", RegexOption.IGNORE_CASE)
        private val sufijoVersion = Regex("""\s+-\s+.*$""")
        private val versionEntreParentesis = Regex("""\s*[(\[]([^)\]]*(remix|mix|edit|version|versión|remaster|remasterizad|mono|stereo|live|en vivo|en directo|acoustic|acústic|a ?cappella|instrumental|demo|single|album|deluxe|bonus|explicit|clean|sped up|slowed)[^)\]]*)[)\]]""", RegexOption.IGNORE_CASE)
        private val espacios = Regex("""\s+""")

        fun quitarColaboradores(titulo: String): String =
            colaboradores.replace(titulo, "").replace(espacios, " ").trim().ifEmpty { titulo }

        fun quitarVersion(titulo: String): String {
            var t = versionEntreParentesis.replace(titulo, "")
            t = sufijoVersion.replace(t, "")
            return t.replace(espacios, " ").trim().ifEmpty { titulo }
        }

        /** Quita adornos típicos de los títulos de YouTube ("(Official Video)", "[Lyrics]"…). */
        fun limpiarTitulo(titulo: String): String {
            var t = titulo
            for (r in basura) t = r.replace(t, "")
            return t.replace(Regex("\\s+"), " ").trim().ifEmpty { titulo.trim() }
        }

        /** En YouTube (no Music) el título suele ser "Artista - Canción" y el artista un canal. */
        fun separarArtistaYTitulo(titulo: String, artista: String): Pair<String, String> {
            val partes = titulo.split(Regex("\\s+[-–—]\\s+"), limit = 2)
            val canal = artista.removeSuffix(" - Topic").trim()
            return if (partes.size == 2 && partes[0].length <= 40) {
                partes[0].trim() to limpiarTitulo(partes[1])
            } else {
                canal to limpiarTitulo(titulo)
            }
        }
    }
}
