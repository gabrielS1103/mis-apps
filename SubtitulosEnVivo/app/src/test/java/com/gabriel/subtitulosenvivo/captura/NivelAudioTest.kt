package com.gabriel.subtitulosenvivo.captura

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class NivelAudioTest {

    @Test
    fun silencioDigitalDaElMinimo() {
        assertEquals(NivelAudio.SILENCIO_DB, NivelAudio.rmsDb(ShortArray(1600)), 0f)
    }

    @Test
    fun ondaCuadradaAlMaximoDaCasiCero() {
        val bloque = ShortArray(1600) { if (it % 2 == 0) Short.MAX_VALUE else Short.MIN_VALUE }
        assertEquals(0f, NivelAudio.rmsDb(bloque), 0.01f)
    }

    @Test
    fun soloCuentaLasMuestrasLeidas() {
        val bloque = ShortArray(1600)
        for (i in 0 until 800) bloque[i] = 16384 // −6 dB
        assertEquals(-6.02f, NivelAudio.rmsDb(bloque, 800), 0.05f)
    }

    @Test
    fun laMuestraCircularGuardaLoMasReciente() {
        val m = MuestraCircular(5)
        m.agregar(shortArrayOf(1, 2, 3))
        assertArrayEquals(shortArrayOf(1, 2, 3), m.contenido())
        m.agregar(shortArrayOf(4, 5, 6, 7))
        assertArrayEquals(shortArrayOf(3, 4, 5, 6, 7), m.contenido())
        m.vaciar()
        assertEquals(0, m.contenido().size)
    }
}
