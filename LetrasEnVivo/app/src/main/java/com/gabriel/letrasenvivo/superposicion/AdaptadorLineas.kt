package com.gabriel.letrasenvivo.superposicion

import android.graphics.Typeface
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.gabriel.letrasenvivo.R
import com.gabriel.letrasenvivo.ajustes.ModoVisualizacion
import com.gabriel.letrasenvivo.databinding.FilaLetraBinding
import com.gabriel.letrasenvivo.letras.modelos.LineaLetra

/**
 * Lista completa de la letra para la vista expandida, con la línea actual resaltada.
 * @param alTocar se llama con el índice de la línea tocada (para sincronizar con lo que suena).
 */
class AdaptadorLineas(private val alTocar: (Int) -> Unit) : RecyclerView.Adapter<AdaptadorLineas.Fila>() {

    var lineas: List<LineaLetra> = emptyList()
        set(v) { field = v; notifyDataSetChanged() }
    var modo: ModoVisualizacion = ModoVisualizacion.AMBOS
        set(v) { field = v; notifyDataSetChanged() }
    var tamanoSp: Float = 15f
        set(v) { field = v; notifyDataSetChanged() }
    var indiceActual: Int = -1
        set(v) {
            val anterior = field
            field = v
            if (anterior in lineas.indices) notifyItemChanged(anterior)
            if (v in lineas.indices) notifyItemChanged(v)
        }

    class Fila(val b: FilaLetraBinding) : RecyclerView.ViewHolder(b.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): Fila {
        val f = Fila(FilaLetraBinding.inflate(LayoutInflater.from(parent.context), parent, false))
        f.b.root.setOnClickListener { val i = f.bindingAdapterPosition; if (i != RecyclerView.NO_POSITION) alTocar(i) }
        return f
    }

    override fun getItemCount(): Int = lineas.size

    override fun onBindViewHolder(h: Fila, i: Int) {
        val l = lineas[i]
        val actual = i == indiceActual
        val (arriba, abajo) = TextosDeLinea.para(l, modo)
        h.b.filaPrincipal.text = arriba
        h.b.filaPrincipal.visibility = if (arriba == null) View.GONE else View.VISIBLE
        h.b.filaSecundaria.text = abajo
        h.b.filaSecundaria.visibility = if (abajo == null) View.GONE else View.VISIBLE
        h.b.filaPrincipal.textSize = tamanoSp
        h.b.filaSecundaria.textSize = tamanoSp - 1
        h.b.filaPrincipal.setTypeface(null, if (actual) Typeface.BOLD else Typeface.NORMAL)
        h.b.filaPrincipal.alpha = if (actual || indiceActual == -1) 1f else 0.7f
        h.b.filaSecundaria.alpha = h.b.filaPrincipal.alpha
        h.b.fila.setBackgroundResource(if (actual) R.drawable.fondo_linea_actual else 0)
    }
}
