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
      if (msg.type === 'hello') ws.send(JSON.stringify({ type: 'welcome', game: 'hades' }));
      if (msg.type === 'state' && msg.inPlay && !sent) {
        sent = true;
        ws.send(JSON.stringify({ type: 'effect', id: 'e-42', effect: 'slow', ms: 15000, by: 'Ana', safe: true }));
      }
      if (msg.type === 'ack') setTimeout(() => process.exit(0), 200);
    });
  });
});
server.listen(Number(process.env.PORT || 39090), () => console.log('LISTENING'));
setTimeout(() => { console.log('TIMEOUT'); process.exit(1); }, 30000);
