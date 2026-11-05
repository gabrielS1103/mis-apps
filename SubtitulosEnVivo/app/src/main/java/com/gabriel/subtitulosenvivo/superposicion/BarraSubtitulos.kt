package com.gabriel.subtitulosenvivo.superposicion

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.PixelFormat
import android.graphics.Typeface
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.text.SpannableStringBuilder
import android.text.Spanned
import android.text.style.ForegroundColorSpan
import android.text.style.StyleSpan
import android.view.Gravity
import android.view.LayoutInflater
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import androidx.core.content.ContextCompat
import com.gabriel.subtitulosenvivo.R
import com.gabriel.subtitulosenvivo.Registro
import com.gabriel.subtitulosenvivo.databinding.BarraSubtitulosBinding
import kotlin.math.roundToInt

/**
 * Subtítulo flotante encima de cualquier app (Facebook, etc.). Se arrastra en vertical para moverlo
 * y muestra hasta dos frases; cada una se queda su tiempo de lectura y luego se va, para no tapar el video.
 * Todos los métodos se llaman desde el hilo principal.
 */
class BarraSubtitulos(
    private val context: Context,
    posicionY: Int,
    private val alMoverse: (Int) -> Unit,
) {
    private val gestor = context.getSystemService(Context.WINDOW_SERVICE) as WindowManager
    private val b = BarraSubtitulosBinding.inflate(LayoutInflater.from(context))
    private val parametros = WindowManager.LayoutParams(
        WindowManager.LayoutParams.MATCH_PARENT,
        WindowManager.LayoutParams.WRAP_CONTENT,
        WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
        WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
            WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
            WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
        PixelFormat.TRANSLUCENT,
    ).apply {
        gravity = Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL
        y = posicionY
    }
    private val principal = Handler(Looper.getMainLooper())
    private val colorParcial = ContextCompat.getColor(context, R.color.texto_parcial)
    private val lineas = ArrayList<Linea>()
    private val caducar = Runnable { quitarVencidas() }
    private var visible = false

    var mostrarIngles = false

    /** Una frase en pantalla. Las parciales (la frase todavía se está diciendo) no caducan hasta cerrarse. */
    private class Linea(var espanol: String, var ingles: String, var parcial: Boolean, var vence: Long)

    init {
        b.contenedor.visibility = View.GONE
        instalarArrastre()
    }

    fun mostrar() {
        if (visible) return
        try {
            gestor.addView(b.root, parametros)
            visible = true
        } catch (e: Exception) {
            Registro.error(Registro.OVERLAY, "No se pudo mostrar el subtítulo flotante", e)
        }
    }

    fun quitar() {
        principal.removeCallbacks(caducar)
        if (!visible) return
        try { gestor.removeView(b.root) } catch (_: Exception) {}
        visible = false
    }

    /**
     * Avance de la frase que todavía se está diciendo: aparece antes y se va completando.
     * Ocupa la última línea hasta que llega la frase terminada.
     */
    fun parcial(espanol: String, ingles: String) {
        val ultima = lineas.lastOrNull()
        if (ultima != null && ultima.parcial) {
            ultima.espanol = espanol; ultima.ingles = ingles
            // Por si la frase final nunca llega (Whisper la descarta).
            ultima.vence = SystemClock.uptimeMillis() + PARCIAL_MAXIMO_MS
        } else {
            agregar(Linea(espanol, ingles, true, SystemClock.uptimeMillis() + PARCIAL_MAXIMO_MS))
        }
        pintar()
    }

    /**
     * Frase terminada: reemplaza su avance o se agrega abajo. La de arriba sigue en pantalla
     * hasta cumplir su propio tiempo de lectura, así no desaparece apenas llega otra.
     */
    fun frase(espanol: String, ingles: String) {
        val vence = SystemClock.uptimeMillis() + tiempoLectura(espanol)
        val ultima = lineas.lastOrNull()
        if (ultima != null && ultima.parcial) {
            ultima.espanol = espanol; ultima.ingles = ingles; ultima.parcial = false; ultima.vence = vence
        } else {
            agregar(Linea(espanol, ingles, false, vence))
        }
        pintar()
    }

    /** La frase en curso se descartó (era música o ruido): se quita su avance. */
    fun descartarParcial() {
        if (lineas.lastOrNull()?.parcial == true) {
            lineas.removeAt(lineas.lastIndex)
            pintar()
        }
    }

    private fun agregar(linea: Linea) {
        lineas += linea
        while (lineas.size > MAXIMO_LINEAS) lineas.removeAt(0)
    }

    private fun quitarVencidas() {
        val ahora = SystemClock.uptimeMillis()
        lineas.removeAll { it.vence <= ahora }
        pintar()
    }

    private fun pintar() {
        principal.removeCallbacks(caducar)
        if (lineas.isEmpty()) {
            b.contenedor.visibility = View.GONE
            return
        }
        val texto = SpannableStringBuilder()
        lineas.forEachIndexed { i, l ->
            if (i > 0) texto.append('\n')
            val inicio = texto.length
            texto.append(l.espanol)
            if (l.parcial) {
                texto.setSpan(ForegroundColorSpan(colorParcial), inicio, texto.length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
                texto.setSpan(StyleSpan(Typeface.ITALIC), inicio, texto.length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            }
        }
        b.textoEspanol.text = texto
        b.textoIngles.text = lineas.last().ingles
        b.textoIngles.visibility = if (mostrarIngles) View.VISIBLE else View.GONE
        b.contenedor.visibility = View.VISIBLE
        principal.postAtTime(caducar, lineas.minOf { it.vence })
    }

    /** ~12 caracteres por segundo, entre 4 y 10 s. */
    private fun tiempoLectura(texto: String) = (texto.length * 1000L / 12).coerceIn(4_000, 10_000)

    /** Arrastrar mueve el subtítulo hacia arriba o abajo; la posición se guarda al soltar. */
    @SuppressLint("ClickableViewAccessibility")
    private fun instalarArrastre() {
        var y0 = 0f
        var inicio = 0
        b.contenedor.setOnTouchListener { _, e ->
            when (e.actionMasked) {
                MotionEvent.ACTION_DOWN -> { y0 = e.rawY; inicio = parametros.y; true }
                MotionEvent.ACTION_MOVE -> {
                    parametros.y = (inicio - (e.rawY - y0)).roundToInt().coerceAtLeast(0)
                    if (visible) try { gestor.updateViewLayout(b.root, parametros) } catch (_: Exception) {}
                    true
                }
                MotionEvent.ACTION_UP -> { alMoverse(parametros.y); true }
                else -> false
            }
        }
    }

    private companion object {
        const val MAXIMO_LINEAS = 2
        const val PARCIAL_MAXIMO_MS = 6_000L
    }
}
