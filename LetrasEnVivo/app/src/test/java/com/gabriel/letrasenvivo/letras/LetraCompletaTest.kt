package com.gabriel.letrasenvivo.letras

import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LetraCompletaTest {

    // "Black Room Boy" de Above & Beyond en YouTube Music: 6:10.
    private val cancion = CancionDetectada("Black Room Boy", "Above & Beyond", "Group Therapy", 370_000, "p")

    private fun letra(id: String, titulo: String, ultimo: String) =
        LetraEncontrada("LRCLIB", id, titulo, "Above & Beyond", 370.0, "[00:29.36]It's the same old scene\n[$ultimo]You're a black room boy")

    @Test
    fun leeElUltimoTiempo() {
        assertEquals(153.98, letra("1", "Black Room Boy", "02:33.98").ultimoTiempoSeg!!, 0.001)
    }

    @Test
    fun cambiaUnaLetraCortadaPorOtraQueSigueMasAlla() {
        val cortada = letra("1150946", "Black Room Boy", "02:33.98")
        val completa = letra("36221937", "Black Room Boy (Original Mix)", "05:23.81")
        assertTrue(Emparejador.pareceCortada(cortada, cancion))
        assertNotNull(Emparejador.puntuar(completa.titulo, completa.artista, completa.duracionSeg, cancion))
        assertEquals("36221937", Emparejador.preferirCompleta(cortada, listOf(cortada, completa), cancion).id)
    }

    @Test
    fun noCambiaPorPocosSegundosNiPorTiemposImposibles() {
        val buena = letra("1", "Black Room Boy", "05:23.81")
        assertFalse(Emparejador.pareceCortada(buena, cancion))
        assertEquals("1", Emparejador.preferirCompleta(buena, listOf(letra("2", "Black Room Boy", "05:40.00")), cancion).id)
        assertEquals("1", Emparejador.preferirCompleta(buena, listOf(letra("3", "Black Room Boy", "09:00.00")), cancion).id)
    }

    @Test
    fun descartaLetrasDeRelleno() {
        // Lo que dio Kugou para "Chasing Summers (R3hab & Quintino Remix)": una sola vocalización.
        assertFalse(LetraEncontrada("Kugou", "533001848", "Chasing Summers", "Tiësto", 282.0, "[02:21.35]ah ah oh").tieneTextoReal)
        assertTrue(letra("1", "Black Room Boy", "02:33.98").copy(lrc = "[00:29.36]It's the same old scene\n[00:30.94]You've been stuck at the screen for a lifetime\n[00:36.36]You keep your mind sharp and clean").tieneTextoReal)
    }

    @Test
    fun detectaTiemposImposibles() {
        // Final de "Empty Streets" (Kaskade) en LRCLIB: líneas enteras separadas por 0,2 s.
        val mala = LetraEncontrada("LRCLIB", "2721766", "Empty Streets", "Kaskade", 247.0, "[02:28.17]If love's not meant to be\n[02:30.35]If the heart's not ready to open\n[02:30.76]If we make it\n[02:30.96]I won't see It's broken\n[02:31.15]If love's not meant to be\n[02:31.66]If the heart's not ready to open\n[02:31.82]If we make it I won't see\n[02:32.19]How it's broken")
        val buena = LetraEncontrada("NetEase", "1", "Empty Streets", "Kaskade", 247.0, "[00:29.36]It's the same old scene\n[00:30.94]You've been stuck at the screen\n[00:36.36]You keep your mind sharp\n[00:38.41]With a fix of caffeine\n[00:43.96]You got a page full of friends\n[00:45.88]In a world without end")
        assertTrue(mala.sincronizacionDudosa)
        assertFalse(buena.sincronizacionDudosa)
        val cancion = CancionDetectada("Empty Streets", "Kaskade", null, 247_000, "p")
        assertEquals("NetEase", Emparejador.preferirCompleta(mala, listOf(mala, buena), cancion).fuente)
    }

    @Test
    fun elAvisoChinoDeInstrumentalEsInstrumental() {
        // Lo que dio Kugou para "Slip" de deadmau5.
        val k = LetraEncontrada("Kugou", "19525574", "Slip", "deadmau5", 406.0, "[00:01.58]纯音乐，请欣赏").normalizada()
        assertTrue(k.instrumental)
        assertFalse(k.sincronizada)
    }
}
