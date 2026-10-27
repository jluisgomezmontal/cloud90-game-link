// Manual live smoke test only. Never uses production credentials or endpoints.
const http = require('node:http');
const { WebSocketServer } = require('ws');
const server = http.createServer();
const wss = new WebSocketServer({ noServer: true });
server.on('upgrade', (request, socket, head) => {
  if (request.url !== '/api/game-link/ws' || request.headers.authorization !== 'Bearer c90gl_test') {
    socket.destroy();
    return;
  }
  wss.handleUpgrade(request, socket, head, (ws) => {
    let sent = false;
    ws.on('message', (data) => {
      const message = JSON.parse(data.toString());
      console.log(JSON.stringify(message));
      if (message.type === 'hello') {
        if (message.game !== 'cuphead' || message.version !== 'cloud90-game-link-1.3.0') return ws.close(4426, 'test build mismatch');
        ws.send(JSON.stringify({ type: 'welcome', game: 'cuphead' }));
      }
      if (message.type === 'state' && message.inPlay && !sent) {
        sent = true;
        ws.send(JSON.stringify({ type: 'effect', id: 'cuphead-live-1', effect: 'slow', ms: 1000, by: 'Prueba local', safe: true, arg: '', epoch:message.epoch }));
      }
      if (message.type === 'ack' && message.id === 'cuphead-live-1') {
        setTimeout(() => process.exit(message.ok ? 0 : 1), 6000);
      }
    });
    ws.on('error', (error) => console.error(error.message));
  });
});
server.listen(Number(process.env.PORT || 39090), '127.0.0.1', () => console.log('LISTENING'));
setTimeout(() => process.exit(1), 10 * 60 * 1000);
