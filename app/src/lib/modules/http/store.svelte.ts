import { api, ApiError } from '$lib/api/client';
import { realtime } from '$lib/api/realtime.svelte';
import type { ModuleEvent } from '$lib/types';
import type { HttpCheckResult, HttpTarget } from './types';

export const HTTP_MODULE_ID = 'http';

/** Ekranda bir adres satiri: ne izlendigi (hedef) + son olculen (varsa). */
export type CheckCard = {
	id: string;
	name: string;
	groupName: string;
	url: string;
	enabled: boolean;
	/** null = izleniyor ama henuz olcum gelmedi. */
	result: HttpCheckResult | null;
};
const BASE = `/api/modules/${HTTP_MODULE_ID}`;

/**
 * Same shape as the MSSQL store — deliberately. A tool's front end is a REST read on open plus
 * a filtered subscription to the shared hub; nothing module-specific about the plumbing.
 */
class HttpStore {
	results = $state<Map<string, HttpCheckResult>>(new Map());
	targets = $state<HttpTarget[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	#unsubscribe: (() => void) | null = null;
	#started = false;

	/**
	 * Ekrandaki liste — MSSQL store'u ile ayni kural: IZLENENLERDEN turer, olcum
	 * onbelleginden degil. Gerekcesi orada uzun uzun yazili; ozeti: silinen kayit
	 * ekranda kalmasin, olculmemis kayit da ekrandan kaybolmasin.
	 */
	get checks(): CheckCard[] {
		return this.targets.map((t) => ({
			id: t.id,
			name: t.name,
			groupName: t.groupName,
			url: t.url,
			enabled: t.enabled,
			result: this.results.get(t.id) ?? null
		}));
	}

	check(id: string): HttpCheckResult | undefined {
		return this.results.get(id);
	}

	target(id: string): HttpTarget | undefined {
		return this.targets.find((t) => t.id === id);
	}

	async start(): Promise<void> {
		if (this.#started) return;
		this.#started = true;

		this.#unsubscribe = realtime.onEvent((event: ModuleEvent) => {
			if (event.moduleId !== HTTP_MODULE_ID || event.event !== 'check') return;

			const result = event.payload as HttpCheckResult;
			const next = new Map(this.results);
			next.set(result.targetId, result);
			this.results = next;
		});

		await realtime.subscribeModule(HTTP_MODULE_ID);
		await this.refresh();
	}

	async stop(): Promise<void> {
		this.#unsubscribe?.();
		this.#unsubscribe = null;
		this.#started = false;
		await realtime.unsubscribeModule(HTTP_MODULE_ID);
	}

	/** Same as the MSSQL store: none of this survives a panel change. */
	reset(): void {
		this.results = new Map();
		this.targets = [];
		this.error = null;
	}

	async refresh(): Promise<void> {
		this.loading = true;
		this.error = null;

		try {
			const [checks, targets] = await Promise.all([
				api<HttpCheckResult[]>(`${BASE}/checks`),
				api<HttpTarget[]>(`${BASE}/targets`)
			]);

			// Hedefte karsiligi olmayan olcum tutulmaz (bkz. MSSQL store'undaki gerekce).
			const bilinen = new Set(targets.map((t) => t.id));
			this.results = new Map(
				checks.filter((c) => bilinen.has(c.targetId)).map((c) => [c.targetId, c])
			);
			this.targets = targets;
		} catch (error) {
			this.error = error instanceof Error ? error.message : String(error);
		} finally {
			this.loading = false;
		}
	}

	async save(request: unknown, id?: string): Promise<HttpTarget> {
		const saved = await api<HttpTarget>(id ? `${BASE}/targets/${id}` : `${BASE}/targets`, {
			method: id ? 'PUT' : 'POST',
			body: JSON.stringify(request)
		});

		await this.refresh();
		return saved;
	}

	/** MSSQL store ile ayni: 404 "zaten silinmis" demektir, karti ekranda birakma. */
	async remove(id: string): Promise<void> {
		try {
			await api<void>(`${BASE}/targets/${id}`, { method: 'DELETE' });
		} catch (e) {
			if (!(e instanceof ApiError) || e.status !== 404) throw e;
		}

		const next = new Map(this.results);
		next.delete(id);
		this.results = next;
		this.targets = this.targets.filter((t) => t.id !== id);
	}

	test(request: unknown): Promise<{ ok: boolean; result: HttpCheckResult }> {
		return api(`${BASE}/targets/test`, { method: 'POST', body: JSON.stringify(request) });
	}
}

export const http = new HttpStore();
