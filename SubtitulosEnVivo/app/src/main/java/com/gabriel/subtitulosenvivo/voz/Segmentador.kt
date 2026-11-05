package com.gabriel.subtitulosenvivo.voz

/**
 * Corta el audio continuo en frases para Whisper: empieza cuando alguien habla, termina en la primera
 * pausa (o al llegar al máximo, si nunca hay una pausa clara).
 *
 * Cada bloque llega con la probabilidad de voz del detector Silero ([DetectorVoz]), que ignora la música:
 * una canción de fondo no abre frases ni impide ver las pausas entre una frase y otra.
 */
class Segmentador(
    private val frecuencia: Int = 16_000,
    /** Probabilidad desde la que empieza una frase. */
    private val umbralInicio: Float = 0.5f,
    /** Ya dentro de una frase basta algo menos, para no cortar en una palabra dicha más bajo. */
    private val umbralSigue: Float = 0.35f,
    private val silencioParaCortarMs: Int = 300,
    /** Una frase corta ("I never…") necesita una pausa más larga para cerrarse: suele seguir. */
    private val silencioFraseCortaMs: Int = 700,
    private val fraseCortaMs: Int = 2_000,
    private val maximoMs: Int = 6_000,
    /**
     * Si se llega al máximo sin pausa, el corte se hace en el punto más dudoso del último tramo
     * (el hueco entre dos palabras) en vez de partir una palabra; lo que sigue empieza la próxima frase.
     */
    private val buscarCorteMs: Int = 1_500,
    /** Audio previo al primer bloque con voz, para no comerse el inicio de la primera palabra. */
    private val previoMs: Int = 200,
    /** Por debajo de esto es un ruido suelto, no una palabra. */
    private val minimoVozMs: Int = 300,
    /** Cada cuánto se entrega el audio de la frase en curso para mostrar un avance antes de que termine. */
    private val cadaParcialMs: Int = 1_000,
) {
    private val actual = ArrayList<FloatArray>()
    /** Probabilidad de voz de cada bloque de [actual] (0 para el audio previo). */
    private val probabilidades = ArrayList<Float>()
    private val previo = ArrayDeque<FloatArray>()
    private var muestrasActual = 0
    private var muestrasPrevio = 0
    private var vozMs = 0f
    private var silencioMs = 0f
    private var muestrasUltimoParcial = 0

    /** Cuántas frases se cerraron; sirve para reconocer avances viejos de una frase que ya terminó. */
    var cerradas = 0
        private set

    /** Devuelve una frase completa cuando se cierra, o null mientras se sigue acumulando. */
    fun agregar(bloque: ShortArray, cantidad: Int, probabilidadVoz: Float): FloatArray? {
        val f = FloatArray(cantidad) { bloque[it] / 32768f }
        val ms = cantidad * 1000f / frecuencia
        val hayVoz = probabilidadVoz >= if (actual.isEmpty()) umbralInicio else umbralSigue

        if (actual.isEmpty()) {
            if (!hayVoz) {
                guardarPrevio(f)
                return null
            }
            actual.addAll(previo); muestrasActual = muestrasPrevio
            repeat(previo.size) { probabilidades += 0f }
            previo.clear(); muestrasPrevio = 0
            vozMs = 0f; silencioMs = 0f
        }

        actual += f
        probabilidades += probabilidadVoz
        muestrasActual += cantidad
        if (hayVoz) { vozMs += ms; silencioMs = 0f } else silencioMs += ms

        val duracionMs = muestrasActual * 1000f / frecuencia
        val pausaNecesaria = if (duracionMs - silencioMs < fraseCortaMs) silencioFraseCortaMs else silencioParaCortarMs
        if (silencioMs >= pausaNecesaria) return cerrar()
        if (duracionMs < maximoMs) return null
        return cortarSinPausa()
    }

    /** Cierra la frase en el bloque menos "voz" del último tramo y deja el resto como inicio de la siguiente. */
    private fun cortarSinPausa(): FloatArray? {
        val ms = actual.last().size * 1000f / frecuencia
        val tramo = (buscarCorteMs / ms).toInt().coerceIn(1, actual.size - 1)
        var corte = actual.size - 1
        for (i in actual.size - 1 downTo actual.size - tramo) if (probabilidades[i] < probabilidades[corte]) corte = i
        val restoAudio = actual.subList(corte + 1, actual.size).toList()
        val restoProbs = probabilidades.subList(corte + 1, actual.size).toList()
        repeat(restoAudio.size) { actual.removeAt(actual.lastIndex); probabilidades.removeAt(probabilidades.lastIndex) }
        muestrasActual -= restoAudio.sumOf { it.size }
        val frase = cerrar()
        // El resto ya se sabe que es voz (la frase sigue): empieza la siguiente sin esperar.
        for ((bloque, p) in restoAudio.zip(restoProbs)) {
            actual += bloque; probabilidades += p; muestrasActual += bloque.size
            val bloqueMs = bloque.size * 1000f / frecuencia
            if (p >= umbralSigue) { vozMs += bloqueMs; silencioMs = 0f } else silencioMs += bloqueMs
        }
        return frase
    }

    /**
     * El audio de la frase en curso si toca mostrar un avance: mientras se sigue hablando,
     * con al menos 1 s de voz y cada [cadaParcialMs].
     */
    fun parcial(): FloatArray? {
        if (actual.isEmpty() || silencioMs > 0f || vozMs < cadaParcialMs) return null
        if (muestrasActual - muestrasUltimoParcial < cadaParcialMs * frecuencia / 1000) return null
        muestrasUltimoParcial = muestrasActual
        return unir()
    }

    /** Cierra la frase en curso aunque no haya pausa: el audio dejó de llegar (terminó el video). */
    fun vaciar(): FloatArray? = if (actual.isEmpty()) null else cerrar()

    private fun cerrar(): FloatArray? {
        val frase = if (vozMs >= minimoVozMs) unir() else null
        actual.clear(); probabilidades.clear(); muestrasActual = 0
        vozMs = 0f; silencioMs = 0f; muestrasUltimoParcial = 0
        if (frase != null) cerradas++
        return frase
    }

    private fun unir(): FloatArray {
        val salida = FloatArray(muestrasActual)
        var i = 0
        for (b in actual) { b.copyInto(salida, i); i += b.size }
        return salida
    }

    private fun guardarPrevio(f: FloatArray) {
        previo.addLast(f); muestrasPrevio += f.size
        val limite = previoMs * frecuencia / 1000
        while (muestrasPrevio - previo.first().size >= limite) muestrasPrevio -= previo.removeFirst().size
    }
}
