package com.gabriel.letrasenvivo.ajustes

import android.content.Context
import android.content.SharedPreferences
import androidx.preference.PreferenceManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

enum class ModoVisualizacion { AMBOS, SOLO_ESPANOL, SOLO_ORIGINAL }
enum class AppsObjetivo { YTMUSIC, YOUTUBE, CUALQUIERA }

data class Configuracion(
    val modo: ModoVisualizacion = ModoVisualizacion.AMBOS,
    val tamanoFuenteSp: Int = 18,
    /** 20..100 */
    val opacidad: Int = 85,
    val mostrarSiguiente: Boolean = true,
    /** Mostrar "traducción automática" cuando la traducción es de ML Kit. */
    val avisoTraduccionAutomatica: Boolean = false,
    /** Quitar lo que va entre paréntesis del título de la cabecera ("(con Mindy Gledhill)"). */
    val acortarTitulo: Boolean = true,
    val desfaseGlobalMs: Long = 0,
    val usarLrclib: Boolean = true,
    val usarLyricsTranslate: Boolean = true,
    val usarMlKit: Boolean = true,
    val appsObjetivo: AppsObjetivo = AppsObjetivo.YTMUSIC,
    val ocultarSinMusica: Boolean = true,
    val repartirSinTiempos: Boolean = false,
    val posicionX: Int = 0,
    val posicionY: Int = 160,
    val superposicionActiva: Boolean = false,
)

/** Envoltorio de SharedPreferences con un StateFlow para reaccionar a los cambios en vivo. */
class Ajustes(context: Context) {

    private val prefs: SharedPreferences = PreferenceManager.getDefaultSharedPreferences(context)
    private val _flujo = MutableStateFlow(leer())
    val flujo: StateFlow<Configuracion> = _flujo
    val actual: Configuracion get() = _flujo.value

    private val oyente = SharedPreferences.OnSharedPreferenceChangeListener { _, _ -> _flujo.value = leer() }

    init { prefs.registerOnSharedPreferenceChangeListener(oyente) }

    private fun leer(): Configuracion = Configuracion(
        modo = enumOrDefault(prefs.getString(CLAVE_MODO, null), ModoVisualizacion.AMBOS),
        tamanoFuenteSp = prefs.getInt(CLAVE_TAMANO, 18),
        opacidad = prefs.getInt(CLAVE_OPACIDAD, 85),
        mostrarSiguiente = prefs.getBoolean(CLAVE_SIGUIENTE, true),
        avisoTraduccionAutomatica = prefs.getBoolean(CLAVE_AVISO_AUTOMATICA, false),
        acortarTitulo = prefs.getBoolean(CLAVE_ACORTAR_TITULO, true),
        desfaseGlobalMs = (prefs.getInt(CLAVE_DESFASE, 20) - 20) * 250L,
        usarLrclib = prefs.getBoolean(CLAVE_LRCLIB, true),
        usarLyricsTranslate = prefs.getBoolean(CLAVE_LT, true),
        usarMlKit = prefs.getBoolean(CLAVE_MLKIT, true),
        appsObjetivo = enumOrDefault(prefs.getString(CLAVE_APPS, null), AppsObjetivo.YTMUSIC),
        ocultarSinMusica = prefs.getBoolean(CLAVE_OCULTAR, true),
        repartirSinTiempos = prefs.getBoolean(CLAVE_REPARTIR, false),
        posicionX = prefs.getInt(CLAVE_POS_X, 0),
        posicionY = prefs.getInt(CLAVE_POS_Y, 160),
        superposicionActiva = prefs.getBoolean(CLAVE_ACTIVA, false),
    )

    fun guardarPosicion(x: Int, y: Int) = prefs.edit().putInt(CLAVE_POS_X, x).putInt(CLAVE_POS_Y, y).apply()
    fun restablecerPosicion() = prefs.edit().remove(CLAVE_POS_X).remove(CLAVE_POS_Y).apply()
    fun guardarActiva(activa: Boolean) = prefs.edit().putBoolean(CLAVE_ACTIVA, activa).commit()

    /** Enfriamiento de lyricstranslate tras un bloqueo (epoch ms hasta el que no se vuelve a intentar). */
    var ltBloqueadoHasta: Long
        get() = prefs.getLong(CLAVE_LT_BLOQUEO, 0L)
        set(v) { prefs.edit().putLong(CLAVE_LT_BLOQUEO, v).apply() }
    var ltBloqueosSeguidos: Int
        get() = prefs.getInt(CLAVE_LT_BLOQUEOS, 0)
        set(v) { prefs.edit().putInt(CLAVE_LT_BLOQUEOS, v).apply() }

    private inline fun <reified T : Enum<T>> enumOrDefault(valor: String?, porDefecto: T): T =
        valor?.let { v -> enumValues<T>().firstOrNull { it.name == v } } ?: porDefecto

    companion object {
        const val CLAVE_MODO = "modo_visualizacion"
        const val CLAVE_TAMANO = "tamano_fuente"
        const val CLAVE_OPACIDAD = "opacidad"
        const val CLAVE_SIGUIENTE = "mostrar_siguiente"
        const val CLAVE_AVISO_AUTOMATICA = "aviso_traduccion_automatica"
        const val CLAVE_ACORTAR_TITULO = "acortar_titulo"
        const val CLAVE_DESFASE = "desfase_global"
        const val CLAVE_LRCLIB = "usar_lrclib"
        const val CLAVE_LT = "usar_lyricstranslate"
        const val CLAVE_MLKIT = "usar_mlkit"
        const val CLAVE_APPS = "apps_objetivo"
        const val CLAVE_OCULTAR = "ocultar_sin_musica"
        const val CLAVE_REPARTIR = "repartir_sin_tiempos"
        const val CLAVE_POS_X = "posicion_x"
        const val CLAVE_POS_Y = "posicion_y"
        const val CLAVE_ACTIVA = "superposicion_activa"
        const val CLAVE_LT_BLOQUEO = "lt_bloqueado_hasta"
        const val CLAVE_LT_BLOQUEOS = "lt_bloqueos_seguidos"
        const val CLAVE_RESTABLECER = "restablecer_posicion"
        const val CLAVE_BORRAR_CACHE = "borrar_cache"
    }
}
