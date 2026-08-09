import { beforeEach, describe, expect, test, vi } from 'vitest';
import type { HttpCheckResult, HttpTarget } from './types';

/**
 * MSSQL store'undaki kuralın site/API modülündeki karşılığı.
 *
 * Ayrı dosya olmasının sebebi: aynı hata sınıfı iki modülde de vardı ve biri düzeltilip
 * öteki unutulabilir. Test iki yerde birden duruyorsa unutulmuyor.
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

vi.mock('$lib/api/realtime.svelte', () => ({
	realtime: {
		onEvent: () => () => {},
		subscribeModule: async () => {},
		unsubscribeModule: async () => {}
	}
}));

const { http } = await import('./store.svelte');
const { ApiError } = await import('$lib/api/client');

function hedef(id: string, ad: string, enabled = true): HttpTarget {
	return {
		id,
		name: ad,
		groupName: 'Müşteri siteleri',
		url: 'https://ornek.invalid',
		method: 'GET',
		expectedStatusCode: 200,
		enabled,
		checkIntervalSeconds: 60,
		timeoutSeconds: 10
	} as HttpTarget;
}

function sonuc(targetId: string): HttpCheckResult {
	return {
		targetId,
		targetName: 'ölçümdeki ad',
		groupName: 'ölçümdeki grup',
		url: 'https://ornek.invalid',
		checkedAt: '2026-08-10T00:00:00Z',
		status: 0,
		severity: 0,
		responseTimeMs: 120,
		statusCode: 200,
		uptimePercent: 100,
		activeAlerts: []
	} as unknown as HttpCheckResult;
}

function yanitla(checks: HttpCheckResult[], targets: HttpTarget[]) {
	api.mockReset();
	api.mockImplementation((path: string) => {
		if (path.endsWith('/checks')) return Promise.resolve(checks);
		if (path.endsWith('/targets')) return Promise.resolve(targets);
		return Promise.resolve(undefined);
	});
}

beforeEach(() => {
	http.reset();
	api.mockReset();
});

describe('liste izlenenlerden türer', () => {
	test('kaydı olan ama hiç ölçülmemiş adres EKRANDA GÖRÜNÜR', async () => {
		yanitla([], [hedef('a', 'Kapalı Adres', false)]);
		await http.refresh();

		expect(http.checks).toHaveLength(1);
		expect(http.checks[0].name).toBe('Kapalı Adres');
		expect(http.checks[0].result).toBeNull();
		expect(http.checks[0].enabled).toBe(false);
	});

	test('hedefi olmayan ölçüm kart üretmez', async () => {
		yanitla([sonuc('silinmis')], []);
		await http.refresh();

		expect(http.checks).toHaveLength(0);
	});

	test('ad ve grup HEDEFTEN okunur, ölçümden değil', async () => {
		yanitla([sonuc('a')], [hedef('a', 'Yeni Ad')]);
		await http.refresh();

		expect(http.checks[0].name).toBe('Yeni Ad');
		expect(http.checks[0].groupName).toBe('Müşteri siteleri');
	});
});

describe('silme', () => {
	test('404 BAŞARI sayılır ve kart ekrandan kalkar', async () => {
		yanitla([sonuc('a')], [hedef('a', 'Gidecek')]);
		await http.refresh();
		expect(http.checks).toHaveLength(1);

		api.mockRejectedValueOnce(new ApiError('yok', 404));
		await expect(http.remove('a')).resolves.toBeUndefined();

		expect(http.checks).toHaveLength(0);
	});

	test('404 dışındaki hata yutulmaz', async () => {
		yanitla([sonuc('a')], [hedef('a', 'Duracak')]);
		await http.refresh();

		api.mockRejectedValueOnce(new ApiError('sunucu hatası', 500));
		await expect(http.remove('a')).rejects.toThrow();
		expect(http.checks).toHaveLength(1);
	});
});
