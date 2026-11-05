package com.gabriel.subtitulosenvivo.voz

/** Quita de la salida de Whisper lo que no es habla: acotaciones, bucles y frases que inventa con música o silencio. */
object LimpiadorTexto {
    private val acotaciones = Regex("""\[[^]]*]|\([^)]*\)|\*[^*]*\*|♪+""")
    private val espacios = Regex("""\s+""")
    /** Una palabra repetida 4 veces o más seguidas ("4, 4, 4, 4…"): Whisper en bucle. Se deja una. */
    private val repeticion = Regex("""\b(\w+)(?:[\s,.;!?]+\1\b){3,}[\s,.;!?]*""")
    /** Un grupo de 2 a 4 palabras repetido 3 veces o más ("I never I never I never"). Se deja uno. */
    private val repeticionVarias = Regex("""\b((?:\w+[\s,.;!?]+){1,3}?\w+)(?:[\s,.;!?]+\1\b){2,}[\s,.;!?]*""", RegexOption.IGNORE_CASE)
    private val letraONumero = Regex("""[\p{L}\p{N}]""")
    private val noLetra = Regex("""[^\p{L}\p{N}']""")
    /** Frases típicas que Whisper "alucina" al final de los videos o sobre música. */
    private val inventadas = setOf(
        "thanks for watching", "thank you for watching", "please subscribe",
        "subscribe to my channel", "you", "bye",
    )

    /** [segundosAudio]: duración del audio que se transcribió (0 si no se sabe). */
    fun limpiar(texto: String?, segundosAudio: Float = 0f): String? {
        if (texto == null) return null
        var t = texto.replace(acotaciones, " ").replace(repeticion, "$1 ").replace(espacios, " ").trim()
        t = quitarFraseRepetida(t).replace(repeticionVarias, "$1 ").trim()
        // Una letra suelta ("M") es ruido, no una palabra.
        if (letraONumero.findAll(t).count() < 2) return null
        val clave = t.lowercase().trimEnd('.', '!', '?', ',').trim()
        if (clave in inventadas) return null
        // Una sola palabra ("It", "Oh", "money") de varios segundos de audio: casi siempre es Whisper oyendo música.
        if (segundosAudio >= 2.5f && ' ' !in t) return null
        return t
    }

    /**
     * Con la ventana de audio recortada, Whisper a veces vuelve a escribir la frase desde el principio
     * ("Are you okay? Oh… Are you okay? Oh…"). Si el final del texto (3 palabras o más) repite el comienzo,
     * se queda solo con la primera vez. Se tolera que la primera palabra de la copia cambie ("Cause…" / "Because…").
     */
    internal fun quitarFraseRepetida(texto: String): String {
        val palabras = texto.split(' ')
        val norm = palabras.map { it.lowercase().replace(noLetra, "") }
        val n = norm.size
        for (j in 2..n - 3) {
            for (desde in 0..1) {
                val corte = j - desde
                if (corte < 2) continue
                val coincide = (0 until n - j).all { k -> norm[j + k] == norm[desde + k] && norm[j + k].isNotEmpty() }
                if (coincide) return palabras.subList(0, corte).joinToString(" ").trimEnd(',', ' ')
            }
        }
        return texto
    }
}
