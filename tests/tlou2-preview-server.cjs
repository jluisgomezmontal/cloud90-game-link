const http = require('node:http');
const { randomUUID } = require('node:crypto');
const { WebSocketServer } = require(process.env.WS_MODULE || 'ws');

const port = Number(process.env.CLOUD90_TEST_PORT || 39182);
const origin = 'http://127.0.0.1:' + port;
const token = process.env.CLOUD90_TEST_TOKEN;
if (!token) throw new Error('Falta el token local de prueba.');

let client, state, pending, scheduled, lastOptions = [];
let usesReadyCapability = false;
let nextSpawnAt = 0;
let readySince = 0;
let immediatePreview = false;
let message = 'Esperando Game Link.';
const sockets = new WebSocketServer({ noServer: true, maxPayload: 65536 });
const send = (payload) => client.send(JSON.stringify(payload));
const options = (snapshot = state) => {
  if (!snapshot?.inPlay || snapshot.paused || !snapshot.epoch) return [];
  const available = snapshot.capabilities?.includes('spawn_runner') ? ['legacy_runner'] : [];
  if (snapshot.capabilities?.includes('enemy_ambush') && Array.isArray(snapshot.ambushOptions))
    available.push(...snapshot.ambushOptions.filter((id) => /^[a-z0-9_]{1,40}$/.test(id)));
  return available;
};

function dispatchScheduled() {
  const now = Date.now();
  if (!scheduled || !options().includes(scheduled.enemy)) return;
  if (usesReadyCapability && scheduled.enemy !== 'legacy_runner' &&
      !state.capabilities.includes('enemy_ambush_ready')) return;
  if (!immediatePreview && (now < Math.max(scheduled.earliest, nextSpawnAt) ||
      !readySince || now - readySince < 1000)) return;
  const { enemy, timer } = scheduled;
  clearTimeout(timer);
  scheduled = undefined;
  const id = randomUUID();
  pending = id;
  nextSpawnAt = immediatePreview ? 0 : now + 31000;
  message = `Esperando confirmación del juego para ${enemy}.`;
  send({ type: 'effect', id, ms: 1000, epoch: state.epoch, safe: false, test: true, by: 'Cloud90',
    ...(enemy === 'legacy_runner' ? { effect: 'spawn_runner' } : { effect: 'enemy_ambush', arg: enemy }) });
  setTimeout(() => {
    if (pending !== id) return;
    pending = undefined;
    message = 'El juego no confirmó la aparición.';
  }, 11000).unref();
}
setInterval(dispatchScheduled, 250).unref();

const page = `<!doctype html><html lang="es"><meta charset="utf-8">
<title>Prueba local de TLOU2</title><main>
<h1>Emboscada</h1><p>Prueba local de Game Link para The Last of Us Part II.</p>
<label for="enemy">Enemigo</label><select id="enemy" disabled><option value="">Esperando al juego</option></select>
<button id="test" disabled>Programar prueba</button><p id="status" role="status">Esperando Game Link.</p>
</main><script>
const button = document.querySelector('#test');
const status = document.querySelector('#status');
const enemy = document.querySelector('#enemy');
async function refresh() {
  try {
    const state = await fetch('/status').then(r => r.json());
    if (Array.from(enemy.options, option => option.value).join(',') !== state.options.join(',')) {
      const selected = enemy.value;
      enemy.replaceChildren(...(state.options.length
        ? state.options.map(option => new Option(option, option))
        : [new Option('Esperando al juego', '')]));
      if (state.options.includes(selected)) enemy.value = selected;
    }
    enemy.disabled = !state.options.length;
    button.disabled = !state.ready;
    status.textContent = state.message;
  } catch {
    button.disabled = true;
    status.textContent = 'La página perdió la conexión con la prueba local.';
  }
}
button.onclick = async () => {
  button.disabled = true;
  await fetch('/test?enemy=' + encodeURIComponent(enemy.value), { method: 'POST' });
  await refresh();
};
setInterval(refresh, 500);
refresh();
</script></html>`;

