package com.gabriel.letrasenvivo.superposicion

import android.app.NotificationManager
import android.app.Service
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.service.quicksettings.TileService
import androidx.core.app.ServiceCompat
import com.gabriel.letrasenvivo.AplicacionLetras
import com.gabriel.letrasenvivo.R
import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.ajustes.Permisos
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.deteccion.MonitorDeSesiones
import com.gabriel.letrasenvivo.letras.modelos.EstadoLetra
import com.gabriel.letrasenvivo.panel.BotonPanel
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.launch

/**
 * Servicio en primer plano que mantiene la ventana flotante: vigila la sesión de medios,
 * pide la letra al repositorio y mueve la línea actual al ritmo de la reproducción.
 */
class ServicioSuperposicion : Service() {

    private val ambito = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private lateinit var app: AplicacionLetras
    private lateinit var monitor: MonitorDeSesiones
    private lateinit var vista: VistaSuperposicion
    private lateinit var reproductor: ReproductorDeLineas
    private var tareaLetra: Job? = null
    private var tareaGenero: Job? = null
    private var tareaOcultar: Job? = null
    private var encendido = false

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        app = AplicacionLetras.de(this)
        ponerEnPrimerPlano(null)
        monitor =MonitorDeSesiones(this) { app.ajustes.actual.appsObjetivo }
        vista = VistaSuperposicion(
            this,
            alCambiarDesfase = { cambiarDesfase(it) },
            alMoverse = { x, y -> app.ajustes.guardarPosicion(x, y) },
            alSincronizarLinea = { sincronizarLinea(it) },
        )
        reproductor = ReproductorDeLineas(ambito, EstadoSuperposicion.letra, monitor.reproduccion) {
            app.ajustes.actual.desfaseGlobalMs + EstadoSuperposicion.desfaseCancionMs.value
        }
    }

    /**
     * Pasa (o mantiene) el servicio en primer plano. Se llama en cada arranque porque, si alguien usó
     * startForegroundService(), Android exige una llamada a startForeground() aunque solo vengamos a apagar.
     */
    private fun ponerEnPrimerPlano(cancion: CancionDetectada?) {
        ServiceCompat.startForeground(
            this, NotificacionServicio.ID, NotificacionServicio.construir(this, cancion?.let { "${it.titulo} – ${it.artista}" }),
            if (Build.VERSION.SDK_INT >= 34) ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE else 0,
        )
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        ponerEnPrimerPlano(EstadoSuperposicion.cancion.value)
        when (intent?.action) {
            ACCION_APAGAR -> { apagar(); return START_NOT_STICKY }
            ACCION_ALTERNAR -> if (encendido) { apagar(); return START_NOT_STICKY } else encender()
            ACCION_REINTENTAR -> { if (!encendido) encender(); reintentarSinCache() }
            else -> if (!encendido) encender()
        }
        return START_STICKY
    }

    private fun encender() {
        if (encendido) return
        if (!Permisos.imprescindibles(this)) {
            Registro.aviso(Registro.OVERLAY, "Faltan permisos; no se enciende")
            stopSelf()
            return
        }
        encendido = true
        app.ajustes.guardarActiva(true)
        EstadoSuperposicion.activa.value = true
        EstadoSuperposicion.mensaje.value = getString(R.string.sin_musica)
        vista.aplicarConfiguracion(app.ajustes.actual)
        vista.mostrarLetra(null, EstadoSuperposicion.mensaje.value)
        vista.mostrar()
        monitor.iniciar()
        reproductor.iniciar()
        observar()
        actualizarPanel()
        Registro.info(Registro.OVERLAY, "Ventana encendida")
    }

    private fun apagar() {
        if (encendido) {
            encendido = false
            Registro.info(Registro.OVERLAY, "Ventana apagada")
        }
        app.ajustes.guardarActiva(false)
        EstadoSuperposicion.activa.value = false
        EstadoSuperposicion.letra.value = null
        EstadoSuperposicion.cancion.value = null
        EstadoSuperposicion.mensaje.value = null
        actualizarPanel()
        stopSelf()
    }

    private fun observar() {
        // Canción en curso → buscar letra.
        ambito.launch {
            monitor.cancion.collectLatest { c ->
                EstadoSuperposicion.cancion.value = c
                EstadoSuperposicion.metadatosCrudos = monitor.metadatosCrudos
                vista.mostrarCancion(c)
                notificar(c)
                actualizarPanel()
                cargarLetra(c, ignorarCache = false)
                cargarGenero(c)
            }
        }
        // Ajustes en vivo.
        ambito.launch { app.ajustes.flujo.collect { vista.aplicarConfiguracion(it); vista.mostrarDesfase(it.desfaseGlobalMs + EstadoSuperposicion.desfaseCancionMs.value); monitor.refrescar() } }
        // Línea actual.
        ambito.launch { reproductor.indice.collect { EstadoSuperposicion.indiceLinea.value = it; vista.marcarLinea(it) } }
        ambito.launch { reproductor.pausa.collect { vista.marcarPausa(it) } }
        // Permiso revocado.
        ambito.launch { monitor.sinPermiso.collect { if (it) mostrarMensaje(getString(R.string.falta_acceso_notificaciones)) } }
        // Sin música: esconder la ventana pasado un rato.
        ambito.launch {
            monitor.haySesion.collect { hay ->
                tareaOcultar?.cancel()
                if (hay) { vista.mostrar(); return@collect }
                if (!app.ajustes.actual.ocultarSinMusica) return@collect
                tareaOcultar = launch { delay(30_000); vista.ocultar() }
            }
        }
    }

    private fun cargarLetra(c: CancionDetectada?, ignorarCache: Boolean, intento: Int = 0) {
        tareaLetra?.cancel()
        if (c == null) {
            EstadoSuperposicion.letra.value = null
            mostrarMensaje(getString(R.string.sin_musica))
            return
        }
        tareaLetra = ambito.launch {
            val manual = app.cache.obtener(c.clave)?.desfaseMs
            desfaseManual = manual != null
            fijarDesfase(manual ?: 0L)
            // La red, el análisis HTML y la alineación corren fuera del hilo principal; la vista se pinta en él.
            app.repositorio.obtener(c, ignorarCache).flowOn(Dispatchers.Default).collect { estado ->
                when (estado) {
                    EstadoLetra.Cargando -> { EstadoSuperposicion.letra.value = null; mostrarMensaje(getString(R.string.buscando_letra)) }
                    is EstadoLetra.Parcial -> mostrarLetra(estado.letra)
                    is EstadoLetra.Lista -> mostrarLetra(estado.letra)
                    is EstadoLetra.SinLetra -> { EstadoSuperposicion.letra.value = null; mostrarMensaje(getString(R.string.sin_letra)) }
                    is EstadoLetra.Error -> {
                        EstadoSuperposicion.letra.value = null
                        // Sin red o con una fuente caída: se reintenta solo mientras siga sonando la misma canción.
                        // Tras el último intento se deja de prometer un reintento.
                        mostrarMensaje(when {
                            intento >= 3 -> getString(R.string.sin_letra)
                            estado.mensaje == "Sin conexión" -> getString(R.string.sin_conexion)
                            else -> getString(R.string.sin_letra_por_ahora, estado.mensaje)
                        })
                        if (intento < 3) { delay(30_000L * (intento + 1)); cargarLetra(c, ignorarCache = true, intento = intento + 1) }
                    }
                }
            }
        }
    }

    private fun cargarGenero(c: CancionDetectada?) {
        tareaGenero?.cancel()
        vista.mostrarGenero(null)
        if (c == null) return
        tareaGenero = ambito.launch { vista.mostrarGenero(app.generos.buscar(c)) }
    }

    private fun reintentarSinCache() {
        val c = monitor.cancion.value ?: return
        ambito.launch { app.cache.borrar(c.clave); cargarLetra(c, ignorarCache = true) }
    }

    /** true si el usuario ya ajustó esta canción (entonces no se pisa con el desfase sugerido). */
    private var desfaseManual = false

    private fun mostrarLetra(l: com.gabriel.letrasenvivo.letras.modelos.Letra) {
        if (!desfaseManual) fijarDesfase(l.desfaseSugeridoMs)
        EstadoSuperposicion.letra.value = l
        EstadoSuperposicion.mensaje.value = null
        vista.mostrarLetra(l, null)
    }

    private fun fijarDesfase(ms: Long) {
        EstadoSuperposicion.desfaseCancionMs.value = ms
        vista.mostrarDesfase(app.ajustes.actual.desfaseGlobalMs + ms)
        reproductor.recalcular()
    }

    private fun guardarDesfaseManual(ms: Long) {
        val c = monitor.cancion.value ?: return
        desfaseManual = true
        fijarDesfase(ms)
        ambito.launch { app.cache.guardarDesfase(c.clave, ms) }
    }

    private fun cambiarDesfase(deltaMs: Long) = guardarDesfaseManual(EstadoSuperposicion.desfaseCancionMs.value + deltaMs)

    /** El usuario tocó la línea que está sonando: el desfase pasa a ser exactamente el que la hace coincidir. */
    private fun sincronizarLinea(indice: Int) {
        val l = EstadoSuperposicion.letra.value ?: return
        val t = l.lineas.getOrNull(indice)?.tiempoMs ?: return
        val posicion = monitor.reproduccion.value.posicionAhora()
        val nuevo = t - posicion - app.ajustes.actual.desfaseGlobalMs + 150 // margen para el tiempo de reacción
        Registro.info(Registro.OVERLAY, "Sincronizado con la línea $indice: desfase $nuevo ms")
        guardarDesfaseManual(nuevo)
    }

    private fun mostrarMensaje(texto: String) {
        EstadoSuperposicion.mensaje.value = texto
        vista.mostrarLetra(null, texto)
    }

    private fun notificar(c: CancionDetectada?) {
        getSystemService(NotificationManager::class.java).notify(NotificacionServicio.ID, NotificacionServicio.construir(this, c?.let { "${it.titulo} – ${it.artista}" }))
    }

    private fun actualizarPanel() {
        try { TileService.requestListeningState(this, ComponentName(this, BotonPanel::class.java)) } catch (_: Exception) {}
    }

    override fun onDestroy() {
        tareaLetra?.cancel()
        tareaGenero?.cancel()
        monitor.detener()
        vista.ocultar()
        ambito.cancel()
        EstadoSuperposicion.activa.value = false
        actualizarPanel()
        super.onDestroy()
    }

    companion object {
        const val ACCION_ALTERNAR = "com.gabriel.letrasenvivo.ALTERNAR"
        const val ACCION_ENCENDER = "com.gabriel.letrasenvivo.ENCENDER"
        const val ACCION_APAGAR = "com.gabriel.letrasenvivo.APAGAR"
        const val ACCION_REINTENTAR = "com.gabriel.letrasenvivo.REINTENTAR"

        fun intent(context: Context, accion: String): Intent =
            Intent(context, ServicioSuperposicion::class.java).setAction(accion)
    }
}
