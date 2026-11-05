package com.gabriel.letrasenvivo.letras

import com.gabriel.letrasenvivo.letras.modelos.LineaLetra

/** Las líneas que se repiten (estribillos) llevan la misma traducción aunque el alineador solo emparejara una. */
object TraduccionesRepetidas {
    fun completar(lineas: List<LineaLetra>): List<LineaLetra> {
        val conocidas = HashMap<String, LineaLetra>()
        for (l in lineas) if (l.traducida != null && l.original.isNotBlank()) conocidas.putIfAbsent(NormalizadorTexto.normalizar(l.original), l)
        return lineas.map { l ->
            if (l.traducida != null || l.original.isBlank()) l
            else conocidas[NormalizadorTexto.normalizar(l.original)]?.let { l.copy(traducida = it.traducida, traduccionAutomatica = it.traduccionAutomatica) } ?: l
        }
    }
}
