package com.gabriel.letrasenvivo

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager
import android.content.Context
import com.gabriel.letrasenvivo.ajustes.Ajustes
import com.gabriel.letrasenvivo.genero.ProveedorDeGeneros
import com.gabriel.letrasenvivo.letras.CacheDeLetras
import com.gabriel.letrasenvivo.letras.ProveedorKugou
import com.gabriel.letrasenvivo.letras.ProveedorLrclib
import com.gabriel.letrasenvivo.letras.ProveedorNetEase
import com.gabriel.letrasenvivo.letras.ProveedorLyricsTranslate
import com.gabriel.letrasenvivo.letras.RepositorioDeLetras
import com.gabriel.letrasenvivo.letras.TraductorMlKit
import com.gabriel.letrasenvivo.red.ClienteHttp
import com.gabriel.letrasenvivo.red.ClienteMusicBrainz
import com.gabriel.letrasenvivo.letras.BuscadorDeOriginal
import com.gabriel.letrasenvivo.red.NavegadorOculto

/** Punto único donde se crean los objetos compartidos (sin inyección de dependencias: la app es pequeña). */
class AplicacionLetras : Application() {

    lateinit var ajustes: Ajustes; private set
    lateinit var http: ClienteHttp; private set
    lateinit var cache: CacheDeLetras; private set
    lateinit var repositorio: RepositorioDeLetras; private set
    lateinit var generos: ProveedorDeGeneros; private set

    override fun onCreate() {
        super.onCreate()
        ajustes = Ajustes(this)
        http = ClienteHttp(this)
        cache = CacheDeLetras(this)
        val navegador = NavegadorOculto(this)
        val musicBrainz = ClienteMusicBrainz(http)
        repositorio = RepositorioDeLetras(
            cache = cache,
            sincronizadas = listOf(ProveedorLrclib(http), ProveedorNetEase(http), ProveedorKugou(http)),
            lyricsTranslate = ProveedorLyricsTranslate(http, navegador, ajustes),
            mlKit = TraductorMlKit(),
            ajustes = ajustes,
            buscadorOriginal = BuscadorDeOriginal(musicBrainz),
        )
        generos = ProveedorDeGeneros(http, musicBrainz, navegador, java.io.File(cacheDir, "beatport_ultima.html"))
        crearCanal()
        Registro.info(Registro.APP, "Aplicación iniciada")
    }

    private fun crearCanal() {
        val gestor = getSystemService(NotificationManager::class.java)
        val canal = NotificationChannel(CANAL_SERVICIO, getString(R.string.canal_servicio), NotificationManager.IMPORTANCE_LOW).apply {
            description = getString(R.string.notificacion_titulo)
            setShowBadge(false)
        }
        gestor.createNotificationChannel(canal)
    }

    companion object {
        const val CANAL_SERVICIO = "servicio_superposicion"
        fun de(context: Context): AplicacionLetras = context.applicationContext as AplicacionLetras
    }
}
