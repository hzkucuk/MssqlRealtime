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
const fetchMock = vi.fn();
vi.stubGlobal('fetch', (...args: unknown[]) => fetchMock(...args));

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
		fetchMock.mockReset();
		fetchMock.mockResolvedValue({ ok: false });
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

/**
 * Ölçüldü 2026-08-23: bir müşteride ters vekil `/hubs` yolunu panele iletmiyordu. `/api/*`
 * çalıştığı için panel açılıyor ve giriş yapılıyordu; yalnız canlı akış gelmiyordu. Tarayıcı
 * bunu `TypeError: Failed to fetch` diye bildirir — ayırt edilemez. Panel ayaktaysa bunu
 * söylemek teşhisin tamamıdır.
 */
describe('bağlantı kurulamadığında panelin ayakta olup olmadığı', () => {
	beforeEach(() => {
		getAccessToken.mockReset();
		start.mockReset();
		fetchMock.mockReset();
		realtime.lastError = null;
		realtime.sessionExpired = false;
	});

	test('panel cevap veriyorsa arızanın arada olduğunu söyler', async () => {
		getAccessToken.mockResolvedValue('token');
		start.mockRejectedValue(new Error('Failed to complete negotiation with the server'));
		fetchMock.mockResolvedValue({ ok: true });

		await realtime.start();

		expect(realtime.lastError).toContain('Failed to complete negotiation');
		expect(realtime.lastError).toContain('/api/health');
		expect(realtime.lastError).toContain('/hubs');
	});

	test('panel de cevap vermiyorsa yönlendirme uydurmaz', async () => {
		getAccessToken.mockResolvedValue('token');
		start.mockRejectedValue(new Error('Failed to fetch'));
		fetchMock.mockRejectedValue(new Error('network'));

		await realtime.start();

		expect(realtime.lastError).toBe('Failed to fetch');
	});
});
