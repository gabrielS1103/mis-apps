// Trabajo de fondo de la extensión.
//
// 1. Al pulsar el botón de la extensión, abre el panel del colector en la pestaña actual.
// 2. Lote de perfiles: recorre en una pestaña los perfiles de la muestra, uno por uno, a ritmo de
//    persona, y descarga la ficha de cada uno. El estado se guarda en chrome.storage.session y las
//    esperas usan chrome.alarms, porque el navegador puede dormir este script entre perfil y perfil.

const FACEBOOK = /^https:\/\/([a-z0-9-]+\.)?facebook\.com\//i;
// Ritmo de cada modo. Vistazo: solo lo de arriba de cada perfil. Completo: recorre el muro.
const RITMOS = {
  vistazo: {
    pausaMin: 5, pausaMax: 12,           // segundos entre perfiles
    descansoCada: 25,                    // cada tantos perfiles...
    descansoMin: 30, descansoMax: 60,    // ...un descanso más largo (segundos)
    minutosPorPerfil: 1.5,               // si un perfil tarda más, se da por fallido y se sigue
  },
  completo: {
    pausaMin: 15, pausaMax: 35,
    descansoCada: 10,
    descansoMin: 60, descansoMax: 120,
    minutosPorPerfil: 4,
  },
};
const MAXIMO_POR_LOTE = 150;

// --- Panel ----------------------------------------------------------------------

chrome.action.onClicked.addListener(async (pestana) => {
  if (!pestana.url || !FACEBOOK.test(pestana.url)) {
    await chrome.action.setBadgeText({ tabId: pestana.id, text: 'FB?' });
    await chrome.action.setTitle({ tabId: pestana.id, title: 'AuditorBots solo funciona en facebook.com' });
    return;
  }
  await chrome.action.setBadgeText({ tabId: pestana.id, text: '' });
  await chrome.scripting.executeScript({ target: { tabId: pestana.id }, files: ['colector.js'] });
});

// --- Lote de perfiles -------------------------------------------------------------

async function leerLote() {
  return (await chrome.storage.session.get('lote')).lote || null;
}

async function guardarLote(lote) {
  await chrome.storage.session.set({ lote });
}

const azar = (min, max) => min + Math.random() * (max - min);

async function mostrarProgreso(lote) {
  const texto = lote.activo ? `${Math.min(lote.indice + 1, lote.urls.length)}/${lote.urls.length}` : '✓';
  await chrome.action.setBadgeText({ text: texto });
  await chrome.action.setTitle({ title: `AuditorBots: ${lote.mensaje || 'lote en marcha'}` });
}

async function iniciarLote(tabId, urls, modo = 'vistazo', ritmo = {}) {
  const actual = await leerLote();
  if (actual && actual.activo) return { error: 'Ya hay un lote en marcha. Detenlo antes de empezar otro.' };
  const lote = {
    tabId,
    urls: urls.slice(0, MAXIMO_POR_LOTE),
    modo: RITMOS[modo] ? modo : 'vistazo',
    indice: 0, listos: 0, fallos: 0,
    activo: true, inyectado: false,
    ritmo: { ...(RITMOS[modo] || RITMOS.vistazo), ...ritmo },
    mensaje: '',
  };
  await guardarLote(lote);
  await abrirActual();
  return { ok: true, total: lote.urls.length };
}

async function abrirActual() {
  const lote = await leerLote();
  if (!lote || !lote.activo) return;
  if (lote.indice >= lote.urls.length) {
    return terminar(lote, `Lote terminado: ${lote.listos} fichas descargadas` +
      (lote.fallos ? `, ${lote.fallos} perfiles con problemas.` : '.'));
  }
  lote.inyectado = false;
  lote.mensaje = `perfil ${lote.indice + 1} de ${lote.urls.length}`;
  await guardarLote(lote);
  await mostrarProgreso(lote);
  await chrome.alarms.create('vigilante', { delayInMinutes: lote.ritmo.minutosPorPerfil });
  try {
    await chrome.tabs.update(lote.tabId, { url: lote.urls[lote.indice], active: true });
  } catch (e) {
    await terminar(lote, 'Se cerró la pestaña del lote: lote detenido.');
  }
}

