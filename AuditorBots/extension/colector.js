// AuditorBots — colector de datos de Facebook.
//
// Se pega en la consola del navegador (F12) o se usa como marcador (ver marcador.txt).
// Solo lee lo que la página ya muestra: no hace clics, no desplaza la página y no envía nada a
// ningún servidor. Al pulsar «Descargar» guarda un archivo .json en la carpeta de descargas.
(() => {
  'use strict';

  if (window.__auditorBots) {
    window.__auditorBots.mostrar();
    return;
  }

  // Selectores de Facebook. Si Facebook cambia su página y el colector deja de encontrar datos,
  // casi siempre basta con ajustar estos.
  const SEL = {
    dialogo: '[role="dialog"]',
    pestanaActiva: '[role="tab"][aria-selected="true"]',
    pestanas: '[role="tablist"]',
    comentario: '[role="article"]',
    publicacion: '[role="article"], [aria-posinset], [data-pagelet*="FeedUnit"]',
    textoLibre: 'div[dir="auto"], span[dir="auto"]',
    mensajePublicacion: '[data-ad-preview="message"], [data-ad-comet-preview="message"]',
    principal: '[role="main"]',
    nombrePerfil: 'h1',
  };
  const ETIQUETA_COMENTARIO = /coment|comment|respuest|repl/i;
  const CERRADO = /bloqueó su perfil|locked (his|her|their) profile|perfil bloqueado|profile is locked|no hay publicaciones disponibles|no posts available/i;
  const NO_DISPONIBLE = /este contenido no está disponible|this content isn't available|esta página no está disponible|this page isn't available/i;
  // Facebook pide verificar la cuenta: hay que parar todo
  const BLOQUEO = /\/checkpoint|\/login|two_step_verification|captcha/i;
  const ES_EXTENSION = typeof chrome !== 'undefined' && !!(chrome.runtime && chrome.runtime.id);
  // Textos sueltos de un comentario que no son parte de lo que escribió la persona
  const INSIGNIAS = /^(·|autor|author|fan destacado|top fan|seguir|follow|editado|edited|ver más|see more|ver traducción|see translation|ver original|see original)$/i;
  // Enlaces que sí son de una publicación concreta (y no de la página o del perfil que la publicó)
  const URL_PUBLICACION = /\/posts\/|\/reel\/|\/videos?\/|\/watch|story_fbid|permalink|fbid=|\/photos?\/|\/share\/(p|r|v)\//;
  const ENLACE_PUBLICACION = /\/posts\/|story_fbid|permalink|\/photos?\/|\/videos?\/|\/reel\/|fbid=/;
  const RE_FECHA = new RegExp(
    '^(hace\\s+)?\\d+\\s*(a|años?|y|yrs?|years?|mes(es)?|mo|months?|sem(anas?)?|w|wks?|weeks?|d|días?|days?|h|hrs?|horas?|min|m)\\.?(\\s+ago)?$' +
      '|^(ayer|yesterday|hoy|today)\\b' +
      '|\\b(ene|feb|mar|abr|may|jun|jul|ago|sep|set|oct|nov|dic|jan|apr|aug|dec)[a-z]*\\.?\\s+(de\\s+)?\\d' +
      '|\\d{1,2}\\s+(de\\s+)?(ene|feb|mar|abr|may|jun|jul|ago|sep|set|oct|nov|dic|jan|apr|aug|dec)[a-z]*',
    'i');
  const REACCIONES = [
    [/me gusta|\blike/i, 'Me gusta'], [/me encanta|\blove/i, 'Me encanta'],
    [/me importa|\bcare/i, 'Me importa'], [/me divierte|haha/i, 'Me divierte'],
    [/me asombra|\bwow/i, 'Me asombra'], [/me entristece|\bsad/i, 'Me entristece'],
    [/me enoja|angry/i, 'Me enoja'],
  ];
  // Primeras partes de la ruta que no son perfiles (igual que auditor/enlaces.py)
  const RESERVADAS = new Set(('ads bookmarks business commerce dialog donate events friends fundraisers ' +
    'gaming groups hashtag help home.php l.php live login marketplace me media memories messages ' +
    'notifications pages permalink.php photo photo.php photos plugins policies posts privacy profile ' +
    'reel reels saved search settings share sharer sharer.php stories story.php ufi videos watch').split(' '));

  // --- Utilidades -----------------------------------------------------------

  function esFacebook(host) {
    host = host.toLowerCase();
    return host === 'facebook.com' || host === 'fb.com' || host.endsWith('.facebook.com');
  }

  function identificar(href) {
    let u;
    try { u = new URL(href, location.href); } catch (e) { return null; }
    if (!esFacebook(u.hostname)) return null;
    const partes = u.pathname.split('/').filter(Boolean);
    let id = null;
    if (partes[0] === 'profile.php' && /^\d+$/.test(u.searchParams.get('id') || '')) id = u.searchParams.get('id');
    else if (partes[0] === 'people' && /^\d+$/.test(partes[2] || '')) id = partes[2];
    else if (partes[0] === 'groups' && partes[2] === 'user' && /^\d+$/.test(partes[3] || '')) id = partes[3];
    if (id) return { clave: 'id:' + id, url: 'https://www.facebook.com/profile.php?id=' + id, id, usuario: null };
    if (partes.length === 1 && !RESERVADAS.has(partes[0].toLowerCase()) && /^[A-Za-z0-9.\-]{2,80}$/.test(partes[0])) {
      const usuario = partes[0].toLowerCase();
      return { clave: 'u:' + usuario, url: 'https://www.facebook.com/' + usuario, id: null, usuario };
    }
    return null;
  }

  // Texto de un elemento conservando los emojis, que Facebook dibuja como <img alt="😂">
  function textoDe(el) {
    if (!el) return '';
    const copia = el.cloneNode(true);
    copia.querySelectorAll('img[alt]').forEach((img) => img.replaceWith(img.alt));
    return (copia.textContent || '').replace(/\s+/g, ' ').trim();
  }

  function dialogoAbierto() {
    const dialogos = [...document.querySelectorAll(SEL.dialogo)].filter((d) => d.offsetParent !== null);
    return dialogos.length ? dialogos[dialogos.length - 1] : null;
  }

  function esPaginaDePerfil() {
    return identificar(location.href) !== null;
  }

  function sello() {
    const d = new Date();
    const p = (n) => String(n).padStart(2, '0');
    return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
  }

  function descargar(nombre, datos) {
    const blob = new Blob([JSON.stringify(datos, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = nombre;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 5000);
  }

  // --- Estado ---------------------------------------------------------------

  const estado = {
    modo: null,
    publicacion: location.href,
    reacciones: new Map(),   // clave -> {nombre, url, id, usuario, reaccion}
    comentarios: new Map(),  // clave + texto -> {nombre, url, id, usuario, texto, fecha}
    perfil: null,
    totalMostrado: '',       // total de reacciones que muestra Facebook ("Todas 178 mil")
    limite: 2000,            // el modo automático se detiene y descarga al llegar a esta cantidad
  };

  function vaciar() {
    estado.publicacion = location.href;
    estado.reacciones.clear();
    estado.comentarios.clear();
    estado.perfil = null;
    estado.totalMostrado = '';
    ui.filasPerfil.replaceChildren();
  }

  // --- Reacciones -----------------------------------------------------------

  function nombreReaccion(pestana) {
    const texto = ((pestana.getAttribute('aria-label') || '') + ' ' + textoDe(pestana)).trim();
    if (/^(todas|all)\b/i.test(textoDe(pestana)) || /\b(todas las|all) reac/i.test(texto)) return null;
    for (const [re, nombre] of REACCIONES) if (re.test(texto)) return nombre;
    return null; // pestaña "Todas"
  }

  function escanearReacciones() {
    const raiz = dialogoAbierto();
    if (!raiz || !raiz.querySelector(SEL.pestanas)) return 'Abre la lista de reacciones de la publicación.';
    const pestana = raiz.querySelector(SEL.pestanaActiva);
    const reaccion = pestana ? nombreReaccion(pestana) : null;
    // La primera pestaña ("Todas 178 mil") trae el total que muestra Facebook
    const primera = raiz.querySelector('[role="tab"]');
    if (primera) estado.totalMostrado = (textoDe(primera) + ' ' + (primera.getAttribute('aria-label') || '')).trim();
    for (const a of raiz.querySelectorAll('a[href]')) {
      const p = identificar(a.href);
      if (!p) continue;
      const nombre = textoDe(a);
      const previo = estado.reacciones.get(p.clave);
      if (previo) {
        if (!previo.nombre && nombre) previo.nombre = nombre;
        if (reaccion && !previo.reaccion) previo.reaccion = reaccion;
      } else {
        estado.reacciones.set(p.clave, { nombre, url: p.url, id: p.id, usuario: p.usuario, reaccion });
      }
    }
    return `Desplaza la lista hacia abajo hasta el final.${reaccion ? ' Pestaña: ' + reaccion + '.' : ''}`;
  }

  // --- Comentarios ----------------------------------------------------------

  function escanearComentarios() {
    const raiz = dialogoAbierto() || document;
    let vistos = 0;
    for (const art of raiz.querySelectorAll(SEL.comentario)) {
      if (!ETIQUETA_COMENTARIO.test(art.getAttribute('aria-label') || '')) continue;
      vistos++;
      const propios = (sel) => [...art.querySelectorAll(sel)].filter((e) => e.closest(SEL.comentario) === art);
      const autor = propios('a[href]').find((a) => identificar(a.href) && textoDe(a));
      if (!autor) continue;
      const p = identificar(autor.href);
      const nombre = textoDe(autor);
      const enlaceFecha = propios('a[href*="comment_id"]').find((a) => a !== autor && RE_FECHA.test(textoDe(a)));
      let fecha = enlaceFecha ? textoDe(enlaceFecha) : '';
      const trozos = [];
      for (const el of propios(SEL.textoLibre)) {
        if (el.closest('a') || el.querySelector(SEL.textoLibre)) continue;
        const t = textoDe(el);
        if (!t || t === nombre || trozos.includes(t) || INSIGNIAS.test(t)) continue;
        // La fecha ("11 sem") a veces va en un texto suelto y no en un enlace
        if (t.length <= 25 && RE_FECHA.test(t)) {
          if (!fecha) fecha = t;
          continue;
        }
        trozos.push(t);
      }
      const texto = trozos.join(' ').replace(/\s*(\.\.\.|…)?\s*(Ver más|See more)$/i, '…');
      const llave = p.clave + '\n' + texto;
      if (!estado.comentarios.has(llave)) {
        estado.comentarios.set(llave, { nombre, url: p.url, id: p.id, usuario: p.usuario, texto, fecha });
      }
    }
    return vistos ? 'Abre las respuestas y desplázate hasta el final.' :
      'No veo comentarios. Elige «Todos los comentarios» en el orden de la publicación.';
  }

  // --- Perfil ---------------------------------------------------------------

  function idDelPerfil(ident) {
    if (ident && ident.id) return ident.id;
    const meta = document.querySelector('meta[property="al:android:url"]');
    const m = meta && /fb:\/\/profile\/(\d+)/.exec(meta.content || '');
    if (m) return m[1];
    // El número solo se acepta si viene junto al nombre de usuario de esta página, para no
    // confundirlo con el de tu propia cuenta.
    if (ident && ident.usuario) {
      const html = document.documentElement.innerHTML;
      const u = ident.usuario.replace(/[.\-]/g, '\\$&');
      const re1 = new RegExp(`"userVanity":"${u}","userID":"(\\d+)"`, 'i');
      const re2 = new RegExp(`"userID":"(\\d+)","userVanity":"${u}"`, 'i');
      const encontrado = re1.exec(html) || re2.exec(html);
      const propio = /(?:^|;\s*)c_user=(\d+)/.exec(document.cookie);
      if (encontrado && (!propio || propio[1] !== encontrado[1])) return encontrado[1];
    }
    return null;
  }

  function iniciarPerfil() {
    const ident = identificar(location.href);
    const principal = document.querySelector(SEL.principal) || document.body;
    const lineas = (principal.innerText || '').split('\n');
    const seUnio = lineas.find((l) => /(se unió|joined|miembro desde|member since).*\d{4}/i.test(l)) || '';
    const fotoEnlace = [...principal.querySelectorAll('a[href], [aria-label]')]
      .some((el) => /foto del perfil|profile picture|profile photo/i.test(el.getAttribute('aria-label') || ''));
    const amigos = [...principal.querySelectorAll('a[href*="friends"]')].some((a) => /\d/.test(textoDe(a)));
    estado.perfil = {
      url: ident ? ident.url : location.href,
      id: idDelPerfil(ident),
      usuario: ident ? ident.usuario : null,
      nombre: nombreDelPerfil(),
      se_unio: seUnio.trim(),
      tiene_foto: fotoEnlace ? true : null,
      amigos_visibles: amigos ? true : null,
      sin_publicaciones: false,
      no_disponible: NO_DISPONIBLE.test(principal.innerText || document.body.innerText || ''),
      publicaciones: [],
      vistas: new Set(),
    };
    pintarCamposPerfil();
  }

  // Facebook esconde la fecha de las publicaciones mezclando letras que no se ven (colocadas fuera
  // de la caja del enlace o con tamaño cero). Aquí se leen solo las letras visibles, en orden.
  function textoVisible(el) {
    const caja = el.getBoundingClientRect();
    if (!caja.width || !caja.height) return '';
    const letras = [];
    const recorrido = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    for (let nodo = recorrido.nextNode(); nodo; nodo = recorrido.nextNode()) {
      if (!nodo.textContent.trim() && nodo.textContent !== ' ') continue;
      const padre = nodo.parentElement;
      const estilo = getComputedStyle(padre);
      if (estilo.display === 'none' || estilo.visibility === 'hidden' || estilo.opacity === '0' ||
          parseFloat(estilo.fontSize) === 0) continue;
      const rango = document.createRange();
      rango.selectNodeContents(nodo);
      const r = rango.getBoundingClientRect();
      if (!r.width && nodo.textContent !== ' ') continue;
      if (r.bottom < caja.top - 2 || r.top > caja.bottom + 2 || r.right < caja.left - 2 || r.left > caja.right + 2) continue;
      letras.push({ x: r.left, y: Math.round(r.top), t: nodo.textContent });
    }
    // Facebook también puede desordenar las letras y reordenarlas con CSS: se ordenan por posición
    letras.sort((a, b) => a.y - b.y || a.x - b.x);
    return letras.map((l) => l.t).join('').replace(/\s+/g, ' ').trim();
  }

  // El enlace de la fecha de una publicación (lleva a la propia publicación)
  function enlacesDeFecha(art) {
    return [...art.querySelectorAll('a[href]')].filter((a) =>
      (!a.closest(SEL.comentario) || a.closest(SEL.comentario) === art) &&
      !identificar(a.href) && (ENLACE_PUBLICACION.test(a.href) || /pfbid|\/posts\//.test(a.href) || a.getAttribute('href') === '#'));
  }

  function fechaDePublicacion(art) {
    const candidatos = [...enlacesDeFecha(art),
      ...[...art.querySelectorAll('span')].filter((s) => !s.children.length &&
        (!s.closest(SEL.comentario) || s.closest(SEL.comentario) === art))];
    for (const el of candidatos) {
      const etiqueta = el.getAttribute('aria-label') || '';
      if (etiqueta.length <= 60 && RE_FECHA.test(etiqueta)) return etiqueta;
      for (const t of [textoVisible(el), textoDe(el)]) {
        if (t && t.length <= 40 && RE_FECHA.test(t)) return t;
      }
    }
    return '';
  }

  // Marcas de tiempo ("creation_time") de las publicaciones que Facebook incluye en los datos de la
  // página. Sirven de respaldo si la fecha visible no se pudo leer.
  function marcasDeTiempo() {
    const marcas = new Set();
    for (const s of document.querySelectorAll('script[type="application/json"]')) {
      const texto = s.textContent;
      if (!texto.includes('creation_time')) continue;
      for (const m of texto.matchAll(/"creation_time":(\d{10})\b/g)) marcas.add(Number(m[1]));
    }
    return [...marcas].sort((a, b) => b - a).slice(0, 20);
  }

  // Muestra de cómo Facebook dibujó la fecha, para poder ajustar el colector si no se lee
  function diagnosticoFecha(art) {
    const a = enlacesDeFecha(art)[0];
    if (!a) return null;
    return { html: a.outerHTML.slice(0, 1500), visible: textoVisible(a), texto: textoDe(a).slice(0, 80),
      etiqueta: a.getAttribute('aria-label') || '' };
  }

  // Nombre del perfil: el primer encabezado con texto que no sea parte de la interfaz de Facebook
  function nombreDelPerfil() {
    for (const h of document.querySelectorAll('h1')) {
      if (anfitrion.contains(h)) continue;
      const t = textoDe(h);
      if (t && !/^(facebook|notificaciones|notifications|chats|menú|menu)$/i.test(t)) return t;
    }
    const meta = document.querySelector('meta[property="og:title"]');
    if (meta && meta.content && !/^facebook$/i.test(meta.content)) return meta.content;
    const titulo = document.title.replace(/^\(\d+\)\s*/, '').replace(/\s*\|\s*Facebook\s*$/i, '');
    return /^facebook$/i.test(titulo) ? '' : titulo;
  }

  // Qué había en la página cuando no se encontraron publicaciones, para poder ajustar el colector
  function diagnosticoPagina() {
    const principal = document.querySelector(SEL.principal);
    return {
      titulo: document.title,
      encabezados: [...document.querySelectorAll('h1, h2')].filter((h) => !anfitrion.contains(h))
        .map((h) => textoDe(h)).filter(Boolean).slice(0, 8),
      articulos: document.querySelectorAll('[role="article"]').length,
      posinset: document.querySelectorAll('[aria-posinset]').length,
      mensajes: document.querySelectorAll(SEL.mensajePublicacion).length,
      principal: principal ? (principal.innerText || '').slice(0, 600) : null,
    };
  }

  function enlacesDe(art) {
    const enlaces = new Set();
    for (const a of art.querySelectorAll('a[href]')) {
      let u;
      try { u = new URL(a.href); } catch (e) { continue; }
      if (!/^https?:$/.test(u.protocol)) continue;
      if (esFacebook(u.hostname)) {
        if (u.pathname.startsWith('/l.php') || /^\/(donate|fundraisers)\//.test(u.pathname)) enlaces.add(u.href);
      } else {
        enlaces.add(u.href);
      }
    }
    return [...enlaces];
  }

  function escanearPerfil() {
    if (!esPaginaDePerfil()) return 'Abre la página de un perfil.';
    const ident = identificar(location.href);
    if (estado.perfil && ident && estado.perfil.url !== ident.url) {
      return 'Cambiaste de perfil: pulsa «Vaciar» antes de seguir.';
    }
    if (!estado.perfil) iniciarPerfil();
    const principal = document.querySelector(SEL.principal) || document.body;
    // Solo las publicaciones de primer nivel: los comentarios dentro de ellas también son "article"
    const externas = [...principal.querySelectorAll(SEL.publicacion)]
      .filter((e) => !(e.parentElement && e.parentElement.closest(SEL.publicacion)));
    for (const art of externas) {
      const mensaje = art.querySelector(SEL.mensajePublicacion);
      const texto = mensaje ? textoDe(mensaje) : '';
      const fecha = fechaDePublicacion(art);
      const enlaces = enlacesDe(art);
      if (!texto && !fecha && !enlaces.length) continue;
      const llave = texto.slice(0, 200) + '\n' + fecha + '\n' + enlaces.join(' ');
      if (estado.perfil.vistas.has(llave)) continue;
      estado.perfil.vistas.add(llave);
      const pub = { fecha, texto: texto.slice(0, 3000), enlaces, borrada: false };
      if (!fecha && !estado.perfil.diagnostico) estado.perfil.diagnostico = diagnosticoFecha(art);
      estado.perfil.publicaciones.push(pub);
      agregarFilaPublicacion(pub);
    }
    return 'Desplázate por sus publicaciones y revisa las fechas antes de descargar.';
  }

  // --- Modo automático --------------------------------------------------------
  // Desplaza la lista y abre "Ver más comentarios" a ritmo de persona: pausas al azar y un
  // descanso más largo de vez en cuando. Lo que lee es lo mismo que verías desplazando a mano.

  const RITMO = {
    pausaMin: 600, pausaMax: 1500,          // entre cada paso (ms)
    avanceMin: 0.35, avanceMax: 0.55,       // cuánto baja cada paso, en altos de la lista visible
    descansoCada: 40, descansoMin: 4000, descansoMax: 9000,
    intentosAlFinal: 6,                     // pasos sin nada nuevo al final antes de terminar
    limitePasos: 8000,                      // unas 30.000 filas
    publicacionesPerfil: 10,                // en un perfil basta con las más recientes
  };
  const ABRIR_RESPUESTAS = /^(ver|view|mostrar|show)\b.*\b(comentarios?|comments?|respuestas?|repl(y|ies))\b|^\d+\s+(respuestas?|repl(y|ies))$/i;
  const VER_MAS_TEXTO = /^(ver más|see more)$/i;
  const ABRIR_REACCIONES = /quién reaccionó|who reacted|todas las reacciones|all reactions/i;

  const espera = (min, max) => new Promise((r) => setTimeout(r, min + Math.random() * (max - min)));
  // Espera a que la página se dibuje (unos 30 ms en una pestaña visible), con un tope por si el
  // navegador no está dibujando
  const dibujado = () => Promise.race([
    new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))),
    new Promise((r) => setTimeout(r, 500)),
  ]);
  let automatico = null;   // { detener: bool } mientras está en marcha

  function desplazable(raiz) {
    if (raiz === document) return document.scrollingElement;
    let mejor = null;
    for (const e of [raiz, ...raiz.querySelectorAll('div')]) {
      if (e.scrollHeight <= e.clientHeight + 20) continue;
      if (!/(auto|scroll)/.test(getComputedStyle(e).overflowY)) continue;
      if (!mejor || e.scrollHeight > mejor.scrollHeight) mejor = e;
    }
    return mejor || document.scrollingElement;
  }

  function alFinal(e) {
    return e.scrollTop + e.clientHeight >= e.scrollHeight - 10;
  }

  function cantidad() {
    if (estado.modo === 'reacciones') return estado.reacciones.size;
    if (estado.modo === 'comentarios') return estado.comentarios.size;
    return estado.perfil ? estado.perfil.publicaciones.filter((p) => !p.borrada).length : 0;
  }

  // Un botón "Ver más comentarios" / "Ver 3 respuestas" / "Ver más" (texto cortado). Se puede pulsar
  // varias veces (Facebook a veces reutiliza el mismo botón), pero se deja de lado si dos pulsaciones
  // seguidas no traen nada nuevo.
  const sinEfecto = new WeakMap();
  function siguienteBoton(raiz) {
    for (const b of raiz.querySelectorAll('[role="button"]')) {
      if ((sinEfecto.get(b) || 0) >= 2 || b.offsetParent === null) continue;
      const t = textoDe(b);
      if (t.length > 50) continue;
      if (ABRIR_RESPUESTAS.test(t) || (VER_MAS_TEXTO.test(t) && b.closest(SEL.comentario))) return b;
    }
    return null;
  }

  // Si todas las filas leídas siguen en la página, Facebook no está borrando las que pasan: se puede
  // saltar directo al final de la lista para que cargue el siguiente lote, que es mucho más rápido.
  function listaCompletaEnPagina(raiz) {
    if (cantidad() < 20) return false;
    const claves = new Set();
    for (const a of raiz.querySelectorAll('a[href]')) {
      const p = identificar(a.href);
      if (p) claves.add(p.clave);
    }
    return claves.size >= cantidad() * 0.95;
  }

  async function abrirReacciones() {
    const boton = [...document.querySelectorAll('[aria-label]')]
      .find((e) => ABRIR_REACCIONES.test(e.getAttribute('aria-label')) && e.offsetParent !== null);
    if (!boton) return false;
    boton.click();
    for (let i = 0; i < 10 && !(dialogoAbierto() && dialogoAbierto().querySelector(SEL.pestanas)); i++) {
      await espera(400, 600);
    }
    return !!(dialogoAbierto() && dialogoAbierto().querySelector(SEL.pestanas));
  }

  async function recorrer() {
    if (estado.modo === 'reacciones' && !(dialogoAbierto() && dialogoAbierto().querySelector(SEL.pestanas))) {
      if (!(await abrirReacciones())) return 'No encontré la lista de reacciones: haz clic en el número de reacciones y vuelve a pulsar «Automático».';
    }
    let sinNovedad = 0;
    for (let paso = 1; paso <= RITMO.limitePasos; paso++) {
      if (automatico.detener) return 'Detenido. Puedes descargar lo recolectado.';
      // Con la pestaña oculta el navegador no dibuja la lista y frena los temporizadores: se espera
      while (document.hidden && !automatico.detener) {
        ui.mensaje.textContent = 'En pausa: vuelve a esta pestaña para que siga (el navegador detiene las pestañas ocultas).';
        await espera(1000, 1000);
      }
      actualizar();   // lee lo que ya está a la vista antes de moverse
      const antes = cantidad();
      const raiz = estado.modo === 'perfil' ? document : (dialogoAbierto() || document);
      const lista = desplazable(raiz);
      const boton = estado.modo === 'comentarios' ? siguienteBoton(raiz) : null;
      const posicion = lista.scrollTop;
      if (boton) {
        boton.scrollIntoView({ block: 'center' });
        await espera(300, 700);
        boton.click();
      } else if (estado.modo !== 'perfil' && raiz !== document && listaCompletaEnPagina(raiz)) {
        lista.scrollTop = lista.scrollHeight;
      } else {
        // Pasos cortos: si la página no alcanza a dibujar una posición, la siguiente aún se solapa
        // con la anterior y no se salta ninguna fila
        lista.scrollTop += Math.round(lista.clientHeight * (RITMO.avanceMin + Math.random() * (RITMO.avanceMax - RITMO.avanceMin)));
      }
      await dibujado();
      actualizar();
      await espera(RITMO.pausaMin, RITMO.pausaMax);
      if (paso % RITMO.descansoCada === 0) await espera(RITMO.descansoMin, RITMO.descansoMax);
      actualizar();
      if (boton) sinEfecto.set(boton, cantidad() > antes ? 0 : (sinEfecto.get(boton) || 0) + 1);
      ui.mensaje.textContent = `Automático: paso ${paso}… (pulsa «Detener» cuando quieras)`;
      if (estado.modo === 'perfil' && cantidad() >= RITMO.publicacionesPerfil) return null;
      if (estado.modo !== 'perfil' && estado.limite && cantidad() >= estado.limite) return null;
      // Sin nada nuevo y sin poder bajar más (al final, o la página no se deja desplazar)
      const atascado = alFinal(lista) || lista.scrollTop === posicion;
      sinNovedad = cantidad() === antes && !boton && atascado ? sinNovedad + 1 : 0;
      if (sinNovedad >= RITMO.intentosAlFinal) return null;
    }
    return null;
  }

  // Devuelve null si terminó completo, o un mensaje si se detuvo o falló
  async function correrAutomatico() {
    automatico = { detener: false };
    ui.automatico.textContent = 'Detener';
    let mensaje;
    try {
      mensaje = await recorrer();
    } catch (e) {
      mensaje = 'Error en el modo automático: ' + e.message;
    }
    automatico = null;
    ui.automatico.textContent = 'Automático';
    actualizar();
    return mensaje;
  }

  async function alternarAutomatico() {
    if (automatico) {
      automatico.detener = true;
      return;
    }
    const mensaje = await correrAutomatico();
    const completo = mensaje === null;
    if (completo) {
      if (estado.modo === 'perfil') {
        document.scrollingElement.scrollTo({ top: 0, behavior: 'smooth' });
        ui.mensaje.textContent = 'Listo. Revisa las fechas y los datos del perfil y pulsa «Descargar».';
      } else {
        descargarActual();
        ui.mensaje.textContent = 'Terminado: archivo descargado.';
      }
    } else {
      ui.mensaje.textContent = mensaje;
    }
  }

  // --- Lote de perfiles (solo con la extensión) ------------------------------
  // La extensión abre cada perfil de la muestra en esta pestaña, inyecta el colector y llama a
  // __auditorBots.lote(). Al terminar se le devuelve la ficha para que la descargue y pase al siguiente.

  // Espera a que Facebook arme el perfil (aparece el nombre o el aviso de "no disponible"), máximo 8 s
  async function esperarPerfil() {
    const inicio = Date.now();
    while (Date.now() - inicio < 8000) {
      const principal = document.querySelector(SEL.principal);
      if (principal && (principal.querySelector(SEL.publicacion) || NO_DISPONIBLE.test(principal.innerText || ''))) break;
      if (Date.now() - inicio > 3000 && document.querySelector('h1') && nombreDelPerfil()) break;
      await espera(250, 250);
    }
    await espera(700, 1200);
  }

  // Vistazo: solo lo de arriba del perfil. Baja un poco hasta ver 3 publicaciones con fecha (o 4 pasos)
  async function vistazo() {
    for (let i = 0; i < 8; i++) {
      actualizar();
      if (estado.perfil.publicaciones.filter((p) => !p.borrada).length >= 3) break;
      document.scrollingElement.scrollTop += Math.round(innerHeight * 0.8);
      await dibujado();
      await espera(500, 900);
    }
    actualizar();
    return null;
  }

  function cuentaRegresiva(segundos, info) {
    let resta = Math.round(segundos);
    const pintar = () => {
      ui.mensaje.textContent = info.indice >= info.total
        ? 'Ficha guardada. Era el último perfil del lote.'
        : `Ficha guardada. Siguiente perfil en ${resta} s (va ${info.indice} de ${info.total}). No hace falta tocar nada.`;
    };
    pintar();
    const reloj = setInterval(() => {
      resta = Math.max(0, resta - 1);
      pintar();
      if (!resta) clearInterval(reloj);
    }, 1000);
  }

  async function ejecutarLote(info) {
    anfitrion.style.display = 'block';
    cambiarModo('perfil');
    ui.lineaLote.textContent = `Lote: perfil ${info.indice} de ${info.total}`;
    ui.lineaLote.style.display = 'block';
    await esperarPerfil();
    if (BLOQUEO.test(location.href) || document.querySelector('form[action*="login"]')) {
      chrome.runtime.sendMessage({ tipo: 'bloqueo', url: location.href });
      return;
    }
    if (!esPaginaDePerfil()) {
      chrome.runtime.sendMessage({ tipo: 'perfilListo', error: 'la página abierta no es un perfil' });
      return;
    }
    // Se vuelve a leer desde cero: lo leído mientras la página cargaba puede estar incompleto
    estado.perfil = null;
    ui.filasPerfil.replaceChildren();
    actualizar();
    if (!(estado.perfil && estado.perfil.no_disponible)) {
      const mensaje = info.modo === 'completo' ? await correrAutomatico() : await vistazo();
      if (mensaje !== null) {
        chrome.runtime.sendMessage({ tipo: 'perfilListo', error: mensaje });
        return;
      }
    }
    const { archivo, datos } = datosActuales();
    datos.revision = info.modo === 'completo' ? 'completa' : 'vistazo';
    const respuesta = await chrome.runtime.sendMessage({ tipo: 'perfilListo', archivo, datos });
    if (respuesta && respuesta.segundos) cuentaRegresiva(respuesta.segundos, info);
  }

  // --- Interfaz ---------------------------------------------------------------
  // Va dentro de un Shadow DOM para que los estilos de Facebook no la afecten, y se construye
  // sin innerHTML porque Facebook lo bloquea (Trusted Types).

  function crear(etiqueta, estilo = {}, props = {}, hijos = []) {
    const e = document.createElement(etiqueta);
    Object.assign(e.style, estilo);
    Object.assign(e, props);
    for (const h of hijos) e.append(h);
    return e;
  }

  const C = { fondo: '#1d2129', texto: '#e6e8ee', suave: '#9aa3b5', borde: '#3a4152', acento: '#8fa6ff', campo: '#272c36' };
  const estiloBoton = {
    background: C.campo, color: C.texto, border: `1px solid ${C.borde}`, borderRadius: '6px',
    padding: '5px 9px', cursor: 'pointer', font: 'inherit',
  };
  const estiloCampo = {
    background: C.campo, color: C.texto, border: `1px solid ${C.borde}`, borderRadius: '5px',
    padding: '3px 6px', font: 'inherit', width: '100%', boxSizing: 'border-box',
  };

  const ui = {};
  const anfitrion = crear('div', { position: 'fixed', right: '16px', bottom: '16px', zIndex: '2147483647' });
  const sombra = anfitrion.attachShadow({ mode: 'open' });
  ui.panel = crear('div', {
    width: '330px', maxHeight: '75vh', overflowY: 'auto', background: C.fondo, color: C.texto,
    border: `1px solid ${C.borde}`, borderRadius: '10px', padding: '12px', boxShadow: '0 8px 30px rgba(0,0,0,.4)',
    font: '13px/1.4 system-ui, "Segoe UI", sans-serif',
  });
  sombra.append(ui.panel);

  ui.cerrar = crear('button', { ...estiloBoton, float: 'right', padding: '0 7px' }, { textContent: '×', title: 'Ocultar' });
  ui.panel.append(ui.cerrar, crear('b', { fontSize: '14px' }, { textContent: 'AuditorBots' }));

  ui.modos = crear('div', { display: 'flex', gap: '6px', margin: '10px 0' });
  ui.botonesModo = {};
  const MODOS = [['reacciones', 'Reacciones'], ['comentarios', 'Comentarios'], ['perfil', 'Perfil']];
  if (ES_EXTENSION) MODOS.push(['lote', 'Lote']);
  for (const [modo, texto] of MODOS) {
    const b = crear('button', { ...estiloBoton, flex: '1' }, { textContent: texto });
    b.addEventListener('click', () => cambiarModo(modo));
    ui.botonesModo[modo] = b;
    ui.modos.append(b);
  }
  ui.panel.append(ui.modos);

  ui.campoPublicacion = crear('input', estiloCampo, { value: estado.publicacion, title: 'URL de la publicación auditada' });
  let publicacionEditada = false;
  ui.campoPublicacion.addEventListener('input', () => {
    estado.publicacion = ui.campoPublicacion.value.trim();
    publicacionEditada = true;
  });
  ui.avisoPublicacion = crear('div', { color: '#f0a73a', marginTop: '3px', display: 'none' },
    { textContent: 'Esta URL es la de la página, no la de una publicación. Abre la publicación en su propia página (clic en su fecha) o pega aquí su enlace.' });
  ui.campoLimite = crear('input', { ...estiloCampo, width: '90px' },
    { type: 'number', min: '0', step: '500', value: String(estado.limite), title: '0 = sin límite' });
  ui.campoLimite.addEventListener('input', () => { estado.limite = Math.max(0, parseInt(ui.campoLimite.value, 10) || 0); });
  ui.bloquePublicacion = crear('div', { marginBottom: '8px' }, {}, [
    crear('label', { display: 'block', color: C.suave }, { textContent: 'Publicación' }, [ui.campoPublicacion]),
    ui.avisoPublicacion,
    crear('label', { display: 'block', color: C.suave, marginTop: '6px' },
      { textContent: 'Automático: parar y descargar al llegar a ' }, [ui.campoLimite, ' (0 = todas)']),
  ]);
  ui.panel.append(ui.bloquePublicacion);

  ui.contador = crear('div', { fontSize: '15px', fontWeight: '600' });
  ui.mensaje = crear('div', { color: C.suave, margin: '2px 0 8px' });
  ui.panel.append(ui.contador, ui.mensaje);

  // Campos del modo perfil (el usuario puede corregir lo detectado)
  ui.bloquePerfil = crear('div', { display: 'none' });
  ui.camposPerfil = crear('div', { display: 'grid', gap: '6px', marginBottom: '8px' });
  ui.filasPerfil = crear('div', { display: 'grid', gap: '6px' });
  ui.bloquePerfil.append(ui.camposPerfil,
    crear('div', { color: C.suave, margin: '6px 0 4px' }, { textContent: 'Publicaciones (corrige la fecha si hace falta):' }),
    ui.filasPerfil);
  ui.panel.append(ui.bloquePerfil);

  ui.lineaLote = crear('div', { display: 'none', color: C.acento, fontWeight: '600', marginTop: '6px' });
  ui.panel.append(ui.lineaLote);

  // Modo Lote: se pegan los enlaces de muestra.html y la extensión los recorre uno por uno
  ui.bloqueLote = crear('div', { display: 'none' });
  ui.enlacesLote = crear('textarea', { ...estiloCampo, height: '90px', resize: 'vertical' },
    { placeholder: 'Pega aquí los enlaces de muestra.html (uno por línea)' });
  ui.empezarLote = crear('button', { ...estiloBoton, background: C.acento, color: '#10131a', fontWeight: '600' },
    { textContent: 'Empezar' });
  ui.detenerLote = crear('button', estiloBoton, { textContent: 'Detener lote' });
  ui.ritmoLote = crear('select', { ...estiloCampo, width: 'auto', flex: '1' });
  for (const [valor, texto] of [['vistazo', 'Vistazo rápido: lo de arriba de cada perfil (~15 s)'],
    ['completo', 'Completo: recorre el muro, detecta plantillas (~1-2 min)']]) {
    ui.ritmoLote.append(crear('option', {}, { value: valor, textContent: texto }));
  }
  ui.bloqueLote.append(
    crear('div', { color: C.suave, marginBottom: '6px' }, { textContent:
      'Abre cada perfil en esta pestaña, guarda su ficha y pasa solo al siguiente hasta terminar: no hace ' +
      'falta tocar nada. Deja la pestaña a la vista. Si Facebook pide verificar la cuenta, se detiene.' }),
    ui.enlacesLote,
    crear('div', { display: 'flex', gap: '6px', marginTop: '6px' }, {}, [ui.ritmoLote]),
    crear('div', { display: 'flex', gap: '6px', marginTop: '6px' }, {}, [ui.empezarLote, ui.detenerLote]),
  );
  ui.panel.append(ui.bloqueLote);

  ui.acciones = crear('div', { display: 'flex', gap: '6px', marginTop: '10px' });
  ui.descargar = crear('button', { ...estiloBoton, flex: '1', background: C.acento, color: '#10131a', fontWeight: '600' },
    { textContent: 'Descargar' });
  ui.automatico = crear('button', { ...estiloBoton, flex: '1' },
    { textContent: 'Automático', title: 'Recorre la lista sola, a ritmo de persona, y descarga al terminar' });
  ui.vaciar = crear('button', estiloBoton, { textContent: 'Vaciar' });
  ui.acciones.append(ui.automatico, ui.descargar, ui.vaciar);
  ui.panel.append(ui.acciones);

  function campoTexto(etiqueta, clave) {
    const input = crear('input', estiloCampo, { value: estado.perfil[clave] || '' });
    input.addEventListener('input', () => { estado.perfil[clave] = input.value.trim(); });
    return crear('label', { color: C.suave }, { textContent: etiqueta }, [input]);
  }

  function campoSiNo(etiqueta, clave) {
    const select = crear('select', estiloCampo);
    for (const [valor, texto] of [['', 'No sé'], ['si', 'Sí'], ['no', 'No']]) {
      select.append(crear('option', {}, { value: valor, textContent: texto }));
    }
    const actual = estado.perfil[clave];
    select.value = actual === true ? 'si' : actual === false ? 'no' : '';
    select.addEventListener('change', () => {
      estado.perfil[clave] = select.value === 'si' ? true : select.value === 'no' ? false : null;
    });
    return crear('label', { color: C.suave }, { textContent: etiqueta }, [select]);
  }

  function pintarCamposPerfil() {
    const sinPubs = crear('input', {}, { type: 'checkbox', checked: estado.perfil.sin_publicaciones });
    sinPubs.addEventListener('change', () => { estado.perfil.sin_publicaciones = sinPubs.checked; });
    ui.camposPerfil.replaceChildren(
      campoTexto('Nombre', 'nombre'),
      campoTexto('Se unió (tal como lo muestra Facebook)', 'se_unio'),
      campoSiNo('¿Tiene foto de perfil propia?', 'tiene_foto'),
      campoSiNo('¿Se ve su lista de amigos?', 'amigos_visibles'),
      crear('label', { color: C.suave }, {}, [sinPubs, ' No tiene publicaciones visibles']),
    );
  }

  function agregarFilaPublicacion(pub) {
    const fecha = crear('input', { ...estiloCampo, width: '110px', flex: 'none' }, { value: pub.fecha, placeholder: 'fecha' });
    fecha.addEventListener('input', () => { pub.fecha = fecha.value.trim(); });
    const quitar = crear('button', { ...estiloBoton, padding: '0 7px' }, { textContent: '×', title: 'No es suya / quitar' });
    const resumen = pub.texto || pub.enlaces[0] || '(sin texto)';
    const fila = crear('div', { display: 'flex', gap: '6px', alignItems: 'center' }, {}, [
      fecha,
      crear('span', { flex: '1', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: C.suave },
        { textContent: resumen, title: resumen }),
      quitar,
    ]);
    quitar.addEventListener('click', () => { pub.borrada = true; fila.remove(); actualizar(); });
    ui.filasPerfil.append(fila);
  }

  function cambiarModo(modo) {
    if (automatico) automatico.detener = true;
    estado.modo = modo;
    for (const [m, b] of Object.entries(ui.botonesModo)) {
      b.style.background = m === modo ? C.acento : C.campo;
      b.style.color = m === modo ? '#10131a' : C.texto;
    }
    ui.bloquePublicacion.style.display = modo === 'reacciones' || modo === 'comentarios' ? 'block' : 'none';
    ui.bloquePerfil.style.display = modo === 'perfil' ? 'block' : 'none';
    ui.bloqueLote.style.display = modo === 'lote' ? 'block' : 'none';
    ui.acciones.style.display = modo === 'lote' ? 'none' : 'flex';
    actualizar();
  }

  function actualizar() {
    // Mientras no haya nada recolectado ni editado a mano, la publicación sigue a la URL actual
    if (!publicacionEditada && !estado.reacciones.size && !estado.comentarios.size &&
        estado.publicacion !== location.href) {
      estado.publicacion = location.href;
      ui.campoPublicacion.value = location.href;
    }
    if (estado.modo === 'lote') {
      if (!automatico) mostrarEstadoLote();
      return;
    }
    let mensaje = '';
    try {
      if (estado.modo === 'reacciones') mensaje = escanearReacciones();
      else if (estado.modo === 'comentarios') mensaje = escanearComentarios();
      else mensaje = escanearPerfil();
    } catch (e) {
      mensaje = 'Error al leer la página: ' + e.message;
    }
    if (estado.modo === 'reacciones') ui.contador.textContent = `Recolectando: ${estado.reacciones.size} perfiles`;
    else if (estado.modo === 'comentarios') ui.contador.textContent = `Recolectando: ${estado.comentarios.size} comentarios`;
    else {
      const n = estado.perfil ? estado.perfil.publicaciones.filter((p) => !p.borrada).length : 0;
      ui.contador.textContent = `Perfil: ${n} publicaciones`;
    }
    if (!automatico) ui.mensaje.textContent = mensaje;
    ui.avisoPublicacion.style.display = estado.modo !== 'perfil' && !URL_PUBLICACION.test(estado.publicacion) ? 'block' : 'none';
    if (estado.modo === 'reacciones' && estado.totalMostrado) {
      ui.contador.textContent += ` · Facebook muestra: ${estado.totalMostrado}`;
    }
  }

  // { archivo, datos } de lo recolectado en el modo actual, o null si no hay nada
  function datosActuales() {
    actualizar();
    const base = { fuente: 'AuditorBots', version: 1, tipo: estado.modo, capturado: new Date().toISOString() };
    if (estado.modo === 'perfil') {
      if (!estado.perfil) return null;
      const { vistas, ...perfil } = estado.perfil;
      perfil.publicaciones = perfil.publicaciones.filter((p) => !p.borrada);
      perfil.marcas_tiempo = marcasDeTiempo();
      const textoPrincipal = (document.querySelector(SEL.principal) || document.body).innerText || '';
      if (CERRADO.test(textoPrincipal) && !perfil.publicaciones.length) perfil.sin_publicaciones = true;
      if (!perfil.publicaciones.length) perfil.diagnostico_pagina = diagnosticoPagina();
      if (!perfil.nombre || /^(\(\d+\)\s*)?facebook$/i.test(perfil.nombre)) perfil.nombre = nombreDelPerfil();
      const nombre = (perfil.id || perfil.usuario || 'perfil').replace(/[^\w.\-]/g, '_');
      return { archivo: `perfil-${nombre}.json`, datos: { ...base, ...perfil } };
    }
    const mapa = estado.modo === 'reacciones' ? estado.reacciones : estado.comentarios;
    if (!mapa.size) return null;
    const extra = estado.modo === 'reacciones' ? { total_mostrado: estado.totalMostrado } : {};
    return {
      archivo: `${estado.modo}-${sello()}.json`,
      datos: { ...base, ...extra, publicacion: estado.publicacion, elementos: [...mapa.values()] },
    };
  }

  function descargarActual() {
    const actual = datosActuales();
    if (actual) descargar(actual.archivo, actual.datos);
  }

  let consultandoLote = false;
  async function mostrarEstadoLote() {
    if (consultandoLote) return;
    consultandoLote = true;
    try {
      const lote = await chrome.runtime.sendMessage({ tipo: 'estadoLote' });
      ui.contador.textContent = !lote ? 'Lote: sin empezar'
        : lote.activo ? `Lote: perfil ${lote.indice + 1} de ${lote.urls.length}`
        : 'Lote: terminado';
      ui.mensaje.textContent = lote && lote.mensaje ? lote.mensaje : '';
    } catch (e) {
      ui.mensaje.textContent = 'No se pudo hablar con la extensión: recárgala en chrome://extensions.';
    } finally {
      consultandoLote = false;
    }
  }

  ui.empezarLote.addEventListener('click', async () => {
    const urls = [...new Set((ui.enlacesLote.value.match(/https:\/\/[^\s"'<>]+/g) || [])
      .map((u) => identificar(u)).filter(Boolean).map((p) => p.url))];
    if (!urls.length) {
      ui.mensaje.textContent = 'No encontré enlaces de perfiles de Facebook en el texto pegado.';
      return;
    }
    const respuesta = await chrome.runtime.sendMessage({ tipo: 'iniciarLote', urls, modo: ui.ritmoLote.value });
    ui.mensaje.textContent = respuesta && respuesta.error ? respuesta.error : `Empezando con ${urls.length} perfiles…`;
  });
  ui.detenerLote.addEventListener('click', async () => {
    await chrome.runtime.sendMessage({ tipo: 'detenerLote' });
    if (automatico) automatico.detener = true;
    mostrarEstadoLote();
  });

  ui.descargar.addEventListener('click', descargarActual);
  ui.automatico.addEventListener('click', alternarAutomatico);
  ui.vaciar.addEventListener('click', () => {
    if (automatico) automatico.detener = true;
    vaciar();
    publicacionEditada = false;
    ui.campoPublicacion.value = estado.publicacion;
    actualizar();
  });
  ui.cerrar.addEventListener('click', () => { anfitrion.style.display = 'none'; });

  // Facebook quita de la página las filas que salen de la vista, así que hay que leerlas en
  // cuanto aparecen: se vuelve a leer con cada cambio de la página (agrupando los cambios).
  let pendiente = null;
  const observador = new MutationObserver((cambios) => {
    if (cambios.every((c) => anfitrion.contains(c.target))) return;
    if (pendiente) return;
    pendiente = setTimeout(() => { pendiente = null; actualizar(); }, 250);
  });
  observador.observe(document.body, { childList: true, subtree: true });
  setInterval(actualizar, 2000);

  document.body.append(anfitrion);
  window.__auditorBots = {
    mostrar: () => { anfitrion.style.display = 'block'; actualizar(); },
    lote: ejecutarLote,
  };

  const dialogo = dialogoAbierto();
  cambiarModo(dialogo && dialogo.querySelector(SEL.pestanas) ? 'reacciones' : esPaginaDePerfil() ? 'perfil' : 'comentarios');
})();
