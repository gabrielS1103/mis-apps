package com.gabriel.subtitulosenvivo.captura

import android.app.Notification
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import com.gabriel.subtitulosenvivo.ActividadPrincipal
import com.gabriel.subtitulosenvivo.AplicacionSubtitulos
import com.gabriel.subtitulosenvivo.R

/** Notificación fija del servicio en primer plano. */
object NotificacionServicio {
    const val ID = 1

    fun construir(context: Context, texto: String?): Notification {
        val abrir = PendingIntent.getActivity(
            context, 0, Intent(context, ActividadPrincipal::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val detener = PendingIntent.getService(
            context, 1, ServicioCaptura.intentDetener(context),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        return NotificationCompat.Builder(context, AplicacionSubtitulos.CANAL_SERVICIO)
            .setSmallIcon(R.drawable.ic_subtitulos)
            .setContentTitle(context.getString(R.string.notificacion_titulo))
            .setContentText(texto ?: context.getString(R.string.notificacion_preparando))
            .setContentIntent(abrir)
            .addAction(0, context.getString(R.string.notificacion_detener), detener)
            .setOngoing(true)
            .setSilent(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()
    }
}
