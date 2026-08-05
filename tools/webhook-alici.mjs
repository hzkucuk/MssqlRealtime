// Minimal webhook receiver for verifying out-of-app alert delivery.
// Run it, point the webhook channel at http://localhost:9099/, and watch alerts arrive with
// no client connected anywhere.
import { createServer } from 'node:http';

const port = Number(process.argv[2] ?? 9099);

createServer((req, res) => {
	let body = '';
	req.on('data', (chunk) => (body += chunk));
	req.on('end', () => {
		const stamp = new Date().toLocaleTimeString('tr-TR');
		const signature = req.headers['x-signature'];

		console.log(`\n[${stamp}] ${req.method} ${req.url}${signature ? `  imza=${String(signature).slice(0, 16)}…` : ''}`);

		try {
			console.log(JSON.stringify(JSON.parse(body), null, 2));
		} catch {
			console.log(body);
		}

		res.writeHead(200, { 'Content-Type': 'application/json' });
		res.end('{"ok":true}');
	});
}).listen(port, () => console.log(`webhook alicisi dinliyor: http://localhost:${port}/`));
