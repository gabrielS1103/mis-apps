// Puente JNI mínimo entre Kotlin (voz/WhisperNativo.kt) y whisper.cpp.
#include <jni.h>
#include <android/log.h>
#include <algorithm>
#include <cmath>
#include <string>
#include "whisper.h"

#define ETIQUETA "SEV/Whisper"
#define LOG_I(...) __android_log_print(ANDROID_LOG_INFO, ETIQUETA, __VA_ARGS__)

namespace {

/** Límite de tokens del segmento actual (lo calcula transcribir según la duración del audio). */
struct Limites { int maxTokens; };

/**
 * Se llama en cada paso del decodificador. Fuerza el fin de frase cuando:
 *  - ya se generaron más tokens de los que caben en ese audio (habla rápida ≈ 5 tokens/s), o
 *  - los últimos 8 tokens son solo 1 o 2 distintos ("4, 4, 4, 4…"): Whisper entró en un bucle.
 * Sin esto, una alucinación sobre música llegó a tardar 47 s y bloqueó todas las frases siguientes.
 */
void cortarBucles(whisper_context *ctx, whisper_state *, const whisper_token_data *tokens,
                  int n, float *logits, void *datos) {
    const auto *lim = static_cast<const Limites *>(datos);
    bool cortar = n >= lim->maxTokens;
    if (!cortar && n >= 8) {
        whisper_token a = tokens[n - 1].id, b = -1;
        cortar = true;
        for (int i = n - 8; i < n; i++) {
            whisper_token t = tokens[i].id;
            if (t == a || t == b) continue;
            if (b == -1) { b = t; continue; }
            cortar = false;
            break;
        }
    }
    if (!cortar) return;
    const whisper_token fin = whisper_token_eot(ctx);
    const int vocab = whisper_n_vocab(ctx);
    for (int i = 0; i < vocab; i++) if (i != fin) logits[i] = -INFINITY;
}

/** El detector de voz escribe varias líneas por llamada (10 por segundo): solo se dejan avisos y errores. */
void soloAvisos(ggml_log_level nivel, const char *texto, void *) {
    if (nivel >= GGML_LOG_LEVEL_WARN) __android_log_write(ANDROID_LOG_WARN, ETIQUETA, texto);
}

} // namespace

