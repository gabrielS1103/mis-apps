package com.gabriel.letrasenvivo.genero

import org.junit.Assert.assertEquals
import org.junit.Test

class FuenteBeatportTest {

    private val html = """
        <html><body><script id="__NEXT_DATA__" type="application/json">
        {"props":{"pageProps":{"dehydratedState":{"queries":[{"state":{"data":{"data":[
          {"track_name":"Say It's Over (feat. Mindy Gledhill)","mix_name":"Original Mix","artists":[{"artist_name":"Mindy Gledhill"},{"artist_name":"Kaskade"}],
           "genre":[{"genre_id":39,"genre_name":"Dance / Pop"}],"sub_genre":null,"release_date":"2016-01-01T00:00:00"},
          {"track_name":"Say It's Over feat. Mindy Gledhill","mix_name":"Original Mix","artists":[{"artist_name":"Kaskade"}],
           "genre":[{"genre_id":15,"genre_name":"Progressive House"}],"sub_genre":null,"release_date":"2010-05-11T00:00:00"},
          {"track_name":"Liquid Dreams","mix_name":"Original Mix","artists":[{"artist_name":"Someone"}],
           "genre":{"name":"Drum & Bass"},"sub_genre":{"name":"Liquid"}}
        ]}}}]}}}}
        </script></body></html>
    """.trimIndent()

    @Test
    fun leePistasConGeneroSubgeneroYFecha() {
        val pistas = FuenteBeatport.analizar(html)
        assertEquals(3, pistas.size)
        assertEquals("Progressive House", pistas[1].genero)
        assertEquals("2010-05-11T00:00:00", pistas[1].fecha)
        assertEquals("Drum & Bass", pistas[2].genero)
        assertEquals("Liquid", pistas[2].subgenero)
    }

    @Test
    fun quitaLosColaboradoresDelTitulo() {
        assertEquals("Say It's Over", FuenteBeatport.sinFeat("Say It's Over feat. Mindy Gledhill"))
        assertEquals("Say It's Over", FuenteBeatport.sinFeat("Say It's Over (feat. Mindy Gledhill)"))
        assertEquals("Stay With Me", FuenteBeatport.sinFeat("Stay With Me"))
    }

    @Test
    fun reconoceLasCategoriasGenericas() {
        assertEquals(true, FuenteBeatport.esGenerico(Genero("Pop", "Dance / Pop", "Beatport")))
        assertEquals(false, FuenteBeatport.esGenerico(Genero("Electrónica", "Progressive House", "Beatport")))
        assertEquals(false, FuenteBeatport.esGenerico(Genero("Pop", "Dance / Pop", "MusicBrainz")))
    }

    @Test
    fun laFamiliaSaleDelEstiloDeBeatport() {
        assertEquals("Electrónica", ClasificadorDeGeneros.familiaParaMostrar("Progressive House"))
        assertEquals("Electrónica", ClasificadorDeGeneros.familiaParaMostrar("Drum & Bass"))
        assertEquals("Electrónica", ClasificadorDeGeneros.familiaParaMostrar("Trance (Main Floor)"))
    }
}
