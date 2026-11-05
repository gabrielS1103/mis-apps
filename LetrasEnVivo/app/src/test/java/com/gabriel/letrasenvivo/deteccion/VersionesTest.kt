package com.gabriel.letrasenvivo.deteccion

import com.gabriel.letrasenvivo.letras.Emparejador
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class VersionesTest {

    @Test
    fun reconoceRemixesYDirectos() {
        assertEquals("alesso remix", Versiones.clave("Pressure (Alesso Remix)"))
        assertEquals("alesso remix", Versiones.clave("Pressure (feat. Nadia Ali) (Alesso Remix [Radio Edit])"))
        assertEquals("adam k soha", Versiones.clave("4 AM (Adam K & Soha Mix)"))
        assertEquals("above beyond", Versiones.clave("Sirens Of The Sea (ABGT655A) (Above & Beyond Club Mix (Mixed))"))
        assertEquals("live", Versiones.clave("Hurt (Live)"))
        assertEquals("dance love", Versiones.clave("Raining (feat. SunSun) (dance.love Edit)"))
    }

    @Test
    fun lasMezclasDeLaMismaProduccionCuentanComoOriginal() {
        assertNull(Versiones.clave("Raining (Extended Mix) (con SunSun)"))
        assertNull(Versiones.clave("Black Room Boy (Original Mix)"))
        assertNull(Versiones.clave("Yellow (Remastered 2009)"))
        assertNull(Versiones.clave("Mix Tape"))
        assertNull(Versiones.clave("Waiting - Radio Edit"))
    }

    @Test
    fun noConfundeElOriginalConUnRemixNiUnRemixConOtro() {
        val remix = CancionDetectada("Pressure (Alesso Remix)", "Nadia Ali, Starkillers y Alex Kenji", null, 368_000, "p")
        assertNotNull(Emparejador.puntuar("Pressure (Alesso Remix)", "Nadia Ali", null, remix))
        assertNull(Emparejador.puntuar("Pressure", "Nadia Ali", null, remix))
        assertNull(Emparejador.puntuar("Pressure (Hardwell Remix)", "Nadia Ali", null, remix))
        val original = CancionDetectada("Raining", "Kaskade", null, 236_000, "p")
        assertNull(Emparejador.puntuar("Raining (Halogen Remix)", "Kaskade", 236.0, original))
        assertTrue(Versiones.compatibles(null, null))
        assertFalse(Versiones.compatibles("live", null))
    }
}
