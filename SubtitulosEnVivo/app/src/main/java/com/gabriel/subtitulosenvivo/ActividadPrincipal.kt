package com.gabriel.subtitulosenvivo

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.media.MediaPlayer
import android.media.projection.MediaProjectionManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.SystemClock
import android.provider.Settings
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.gabriel.subtitulosenvivo.captura.CapturadorAudio
import com.gabriel.subtitulosenvivo.captura.EstadoCaptura
import com.gabriel.subtitulosenvivo.captura.MuestraCircular
import com.gabriel.subtitulosenvivo.captura.NivelAudio
import com.gabriel.subtitulosenvivo.captura.ServicioCaptura
import com.gabriel.subtitulosenvivo.databinding.ActividadPrincipalBinding
import com.gabriel.subtitulosenvivo.traduccion.Traductor
import com.gabriel.subtitulosenvivo.voz.ModeloVoz
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import kotlin.math.roundToInt

/** Pantalla de inicio: arranca/detiene la captura y muestra si de verdad llega audio. */
class ActividadPrincipal : AppCompatActivity() {

    private lateinit var b: ActividadPrincipalBinding
    private val ajustes by lazy { Ajustes(this) }
    private var inicioCaptura = 0L
    private var reproductor: MediaPlayer? = null

    private val pedirPermisos = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { resultado ->
        if (resultado[Manifest.permission.RECORD_AUDIO] == true || tienePermisoAudio()) pedirProyeccion()
        else Toast.makeText(this, R.string.falta_permiso_audio, Toast.LENGTH_LONG).show()
    }

