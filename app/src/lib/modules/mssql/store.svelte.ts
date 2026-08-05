import { api } from '$lib/api/client';
import { realtime } from '$lib/api/realtime.svelte';
import type { ModuleEvent, ServerProfile, ServerSnapshot } from '$lib/types';

export const MSSQL_MODULE_ID = 'mssql';
const BASE = `/api/modules/${MSSQL_MODULE_ID}`;

/**
 * Live state for the MSSQL tool.
 *
 * Two sources feed the same map: a REST read on open (so the screen is never blank while
 * waiting for the first push) and the SignalR stream after that.
 */
/** How many measurements a sparkline shows. ~2 minutes at a 3s interval. */
const HISTORY_LENGTH = 40;

export type MetricHistory = { cpu: (number | null)[]; memory: (number | null)[] };

class MssqlStore {
	snapshots = $state<Map<string, ServerSnapshot>>(new Map());

	/** Recent CPU/memory readings per server, kept in memory only — the sparkline is a
	 *  "what just happened" device, not a report; persisting it would imply otherwise. */
	history = $state<Map<string, MetricHistory>>(new Map());
	profiles = $state<ServerProfile[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	#unsubscribe: (() => void) | null = null;
	#started = false;

	get servers(): ServerSnapshot[] {
		return [...this.snapshots.values()].sort((a, b) => {
			// Anything alarming floats to the top: on a phone the first card is all you see.
			const bySeverity = b.summary.severity - a.summary.severity;
			if (bySeverity !== 0) return bySeverity;

			const byCustomer = a.customerName.localeCompare(b.customerName, 'tr');
			return byCustomer !== 0 ? byCustomer : a.serverName.localeCompare(b.serverName, 'tr');
		});
	}

	snapshot(serverId: string): ServerSnapshot | undefined {
		return this.snapshots.get(serverId);
	}

	metrics(serverId: string): MetricHistory {
		return this.history.get(serverId) ?? { cpu: [], memory: [] };
	}

	#record(snapshot: ServerSnapshot): void {
		const next = new Map(this.history);
		const existing = next.get(snapshot.serverId) ?? { cpu: [], memory: [] };

		next.set(snapshot.serverId, {
			cpu: [...existing.cpu, snapshot.summary.cpuPercent ?? null].slice(-HISTORY_LENGTH),
			memory: [...existing.memory, snapshot.summary.memoryUsedPercent ?? null].slice(-HISTORY_LENGTH)
		});

		this.history = next;
	}

	profile(serverId: string): ServerProfile | undefined {
		return this.profiles.find((p) => p.id === serverId);
	}

	async start(): Promise<void> {
		if (this.#started) return;
		this.#started = true;

		this.#unsubscribe = realtime.onEvent((event: ModuleEvent) => {
			if (event.moduleId !== MSSQL_MODULE_ID || event.event !== 'snapshot') return;

			const snapshot = event.payload as ServerSnapshot;
			const next = new Map(this.snapshots);
			next.set(snapshot.serverId, snapshot);
			this.snapshots = next;
			this.#record(snapshot);
		});

		await realtime.subscribeModule(MSSQL_MODULE_ID);
		await this.refresh();
	}

	async stop(): Promise<void> {
		this.#unsubscribe?.();
		this.#unsubscribe = null;
		this.#started = false;
		await realtime.unsubscribeModule(MSSQL_MODULE_ID);
	}

	async refresh(): Promise<void> {
		this.loading = true;
		this.error = null;

		try {
			const [snapshots, profiles] = await Promise.all([
				api<ServerSnapshot[]>(`${BASE}/snapshots`),
				api<ServerProfile[]>(`${BASE}/servers`)
			]);

			this.snapshots = new Map(snapshots.map((s) => [s.serverId, s]));
			this.profiles = profiles;
		} catch (error) {
			this.error = error instanceof Error ? error.message : String(error);
		} finally {
			this.loading = false;
		}
	}

	async save(request: unknown, id?: string): Promise<ServerProfile> {
		const saved = await api<ServerProfile>(id ? `${BASE}/servers/${id}` : `${BASE}/servers`, {
			method: id ? 'PUT' : 'POST',
			body: JSON.stringify(request)
		});

		await this.refresh();
		return saved;
	}

	async remove(id: string): Promise<void> {
		await api<void>(`${BASE}/servers/${id}`, { method: 'DELETE' });

		const next = new Map(this.snapshots);
		next.delete(id);
		this.snapshots = next;
		this.profiles = this.profiles.filter((p) => p.id !== id);
	}

	test(request: unknown): Promise<{ ok: boolean; snapshot: ServerSnapshot }> {
		return api(`${BASE}/servers/test`, { method: 'POST', body: JSON.stringify(request) });
	}

	kill(serverId: string, sessionId: number): Promise<{ ok: boolean }> {
		return api(`${BASE}/servers/${serverId}/kill`, {
			method: 'POST',
			body: JSON.stringify({ sessionId })
		});
	}
}

export const mssql = new MssqlStore();
