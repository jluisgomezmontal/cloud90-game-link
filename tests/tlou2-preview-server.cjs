const http = require('node:http');
const { randomUUID } = require('node:crypto');
const { WebSocketServer } = require(process.env.WS_MODULE || 'ws');

const port = Number(process.env.CLOUD90_TEST_PORT || 39182);
const origin = 'http://127.0.0.1:' + port;
const token = process.env.CLOUD90_TEST_TOKEN;
if (!token) throw new Error('Falta el token local de prueba.');

let client, state, pending, message = 'Esperando Game Link.';
const sockets = new WebSocketServer({ noServer: true, maxPayload: 65536 });
const send = (payload) => client.send(JSON.stringify(payload));
const ready = (snapshot = state) => Boolean(snapshot?.inPlay && !snapshot.paused &&
  Array.isArray(snapshot.capabilities) && snapshot.capabilities.includes('spawn_runner') && snapshot.epoch);

const page = `<!doctype html><html lang="es"><meta charset="utf-8">
<title>Prueba local de TLOU2</title><main>
<h1>Refuerzo infectado</h1><p>Prueba local de Game Link para The Last of Us Part II.</p>
<button id="test" disabled>Probar</button><p id="status" role="status">Esperando Game Link.</p>
</main><script>
const button = document.querySelector('#test');
const status = document.querySelector('#status');
async function refresh() {
  const state = await fetch('/status').then(r => r.json());
  button.disabled = !state.ready;
  status.textContent = state.message;
}
button.onclick = async () => {
  button.disabled = true;
  await fetch('/test', { method: 'POST' });
  await refresh();
};
setInterval(refresh, 500);
refresh();
</script></html>`;

const server = http.createServer((req, res) => {
  if (req.headers.host !== '127.0.0.1:' + port) { res.writeHead(403).end(); return; }
  if (req.method === 'GET' && req.url === '/') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }).end(page);
  } else if (req.method === 'GET' && req.url === '/status') {
    res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' })
      .end(JSON.stringify({ ready: Boolean(ready() && !pending), message }));
  } else if (req.method === 'POST' && req.url === '/test') {
    if (req.headers.origin !== origin) { res.writeHead(403).end(); return; }
    if (!ready() || pending) { res.writeHead(409).end(); return; }
    const id = randomUUID();
    pending = id;
    message = 'Esperando confirmación del juego.';
    send({ type: 'effect', id, effect: 'spawn_runner', ms: 1000, epoch: state.epoch, safe: false, test: true, by: 'Cloud90' });
    setTimeout(() => {
      if (pending !== id) return;
      pending = undefined;
      message = 'El juego no confirmó la aparición.';
    }, 11000).unref();
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
            incoming.version !== 'cloud90-game-link-tlou2-1') {
          ws.close(1008);
          return;
        }
        greeted = true;
        send({ type: 'welcome', game: 'tlou2', noDeath: false, allowDestructive: false });
      } else if (incoming?.type === 'state') {
        if (!pending && (!state || ready() !== ready(incoming))) {
          message = incoming.inPlay ? 'Game Link conectado.' : 'Carga una campaña y conecta el juego.';
        }
        state = incoming;
      } else if (incoming?.type === 'ack' && pending && incoming.id === pending) {
        pending = undefined;
        message = incoming.ok === true ? 'Aparición confirmada por el juego.' : (incoming.reason || 'El juego rechazó la orden.');
      }
    });
    ws.on('error', () => ws.terminate());
    ws.on('close', () => {
      client = state = pending = undefined;
      message = 'Game Link desconectado.';
    });
  });
});
server.listen(port, '127.0.0.1', () => console.log(origin));
