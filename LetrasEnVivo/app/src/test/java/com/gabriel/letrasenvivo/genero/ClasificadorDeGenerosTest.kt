package com.gabriel.letrasenvivo.genero

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ClasificadorDeGenerosTest {

    @Test
    fun elMasVotadoDecideLaFamiliaYElSubgenero() {
        // Géneros de Coldplay en MusicBrainz.
        val g = ClasificadorDeGeneros.clasificar(listOf("pop" to 15, "alternative rock" to 36, "pop rock" to 13, "rock" to 5))
        assertEquals(Genero("Rock", "Rock alternativo"), g)
    }

    @Test
    fun siElMasVotadoEsLaFamiliaElSubgeneroEsElSiguienteDeEsaFamilia() {
        // The Weeknd.
        val g = ClasificadorDeGeneros.clasificar(listOf("r&b" to 15, "contemporary r&b" to 8, "dance-pop" to 7, "pop" to 4))
        assertEquals(Genero("R&B", "R&B contemporáneo"), g)
    }

    @Test
    fun generosLatinosYElectronicos() {
        assertEquals(Genero("Música latina", "Reguetón"), ClasificadorDeGeneros.clasificar(listOf("reggaeton" to 7, "trap latino" to 5, "latin" to 3)))
        assertEquals(Genero("Electrónica", "Trance vocal"), ClasificadorDeGeneros.clasificar(listOf("vocal trance" to 4, "trance" to 3)))
        assertEquals("latin", ClasificadorDeGeneros.familiaDe("trap latino"))
        assertEquals("pop", ClasificadorDeGeneros.familiaDe("dance-pop"))
    }

    @Test
    fun ignoraEtiquetasQueNoSonGeneros() {
        assertNull(ClasificadorDeGeneros.clasificar(listOf("seen live" to 9, "dutch" to 3)))
        assertEquals(Genero("Pop", null), ClasificadorDeGeneros.clasificar(listOf("seen live" to 9, "pop" to 2)))
    }
}
