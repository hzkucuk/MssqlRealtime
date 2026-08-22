import { beforeEach, describe, expect, test, vi } from 'vitest';

/**
 * Canlı bağlantının "neden bağlanamadım" kararının testi.
 *
 * Ölçüldü 2026-08-22, telefonda iki panel: birinde "canlı", diğerinde sonsuza kadar
 * "bağlı değil". Sebep uygulamanın elindeydi ve hiç gösterilmiyordu — süresi dolmuş bir
 * oturumda `getAccessToken()` null döner, token fabrikası bunu boş dizeye çevirir ve hub
 * her denemede 401 verir. Uygulama 30 saniyede bir aynı şeyi tekrarlıyordu.
 *
 * Burada ölçülen şey ağ değil, KARAR: token yokken bağlanmayı denememek, sebebi söylemek.
 */

const getAccessToken = vi.fn();
const start = vi.fn();

vi.mock('./client', () => ({
	getAccessToken: () => getAccessToken(),
	getServerUrl: () => 'https://panel.example'
}));

vi.mock('$lib/notify', () => ({ notify: () => {} }));

// Gerçek bir soket açılmasın: bağlantı kurulmaya ÇALIŞILDI mı, testin sorduğu bu.
vi.mock('@microsoft/signalr', () => {
	class HubConnectionBuilder {
		withUrl() {
			return this;
		}
		withAutomaticReconnect() {
			return this;
		}
		configureLogging() {
			return this;
		}
		build() {
			return {
				start,
				stop: async () => {},
				state: 'Disconnected',
				on: () => {},
				onreconnecting: () => {},
				onreconnected: () => {},
				onclose: () => {},
				invoke: async () => {}
			};
		}
	}

	return {
		HubConnection: class {},
		HubConnectionBuilder,
		HubConnectionState: { Disconnected: 'Disconnected', Connected: 'Connected' },
		LogLevel: { Warning: 3 }
	};
});

const { realtime } = await import('./realtime.svelte');

describe('süresi dolmuş oturum', () => {
	beforeEach(() => {
		getAccessToken.mockReset();
		start.mockReset();
		realtime.sessionExpired = false;
		realtime.lastError = null;
		realtime.attempts = 0;
	});

	test('token yokken BAĞLANMAYI DENEMEZ', async () => {
		getAccessToken.mockResolvedValue(null);

		await realtime.start();

		expect(start).not.toHaveBeenCalled();
	});

	test('token yokken sebebi söyler, "bağlı değil" ile yetinmez', async () => {
		getAccessToken.mockResolvedValue(null);

		await realtime.start();

		expect(realtime.sessionExpired).toBe(true);
		expect(realtime.lastError).toBeTruthy();
		expect(realtime.state).toBe('disconnected');
	});

	test('token yokken yeniden deneme sayacını şişirmez — denenecek bir şey yok', async () => {
		getAccessToken.mockResolvedValue(null);

		await realtime.start();
		await realtime.start();

		expect(realtime.attempts).toBe(0);
	});

	test('token varken bağlanmayı dener ve "oturum bitti" demez', async () => {
		getAccessToken.mockResolvedValue('token');
		start.mockResolvedValue(undefined);

		await realtime.start();

		expect(start).toHaveBeenCalled();
		expect(realtime.sessionExpired).toBe(false);

		await realtime.stop();
	});
});
