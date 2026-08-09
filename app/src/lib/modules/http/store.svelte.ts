import { api } from '$lib/api/client';
import { realtime } from '$lib/api/realtime.svelte';
import type { ModuleEvent } from '$lib/types';
import type { HttpCheckResult, HttpTarget } from './types';

export const HTTP_MODULE_ID = 'http';
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

	get checks(): HttpCheckResult[] {
		return [...this.results.values()].sort((a, b) => {
			const bySeverity = b.severity - a.severity;
			if (bySeverity !== 0) return bySeverity;

			const byGroup = (a.groupName ?? '').localeCompare(b.groupName ?? '', 'tr');
			return byGroup !== 0 ? byGroup : a.targetName.localeCompare(b.targetName, 'tr');
		});
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

			this.results = new Map(checks.map((c) => [c.targetId, c]));
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

	async remove(id: string): Promise<void> {
		await api<void>(`${BASE}/targets/${id}`, { method: 'DELETE' });

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
