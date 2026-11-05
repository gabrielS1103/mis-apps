package com.gabriel.letrasenvivo.superposicion

import android.app.Notification
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import com.gabriel.letrasenvivo.AplicacionLetras
import com.gabriel.letrasenvivo.R
import com.gabriel.letrasenvivo.ajustes.ActividadPrincipal

/** Notificación fija del servicio en primer plano. Sin botón de detener: se apaga solo desde el panel. */
object NotificacionServicio {
    const val ID = 1

    fun construir(context: Context, texto: String?): Notification {
        val abrir = PendingIntent.getActivity(
            context, 0, Intent(context, ActividadPrincipal::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        return NotificationCompat.Builder(context, AplicacionLetras.CANAL_SERVICIO)
            .setSmallIcon(R.drawable.ic_letras)
            .setContentTitle(context.getString(R.string.notificacion_titulo))
            .setContentText(texto ?: context.getString(R.string.sin_cancion))
            .setContentIntent(abrir)
            .setOngoing(true)
            .setSilent(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()
    }
}
