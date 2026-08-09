import { beforeEach, describe, expect, test, vi } from 'vitest';
import type { ServerProfile, ServerSnapshot } from '$lib/types';

/**
 * Ekrandaki listenin neye dayandığının testi.
 *
 * 2026-08-09/10 gecesinde üç hata ayrı ayrı yamandı ve üçü de aynı kökten çıktı: liste
 * izlenenlerden değil, gelen ölçümlerin önbelleğinden çiziliyordu. Buradaki testler o kökü
 * sabitler — HTTP'yi ya da SignalR'ı değil, KARARI ölçerler: ekranda ne olmalı.
 */

const api = vi.fn();

vi.mock('$lib/api/client', () => ({
	api: (...args: unknown[]) => api(...args),
	ApiError: class ApiError extends Error {
		constructor(
			message: string,
			readonly status: number
		) {
			super(message);
		}
	}
}));

// Store açılışta gerçek zamanlı akışa bağlanır; testin ona ihtiyacı yok.
vi.mock('$lib/api/realtime.svelte', () => ({
	realtime: {
		onEvent: () => () => {},
		subscribeModule: async () => {},
		unsubscribeModule: async () => {}
	}
}));

const { mssql } = await import('./store.svelte');
const { ApiError } = await import('$lib/api/client');

function profil(id: string, ad: string, enabled = true): ServerProfile {
	return {
		id,
		name: ad,
		customerName: 'Marmara',
		host: 'srv',
		port: 1433,
		initialCatalog: 'master',
		authMode: 0,
		hasPassword: true,
		encryptConnection: true,
		trustServerCertificate: true,
		connectTimeoutSeconds: 5,
		commandTimeoutSeconds: 15,
		enabled,
		pollIntervalSeconds: 5,
		thresholds: {
			consecutiveBreaches: 3,
			renotifyMinutes: 15,
			alertOnOffline: true
		},
		updatedAt: '2026-08-10T00:00:00Z'
	} as ServerProfile;
}

function olcum(serverId: string): ServerSnapshot {
	return {
		serverId,
		serverName: 'ölçümdeki ad',
		customerName: 'ölçümdeki müşteri',
		capturedAt: '2026-08-10T00:00:00Z',
		status: 1,
		collectionMs: 10,
		summary: {
			totalSessions: 3,
			userSessions: 2,
			activeRequests: 1,
			blockedSessions: 0,
			blockingHeads: 0,
			longestRunningSeconds: 0,
			openTransactions: 0,
			distinctApplications: 1,
			distinctHosts: 1,
			severity: 0
		},
		sessions: [],
		requests: [],
		blocking: [],
		topWaits: [],
		databases: [],
		services: [],
		activeAlerts: []
	} as unknown as ServerSnapshot;
}

/** refresh() iki ucu birden çağırır: önce anlık görüntüler, sonra profiller. */
function yanitla(snapshots: ServerSnapshot[], profiles: ServerProfile[]) {
	api.mockReset();
	api.mockImplementation((path: string) => {
		if (path.endsWith('/snapshots')) return Promise.resolve(snapshots);
		if (path.endsWith('/servers')) return Promise.resolve(profiles);
		return Promise.resolve(undefined);
	});
}

beforeEach(() => {
	mssql.reset();
	api.mockReset();
});

describe('liste izlenenlerden türer, ölçüm önbelleğinden değil', () => {
	test('kaydı olan ama hiç ölçülmemiş sunucu EKRANDA GÖRÜNÜR', async () => {
		// Eski davranışta bu sunucu ekranda hiç yoktu: ölçümü olmadığı için önbellekte
		// yeri yoktu. Kapalı bir sunucuyu görüp yeniden açmak bile mümkün değildi.
		yanitla([], [profil('a', 'Kapalı Sunucu', false)]);
		await mssql.refresh();

		expect(mssql.servers).toHaveLength(1);
		expect(mssql.servers[0].name).toBe('Kapalı Sunucu');
		expect(mssql.servers[0].snapshot).toBeNull();
		expect(mssql.servers[0].enabled).toBe(false);
	});

	test('profili olmayan ölçüm kart üretmez', async () => {
		// Silinmiş bir sunucunun son ölçümü ekranda hayalet kart olarak kalıyordu.
		yanitla([olcum('silinmis')], []);
		await mssql.refresh();

		expect(mssql.servers).toHaveLength(0);
	});

	test('tazeleme profilde karşılığı olmayan ölçümleri budar', async () => {
		yanitla([olcum('a'), olcum('hayalet')], [profil('a', 'Duran')]);
		await mssql.refresh();

		expect(mssql.servers).toHaveLength(1);
		expect(mssql.snapshot('hayalet')).toBeUndefined();
	});

	test('ad ve müşteri PROFİLDEN okunur, ölçümden değil', async () => {
		// Ölçüm eskimiş olabilir; sunucunun adı değiştiğinde ekran profili göstermeli.
		yanitla([olcum('a')], [profil('a', 'Yeni Ad')]);
		await mssql.refresh();

		expect(mssql.servers[0].name).toBe('Yeni Ad');
		expect(mssql.servers[0].customerName).toBe('Marmara');
		expect(mssql.servers[0].snapshot).not.toBeNull();
	});
});

describe('silme', () => {
	test('404 BAŞARI sayılır ve kart ekrandan kalkar', async () => {
		// Ölçüldü 2026-08-09, müşteri makinesi: sunucuda karşılığı olmayan bir kart
		// silinemiyordu. 404 gelince istemci hata fırlatıyor, yerel temizlik hiç
		// çalışmıyordu; kullanıcı aynı kartı üç kez sildi, kart yerinde kaldı.
		yanitla([olcum('a')], [profil('a', 'Gidecek')]);
		await mssql.refresh();
		expect(mssql.servers).toHaveLength(1);

		api.mockRejectedValueOnce(new ApiError('yok', 404));
		await expect(mssql.remove('a')).resolves.toBeUndefined();

		expect(mssql.servers).toHaveLength(0);
		expect(mssql.snapshot('a')).toBeUndefined();
	});

	test('404 dışındaki hata yutulmaz', async () => {
		yanitla([olcum('a')], [profil('a', 'Duracak')]);
		await mssql.refresh();

		api.mockRejectedValueOnce(new ApiError('sunucu hatası', 500));
		await expect(mssql.remove('a')).rejects.toThrow();

		// Silinmediyse ekranda kalmalı: kullanıcıya "gitti" diye yalan söylenmez.
		expect(mssql.servers).toHaveLength(1);
	});
});

describe('panel değişimi', () => {
	test('reset her şeyi bırakır', async () => {
		yanitla([olcum('a')], [profil('a', 'Eski panel')]);
		await mssql.refresh();
		expect(mssql.servers).toHaveLength(1);

		mssql.reset();

		expect(mssql.servers).toHaveLength(0);
		expect(mssql.snapshot('a')).toBeUndefined();
		expect(mssql.error).toBeNull();
	});
});
