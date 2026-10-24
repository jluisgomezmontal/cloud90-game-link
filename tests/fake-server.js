// A stand-in for Cloud90's /api/game-link/ws (tests/link-test.ps1): checks
// the token and the hello, sends welcome and an effect once the game says
// it's in play, and prints what the Link sends. Exits after the ack.
const http = require('http');
const { WebSocketServer } = require(process.env.WS_MODULE || 'ws');

const server = http.createServer();
const wss = new WebSocketServer({ noServer: true });
server.on('upgrade', (req, socket, head) => {
  if (req.url !== '/api/game-link/ws' || req.headers.authorization !== 'Bearer c90gl_test') {
    console.log('REFUSED ' + req.url + ' ' + req.headers.authorization);
    socket.destroy();
    return;
  }
  wss.handleUpgrade(req, socket, head, (ws) => {
    let sent = false;
    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      console.log('GOT ' + JSON.stringify(msg));
      if (msg.type === 'hello') ws.send(JSON.stringify({ type: 'welcome', game: msg.game }));
      if (msg.type === 'hello' && process.env.FAKE_MODE === 'oversize') {
        const oversized = JSON.stringify({ type: 'effect', by: 'x'.repeat(70 * 1024) });
        ws.send(oversized.slice(0, 32 * 1024), { fin: false });
        ws.send(oversized.slice(32 * 1024), { fin: true });
      }
      if (msg.type === 'state' && msg.inPlay && !sent) {
        sent = true;
        const payload = Buffer.from(JSON.stringify({ type: 'effect', id: 'e-42', effect: process.env.FAKE_EFFECT || 'slow', ms: 15000, by: 'Ana \u00f1\u{1F600}', safe: true, arg: 'HeavyMelee' }));
        const split = payload.indexOf(0xc3) + 1;
        ws.send(payload.subarray(0, split), { binary: false, fin: false });
        ws.send(payload.subarray(split), { binary: false, fin: true });
      }
      if (msg.type === 'ack') setTimeout(() => process.exit(0), 200);
    });
    ws.on('error', () => {});
    ws.on('close', () => { if (process.env.FAKE_MODE === 'oversize') setTimeout(() => process.exit(0), 100); });
  });
});
server.listen(Number(process.env.PORT || 39090), () => console.log('LISTENING'));
setTimeout(() => { console.log('TIMEOUT'); process.exit(1); }, 30000);
