package com.gabriel.subtitulosenvivo.voz

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SegmentadorTest {
    private val bloque = ShortArray(1600) { 1000 } // 100 ms
    // Probabilidad de voz que daría el detector Silero
    private val VOZ = 0.9f
    private val SILENCIO = 0.02f
    private val MUSICA = 0.1f

    /** Pasa una secuencia de bloques de 100 ms y devuelve las frases que salieron. */
    private fun Segmentador.pasar(niveles: List<Float>): List<FloatArray> {
        val frases = ArrayList<FloatArray>()
        for (db in niveles) agregar(bloque, bloque.size, db)?.let { frases += it }
        return frases
    }

    private fun repetir(db: Float, veces: Int) = List(veces) { db }

    /** Habla real: palabras con huecos cortos donde el detector duda. */
    private fun habla(bloques: Int, @Suppress("UNUSED_PARAMETER") fondo: Float) = List(bloques) { if (it % 5 == 4) 0.2f else VOZ }

    @Test
    fun cortaEnLaPausa() {
        val frases = Segmentador().pasar(repetir(SILENCIO, 10) + repetir(VOZ, 25) + repetir(SILENCIO, 4))
        assertEquals(1, frases.size)
        // 200 ms previos + 2,5 s de voz + 300 ms de pausa
        assertEquals(1600 * (2 + 25 + 3), frases[0].size)
    }

    @Test
    fun unaFraseCortaEsperaUnaPausaMasLarga() {
        // "I never… marriage": 1,5 s de voz, 400 ms de pausa y sigue hablando → una sola frase
        val frases = Segmentador().pasar(repetir(SILENCIO, 10) + repetir(VOZ, 15) + repetir(SILENCIO, 4) + repetir(VOZ, 10) + repetir(SILENCIO, 8))
        assertEquals(1, frases.size)
        assertEquals(1600 * (2 + 15 + 4 + 10 + 3), frases[0].size)
    }

    @Test
    fun unaFraseCortaSolaSeCierraConLaPausaLarga() {
        val frases = Segmentador().pasar(repetir(SILENCIO, 10) + repetir(VOZ, 15) + repetir(SILENCIO, 8))
        assertEquals(1, frases.size)
        assertEquals(1600 * (2 + 15 + 7), frases[0].size)
    }

    @Test
    fun entregaAvancesMientrasSeHabla() {
        val s = Segmentador()
        var avances = 0
        for (db in repetir(SILENCIO, 10) + habla(40, SILENCIO)) {
            s.agregar(bloque, bloque.size, db)
            s.parcial()?.let { avances++ }
        }
        // cada ~1 s mientras hay voz (3,2 s en total)
        assertEquals(3, avances)
        assertEquals(0, s.cerradas)
    }

    @Test
    fun vaciarCierraLaFraseSinPausa() {
        val s = Segmentador()
        s.pasar(repetir(SILENCIO, 10) + repetir(VOZ, 15))
        assertEquals(1600 * (2 + 15), s.vaciar()?.size)
        assertEquals(1, s.cerradas)
        assertNull(s.vaciar())
    }

    @Test
    fun noCortaEnPausasCortasDentroDeLaFrase() {
        val frases = Segmentador().pasar(repetir(VOZ, 10) + repetir(SILENCIO, 2) + repetir(VOZ, 10))
        assertEquals(0, frases.size)
    }

    @Test
    fun detectaPausasConMusicaDeFondo() {
        val frases = Segmentador().pasar(repetir(MUSICA, 30) + habla(20, MUSICA) + repetir(MUSICA, 4))
        assertEquals(1, frases.size)
        // la frase termina en un hueco de 100 ms, así que bastan 2 bloques de música para llegar a 300 ms
        assertEquals(1600 * (2 + 20 + 2), frases[0].size)
    }

    @Test
    fun laMusicaSolaNoEsVoz() {
        assertEquals(0, Segmentador().pasar(repetir(MUSICA, 100)).size)
    }

    @Test
    fun respetaElMaximoSiNuncaHayPausa() {
        val frases = Segmentador(maximoMs = 5_000).pasar(repetir(MUSICA, 30) + habla(110, MUSICA))
        assertEquals(2, frases.size)
        // ninguna pasa del máximo y ninguna se corta mucho antes
        frases.forEach { assert(it.size in 1600 * 35..1600 * 50) { "${it.size / 1600} bloques" } }
    }

    @Test
    fun sinPausaCortaEnElHuecoEntrePalabras() {
        // 4,4 s de voz, un bloque dudoso (hueco entre palabras) y más voz sin pausa
        val niveles = repetir(SILENCIO, 10) + repetir(VOZ, 44) + listOf(0.4f) + repetir(VOZ, 20)
        val s = Segmentador(maximoMs = 5_000)
        val frases = s.pasar(niveles)
        assertEquals(1, frases.size)
        // 200 ms previos + 4,4 s de voz + el hueco; lo demás queda para la frase siguiente
        assertEquals(1600 * (2 + 44 + 1), frases[0].size)
        assertEquals(1600 * (20 - 3 + 3), s.vaciar()?.size)
    }

    @Test
    fun ignoraRuidosSueltos() {
        assertEquals(0, Segmentador().pasar(repetir(VOZ, 2) + repetir(SILENCIO, 10)).size)
    }

    @Test
    fun soloSilencioNoEmiteNada() {
        val s = Segmentador()
        repeat(200) { assertNull(s.agregar(bloque, bloque.size, SILENCIO)) }
    }

    @Test
    fun conviertePcmAFloat() {
        val f = Segmentador().pasar(repetir(SILENCIO, 3) + repetir(VOZ, 5) + repetir(SILENCIO, 7)).single()
        assertEquals(1000 / 32768f, f[0], 1e-6f)
    }

    @Test
    fun limpiadorQuitaAcotacionesYFrasesInventadas() {
        assertEquals("Hello there.", LimpiadorTexto.limpiar(" [Music] Hello  there. "))
        assertNull(LimpiadorTexto.limpiar("(applause)"))
        assertNull(LimpiadorTexto.limpiar("♪♪"))
        assertNull(LimpiadorTexto.limpiar(" Thanks for watching!"))
        assertNull(LimpiadorTexto.limpiar(" ..."))
        assertEquals("Thank you so much", LimpiadorTexto.limpiar("Thank you so much"))
    }

    @Test
    fun limpiadorQuitaLaFraseRepetida() {
        // Casos reales del registro del S21
        assertEquals(
            "Yeah, are you okay? Oh, if you really must not take...",
            LimpiadorTexto.limpiar("Yeah, are you okay? Oh, if you really must not take... Yeah, are you okay? Oh, if you really must not take"),
        )
        assertEquals(
            "Cause it's the only thing that I have control over.",
            LimpiadorTexto.limpiar("Cause it's the only thing that I have control over. Because it's the only thing that I"),
        )
        assertEquals("...and I think this is...", LimpiadorTexto.limpiar("...and I think this is... ...and I think this is... ...and"))
        assertEquals("I love you.", LimpiadorTexto.limpiar("I love you. I love you. I love you. I love you. I love you."))
        // Sin repetición: no se toca
        assertEquals("Your mother hasn't slept with me in four years.", LimpiadorTexto.limpiar("Your mother hasn't slept with me in four years."))
        assertEquals("To answer your question, Tanner.", LimpiadorTexto.limpiar("To answer your question, Tanner."))
    }

    @Test
    fun limpiadorRecortaRepeticiones() {
        assertEquals("1, 2, 3, 4", LimpiadorTexto.limpiar("1, 2, 3, 4, 4, 4, 4, 4, 4, 4, 4,"))
        assertEquals("no, no, no", LimpiadorTexto.limpiar("no, no, no"))
        // Casos reales del registro del S21
        assertEquals("I ever I never", LimpiadorTexto.limpiar("I ever I never I never I never I never"))
        assertEquals("Hello, man. La Palabra de la La", LimpiadorTexto.limpiar("Hello, man. La Palabra de la. La Palabra de la. La Palabra de la. La Palabra de la. La"))
        assertNull(LimpiadorTexto.limpiar("M"))
        assertEquals("I'm 82 now.", LimpiadorTexto.limpiar("I'm 82 now."))
        // Una palabra suelta sobre varios segundos de música
        assertNull(LimpiadorTexto.limpiar("It", 5f))
        assertNull(LimpiadorTexto.limpiar("money", 2.7f))
        assertEquals("Hey", LimpiadorTexto.limpiar("Hey", 1.8f))
        assertEquals("Thank you.", LimpiadorTexto.limpiar("Thank you.", 3.9f))
    }
}
