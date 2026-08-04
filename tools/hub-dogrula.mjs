// End-to-end check: sign in, open the hub, subscribe to the mssql module, and report what
// actually arrives on the wire — snapshots and alerts.
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const BASE = 'http://localhost:5199';

const loginResponse = await fetch(`${BASE}/api/auth/login`, {
	method: 'POST',
	headers: { 'Content-Type': 'application/json' },
	body: JSON.stringify({ email: 'admin@local', password: 'Test1234567!' })
});

if (!loginResponse.ok) {
	console.error('login basarisiz', loginResponse.status);
	process.exit(1);
}

const { accessToken } = await loginResponse.json();
console.log('giris  : ok');

const connection = new HubConnectionBuilder()
	.withUrl(`${BASE}/hubs/tools`, { accessTokenFactory: () => accessToken })
	.configureLogging(LogLevel.Error)
	.build();

let snapshots = 0;
let alerts = 0;

connection.on('moduleEvent', (event) => {
	snapshots++;
	if (snapshots <= 2) {
		const p = event.payload;
		console.log(
			`snapshot #${snapshots}: modul=${event.moduleId} hedef=${event.targetId} ` +
				`sunucu="${p.serverName}" durum=${p.status} cpu=${p.summary.cpuPercent} ` +
				`ram=${p.summary.memoryUsedPercent} oturum=${p.summary.totalSessions} ` +
				`alarm=${p.activeAlerts.length}`
		);
	}
});

connection.on('alert', (notification) => {
	alerts++;
	console.log(
		`ALARM: ${notification.title} | ${notification.body} | temizlendi=${notification.isCleared}`
	);
});

await connection.start();
console.log('hub    : bagli');

await connection.invoke('SubscribeModule', 'mssql');
console.log('abone  : mssql');

await new Promise((resolve) => setTimeout(resolve, 14000));

console.log(`\nsonuc  : ${snapshots} snapshot, ${alerts} alarm bildirimi alindi`);
await connection.stop();
process.exit(snapshots > 0 ? 0 : 2);