extern "C" {

JNIEXPORT jlong JNICALL
Java_com_gabriel_subtitulosenvivo_voz_WhisperNativo_iniciar(JNIEnv *env, jobject, jstring ruta) {
    const char *r = env->GetStringUTFChars(ruta, nullptr);
    whisper_context_params cparams = whisper_context_default_params();
    cparams.use_gpu = false;
    whisper_context *ctx = whisper_init_from_file_with_params(r, cparams);
    env->ReleaseStringUTFChars(ruta, r);
    return reinterpret_cast<jlong>(ctx);
}

JNIEXPORT void JNICALL
Java_com_gabriel_subtitulosenvivo_voz_WhisperNativo_liberar(JNIEnv *, jobject, jlong ctx) {
    if (ctx != 0) whisper_free(reinterpret_cast<whisper_context *>(ctx));
}

/** Transcribe un segmento corto (PCM float 16 kHz mono). Devuelve null si whisper falla. */
JNIEXPORT jstring JNICALL
Java_com_gabriel_subtitulosenvivo_voz_WhisperNativo_transcribir(
        JNIEnv *env, jobject, jlong ctxPtr, jfloatArray audio, jstring prompt, jint hilos) {
    auto *ctx = reinterpret_cast<whisper_context *>(ctxPtr);
    if (ctx == nullptr) return nullptr;

    whisper_full_params p = whisper_full_default_params(WHISPER_SAMPLING_GREEDY);
    p.n_threads = hilos;
    p.language = "en";
    p.translate = false;
    p.no_timestamps = true;
    p.single_segment = true;      // segmentos cortos: una sola frase
    p.no_context = true;          // el contexto se pasa a mano en initial_prompt
    p.suppress_blank = true;
    p.suppress_nst = true;        // evita "[Music]", "(applause)", etc.
    p.temperature_inc = 0.0f;     // sin reintentos: desfase predecible
    p.print_progress = false;
    p.print_realtime = false;
    p.print_special = false;
    p.print_timestamps = false;

    jsize n = env->GetArrayLength(audio);
    const float segundos = n / 16000.0f;

    // Whisper siempre procesa una ventana de 30 s (1500 posiciones). Para frases de pocos segundos se le
    // indica solo la parte con audio (+ margen): el codificador hace ~4 veces menos trabajo.
    // Con poco margen Whisper tiende a repetir la frase para "llenar" la ventana; 128 (2,5 s) lo reduce.
    p.audio_ctx = std::min(1500, static_cast<int>(n / 320) + 128);

    Limites limites{static_cast<int>(segundos * 5) + 8};
    p.logits_filter_callback = cortarBucles;
    p.logits_filter_callback_user_data = &limites;

    const char *textoPrompt = prompt != nullptr ? env->GetStringUTFChars(prompt, nullptr) : nullptr;
    p.initial_prompt = textoPrompt;

    jfloat *muestras = env->GetFloatArrayElements(audio, nullptr);
    int resultado = whisper_full(ctx, p, muestras, n);
    env->ReleaseFloatArrayElements(audio, muestras, JNI_ABORT);
    if (textoPrompt != nullptr) env->ReleaseStringUTFChars(prompt, textoPrompt);

    if (resultado != 0) {
        LOG_I("whisper_full falló: %d", resultado);
        return nullptr;
    }
    std::string texto;
    int segmentos = whisper_full_n_segments(ctx);
    for (int i = 0; i < segmentos; i++) texto += whisper_full_get_segment_text(ctx, i);
    return env->NewStringUTF(texto.c_str());
}

/** Detector de voz Silero (distingue habla de música). Devuelve 0 si no se pudo cargar. */
JNIEXPORT jlong JNICALL
Java_com_gabriel_subtitulosenvivo_voz_WhisperNativo_vadIniciar(JNIEnv *env, jobject, jstring ruta) {
    whisper_log_set(soloAvisos, nullptr);
    const char *r = env->GetStringUTFChars(ruta, nullptr);
    whisper_vad_context_params cparams = whisper_vad_default_context_params();
    cparams.n_threads = 1;
    cparams.use_gpu = false;
    whisper_vad_context *vctx = whisper_vad_init_from_file_with_params(r, cparams);
    env->ReleaseStringUTFChars(ruta, r);
    return reinterpret_cast<jlong>(vctx);
}

JNIEXPORT void JNICALL
Java_com_gabriel_subtitulosenvivo_voz_WhisperNativo_vadLiberar(JNIEnv *, jobject, jlong vctx) {
    if (vctx != 0) whisper_vad_free(reinterpret_cast<whisper_vad_context *>(vctx));
}

/**
 * Probabilidad de voz de cada ventana de 512 muestras (32 ms). Conserva el estado entre llamadas,
 * así que el audio se pasa en orden y en múltiplos de 512. Devuelve null si falla.
 */
JNIEXPORT jfloatArray JNICALL
Java_com_gabriel_subtitulosenvivo_voz_WhisperNativo_vadProbabilidades(
        JNIEnv *env, jobject, jlong vctxPtr, jfloatArray audio) {
    auto *vctx = reinterpret_cast<whisper_vad_context *>(vctxPtr);
    if (vctx == nullptr) return nullptr;
    jsize n = env->GetArrayLength(audio);
    jfloat *muestras = env->GetFloatArrayElements(audio, nullptr);
    bool ok = whisper_vad_detect_speech_no_reset(vctx, muestras, n);
    env->ReleaseFloatArrayElements(audio, muestras, JNI_ABORT);
    if (!ok) return nullptr;
    int cantidad = whisper_vad_n_probs(vctx);
    jfloatArray salida = env->NewFloatArray(cantidad);
    env->SetFloatArrayRegion(salida, 0, cantidad, whisper_vad_probs(vctx));
    return salida;
}

JNIEXPORT jstring JNICALL
Java_com_gabriel_subtitulosenvivo_voz_WhisperNativo_infoSistema(JNIEnv *env, jobject) {
    return env->NewStringUTF(whisper_print_system_info());
}

} // extern "C"
