package com.gabriel.subtitulosenvivo.captura

import android.app.Activity
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.SystemClock
import android.provider.Settings
import androidx.core.app.ServiceCompat
import androidx.core.content.ContextCompat
import androidx.core.content.IntentCompat
import com.gabriel.subtitulosenvivo.Ajustes
import com.gabriel.subtitulosenvivo.R
import com.gabriel.subtitulosenvivo.Registro
import com.gabriel.subtitulosenvivo.superposicion.BarraSubtitulos
import com.gabriel.subtitulosenvivo.traduccion.Traductor
import com.gabriel.subtitulosenvivo.voz.DetectorVoz
import com.gabriel.subtitulosenvivo.voz.LimpiadorTexto
import com.gabriel.subtitulosenvivo.voz.ModeloVoz
import com.gabriel.subtitulosenvivo.voz.Reconocedor
import com.gabriel.subtitulosenvivo.voz.Segmentador
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.cancelChildren
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.launch
import kotlinx.coroutines.selects.select
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Servicio en primer plano que mantiene la captura del audio interno.
 * Se arranca desde la pantalla principal con el permiso de proyección ya concedido
 * (Android 14+ exige ese permiso antes de pasar a primer plano con tipo mediaProjection).
 */
class ServicioCaptura : Service() {

    private var proyeccion: MediaProjection? = null
    private var capturador: CapturadorAudio? = null
    private val alcance = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    @Volatile private var canalBloques: Channel<Pair<ShortArray, Float>>? = null
    /** Se conserva entre sesiones mientras viva el servicio: cargar el modelo tarda ~1 s. */
    @Volatile private var reconocedor: Reconocedor? = null
    @Volatile private var traductor: Traductor? = null
    /** Solo se toca desde el hilo principal. */
    private var barra: BarraSubtitulos? = null

    private val alDetenerseProyeccion = object : MediaProjection.Callback() {
        override fun onStop() {
            Registro.aviso(Registro.CAPTURA, "El sistema detuvo la proyección")
            detener()
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACCION_INICIAR -> iniciar(intent)
            else -> detener()
        }
        return START_NOT_STICKY
    }

    private fun iniciar(intent: Intent) {
        ponerEnPrimerPlano()
        if (capturador != null) liberar()

        val codigo = intent.getIntExtra(EXTRA_CODIGO, Activity.RESULT_CANCELED)
        val datos = IntentCompat.getParcelableExtra(intent, EXTRA_DATOS, Intent::class.java)
        if (codigo != Activity.RESULT_OK || datos == null) {
            Registro.error(Registro.CAPTURA, "Falta el permiso de proyección")
            detener()
            return
        }
        val gestor = getSystemService(MediaProjectionManager::class.java)
        val p = try {
            gestor.getMediaProjection(codigo, datos)
        } catch (e: Exception) {
            Registro.error(Registro.CAPTURA, "No se pudo obtener la proyección", e)
            null
        }
        if (p == null) { detener(); return }
        p.registerCallback(alDetenerseProyeccion, Handler(Looper.getMainLooper()))
        proyeccion = p

        val soloFacebook = intent.getBooleanExtra(EXTRA_SOLO_FACEBOOK, true)
        val uid = if (soloFacebook) uidDe(PAQUETE_FACEBOOK) else null
        val origen = if (uid != null) getString(R.string.origen_facebook) else getString(R.string.origen_todas)
        if (soloFacebook && uid == null) Registro.aviso(Registro.CAPTURA, "Facebook no está instalado: se captura todo")

        EstadoCaptura._nivel.value = EstadoCaptura.Nivel()
        EstadoCaptura.muestra.vaciar()
        mostrarBarra()
        traductor = Traductor()
        iniciarReconocimiento()
        val c = CapturadorAudio(p, uid, ::alRecibir)
        if (!c.iniciar()) { detener(); return }
        capturador = c
        EstadoCaptura._origen.value = origen
        EstadoCaptura._activa.value = true
        getSystemService(NotificationManager::class.java)
            .notify(NotificacionServicio.ID, NotificacionServicio.construir(this, getString(R.string.notificacion_capturando, origen)))
    }

    /** Sin el permiso de superposición la transcripción sigue funcionando, solo que se ve en la app. */
    private fun mostrarBarra() {
        if (!Settings.canDrawOverlays(this)) {
            Registro.aviso(Registro.OVERLAY, "Sin permiso para mostrar sobre otras apps")
            return
        }
        val ajustes = Ajustes(this)
        barra = BarraSubtitulos(this, ajustes.posicionY) { ajustes.posicionY = it }.apply {
            mostrarIngles = ajustes.mostrarIngles
            mostrar()
        }
    }