    private val pedirCaptura = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { r ->
        val datos = r.data
        if (r.resultCode == RESULT_OK && datos != null) {
            ServicioCaptura.iniciar(this, r.resultCode, datos, b.interruptorSoloFacebook.isChecked)
        } else {
            Registro.aviso(Registro.CAPTURA, "Permiso de captura rechazado")
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        b = ActividadPrincipalBinding.inflate(layoutInflater)
        setContentView(b.root)

        b.interruptorSoloFacebook.isChecked = ajustes.soloFacebook
        b.interruptorSoloFacebook.setOnCheckedChangeListener { _, activo -> ajustes.soloFacebook = activo }
        b.interruptorMostrarIngles.isChecked = ajustes.mostrarIngles
        b.interruptorMostrarIngles.setOnCheckedChangeListener { _, activo -> ajustes.mostrarIngles = activo }
        b.botonInterruptor.setOnClickListener {
            if (EstadoCaptura.activa.value) startService(ServicioCaptura.intentDetener(this)) else empezar()
        }
        b.botonMuestra.setOnClickListener { guardarYEscucharMuestra() }
        b.botonModelo.setOnClickListener {
            ModeloVoz.descargar(this)
            Traductor.descargar()
        }
        b.botonSuperposicion.setOnClickListener { abrirPermisoSuperposicion() }

        lifecycleScope.launch { combine(ModeloVoz.estado, Traductor.estado) { v, t -> v to t }.collect { (v, t) -> pintarModelos(v, t) } }
        lifecycleScope.launch { EstadoCaptura.frases.collect { pintarFrases(it) } }

        lifecycleScope.launch {
            EstadoCaptura.activa.collect { activa ->
                if (activa) inicioCaptura = SystemClock.elapsedRealtime()
                b.botonInterruptor.setText(if (activa) R.string.detener else R.string.iniciar)
                b.interruptorSoloFacebook.isEnabled = !activa
                b.interruptorMostrarIngles.isEnabled = !activa
            }
        }
        lifecycleScope.launch {
            combine(EstadoCaptura.activa, EstadoCaptura.nivel, EstadoCaptura.origen) { a, n, o -> Triple(a, n, o) }
                .collect { (activa, nivel, origen) -> pintarNivel(activa, nivel, origen) }
        }
    }

    override fun onResume() {
        super.onResume()
        ModeloVoz.refrescar(this)
        Traductor.refrescar()
        val superposicion = Settings.canDrawOverlays(this)
        b.textoPermisoSuperposicion.visibility = if (superposicion) View.GONE else View.VISIBLE
        b.botonSuperposicion.visibility = if (superposicion) View.GONE else View.VISIBLE
        Registro.alCambiar = { runOnUiThread { b.textoRegistro.text = Registro.volcar() } }
        b.textoRegistro.text = Registro.volcar()
    }

    override fun onPause() {
        Registro.alCambiar = null
        super.onPause()
    }

    override fun onDestroy() {
        reproductor?.release()
        super.onDestroy()
    }

    private fun abrirPermisoSuperposicion() {
        startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
    }

    private fun empezar() {
        if (!Settings.canDrawOverlays(this)) {
            // Sin este permiso el subtítulo no puede aparecer encima de Facebook.
            Toast.makeText(this, R.string.falta_superposicion, Toast.LENGTH_LONG).show()
            abrirPermisoSuperposicion()
            return
        }
        val faltan = buildList {
            if (!tienePermisoAudio()) add(Manifest.permission.RECORD_AUDIO)
            if (Build.VERSION.SDK_INT >= 33 &&
                ContextCompat.checkSelfPermission(this@ActividadPrincipal, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
            ) add(Manifest.permission.POST_NOTIFICATIONS)
        }
        if (faltan.isEmpty()) pedirProyeccion() else pedirPermisos.launch(faltan.toTypedArray())
    }

    private fun tienePermisoAudio() =
        ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED

    private fun pedirProyeccion() {
        val gestor = getSystemService(MediaProjectionManager::class.java)
        pedirCaptura.launch(gestor.createScreenCaptureIntent())
    }

    private fun pintarNivel(activa: Boolean, nivel: EstadoCaptura.Nivel, origen: String?) {
        if (!activa) {
            b.textoEstado.setText(R.string.estado_apagada)
            b.medidor.progress = 0
            b.textoNivel.text = ""
            return
        }
        b.medidor.progress = (nivel.actualDb - NivelAudio.SILENCIO_DB).roundToInt()
        b.textoNivel.text = getString(R.string.nivel_detalle, nivel.actualDb, nivel.maximoDb, nivel.segundosConSonido)
        val segundos = (SystemClock.elapsedRealtime() - inicioCaptura) / 1000
        b.textoEstado.text = when {
            nivel.segundosConSonido >= 1f -> getString(R.string.estado_llega_audio, origen)
            segundos >= 10 -> getString(R.string.estado_silencio, origen)
            else -> getString(R.string.estado_esperando, origen)
        }
    }

    /** Un renglón por modelo (voz y traducción) y un solo botón que descarga los que falten. */
    private fun pintarModelos(voz: ModeloVoz.Estado, traduccion: Traductor.Estado) {
        val mb = 1024 * 1024
        val lineaVoz = when (voz) {
            ModeloVoz.Estado.Falta -> getString(R.string.modelo_falta)
            is ModeloVoz.Estado.Descargando -> getString(R.string.modelo_descargando, (voz.bytes / mb).toInt(), (ModeloVoz.TAMANO / mb).toInt())
            is ModeloVoz.Estado.Error -> getString(R.string.modelo_error, voz.mensaje)
            ModeloVoz.Estado.Listo -> getString(R.string.modelo_listo)
        }
        val lineaTraduccion = when (traduccion) {
            Traductor.Estado.DESCONOCIDO, Traductor.Estado.FALTA -> getString(R.string.traductor_falta)
            Traductor.Estado.DESCARGANDO -> getString(R.string.traductor_descargando)
            Traductor.Estado.ERROR -> getString(R.string.traductor_error)
            Traductor.Estado.LISTO -> getString(R.string.traductor_listo)
        }
        b.textoModelo.text = "$lineaVoz\n$lineaTraduccion"
        b.progresoModelo.visibility = if (voz is ModeloVoz.Estado.Descargando) View.VISIBLE else View.GONE
        if (voz is ModeloVoz.Estado.Descargando) b.progresoModelo.progress = (voz.bytes * 100 / ModeloVoz.TAMANO).toInt()
        val faltaVoz = voz is ModeloVoz.Estado.Falta || voz is ModeloVoz.Estado.Error
        val faltaTraduccion = traduccion == Traductor.Estado.FALTA || traduccion == Traductor.Estado.ERROR
        b.botonModelo.visibility = if (faltaVoz || faltaTraduccion) View.VISIBLE else View.GONE
        val hayError = voz is ModeloVoz.Estado.Error || traduccion == Traductor.Estado.ERROR
        b.botonModelo.setText(if (hayError) R.string.reintentar_descarga else R.string.descargar_modelo)
    }

    /** La más reciente arriba, con lo que tardó cada una para medir el desfase real. */
    private fun pintarFrases(frases: List<EstadoCaptura.Frase>) {
        if (frases.isEmpty()) { b.textoFrases.setText(R.string.sin_frases); return }
        b.textoFrases.text = frases.asReversed().joinToString("\n\n") {
            (it.espanol?.let { es -> "$es\n" } ?: "") + "${it.ingles}\n" + getString(R.string.frase_tiempos, it.audioS, it.whisperS)
        }
    }

    /** Exporta los últimos 10 s a un WAV y los reproduce, para oír exactamente lo que se capturó. */
    private fun guardarYEscucharMuestra() {
        val muestras = EstadoCaptura.muestra.contenido()
        if (muestras.isEmpty()) {
            Toast.makeText(this, R.string.muestra_vacia, Toast.LENGTH_SHORT).show()
            return
        }
        lifecycleScope.launch {
            val archivo = File(getExternalFilesDir(null) ?: filesDir, "muestra.wav")
            withContext(Dispatchers.IO) { MuestraCircular.escribirWav(archivo, muestras, CapturadorAudio.FRECUENCIA) }
            Registro.info(Registro.APP, "Muestra guardada en ${archivo.path}")
            reproductor?.release()
            reproductor = MediaPlayer().apply {
                setDataSource(archivo.path)
                prepare()
                start()
            }
        }
    }

}
