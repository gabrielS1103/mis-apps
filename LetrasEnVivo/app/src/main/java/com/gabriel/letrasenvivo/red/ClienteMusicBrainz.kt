package com.gabriel.letrasenvivo.red

import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.IOException

/**
 * Acceso compartido a la API de MusicBrainz. Admite una petición por segundo y responde 503 si se le pasa,
 * así que el género y la búsqueda de la canción original hacen cola aquí.
 */
class ClienteMusicBrainz(private val http: ClienteHttp) {

    private val turno = Mutex()
    private var ultimaPeticion = 0L

    /** Cuerpo de la respuesta, null si no existe (404). Lanza IOException ante errores pasajeros, para no recordarlos. */
    suspend fun obtener(ruta: String): String? = turno.withLock {
        val espera = ultimaPeticion + 1100 - System.currentTimeMillis()
        if (espera > 0) delay(espera)
        try {
            val r = http.obtener("https://musicbrainz.org/ws/2/$ruta")
            when {
                r.correcta -> r.cuerpo
                r.codigo == 404 -> null
                else -> throw IOException("MusicBrainz -> ${r.codigo}")
            }
        } finally {
            ultimaPeticion = System.currentTimeMillis()
        }
    }
}