    /**
     * Captura → segmentador → Whisper → traducción, en corrutinas. Las colas descartan lo más viejo si Whisper
     * se atrasa: es mejor saltarse una frase que acumular cada vez más desfase.
     * Mientras alguien habla se manda además un avance cada segundo, para no esperar al final de la frase.
     */
    private fun iniciarReconocimiento() {
        if (!ModeloVoz.disponible(this)) {
            Registro.aviso(Registro.VOZ, "Falta el modelo de voz: solo se mide el nivel")
            return
        }
        val bloques = Channel<Pair<ShortArray, Float>>(capacity = 50, onBufferOverflow = BufferOverflow.DROP_OLDEST)
        val segmentos = Channel<Trozo>(capacity = 2, onBufferOverflow = BufferOverflow.DROP_OLDEST) {
            Registro.aviso(Registro.VOZ, "Whisper va atrasado: se descartó una frase")
        }
        // Solo importa el avance más reciente.
        val parciales = Channel<Trozo>(Channel.CONFLATED)
        canalBloques = bloques
        alcance.launch {
            val detector = DetectorVoz.cargar(this@ServicioCaptura)
            if (detector == null) Registro.aviso(Registro.VOZ, "Sin detector de voz: se usa el volumen y la música puede colarse")
            val segmentador = Segmentador(frecuencia = CapturadorAudio.FRECUENCIA)
            suspend fun enviar(audio: FloatArray) {
                Registro.info(Registro.VOZ, "Frase de %.1f s enviada a Whisper".format(audio.size.toFloat() / CapturadorAudio.FRECUENCIA))
                segmentos.send(Trozo(audio, segmentador.cerradas - 1))
            }
            try { while (true) {
                // Si deja de llegar audio (el video terminó o se pausó) no habrá pausa que cierre la frase: se cierra aquí.
                val r = withTimeoutOrNull(SIN_AUDIO_MS) { bloques.receiveCatching() }
                if (r == null) { segmentador.vaciar()?.let { enviar(it) }; continue }
                val (bloque, db) = r.getOrNull() ?: break
                val voz = detector?.probabilidad(bloque, bloque.size) ?: if (db > UMBRAL_VOZ_DB) 1f else 0f
                val frase = segmentador.agregar(bloque, bloque.size, voz)
                if (frase != null) enviar(frase)
                else segmentador.parcial()?.let { parciales.trySend(Trozo(it, segmentador.cerradas)) }
            } } finally {
                detector?.close()
            }
        }
        alcance.launch(Dispatchers.IO) {
            val r = reconocedor ?: Reconocedor.cargar(ModeloVoz.archivo(this@ServicioCaptura))?.also { reconocedor = it } ?: return@launch
            var contexto: String? = null
            var ultimaCerrada = -1
            // Último avance mostrado y de qué frase era.
            var avance: Pair<Int, String>? = null
            while (true) {
                // Las frases terminadas van primero; un avance solo se procesa si no hay ninguna esperando.
                val (trozo, final) = select {
                    segmentos.onReceive { it to true }
                    parciales.onReceive { it to false }
                }
                if (final) {
                    ultimaCerrada = trozo.numero
                    val suAvance = avance?.takeIf { it.first == trozo.numero }?.second
                    contexto = procesarFrase(r, trozo.audio, contexto, suAvance)
                } else if (trozo.numero > ultimaCerrada) {
                    procesarParcial(r, trozo.audio, contexto)?.let { avance = trozo.numero to it }
                }
            }
        }
    }

    /**
     * Devuelve el contexto para la siguiente frase. [avance]: lo último que se mostró de esta misma frase;
     * a veces Whisper, con la frase entera, devuelve solo el final ("I had confidence.") y el avance tenía más.
     */
    private suspend fun procesarFrase(r: Reconocedor, audio: FloatArray, contexto: String?, avance: String?): String? {
        val audioS = audio.size.toFloat() / CapturadorAudio.FRECUENCIA
        val t0 = SystemClock.elapsedRealtime()
        val crudo = r.transcribir(audio, contexto)
        val whisperS = (SystemClock.elapsedRealtime() - t0) / 1000f
        val limpio = LimpiadorTexto.limpiar(crudo, audioS)
        // Igual a la anterior: casi siempre es el contexto arrastrando una alucinación.
        val repetida = limpio != null && limpio == contexto
        val texto = when {
            repetida -> null
            avance != null && palabras(limpio) * 2 < palabras(avance) -> {
                Registro.info(Registro.VOZ, "Whisper devolvió menos que el avance (\"$limpio\"): se usa el avance")
                avance
            }
            else -> limpio
        }
        Registro.info(Registro.VOZ, "%.1f s de audio → Whisper %.1f s: %s".format(
            audioS, whisperS, if (texto != null) "\"$texto\"" else "descartado (\"${crudo?.trim()?.take(80)}\")"))
        if (texto == null) {
            withContext(Dispatchers.Main) { barra?.descartarParcial() }
            return if (repetida) null else contexto
        }
        val t1 = SystemClock.elapsedRealtime()
        val espanol = traductor?.traducir(texto)
        Registro.info(Registro.TRADUCCION, "%.2f s: %s".format((SystemClock.elapsedRealtime() - t1) / 1000f, espanol ?: "(sin traducción)"))
        EstadoCaptura.agregarFrase(EstadoCaptura.Frase(texto, espanol, audioS, whisperS))
        withContext(Dispatchers.Main) { barra?.frase(espanol ?: texto, texto) }
        // La frase anterior como contexto ayuda con nombres y con frases cortadas a la mitad.
        return texto.takeLast(200)
    }