const server = http.createServer((req, res) => {
  if (req.headers.host !== '127.0.0.1:' + port) { res.writeHead(403).end(); return; }
  const url = new URL(req.url, origin);
  if (req.method === 'GET' && url.pathname === '/') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }).end(page);
  } else if (req.method === 'GET' && url.pathname === '/status') {
    res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' })
    .end(JSON.stringify({
      ready: lastOptions.length > 0 && !pending && !scheduled,
      options: lastOptions,
      message: scheduled && !immediatePreview && Date.now() < nextSpawnAt
        ? `Prueba programada para ${scheduled.enemy}. Vuelve al juego. Quedan ${Math.ceil((nextSpawnAt - Date.now()) / 1000)} s entre invocaciones.`
        : message,
    }));
  } else if (req.method === 'POST' && url.pathname === '/test') {
    if (req.headers.origin !== origin) { res.writeHead(403).end(); return; }
    const enemy = url.searchParams.get('enemy');
    if (!lastOptions.includes(enemy) || pending || scheduled) { res.writeHead(409).end(); return; }
    const request = { enemy, earliest: Date.now() + 5000, timer: undefined };
    request.timer = setTimeout(() => {
      if (scheduled !== request) return;
      scheduled = undefined;
      message = 'La prueba venció. Vuelve a programarla y reanuda la campaña.';
    }, 45000).unref();
    scheduled = request;
    message = `Prueba programada para ${enemy}. Vuelve al juego y reanuda la partida.`;
    dispatchScheduled();
    res.writeHead(202).end();
  } else {
    res.writeHead(404).end();
  }
});

server.on('upgrade', (req, socket, head) => {
  if (req.url !== '/api/game-link/ws' || req.headers.authorization !== 'Bearer ' + token || client) {
    socket.destroy();
    return;
  }
  sockets.handleUpgrade(req, socket, head, (ws) => {
    client = ws;
    let greeted = false;
    ws.on('message', (data) => {
      let incoming;
      try { incoming = JSON.parse(data.toString()); }
      catch { ws.close(1007); return; }
      if (!greeted) {
        if (incoming?.type !== 'hello' || incoming.game !== 'tlou2' ||
            !['cloud90-game-link-tlou2-1', 'cloud90-game-link-tlou2-2', 'cloud90-game-link-tlou2-3'].includes(incoming.version)) {
          ws.close(1008);
          return;
        }
        greeted = true;
        usesReadyCapability = incoming.version === 'cloud90-game-link-tlou2-3';
        immediatePreview = incoming.immediatePreview === true;
        if (!scheduled) message = 'Carga una campaña y conecta el juego.';
        send({ type: 'welcome', game: 'tlou2', noDeath: false, allowDestructive: false });
      } else if (incoming?.type === 'state') {
        const sameEpoch = state?.epoch === incoming.epoch;
        state = incoming;
        const available = options();
        if (available.length) {
          readySince = sameEpoch && readySince ? readySince : Date.now();
          lastOptions = [...new Set([...lastOptions, ...available])];
        } else {
          readySince = 0;
        }
        if (available.length && message === 'Carga una campaña y conecta el juego.') {
          message = 'Elige un enemigo, programa la prueba y vuelve al juego.';
        }
        dispatchScheduled();
      } else if (incoming?.type === 'ack' && pending && incoming.id === pending) {
        pending = undefined;
        message = incoming.ok === true ? 'Aparición confirmada por el juego.' : (incoming.reason || 'El juego rechazó la orden.');
      }
    });
    ws.on('error', () => ws.terminate());
    ws.on('close', () => {
      readySince = 0;
      immediatePreview = false;
      client = state = pending = undefined;
      message = scheduled
        ? 'Prueba programada. Esperando que Game Link se reconecte.'
        : 'Game Link desconectado. El selector conserva los enemigos detectados.';
    });
  });
});
server.listen(port, '127.0.0.1', () => console.log(origin));
