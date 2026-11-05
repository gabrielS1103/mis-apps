package com.gabriel.letrasenvivo.red

import android.content.Context
import android.webkit.CookieManager
import android.webkit.WebSettings
import com.gabriel.letrasenvivo.Registro
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Cache
import okhttp3.Cookie
import okhttp3.CookieJar
import okhttp3.FormBody
import okhttp3.HttpUrl
import okhttp3.Interceptor
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.io.IOException
import java.util.concurrent.TimeUnit

/** Cliente HTTP compartido: caché, User-Agent por sitio, cookies compartidas con el WebView y reintentos. */
class ClienteHttp(context: Context) {

    data class Respuesta(val codigo: Int, val cuerpo: String, val url: String) {
        val correcta: Boolean get() = codigo in 200..299
        /** El servidor está caído o saturado (no es que no tenga lo pedido): conviene reintentar luego. */
        val fallaServidor: Boolean get() = codigo == 429 || codigo >= 500
    }

    val uaApp = "LetrasEnVivo/1.0 (https://github.com/gabrielS1103/proyecto)"

    /** El mismo User-Agent que usa el WebView del teléfono, para que las cookies de Cloudflare valgan en ambos. */
    val uaNavegador: String = try {
        WebSettings.getDefaultUserAgent(context)
    } catch (e: Exception) {
        "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36"
    }

    private val cliente: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(8, TimeUnit.SECONDS)
        .cache(Cache(File(context.cacheDir, "http"), 10L * 1024 * 1024))
        .cookieJar(CookiesDelWebView())
        .addInterceptor(Interceptor { cadena ->
            val original = cadena.request()
            val host = original.url.host
            // LRCLIB y MusicBrainz piden un User-Agent que identifique a la app.
            val ua = if (host.endsWith("lrclib.net") || host.endsWith("musicbrainz.org")) uaApp else uaNavegador
            val constructor = original.newBuilder()
                .header("User-Agent", ua)
                .header("Accept-Language", "en-US,en;q=0.9,es;q=0.8")
            if (host.endsWith("music.163.com")) constructor.header("Referer", "https://music.163.com/")
            val peticion = constructor.build()
            cadena.proceed(peticion)
        })
        .build()

    /** GET con un reintento ante cortes de red (no ante códigos HTTP de error ni si el servidor no contesta a tiempo). */
    suspend fun obtener(url: String): Respuesta = ejecutar(Request.Builder().url(url).get().build())

    /** POST de formulario (application/x-www-form-urlencoded). */
    suspend fun enviarFormulario(url: String, campos: Map<String, String>): Respuesta {
        val cuerpo = FormBody.Builder().apply { campos.forEach { (k, v) -> add(k, v) } }.build()
        return ejecutar(Request.Builder().url(url).post(cuerpo).build())
    }

    private suspend fun ejecutar(peticion: Request): Respuesta = withContext(Dispatchers.IO) {
        val url = peticion.url.toString()
        var ultimoError: IOException? = null
        repeat(2) { intento ->
            try {
                cliente.newCall(peticion).execute().use { r ->
                    val cuerpo = r.body?.string().orEmpty()
                    Registro.info(Registro.RED, "${peticion.method} $url -> ${r.code} (${cuerpo.length} car.)")
                    return@withContext Respuesta(r.code, cuerpo, r.request.url.toString())
                }
            } catch (e: IOException) {
                ultimoError = e
                Registro.aviso(Registro.RED, "${peticion.method} $url falló (intento ${intento + 1}): ${e.message}")
                // Si no contestó a tiempo, reintentar solo duplica la espera.
                if (e is java.net.SocketTimeoutException) throw e
                if (intento < 1) Thread.sleep(300L)
            }
        }
        throw ultimoError ?: IOException("Sin respuesta")
    }

    companion object {
        /** Cloudflare responde con una página "Just a moment..." que carga un desafío en JavaScript. */
        fun esDesafioCloudflare(respuesta: Respuesta): Boolean {
            if (respuesta.codigo !in setOf(403, 429, 503)) return false
            val c = respuesta.cuerpo
            return c.contains("Just a moment", ignoreCase = true) ||
                c.contains("challenges.cloudflare.com") ||
                c.contains("cf-chl") ||
                c.contains("Attention Required! | Cloudflare")
        }
    }

    /** Guarda y lee las cookies en el CookieManager de Android, el mismo que usa el WebView oculto. */
    private class CookiesDelWebView : CookieJar {
        private val gestor: CookieManager? = try { CookieManager.getInstance().also { it.setAcceptCookie(true) } } catch (e: Exception) { null }

        override fun saveFromResponse(url: HttpUrl, cookies: List<Cookie>) {
            val g = gestor ?: return
            for (c in cookies) g.setCookie(url.toString(), c.toString())
            g.flush()
        }

        override fun loadForRequest(url: HttpUrl): List<Cookie> {
            val g = gestor ?: return emptyList()
            val cabecera = g.getCookie(url.toString()) ?: return emptyList()
            return cabecera.split(";").mapNotNull { Cookie.parse(url, it.trim()) }
        }
    }
}
