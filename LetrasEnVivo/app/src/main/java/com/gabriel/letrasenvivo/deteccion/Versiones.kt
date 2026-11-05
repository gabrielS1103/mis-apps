package com.gabriel.letrasenvivo.deteccion

import com.gabriel.letrasenvivo.letras.NormalizadorTexto

/**
 * Qué versión de una canción indica su título: "Pressure (Alesso Remix)" es el remix de Alesso, "Sirens Of The Sea
 * (Above & Beyond Club Mix)" el club mix de Above & Beyond, "Hurt (Live)" una versión en directo. Las mezclas que no
 * cambian la producción ("Original Mix", "Extended Mix", "Radio Edit"…) cuentan como la original.
 */
object Versiones {

    /** Palabras que marcan que un trozo del título habla de la versión. */
    private val marca = Regex(
        """\b(remix|mix|mixed|edit|rework|bootleg|vip|flip|dub|version|versión|cover|live|en vivo|en directo|acoustic|acústic[oa]|slowed|sped up|nightcore|reprise|instrumental)\b""",
        RegexOption.IGNORE_CASE,
    )

    /** Palabras que no distinguen una versión de otra: "Extended Mix" y "Radio Edit" son la misma producción. */
    private val neutras = setOf(
        "original", "extended", "radio", "club", "album", "single", "short", "full", "clean", "explicit", "main",
        "remaster", "remastered", "mix", "mixed", "edit", "version", "versión", "the", "de", "la", "el",
        "feat", "ft", "featuring", "with", "con", "and", "y", "x",
    )

    /** Tipos de versión que no dicen de quién es. */
    private val genericas = setOf("remix", "rework", "bootleg", "vip", "flip", "dub", "cover", "reprise")

    /**
     * Clave de la versión ("alesso", "above beyond", "live"), o null si es la original.
     * El primer trozo del título es el nombre de la canción y nunca cuenta, aunque diga "Mix".
     */
    fun clave(titulo: String): String? {
        val trozos = titulo.split(Regex("""[()\[\]]|\s[-–—]\s""")).drop(1)
        for (t in trozos) {
            if (!marca.containsMatchIn(t)) continue
            val palabras = NormalizadorTexto.tokens(t).filter { it !in neutras && !it.all(Char::isDigit) }
            if (palabras.isNotEmpty()) return palabras.joinToString(" ")
        }
        return null
    }

    /** true si dos títulos son la misma versión: ambas originales, o el mismo remix/directo/acústico. */
    fun compatibles(a: String?, b: String?): Boolean {
        if (a == null || b == null) return a == b
        // "remix" lo comparten todos los remixes: lo que distingue uno de otro es quién lo hizo.
        val ta = a.split(' ').toSet() - genericas
        val tb = b.split(' ').toSet() - genericas
        if (ta.isEmpty() || tb.isEmpty()) return a.split(' ').toSet().intersect(b.split(' ').toSet()).isNotEmpty()
        val comunes = ta.intersect(tb).size
        return comunes > 0 && comunes * 2 >= minOf(ta.size, tb.size)
    }
}