    private fun palabras(texto: String?) = texto?.split(' ')?.count { it.isNotBlank() } ?: 0

    /** Avance de una frase a medias: solo se muestra, no se guarda ni sirve de contexto. Devuelve el texto mostrado. */
    private suspend fun procesarParcial(r: Reconocedor, audio: FloatArray, contexto: String?): String? {
        val audioS = audio.size.toFloat() / CapturadorAudio.FRECUENCIA
        val texto = LimpiadorTexto.limpiar(r.transcribir(audio, contexto), audioS) ?: return null
        val espanol = traductor?.traducir(texto)
        Registro.info(Registro.VOZ, "Avance de %.1f s: \"%s\"".format(audioS, texto))
        withContext(Dispatchers.Main) { barra?.parcial(espanol ?: texto, texto) }
        return texto
    }

    /** Audio de una frase; [numero] dice a qué frase pertenece un avance, para ignorar los de frases ya cerradas. */
    private class Trozo(val audio: FloatArray, val numero: Int)

    /** Se llama desde el hilo de captura cada 100 ms. */
    private fun alRecibir(bloque: ShortArray, cantidad: Int) {
        EstadoCaptura.muestra.agregar(bloque, cantidad)
        val db = NivelAudio.rmsDb(bloque, cantidad)
        // El capturador reutiliza su búfer: se pasa una copia.
        canalBloques?.trySend(bloque.copyOf(cantidad) to db)
        val anterior = EstadoCaptura._nivel.value
        EstadoCaptura._nivel.value = EstadoCaptura.Nivel(
            actualDb = db,
            maximoDb = maxOf(anterior.maximoDb, db),
            segundosConSonido = anterior.segundosConSonido +
                if (db > NivelAudio.UMBRAL_SONIDO_DB) cantidad.toFloat() / CapturadorAudio.FRECUENCIA else 0f,
        )
    }

    private fun uidDe(paquete: String): Int? = try {
        packageManager.getApplicationInfo(paquete, 0).uid
    } catch (_: PackageManager.NameNotFoundException) {
        null
    }

    private fun ponerEnPrimerPlano() {
        val notif = NotificacionServicio.construir(this, null)
        val proyeccion = ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
        val tipos = if (Build.VERSION.SDK_INT >= 30) proyeccion or ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE else proyeccion
        try {
            ServiceCompat.startForeground(this, NotificacionServicio.ID, notif, tipos)
        } catch (e: SecurityException) {
            // Sin el tipo micrófono la captura puede quedar silenciada, pero se intenta igual.
            Registro.aviso(Registro.CAPTURA, "Sin tipo micrófono: ${e.message}")
            ServiceCompat.startForeground(this, NotificacionServicio.ID, notif, proyeccion)
        }
    }

    private fun liberar() {
        capturador?.detener()
        capturador = null
        canalBloques?.close()
        canalBloques = null
        alcance.coroutineContext.cancelChildren()
        barra?.quitar()
        barra = null
        traductor?.close()
        traductor = null
        proyeccion?.let { p ->
            p.unregisterCallback(alDetenerseProyeccion)
            p.stop()
        }
        proyeccion = null
    }

    private fun detener() {
        liberar()
        EstadoCaptura._activa.value = false
        EstadoCaptura._origen.value = null
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    override fun onDestroy() {
        liberar()
        alcance.cancel()
        reconocedor?.close()
        reconocedor = null
        EstadoCaptura._activa.value = false
        super.onDestroy()
    }

    companion object {
        const val PAQUETE_FACEBOOK = "com.facebook.katana"
        /** Sin bloques de audio durante este tiempo, la frase en curso se da por terminada. */
        private const val SIN_AUDIO_MS = 500L
        /** Solo si no carga el detector de voz: cualquier sonido por encima de esto cuenta como voz. */
        private const val UMBRAL_VOZ_DB = -45f
        private const val ACCION_INICIAR = "iniciar"
        private const val ACCION_DETENER = "detener"
        private const val EXTRA_CODIGO = "codigo"
        private const val EXTRA_DATOS = "datos"
        private const val EXTRA_SOLO_FACEBOOK = "solo_facebook"

        fun iniciar(context: Context, codigo: Int, datos: Intent, soloFacebook: Boolean) {
            val i = Intent(context, ServicioCaptura::class.java)
                .setAction(ACCION_INICIAR)
                .putExtra(EXTRA_CODIGO, codigo)
                .putExtra(EXTRA_DATOS, datos)
                .putExtra(EXTRA_SOLO_FACEBOOK, soloFacebook)
            ContextCompat.startForegroundService(context, i)
        }

        fun intentDetener(context: Context): Intent =
            Intent(context, ServicioCaptura::class.java).setAction(ACCION_DETENER)
    }
}
