import { api, ApiError } from '$lib/api/client';
import { realtime } from '$lib/api/realtime.svelte';
import type { ModuleEvent, ServerProfile, ServerSnapshot } from '$lib/types';

export const MSSQL_MODULE_ID = 'mssql';

/**
 * Ekranda bir sunucu satiri: ne izlendigi (profil) + son olculen (varsa).
 * Ikisi ayri tutulur, cunku "kayit var mi" ile "olcum var mi" ayri sorulardir ve
 * ikisini tek nesnede karistirmak bu urunde uc ayri hataya yol acti.
 */
export type ServerCard = {
	id: string;
	name: string;
	customerName: string;
	enabled: boolean;
	/** null = izleniyor ama henuz olcum gelmedi (yeni eklendi ya da hic ulasilamadi). */
	snapshot: ServerSnapshot | null;
};
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
	#profilTazelemesi = false;

	/**
	 * Ekrandaki liste.
	 *
	 * IZLENENLERDEN turer, olcum onbelleginden DEGIL. Onceden `snapshots` haritasi
	 * cizdiriliyordu ve bunun uc sonucu vardi (ucu de 2026-08-09'da musteri makinesinde
	 * gorunur oldu):
	 *
	 *   - Silinen sunucunun son olcumu haritada kaldigi icin kart ekranda kaliyordu;
	 *     silmeye basildiginda sunucu 404 donuyor, kart yine duruyordu.
	 *   - Panel degistirildiginde onceki panelin olcumleri yeni panelin adi altinda
	 *     gorunuyordu.
	 *   - En kotusu: eklenmis ama HENUZ OLCULMEMIS bir sunucu ekranda HIC gorunmuyordu.
	 *     Bir izleme urununde "olcum yok" gizlenecek degil, gosterilecek bir durumdur.
	 *
	 * Artik profil listesi tek gercek: kayit yoksa kart yok, olcum yoksa kart var ama
	 * "olcum bekleniyor" der.
	 */
	get servers(): ServerCard[] {
		return this.profiles.map((p) => ({
			id: p.id,
			name: p.name,
			customerName: p.customerName,
			enabled: p.enabled,
			snapshot: this.snapshots.get(p.id) ?? null
		}));
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

	/**
	 * Bilinmeyen bir olcum geldiginde profil listesini bir kez tazeler.
	 * Ust uste cagriya karsi korumali: her tur her sunucu icin bir olcum dusuyor.
	 */
	async #profilleriTazele(): Promise<void> {
		if (this.#profilTazelemesi) return;
		this.#profilTazelemesi = true;
		try {
			this.profiles = await api<ServerProfile[]>(`${BASE}/servers`);
		} catch {
			// Sessiz gecilir: bir sonraki refresh zaten duzeltir, ekrana hata basmaya degmez.
		} finally {
			this.#profilTazelemesi = false;
		}
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

			// Baska bir cihazdan eklenmis bir sunucunun olcumu gelebilir; liste artik
			// profillerden turdugu icin profil listesi tazelenmeden ekranda gorunmez.
			if (!this.profiles.some((p) => p.id === snapshot.serverId)) this.#profilleriTazele();
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

	/** Everything here belongs to one customer's panel; a switch must not carry it over. */
	reset(): void {
		this.snapshots = new Map();
		this.history = new Map();
		this.profiles = [];
		this.error = null;
	}

	async refresh(): Promise<void> {
		this.loading = true;
		this.error = null;

		try {
			const [snapshots, profiles] = await Promise.all([
				api<ServerSnapshot[]>(`${BASE}/snapshots`),
				api<ServerProfile[]>(`${BASE}/servers`)
			]);

			// Profilde karsiligi olmayan olcum tutulmaz: hem bellek sisirir hem de
			// ileride yanlislikla cizilirse hayalet kart olarak geri doner.
			const bilinen = new Set(profiles.map((p) => p.id));
			this.snapshots = new Map(
				snapshots.filter((s) => bilinen.has(s.serverId)).map((s) => [s.serverId, s])
			);
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

	/**
	 * Sunucuyu izlemeden kaldirir.
	 *
	 * 404 BASARI sayilir: istenen son durum ("bu kayit gitsin") zaten saglanmis demektir.
	 * Olculdu 2026-08-09, musteri makinesi: listede sunucuda karsiligi olmayan bir kart
	 * kaldi (baska panelden gelen bayat veri). Silme 404 donunce api() hata firlatiyor,
	 * asagidaki yerel temizlik hic calismiyor ve kart ekranda kaliyordu. Kullanici ayni
	 * karti UC KEZ silmeye calisti, ucunde de 404 aldi ve kart yerinde durdu.
	 */
	async remove(id: string): Promise<void> {
		try {
			await api<void>(`${BASE}/servers/${id}`, { method: 'DELETE' });
		} catch (e) {
			if (!(e instanceof ApiError) || e.status !== 404) throw e;
		}

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
