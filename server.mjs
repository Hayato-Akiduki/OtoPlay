import http from 'node:http';
import { readFile } from 'node:fs/promises';

const routes = new Map([
  ['/', ['index.html', 'text/html; charset=utf-8']],
  ['/app.js', ['app.js', 'text/javascript; charset=utf-8']],
  ['/style.css', ['style.css', 'text/css; charset=utf-8']],
]);
const server = http.createServer(async (req, res) => {
  const route = routes.get(new URL(req.url, 'http://localhost').pathname);
  if (!route) { res.writeHead(404).end('Not found'); return; }
  try {
    const body = await readFile(new URL(`./public/${route[0]}`, import.meta.url));
    res.writeHead(200, { 'Content-Type': route[1], 'Cache-Control': 'no-store' });
    res.end(body);
  } catch {
    res.writeHead(500).end('Unable to load application');
  }
});
server.on('error', error => { console.error(error.message); process.exitCode = 1; });
server.listen(3210, '127.0.0.1', () => console.log('音鳴らしますねぇ！ http://127.0.0.1:3210  (終了: Ctrl+C)'));
