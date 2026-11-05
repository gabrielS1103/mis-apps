package com.gabriel.letrasenvivo.superposicion

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.PixelFormat
import android.graphics.drawable.GradientDrawable
import android.view.Gravity
import android.view.LayoutInflater
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import android.view.ContextThemeWrapper
import android.view.WindowManager
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.LinearLayoutManager
import com.gabriel.letrasenvivo.R
import com.gabriel.letrasenvivo.Registro
import com.gabriel.letrasenvivo.ajustes.Configuracion
import com.gabriel.letrasenvivo.ajustes.ModoVisualizacion
import com.gabriel.letrasenvivo.databinding.SuperposicionBinding
import com.gabriel.letrasenvivo.deteccion.CancionDetectada
import com.gabriel.letrasenvivo.genero.Genero
import com.gabriel.letrasenvivo.letras.modelos.Letra
import kotlin.math.abs
import kotlin.math.roundToInt

/** La ventana flotante: se añade con WindowManager encima de cualquier app. */
class VistaSuperposicion(
    private val context: Context,
    private val alCambiarDesfase: (Long) -> Unit,
    private val alMoverse: (Int, Int) -> Unit,
    private val alSincronizarLinea: (Int) -> Unit,
) {
    private val gestor = context.getSystemService(Context.WINDOW_SERVICE) as WindowManager
    // El servicio no tiene tema de Material: se envuelve el contexto para que los estilos resuelvan.
    private val b = SuperposicionBinding.inflate(LayoutInflater.from(ContextThemeWrapper(context, R.style.Theme_LetrasEnVivo)))
    private val adaptador = AdaptadorLineas { alSincronizarLinea(it) }
    private val parametros = WindowManager.LayoutParams(
        WindowManager.LayoutParams.WRAP_CONTENT,
        WindowManager.LayoutParams.WRAP_CONTENT,
        WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
        WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
            WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
        PixelFormat.TRANSLUCENT,
    ).apply { gravity = Gravity.TOP or Gravity.START }

    private var visible = false
    private var expandida = false
    private var letra: Letra? = null
    private var indice = -1
    private var pausa = false
    private var modo = ModoVisualizacion.AMBOS
    private var mostrarSiguiente = true
    private var tamanoSp = 18f
    private var avisoAutomatica = false
    private var acortarTitulo = true
    private var cancion: CancionDetectada? = null

    init {
        b.listaLineas.layoutManager = LinearLayoutManager(context)
        b.listaLineas.adapter = adaptador
        b.botonMenos.setOnClickListener { alCambiarDesfase(-500) }
        b.botonMas.setOnClickListener { alCambiarDesfase(500) }
        instalarGestos()
    }

    fun mostrar() {
        if (visible) return
        try {
            gestor.addView(b.root, parametros)
            visible = true
        } catch (e: Exception) {
            Registro.error(Registro.OVERLAY, "No se pudo mostrar la ventana flotante", e)
        }
    }

    fun ocultar() {
        if (!visible) return
        try { gestor.removeView(b.root) } catch (_: Exception) {}
        visible = false
    }

    fun aplicarConfiguracion(c: Configuracion) {
        modo = c.modo
        mostrarSiguiente = c.mostrarSiguiente
        tamanoSp = c.tamanoFuenteSp.toFloat()
        avisoAutomatica = c.avisoTraduccionAutomatica
        acortarTitulo = c.acortarTitulo
        mostrarCancion(cancion)
        pintarAvisos()
        adaptador.modo = c.modo
        adaptador.tamanoSp = c.tamanoFuenteSp - 3f
        (b.root.background.mutate() as? GradientDrawable)?.alpha = (255 * c.opacidad / 100f).roundToInt()
        if (parametros.x != c.posicionX || parametros.y != c.posicionY) {
            parametros.x = c.posicionX
            parametros.y = c.posicionY
            actualizarVentana()
        }
        pintar()
    }

    fun mostrarCancion(c: CancionDetectada?) {
        cancion = c
        b.textoCabecera.text = if (c == null) context.getString(R.string.nombre_app) else {
            val titulo = if (acortarTitulo) sinParentesis(c.titulo) else c.titulo
            "$titulo – ${c.artista}"
        }
    }

    /** null = género desconocido (o aún buscándolo): se oculta el renglón. */
    fun mostrarGenero(g: Genero?) {
        b.textoGenero.text = when {
            g == null -> null
            g.subgenero == null -> context.getString(R.string.genero, g.genero)
            else -> context.getString(R.string.genero_y_subgenero, g.genero, g.subgenero)
        }?.let { if (g?.deArtista == true) it + context.getString(R.string.genero_del_artista) else it }
        b.textoGenero.visibility = if (g == null) View.GONE else View.VISIBLE
    }

    fun mostrarLetra(l: Letra?, mensaje: String?) {
        letra = l
        adaptador.lineas = l?.lineas.orEmpty()
        b.textoAviso.visibility = View.GONE
        if (l == null) {
            mostrarSoloMensaje(mensaje ?: "", tamanoSp - 2)
            return
        }
        if (l.instrumental) {
            mostrarSoloMensaje(context.getString(R.string.instrumental), tamanoSp)
            return
        }
        pintarAvisos()
        pintar()
    }

    private fun pintarAvisos() {
        val l = letra?.takeIf { !it.instrumental }
        val avisos = ArrayList<String>()
        if (l != null) {
            if (l.aproximada) avisos += context.getString(R.string.aproximada)
            if (l.versionDistinta) avisos += context.getString(R.string.version_distinta)
            if (l.fuentes.mlKit && avisoAutomatica) avisos += context.getString(R.string.traduccion_automatica)
        }
        b.textoAviso.text = avisos.joinToString(" · ")
        b.textoAviso.visibility = if (avisos.isEmpty()) View.GONE else View.VISIBLE
    }

    fun mostrarDesfase(totalMs: Long) {
        b.textoDesfase.text = "desfase %+.1f s".format(totalMs / 1000.0)
    }

    fun marcarLinea(i: Int) {
        indice = i
        adaptador.indiceActual = i
        if (expandida && i >= 0) b.listaLineas.smoothScrollToPosition(i)
        pintar()
    }

    /** Un aviso en el renglón de arriba (blanco), sin original ni siguiente. */
    private fun mostrarSoloMensaje(texto: String, tamano: Float) {
        b.textoPrincipal.text = texto
        b.textoPrincipal.textSize = tamano
        b.textoPrincipal.visibility = View.VISIBLE
        b.textoSecundario.visibility = View.GONE
        b.textoSiguiente.visibility = View.GONE
    }

    fun marcarPausa(p: Boolean) {
        if (pausa == p) return
        pausa = p
        pintar()
    }

    private fun pintar() {
        val l = letra ?: return
        if (l.instrumental) return
        val lineas = l.lineas
        val actual = lineas.getOrNull(indice)
        b.textoPrincipal.textSize = tamanoSp
        b.textoSecundario.textSize = tamanoSp - 1
        b.textoSiguiente.textSize = (tamanoSp - 5).coerceAtLeast(10f)
        if (actual == null) {
            if (!l.tieneTiempos) {
                val aviso = l.letraDe?.let { context.getString(R.string.letra_de_la_original, it) }
                    ?: if (l.tiemposDescartados) context.getString(R.string.tiempos_descartados) else context.getString(R.string.sin_tiempos)
                mostrarSoloMensaje(aviso, tamanoSp - 3)
                return
            }
            // Antes de la primera línea: mostrar la primera como "siguiente".
            mostrarSoloMensaje("…", tamanoSp)
            val primera = lineas.firstOrNull { it.original.isNotBlank() }
            b.textoSiguiente.text = primera?.let { TextosDeLinea.principal(it, modo) }
            b.textoSiguiente.visibility = if (primera != null && mostrarSiguiente) View.VISIBLE else View.GONE
            return
        }
        val siguiente = lineas.drop(indice + 1).firstOrNull { it.original.isNotBlank() }
        if (pausa) {
            // La línea ya pasó y no hay voz: no dejarla colgada en pantalla.
            mostrarSoloMensaje("♪", tamanoSp)
            if (mostrarSiguiente && siguiente != null && !expandida) {
                b.textoSiguiente.text = TextosDeLinea.principal(siguiente, modo)
                b.textoSiguiente.visibility = View.VISIBLE
            }
            return
        }
        val (arriba, abajo) = TextosDeLinea.para(actual, modo)
        b.textoPrincipal.text = arriba
        b.textoPrincipal.visibility = if (arriba == null) View.GONE else View.VISIBLE
        b.textoSecundario.text = abajo
        b.textoSecundario.visibility = if (abajo == null) View.GONE else View.VISIBLE
        if (mostrarSiguiente && siguiente != null && !expandida) {
            b.textoSiguiente.text = TextosDeLinea.principal(siguiente, modo)
            b.textoSiguiente.visibility = View.VISIBLE
        } else b.textoSiguiente.visibility = View.GONE
    }

    private fun alternarExpandida() {
        expandida = !expandida
        b.panelExpandido.visibility = if (expandida) View.VISIBLE else View.GONE
        b.panelContraido.visibility = if (expandida) View.GONE else View.VISIBLE
        if (expandida && indice >= 0) b.listaLineas.scrollToPosition(indice)
        pintar()
    }

    private fun actualizarVentana() {
        if (!visible) return
        try { gestor.updateViewLayout(b.root, parametros) } catch (_: Exception) {}
    }

    /** Tocar = expandir/contraer; arrastrar = mover. */
    @SuppressLint("ClickableViewAccessibility")
    private fun instalarGestos() {
        val umbral = ViewConfiguration.get(context).scaledTouchSlop
        var x0 = 0f; var y0 = 0f; var px = 0; var py = 0; var arrastrando = false
        val oyente = View.OnTouchListener { _, e ->
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN -> { x0 = e.rawX; y0 = e.rawY; px = parametros.x; py = parametros.y; arrastrando = false; true }
                MotionEvent.ACTION_MOVE -> {
                    val dx = e.rawX - x0; val dy = e.rawY - y0
                    if (!arrastrando && (abs(dx) > umbral || abs(dy) > umbral)) arrastrando = true
                    if (arrastrando) { parametros.x = (px + dx).roundToInt(); parametros.y = (py + dy).roundToInt(); actualizarVentana() }
                    true
                }
                MotionEvent.ACTION_UP -> {
                    if (arrastrando) alMoverse(parametros.x, parametros.y) else alternarExpandida()
                    true
                }
                else -> false
            }
        }
        b.panelContraido.setOnTouchListener(oyente)
        b.textoCabecera.setOnTouchListener(oyente)
    }

    fun estaVisible() = visible

    companion object {
        private val parentesis = Regex("""\s*[(\[][^)\]]*[)\]]""")

        /** "Say It's Over (con Mindy Gledhill) [Remastered]" -> "Say It's Over". */
        fun sinParentesis(titulo: String) = parentesis.replace(titulo, "").trim().ifEmpty { titulo }

        fun colorTexto(context: Context) = ContextCompat.getColor(context, R.color.texto_principal)
    }
}
