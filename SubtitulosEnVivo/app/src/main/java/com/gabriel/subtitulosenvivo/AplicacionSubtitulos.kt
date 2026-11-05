package com.gabriel.subtitulosenvivo

import android.app.Application
import android.app.NotificationChannel
import android.app.NotificationManager

class AplicacionSubtitulos : Application() {

    override fun onCreate() {
        super.onCreate()
        Registro.usarArchivo(filesDir)
        crearCanal()
        Registro.info(Registro.APP, "Aplicación iniciada")
    }

    private fun crearCanal() {
        val gestor = getSystemService(NotificationManager::class.java)
        val canal = NotificationChannel(CANAL_SERVICIO, getString(R.string.canal_servicio), NotificationManager.IMPORTANCE_LOW).apply {
            setShowBadge(false)
        }
        gestor.createNotificationChannel(canal)
    }

    companion object {
        const val CANAL_SERVICIO = "servicio_captura"
    }
}