async function siguiente(lote, ok) {
  if (ok) lote.listos++;
  else lote.fallos++;
  lote.indice++;
  await chrome.alarms.clear('vigilante');
  let segundos = azar(lote.ritmo.pausaMin, lote.ritmo.pausaMax);
  if (lote.indice % lote.ritmo.descansoCada === 0) segundos += azar(lote.ritmo.descansoMin, lote.ritmo.descansoMax);
  lote.mensaje = `esperando ${Math.round(segundos)} s antes del perfil ${lote.indice + 1}`;
  await guardarLote(lote);
  await mostrarProgreso(lote);
  await chrome.alarms.create('siguiente', { delayInMinutes: segundos / 60 });
  return segundos;
}

async function terminar(lote, mensaje) {
  lote.activo = false;
  lote.mensaje = mensaje;
  await guardarLote(lote);
  await chrome.alarms.clear('siguiente');
  await chrome.alarms.clear('vigilante');
  await mostrarProgreso(lote);
}

async function descargarFicha(archivo, datos) {
  const nombre = String(archivo || 'perfil.json').replace(/[^\w.\-]/g, '_');
  await chrome.downloads.download({
    url: 'data:application/json;charset=utf-8,' + encodeURIComponent(JSON.stringify(datos, null, 2)),
    filename: nombre,
    conflictAction: 'uniquify',
    saveAs: false,
  });
}

// Cuando el perfil termina de cargar, se inyecta el colector y se le pide que haga su parte
chrome.tabs.onUpdated.addListener(async (tabId, cambio, pestana) => {
  if (cambio.status !== 'complete') return;
  const lote = await leerLote();
  if (!lote || !lote.activo || tabId !== lote.tabId || lote.inyectado) return;
  if (!FACEBOOK.test(pestana.url || '')) return;
  lote.inyectado = true;
  await guardarLote(lote);
  try {
    await chrome.scripting.executeScript({ target: { tabId }, files: ['colector.js'] });
    await chrome.scripting.executeScript({
      target: { tabId },
      func: (info) => { window.__auditorBots.lote(info); },
      args: [{ indice: lote.indice + 1, total: lote.urls.length, modo: lote.modo }],
    });
  } catch (e) {
    await siguiente(lote, false);
  }
});

chrome.tabs.onRemoved.addListener(async (tabId) => {
  const lote = await leerLote();
  if (lote && lote.activo && tabId === lote.tabId) await terminar(lote, 'Se cerró la pestaña del lote: lote detenido.');
});

chrome.alarms.onAlarm.addListener(async (alarma) => {
  const lote = await leerLote();
  if (!lote || !lote.activo) return;
  if (alarma.name === 'siguiente') await abrirActual();
  if (alarma.name === 'vigilante') await siguiente(lote, false);   // el perfil no respondió a tiempo
});

async function manejar(mensaje, remitente) {
  const lote = await leerLote();
  switch (mensaje.tipo) {
    case 'iniciarLote':
      return iniciarLote(remitente.tab.id, mensaje.urls, mensaje.modo);
    case 'detenerLote':
      if (lote && lote.activo) await terminar(lote, `Lote detenido: ${lote.listos} fichas descargadas.`);
      return { ok: true };
    case 'estadoLote':
      return lote;
    case 'perfilListo':
      if (!lote || !lote.activo || !remitente.tab || remitente.tab.id !== lote.tabId) return null;
      if (mensaje.datos) {
        // Si la descarga falla, el lote sigue igual: se cuenta como perfil con problemas
        try { await descargarFicha(mensaje.archivo, mensaje.datos); } catch (e) { mensaje.datos = null; }
      }
      return { ok: true, segundos: await siguiente(lote, !!mensaje.datos) };
    case 'bloqueo':
      if (lote && lote.activo) {
        await terminar(lote, 'Facebook pidió verificar la cuenta: lote detenido. Resuelve la verificación ' +
          'y espera unas horas antes de seguir.');
      }
      return { ok: true };
    default:
      return null;
  }
}

chrome.runtime.onMessage.addListener((mensaje, remitente, responder) => {
  manejar(mensaje, remitente).then(responder, (e) => responder({ error: e.message }));
  return true;   // la respuesta llega después
});
