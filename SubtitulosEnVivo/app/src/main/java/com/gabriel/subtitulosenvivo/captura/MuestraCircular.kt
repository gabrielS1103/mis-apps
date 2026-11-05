package com.gabriel.subtitulosenvivo.captura

import java.io.File
import java.io.FileOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Guarda los últimos [capacidad] valores capturados para poder exportarlos como WAV y escucharlos. */
class MuestraCircular(private val capacidad: Int) {
    private val datos = ShortArray(capacidad)
    private var escritura = 0
    private var llenos = 0

    @Synchronized
    fun agregar(bloque: ShortArray, cantidad: Int = bloque.size) {
        for (i in 0 until cantidad) {
            datos[escritura] = bloque[i]
            escritura = (escritura + 1) % capacidad
        }
        llenos = minOf(capacidad, llenos + cantidad)
    }

    /** Copia ordenada de lo más antiguo a lo más reciente. */
    @Synchronized
    fun contenido(): ShortArray {
        val inicio = (escritura - llenos + capacidad) % capacidad
        return ShortArray(llenos) { datos[(inicio + it) % capacidad] }
    }

    @Synchronized
    fun vaciar() { escritura = 0; llenos = 0 }

    companion object {
        /** Escribe un WAV PCM de 16 bits mono. */
        fun escribirWav(destino: File, muestras: ShortArray, frecuencia: Int) {
            val bytesDatos = muestras.size * 2
            val cabecera = ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN).apply {
                put("RIFF".toByteArray()); putInt(36 + bytesDatos); put("WAVE".toByteArray())
                put("fmt ".toByteArray()); putInt(16); putShort(1); putShort(1)
                putInt(frecuencia); putInt(frecuencia * 2); putShort(2); putShort(16)
                put("data".toByteArray()); putInt(bytesDatos)
            }
            val cuerpo = ByteBuffer.allocate(bytesDatos).order(ByteOrder.LITTLE_ENDIAN)
            muestras.forEach { cuerpo.putShort(it) }
            FileOutputStream(destino).use { it.write(cabecera.array()); it.write(cuerpo.array()) }
        }
    }
}
