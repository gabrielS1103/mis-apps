package com.gabriel.subtitulosenvivo

import android.content.Context
import android.content.res.Resources
import androidx.core.content.edit

/** Preferencias compartidas entre la pantalla principal y el servicio. */
class Ajustes(context: Context) {
    private val p = context.applicationContext.getSharedPreferences("ajustes", Context.MODE_PRIVATE)

    var soloFacebook: Boolean
        get() = p.getBoolean("solo_facebook", true)
        set(v) = p.edit { putBoolean("solo_facebook", v) }

    var mostrarIngles: Boolean
        get() = p.getBoolean("mostrar_ingles", false)
        set(v) = p.edit { putBoolean("mostrar_ingles", v) }

    /** Distancia del subtítulo al borde inferior, en píxeles. Por defecto deja libres los botones de Facebook. */
    var posicionY: Int
        get() = p.getInt("posicion_y", (POSICION_INICIAL_DP * Resources.getSystem().displayMetrics.density).toInt())
        set(v) = p.edit { putInt("posicion_y", v) }

    companion object {
        private const val POSICION_INICIAL_DP = 180
    }
}
