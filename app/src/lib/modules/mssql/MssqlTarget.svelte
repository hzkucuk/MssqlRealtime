<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { realtime } from '$lib/api/realtime.svelte';
	import { mssql, MSSQL_MODULE_ID } from './store.svelte';
	import { Sorter } from '$lib/sort.svelte';
	import { TableColumns } from '$lib/table.svelte';
	import SortHeader from '$lib/components/SortHeader.svelte';
	import ColumnPicker from '$lib/components/ColumnPicker.svelte';
	import type { DatabaseInfo, RequestInfo, SessionInfo, SqlServiceInfo } from '$lib/types';
	import {
		ago,
		clock,
		dateTime,
		duration,
		mb,
		num,
		pct,
		sqlEdition,
		sqlServerName,
		statusText
	} from '$lib/format';
	import Sparkline from '$lib/components/Sparkline.svelte';
	import LineChart from '$lib/components/LineChart.svelte';
	import { api } from '$lib/api/client';

	const serverId = $derived(page.params.target!);
	const snapshot = $derived(mssql.snapshot(serverId));

	type Tab = 'ozet' | 'oturumlar' | 'calisan' | 'bloke' | 'veritabani' | 'raporlar' | 'sistem';
	let tab = $state<Tab>('ozet');
	// Sorting is applied to every incoming snapshot, not once to the DOM: rows are replaced
	// every few seconds and the chosen order has to survive that.
	const sessionSort = new Sorter<SessionInfo>(
		{
			sessionId: (s) => s.sessionId,
			program: (s) => s.programName,
			host: (s) => s.hostName,
			login: (s) => s.loginName,
			status: (s) => s.status,
			database: (s) => s.databaseName,
			cpu: (s) => s.cpuTimeMs,
			reads: (s) => s.logicalReads,
			writes: (s) => s.writes,
			memory: (s) => s.memoryUsageKb,
			loginTime: (s) => s.loginTime,
			idle: (s) => s.idleSeconds
		},
		'cpu'
	);

	const requestSort = new Sorter<RequestInfo>(
		{
			sessionId: (r) => r.sessionId,
			elapsed: (r) => r.elapsedSeconds,
			cpu: (r) => r.cpuTimeMs,
			reads: (r) => r.logicalReads,
			program: (r) => r.programName,
			database: (r) => r.databaseName
		},
		'elapsed'
	);

	const databaseSort = new Sorter<DatabaseInfo>(
		{
			name: (d) => d.name,
			state: (d) => d.state,
			recovery: (d) => d.recoveryModel,
			data: (d) => d.dataSizeMb,
			log: (d) => d.logSizeMb,
			backup: (d) => d.lastFullBackup
		},
		'data'
	);

	const serviceSort = new Sorter<SqlServiceInfo>(
		{
			name: (s) => s.serviceName,
			account: (s) => s.serviceAccount,
			status: (s) => s.statusDescription,
			startup: (s) => s.startupType
		},
		'name'
	);

	// Column widths and visibility, remembered per table. SPID and the action column are
	// required: without them a row cannot be identified or acted on.
	const sessionColumns = new TableColumns('mssql.sessions', [
		{ key: 'sessionId', label: 'SPID', width: 60, required: true },
		{ key: 'program', label: 'Uygulama', width: 150 },
		{ key: 'host', label: 'Makine / IP', width: 140 },
		{ key: 'login', label: 'Kullanıcı', width: 170 },
		{ key: 'status', label: 'Durum', width: 130 },
		{ key: 'database', label: 'Veritabanı', width: 160 },
		{ key: 'cpu', label: 'CPU', width: 90 },
		{ key: 'reads', label: 'Okuma', width: 90, hiddenByDefault: true },
		{ key: 'writes', label: 'Yazma', width: 90, hiddenByDefault: true },
		{ key: 'memory', label: 'Bellek', width: 90, hiddenByDefault: true },
		{ key: 'login-time', label: 'Bağlanma', width: 130, hiddenByDefault: true },
		{ key: 'idle', label: 'Boşta', width: 90 },
		{ key: 'action', label: 'İşlem', width: 70, required: true }
	]);

	const databaseColumns = new TableColumns('mssql.databases', [
		{ key: 'name', label: 'Veritabanı', width: 200, required: true },
		{ key: 'state', label: 'Durum', width: 110 },
		{ key: 'recovery', label: 'Kurtarma', width: 110 },
		{ key: 'data', label: 'Veri', width: 100 },
		{ key: 'log', label: 'Log', width: 100 },
		{ key: 'backup', label: 'Son yedek', width: 150 }
	]);

	// Search and grouping live on the client: the snapshot is already in memory and arrives
	// every few seconds, so asking the server to filter would add a round trip and make the
	// list flicker between answers.
	let sessionQuery = $state('');

	type GroupKey = 'program' | 'host' | 'login' | 'status' | 'database';

	// Kademeli gruplama: seçim SIRASI hiyerarşiyi belirler. Önce Makine sonra Uygulama demek
	// ile tersi farklı iki sorudur — "bu makineden hangi uygulamalar bağlı" ve "bu uygulama
	// hangi makinelerden geliyor". O yüzden sıra korunur, alfabetik değil tıklama sırası.
	let groupChain = $state<GroupKey[]>([]);
	let collapsedGroups = $state(new Set<string>());

	const GROUPABLE: [GroupKey, string][] = [
		['host', 'Makine'],
		['program', 'Uygulama'],
		['login', 'Kullanıcı'],
		['database', 'Veritabanı'],
		['status', 'Durum']
	];

	const groupLabel = (key: GroupKey) => GROUPABLE.find(([k]) => k === key)?.[1] ?? key;

	function toggleGroupKey(key: GroupKey) {
		groupChain = groupChain.includes(key)
			? groupChain.filter((k) => k !== key)
			: [...groupChain, key];

		// Zincir değişince eski yol anahtarları anlamsız kalır; hepsi açık başlar.
		collapsedGroups = new Set();
	}

	function groupValue(x: SessionInfo, key: GroupKey): string {
		switch (key) {
			case 'program':
				return x.programName ?? '(bilinmiyor)';
			case 'host':
				return x.hostName ?? x.clientAddress ?? '(bilinmiyor)';
			case 'login':
				return x.loginName ?? '(bilinmiyor)';
			case 'status':
				return x.status ?? '(bilinmiyor)';
			case 'database':
				return x.databaseName ?? '(bilinmiyor)';
		}
	}

	// Matched against the fields a person actually types: a SPID, part of a machine name, a
	// login. Case- and accent-insensitive so "İSTANBUL" finds "istanbul-pc".
	function matches(x: SessionInfo, needle: string): boolean {
		const haystack = [
			String(x.sessionId),
			x.programName,
			x.hostName,
			x.clientAddress,
			x.loginName,
			x.status,
			x.databaseName
		]
			.filter(Boolean)
			.join(' ')
			.toLocaleLowerCase('tr');

		return haystack.includes(needle);
	}

	const filteredSessions = $derived.by(() => {
		const rows = sessionSort.apply(snapshot?.sessions ?? []);
		const needle = sessionQuery.trim().toLocaleLowerCase('tr');
		return needle ? rows.filter((x) => matches(x, needle)) : rows;
	});

	type GroupNode = {
		/** Yol anahtarı: aynı adlı iki alt grup farklı dallarda çakışmasın diye tam yol. */
		path: string;
		label: string;
		rows: SessionInfo[];
		children: GroupNode[];
	};

	// Groups keep the sorted order of their first row, so sorting a column still decides what
	// comes first — grouping rearranges, it does not re-sort.
	function buildTree(rows: SessionInfo[], depth: number, parentPath: string): GroupNode[] {
		if (depth >= groupChain.length) return [];

		const key = groupChain[depth];
		const map = new Map<string, SessionInfo[]>();

		for (const row of rows) {
			const value = groupValue(row, key);
			const bucket = map.get(value);
			if (bucket) bucket.push(row);
			else map.set(value, [row]);
		}

		return [...map.entries()].map(([label, bucket]) => {
			const path = `${parentPath}/${key}=${label}`;
			return {
				path,
				label,
				rows: bucket,
				children: buildTree(bucket, depth + 1, path)
			};
		});
	}

	const sessionTree = $derived(groupChain.length === 0 ? [] : buildTree(filteredSessions, 0, ''));

	function toggleGroup(path: string) {
		const next = new Set(collapsedGroups);
		if (next.has(path)) next.delete(path);
		else next.add(path);
		collapsedGroups = next;
	}

	// --- Raporlar ---------------------------------------------------------------------------
	type MetricPoint = {
		atUtc: string;
		cpuPercent: number | null;
		sqlCpuPercent: number | null;
		memoryPercent: number | null;
		sqlMemoryMb: number | null;
		sessionCount: number | null;
		requestCount: number | null;
		blockedCount: number | null;
		longestQuerySeconds: number | null;
	};

	const RANGES: [string, string][] = [
		['gun', 'Gün'],
		['hafta', 'Hafta'],
		['ay', 'Ay'],
		['yil', 'Yıl']
	];

	let range = $state('gun');
	let metrics = $state<MetricPoint[]>([]);
	let metricsBusy = $state(false);
	let metricsError = $state<string | null>(null);

	async function loadMetrics() {
		metricsBusy = true;
		metricsError = null;

		try {
			const result = await api<{ points: MetricPoint[] }>(
				`/api/metrics/${MSSQL_MODULE_ID}/${serverId.replace(/-/g, '')}?aralik=${range}`
			);
			metrics = result.points;
		} catch (e) {
			metricsError = e instanceof Error ? e.message : String(e);
		} finally {
			metricsBusy = false;
		}
	}

	// Sekmeye girildiğinde ve aralık değiştiğinde çekilir; ekranda kaldığı sürece dakikada bir
	// tazelenir — kayıtlar da dakikada bir yazıldığı için daha sık sormak boşuna trafik.
	$effect(() => {
		if (tab !== 'raporlar') return;

		void range;
		void loadMetrics();

		const timer = setInterval(() => void loadMetrics(), 60_000);
		return () => clearInterval(timer);
	});

	const metricTimes = $derived(metrics.map((m) => m.atUtc));

	// Hangi alanlar çizilecek. Birim burada taşınıyor çünkü çizim kararını o veriyor: yüzde
	// ile adet aynı eksene konamaz.
	type MetricField = {
		key: keyof MetricPoint;
		label: string;
		unit: string;
		/** Yüzdelerde tavan sabit; adetlerde veriden hesaplanır. */
		max: number | null;
	};

	const FIELDS: MetricField[] = [
		{ key: 'cpuPercent', label: 'İşlemci', unit: '%', max: 100 },
		{ key: 'sqlCpuPercent', label: 'SQL işlemci payı', unit: '%', max: 100 },
		{ key: 'memoryPercent', label: 'Bellek', unit: '%', max: 100 },
		{ key: 'sqlMemoryMb', label: 'SQL belleği', unit: ' MB', max: null },
		{ key: 'sessionCount', label: 'Oturum', unit: '', max: null },
		{ key: 'requestCount', label: 'Çalışan sorgu', unit: '', max: null },
		{ key: 'blockedCount', label: 'Bloke', unit: '', max: null },
		{ key: 'longestQuerySeconds', label: 'En uzun sorgu', unit: ' sn', max: null }
	];

	let selectedFields = $state<(keyof MetricPoint)[]>([
		'cpuPercent',
		'memoryPercent',
		'sessionCount',
		'blockedCount'
	]);

	let chartKind = $state<'line' | 'area' | 'bar'>('line');
	let expanded = $state<string | null>(null);
	let showTable = $state(false);

	function toggleField(key: keyof MetricPoint) {
		selectedFields = selectedFields.includes(key)
			? selectedFields.filter((k) => k !== key)
			: [...selectedFields, key];
	}

	// Aynı birimdekiler bir arada, ama grafik başına EN FAZLA İKİ seri: doğrulanmış renk
	// çifti iki tane ve üçüncü bir renk eklemek durum renklerine (yeşil/sarı/kırmızı)
	// girmeyi gerektirirdi — onlar bu üründe ölçülmüş durumu anlatıyor.
	const chartGroups = $derived.by(() => {
		const chosen = FIELDS.filter((f) => selectedFields.includes(f.key));
		const byUnit = new Map<string, MetricField[]>();

		for (const field of chosen) {
			const bucket = byUnit.get(field.unit);
			if (bucket) bucket.push(field);
			else byUnit.set(field.unit, [field]);
		}

		const groups: { id: string; title: string; unit: string; max: number | null; fields: MetricField[] }[] = [];

		for (const [unit, fields] of byUnit) {
			for (let i = 0; i < fields.length; i += 2) {
				const pair = fields.slice(i, i + 2);
				groups.push({
					id: `${unit}-${i}`,
					title: pair.map((f) => f.label).join(' ve '),
					unit,
					max: pair.some((f) => f.max === null) ? null : pair[0].max,
					fields: pair
				});
			}
		}

		return groups;
	});

	function seriesFor(fields: MetricField[]) {
		return fields.map((f) => ({
			label: f.label,
			values: metrics.map((m) => (m[f.key] as number | null) ?? null)
		}));
	}

	// --- Tablo görünümü ---
	let tableQuery = $state('');
	const tableSort = new Sorter<MetricPoint>(
		{
			at: (m) => m.atUtc,
			cpuPercent: (m) => m.cpuPercent,
			sqlCpuPercent: (m) => m.sqlCpuPercent,
			memoryPercent: (m) => m.memoryPercent,
			sqlMemoryMb: (m) => m.sqlMemoryMb,
			sessionCount: (m) => m.sessionCount,
			requestCount: (m) => m.requestCount,
			blockedCount: (m) => m.blockedCount,
			longestQuerySeconds: (m) => m.longestQuerySeconds
		},
		'at'
	);

	const tableRows = $derived.by(() => {
		const rows = tableSort.apply(metrics);
		const needle = tableQuery.trim().toLocaleLowerCase('tr');
		if (!needle) return rows;

		// Tarih metni üzerinden aranıyor: "07.08" ya da "14:" yazmak bir günü ya da saati
		// getirir. Sayılarda arama yerine sıralama işe yarar, o yüzden orada sütun sıralaması.
		return rows.filter((m) =>
			new Date(m.atUtc).toLocaleString('tr').toLocaleLowerCase('tr').includes(needle)
		);
	});

	let killing = $state<number | null>(null);
	let actionError = $state<string | null>(null);
	let actionOk = $state<string | null>(null);

	onMount(async () => {
		await mssql.start();
		// Detail view: subscribe to this one server so its stream keeps flowing even if the
		// module-wide subscription is dropped later.
		await realtime.subscribeTarget(MSSQL_MODULE_ID, serverId.replace(/-/g, ''));
	});

	onDestroy(() => realtime.unsubscribeTarget(MSSQL_MODULE_ID, serverId.replace(/-/g, '')));

	async function kill(sessionId: number) {
		if (!confirm(`${sessionId} numaralı oturum sonlandırılsın mı? Açık işlemi geri alınır.`)) {
			return;
		}

		killing = sessionId;
		actionError = null;
		actionOk = null;

		try {
			await mssql.kill(serverId, sessionId);
			actionOk = `${sessionId} numaralı oturum sonlandırıldı.`;
		} catch (e) {
			actionError = e instanceof Error ? e.message : String(e);
		} finally {
			killing = null;
		}
	}

	function short(sql: string | null | undefined, max = 220): string {
		if (!sql) return '—';
		const clean = sql.replace(/\s+/g, ' ').trim();
		return clean.length > max ? clean.slice(0, max) + '…' : clean;
	}
</script>

{#snippet groupBranch(node: GroupNode, depth: number)}
	<!-- Kendini çağıran snippet: kaç seviye seçilirse seçilsin aynı kod çiziyor. -->
	<tr class="group-row">
		<td colspan={sessionColumns.visible.length}>
			<button
				class="group-toggle"
				style="padding-left:{0.6 + depth * 1.1}rem"
				onclick={() => toggleGroup(node.path)}
			>
				<span class="caret" class:collapsed={collapsedGroups.has(node.path)}>▾</span>
				<span class="muted level">{groupLabel(groupChain[depth])}</span>
				<strong>{node.label}</strong>
				<span class="muted">{node.rows.length}</span>
			</button>
		</td>
	</tr>

	{#if !collapsedGroups.has(node.path)}
		{#if node.children.length > 0}
			{#each node.children as child (child.path)}
				{@render groupBranch(child, depth + 1)}
			{/each}
		{:else}
			{#each node.rows as x (x.sessionId)}
				{@render sessionRow(x)}
			{/each}
		{/if}
	{/if}
{/snippet}

{#snippet sessionRow(x: SessionInfo)}
	<tr class:blocked={x.isBlocked} class:blocker={x.isBlocker}>
		{#each sessionColumns.visible.filter((c) => c.key !== 'action') as col (col.key)}
			{#if col.key === 'sessionId'}
				<td class="mono">{x.sessionId}</td>
			{:else if col.key === 'program'}
				<td class="clamp">{x.programName ?? '—'}</td>
			{:else if col.key === 'host'}
				<td class="clamp">{x.hostName ?? '—'}<div class="muted mono">{x.clientAddress ?? ''}</div></td>
			{:else if col.key === 'login'}
				<td class="clamp">{x.loginName ?? '—'}</td>
			{:else if col.key === 'status'}
				<td>
					{x.status ?? '—'}
					{#if x.isBlocker}<span class="badge badge-crit">engelliyor</span>{/if}
					{#if x.isBlocked}<span class="badge badge-warn">bloke</span>{/if}
					{#if x.openTransactionCount > 0}
						<span class="badge">{x.openTransactionCount} açık işlem</span>
					{/if}
				</td>
			{:else if col.key === 'database'}
				<td class="clamp">{x.databaseName ?? '—'}</td>
			{:else if col.key === 'cpu'}
				<td class="mono">{num(x.cpuTimeMs)} ms</td>
			{:else if col.key === 'reads'}
				<td class="mono">{num(x.logicalReads)}</td>
			{:else if col.key === 'writes'}
				<td class="mono">{num(x.writes)}</td>
			{:else if col.key === 'memory'}
				<td class="mono">{num(x.memoryUsageKb)} KB</td>
			{:else if col.key === 'login-time'}
				<td class="muted">{dateTime(x.loginTime)}</td>
			{:else if col.key === 'idle'}
				<td>{duration(x.idleSeconds)}</td>
			{/if}
		{/each}

		<td class="pinned">
			<button
				class="btn btn-sm btn-danger"
				disabled={killing === x.sessionId}
				onclick={() => kill(x.sessionId)}
			>
				{killing === x.sessionId ? '…' : 'Kes'}
			</button>
		</td>
	</tr>
{/snippet}

<div class="page wide">
	{#if !snapshot}
		<p class="muted">Bu sunucu için henüz veri yok.</p>
	{:else}
		{@const s = snapshot}
		<div class="row between" style="margin-bottom:0.5rem">
			<div class="row" style="min-width:0">
				<span class="dot sev-{s.summary.severity}"></span>
				<div style="min-width:0">
					<h1>{s.serverName}</h1>
					<div class="muted">
						{s.customerName} · {statusText[s.status]} · {ago(s.capturedAt)}
						<!-- Surum ve edisyon basligin altinda: "Express mi?" sorusu her destek
						     gorusmesinde soruluyor ve cevabi Sistem sekmesinde gomuluydu. -->
						{#if sqlServerName(s.instance?.productVersion)}
							<br />
							<span class="edition">
								{sqlServerName(s.instance?.productVersion)}
								{#if sqlEdition(s.instance?.edition)} · {sqlEdition(s.instance?.edition)}{/if}
								{#if s.instance?.productLevel} · {s.instance.productLevel}{/if}
							</span>
						{/if}
					</div>
				</div>
			</div>
			<a class="btn btn-sm" href="/m/mssql/{serverId}/ayarlar">Ayarlar</a>
		</div>

		{#if s.errorMessage}<div class="error">{s.errorMessage}</div>{/if}
		{#if actionError}<div class="error">{actionError}</div>{/if}
		{#if actionOk}<div class="notice">{actionOk}</div>{/if}

		{#each s.activeAlerts as alert (alert.key)}
			<div class="error" style="background:transparent">
				<strong>{alert.ruleTitle}:</strong>
				{alert.message}
				<span class="muted"> · {ago(alert.sinceUtc)} beri</span>
			</div>
		{/each}

		<div class="tabs">
			{#each [['ozet', 'Özet'], ['oturumlar', `Oturumlar (${s.sessions.length})`], ['calisan', `Çalışan (${s.requests.length})`], ['bloke', `Bloke (${s.blocking.length})`], ['veritabani', 'Veritabanları'], ['raporlar', 'Raporlar'], ['sistem', 'Sistem']] as [key, label] (key)}
				<button class="tab" class:active={tab === key} onclick={() => (tab = key as Tab)}>
					{label}
				</button>
			{/each}
		</div>

		{#if tab === 'ozet'}
			<div class="grid">
				<div class="stat">
					<div class="value">{pct(s.resources?.cpuPercent)}</div>
					<div class="label">İşlemci</div>
					<Sparkline values={mssql.metrics(s.serverId).cpu} max={100} height={16} fluid />
				</div>
				<div class="stat">
					<div class="value">{pct(s.resources?.sqlCpuPercent)}</div>
					<div class="label">SQL payı</div>
				</div>
				<div class="stat">
					<div class="value">{pct(s.resources?.memoryUsedPercent, 1)}</div>
					<div class="label">Bellek</div>
					<Sparkline values={mssql.metrics(s.serverId).memory} max={100} height={16} fluid />
				</div>
				<div class="stat">
					<div class="value">{mb(s.resources?.availablePhysicalMemoryMb)}</div>
					<div class="label">Boş bellek</div>
				</div>
				<div class="stat">
					<div class="value">{mb(s.resources?.sqlProcessMemoryMb)}</div>
					<div class="label">SQL belleği</div>
				</div>
				<div class="stat">
					<div class="value">{num(s.resources?.pageLifeExpectancySeconds)}</div>
					<div class="label">PLE (sn)</div>
				</div>
				<div class="stat">
					<div class="value">{num(s.summary.openTransactions)}</div>
					<div class="label">Açık işlem</div>
				</div>
				<div class="stat">
					<div class="value">{num(s.resources?.runnableTasks)}</div>
					<div class="label">CPU bekleyen</div>
				</div>
			</div>

			{#if s.resources?.cpuSampleAgeSeconds != null && s.resources.cpuSampleAgeSeconds > 90}
				<p class="muted" style="margin-top:0.5rem">
					⚠️ İşlemci değeri {s.resources.cpuSampleAgeSeconds} saniye önce ölçüldü. SQL Server bu
					örneği dakikada bir yazar; anlık değil.
				</p>
			{/if}

			{#if s.topWaits.length > 0}
				<div class="card" style="margin-top:0.6rem">
					<h3>Son aralıktaki beklemeler</h3>
					<p class="muted" style="margin:0.2rem 0 0.5rem">
						Toplam değil, iki ölçüm arasındaki fark — şu an neyin beklediğini gösterir.
					</p>
					{#each s.topWaits as w (w.waitType)}
						<div class="row between" style="padding:0.2rem 0">
							<span class="mono">{w.waitType}</span>
							<span class="muted">{num(w.waitTimeMs)} ms · %{num(w.percentage, 1)}</span>
						</div>
					{/each}
				</div>
			{/if}
		{:else if tab === 'oturumlar'}
			<div class="toolbar">
				<input
					class="search"
					type="search"
					placeholder="Ara: SPID, uygulama, makine, kullanıcı, veritabanı…"
					bind:value={sessionQuery}
					aria-label="Oturumlarda ara"
				/>

				<div class="group-pick">
					<span class="muted">Grupla:</span>
					{#each GROUPABLE as [key, label] (key)}
						{@const order = groupChain.indexOf(key)}
						<button
							class="chip"
							class:on={order >= 0}
							onclick={() => toggleGroupKey(key)}
							title={order >= 0 ? `${order + 1}. seviye — kaldırmak için tıklayın` : 'Gruplamaya ekle'}
						>
							{#if order >= 0}<span class="order">{order + 1}</span>{/if}{label}
						</button>
					{/each}
					{#if groupChain.length > 0}
						<button class="chip clear" onclick={() => { groupChain = []; collapsedGroups = new Set(); }}>
							temizle
						</button>
					{/if}
				</div>

				<span class="muted count">
					{#if sessionQuery.trim()}
						{filteredSessions.length} / {s.sessions.length} oturum
					{:else}
						{s.sessions.length} oturum
					{/if}
				</span>

				<ColumnPicker columns={sessionColumns} />
			</div>

			<div class="card scroll-x">
				<table class="sized">
					<thead>
						<tr>
							<!-- Başlıklar sütun listesinden çizilir; sıra değişince başlık ve hücre
							     birlikte taşınır. İşlem sütunu her zaman sonda: sağa sabitlenmiş bir
							     kolonun ortada durması onu sabitlenmiş olmaktan çıkarır. -->
							{#each sessionColumns.visible.filter((c) => c.key !== 'action') as col (col.key)}
								<SortHeader
									sorter={sessionSort}
									column={col.key === 'login-time' ? 'loginTime' : col.key}
									label={col.label}
									columns={sessionColumns}
									resizeKey={col.key}
								/>
							{/each}
							<th class="pinned" style="width:{sessionColumns.width('action')}px"></th>
						</tr>
					</thead>
					<tbody>
						{#if filteredSessions.length === 0}
							<tr>
								<td colspan={sessionColumns.visible.length} class="muted" style="padding:1rem">
									{sessionQuery.trim()
										? `"${sessionQuery}" ile eşleşen oturum yok.`
										: 'Oturum yok.'}
								</td>
							</tr>
						{/if}

						{#each sessionTree as node (node.path)}
							{@render groupBranch(node, 0)}
						{/each}

						{#if groupChain.length === 0}
							{#each filteredSessions as x (x.sessionId)}
								{@render sessionRow(x)}
							{/each}
						{/if}
					</tbody>
				</table>
			</div>
		{:else if tab === 'calisan'}
			{#if s.requests.length === 0}
				<p class="muted">Şu anda çalışan sorgu yok.</p>
			{:else}
				<div class="row" style="gap:0.4rem;margin-bottom:0.5rem;flex-wrap:wrap">
					<span class="muted">Sırala:</span>
					{#each [['elapsed', 'Süre'], ['cpu', 'CPU'], ['reads', 'Okuma'], ['sessionId', 'SPID'], ['program', 'Uygulama']] as [key, label] (key)}
						<button
							class="tab"
							class:active={requestSort.key === key}
							onclick={() => requestSort.toggle(key)}
						>
							{label}
							{requestSort.indicator(key)}
						</button>
					{/each}
				</div>
			{/if}
			{#each requestSort.apply(s.requests) as r (r.sessionId)}
				<div class="card">
					<div class="row between">
						<strong class="mono">SPID {r.sessionId} · {r.command ?? '—'}</strong>
						<span class="muted">{duration(r.elapsedSeconds)}</span>
					</div>
					<div class="muted">
						{r.programName ?? '—'} · {r.loginName ?? '—'} · {r.databaseName ?? '—'}
						{#if r.waitType}· bekliyor: {r.waitType}{/if}
						{#if r.blockingSessionId}· <span style="color:var(--crit)">SPID {r.blockingSessionId} engelliyor</span>{/if}
						{#if r.percentComplete}· %{num(r.percentComplete)} tamam{/if}
					</div>
					<pre class="sql">{short(r.sqlText, 600)}</pre>
				</div>
			{/each}
		{:else if tab === 'bloke'}
			{#if s.blocking.length === 0}
				<p class="muted">Bloke edilen oturum yok.</p>
			{/if}
			{#each s.blocking as b (b.blockedSessionId)}
				<div class="card">
					<div class="row between">
						<strong>SPID {b.blockedSessionId} ← SPID {b.blockingSessionId}</strong>
						<span class="muted">{duration(b.waitTimeMs / 1000)} beklemede</span>
					</div>
					<div class="muted">
						{b.waitType ?? '—'}{#if b.waitResource} · {b.waitResource}{/if}
					</div>

					<h3 style="margin-top:0.5rem">Bekleyen · {b.blockedProgram ?? '—'}</h3>
					<pre class="sql">{short(b.blockedSql)}</pre>

					<h3>Engelleyen · {b.blockingProgram ?? '—'} ({b.blockingLogin ?? '—'})</h3>
					<pre class="sql">{short(b.blockingSql)}</pre>
					<p class="muted" style="margin:0.3rem 0 0">
						Engelleyen oturum uyuyor olabilir — bu metin onun son çalıştırdığı ifadedir.
					</p>
					<button class="btn btn-sm btn-danger" onclick={() => kill(b.blockingSessionId)}>
						Engelleyeni kes
					</button>
				</div>
			{/each}
		{:else if tab === 'veritabani'}
			<div class="row between" style="margin-bottom:0.5rem">
				<span class="muted">Başlığı sürükleyerek genişliği değiştirebilirsiniz.</span>
				<ColumnPicker columns={databaseColumns} />
			</div>

			<div class="card scroll-x">
				<table class="sized">
					<thead>
						<tr>
							{#if databaseColumns.isVisible('name')}<SortHeader sorter={databaseSort} column="name" label="Veritabanı" columns={databaseColumns} />{/if}
							{#if databaseColumns.isVisible('state')}<SortHeader sorter={databaseSort} column="state" label="Durum" columns={databaseColumns} />{/if}
							{#if databaseColumns.isVisible('recovery')}<SortHeader sorter={databaseSort} column="recovery" label="Kurtarma" columns={databaseColumns} />{/if}
							{#if databaseColumns.isVisible('data')}<SortHeader sorter={databaseSort} column="data" label="Veri" columns={databaseColumns} />{/if}
							{#if databaseColumns.isVisible('log')}<SortHeader sorter={databaseSort} column="log" label="Log" columns={databaseColumns} />{/if}
							{#if databaseColumns.isVisible('backup')}<SortHeader sorter={databaseSort} column="backup" label="Son yedek" columns={databaseColumns} />{/if}
						</tr>
					</thead>
					<tbody>
						{#each databaseSort.apply(s.databases) as d (d.name)}
							<tr>
								{#if databaseColumns.isVisible('name')}<td class="clamp">{d.name}{#if d.isReadCommittedSnapshotOn}<span class="badge">RCSI</span>{/if}</td>{/if}
								{#if databaseColumns.isVisible('state')}<td>{d.state ?? '—'}</td>{/if}
								{#if databaseColumns.isVisible('recovery')}<td>{d.recoveryModel ?? '—'}</td>{/if}
								{#if databaseColumns.isVisible('data')}<td>{mb(d.dataSizeMb)}</td>{/if}
								{#if databaseColumns.isVisible('log')}<td>{mb(d.logSizeMb)}</td>{/if}
								{#if databaseColumns.isVisible('backup')}<td class:stale={!d.lastFullBackup}>{dateTime(d.lastFullBackup)}</td>{/if}
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
		{:else if tab === 'raporlar'}
			<div class="row between" style="margin-bottom:0.5rem">
				<div class="tabs" style="margin:0">
					{#each RANGES as [key, label] (key)}
						<button class="tab" class:active={range === key} onclick={() => (range = key)}>
							{label}
						</button>
					{/each}
				</div>
				<span class="muted">{metricsBusy ? 'yükleniyor…' : `${metrics.length} ölçüm`}</span>
			</div>

			<div class="toolbar">
				<span class="muted">Alanlar:</span>
				{#each FIELDS as f (f.key)}
					<button
						class="chip"
						class:on={selectedFields.includes(f.key)}
						onclick={() => toggleField(f.key)}
					>
						{f.label}
					</button>
				{/each}
			</div>

			<div class="toolbar">
				<span class="muted">Görünüm:</span>
				{#each [['line', 'Çizgi'], ['area', 'Alan'], ['bar', 'Sütun']] as [key, label] (key)}
					<button
						class="chip"
						class:on={chartKind === key}
						onclick={() => (chartKind = key as typeof chartKind)}
					>
						{label}
					</button>
				{/each}

				<button class="chip" class:on={showTable} onclick={() => (showTable = !showTable)}>
					Tablo
				</button>
			</div>

			{#if metricsError}<div class="error">{metricsError}</div>{/if}

			{#if selectedFields.length === 0}
				<p class="muted">Çizilecek alan seçin.</p>
			{/if}

			{#each chartGroups as g (g.id)}
				<div class="card">
					<div class="row between">
						<span></span>
						<!-- Odaklanma: grafiği tam ekrana alır. Aynı bileşen, yalnız yükseklik ve
						     genişlik değişir — ikinci bir çizim yolu tutmuyoruz. -->
						<button class="btn btn-sm" onclick={() => (expanded = g.id)} title="Tam ekran">
							⤢
						</button>
					</div>
					<LineChart
						title={g.title}
						unit={g.unit}
						max={g.max}
						kind={chartKind}
						times={metricTimes}
						series={seriesFor(g.fields)}
					/>
				</div>
			{/each}

			{#if showTable}
				<div class="card">
					<div class="toolbar">
						<input
							class="search"
							type="search"
							placeholder="Tarih ya da saat ara: 07.08 · 14:"
							bind:value={tableQuery}
							aria-label="Ölçümlerde ara"
						/>
						<span class="muted count">{tableRows.length} satır</span>
					</div>

					<div class="scroll-x" style="max-height:60vh;overflow-y:auto">
						<table>
							<thead>
								<tr>
									<SortHeader sorter={tableSort} column="at" label="Zaman" />
									{#each FIELDS.filter((f) => selectedFields.includes(f.key)) as f (f.key)}
										<SortHeader sorter={tableSort} column={f.key} label={f.label} />
									{/each}
								</tr>
							</thead>
							<tbody>
								{#each tableRows as m (m.atUtc)}
									<tr>
										<td class="muted">{dateTime(m.atUtc)}</td>
										{#each FIELDS.filter((f) => selectedFields.includes(f.key)) as f (f.key)}
											<td class="mono">
												{m[f.key] === null || m[f.key] === undefined
													? '—'
													: `${(m[f.key] as number).toLocaleString('tr', { maximumFractionDigits: 1 })}${f.unit}`}
											</td>
										{/each}
									</tr>
								{/each}
							</tbody>
						</table>
					</div>
				</div>
			{/if}

			<p class="muted" style="font-size:0.78rem">
				Ölçümler dakikada bir yazılır. Bir haftadan eskiler saatlik, üç aydan eskiler günlük
				ortalamaya iner; iki yıldan eskiler silinir.
			</p>

			{#if expanded}
				{@const g = chartGroups.find((x) => x.id === expanded)}
				{#if g}
					<div
						class="fullscreen"
						role="dialog"
						aria-modal="true"
						aria-label="{g.title} — tam ekran"
					>
						<div class="row between" style="margin-bottom:0.5rem">
							<strong>{g.title}</strong>
							<button class="btn btn-sm" onclick={() => (expanded = null)}>Kapat ✕</button>
						</div>
						<LineChart
							title={g.title}
							unit={g.unit}
							max={g.max}
							kind={chartKind}
							height={Math.round(globalThis.innerHeight * 0.62)}
							times={metricTimes}
							series={seriesFor(g.fields)}
						/>
					</div>
				{/if}
			{/if}
		{:else if tab === 'sistem'}
			<div class="card">
				<h3>Örnek</h3>
				<div class="muted">
					{s.instance?.serverName ?? '—'} · {s.instance?.edition ?? '—'}<br />
					Sürüm {s.instance?.productVersion ?? '—'} ({s.instance?.productLevel ?? '—'}) ·
					{s.instance?.hostPlatform ?? '—'}<br />
					{s.instance?.cpuCount ?? '—'} çekirdek · {duration((s.instance?.uptimeMinutes ?? 0) * 60)} açık
					({dateTime(s.instance?.startedAt)})<br />
					Toplam bellek {mb(s.resources?.totalPhysicalMemoryMb)} ·
					SQL hedefi {mb(s.resources?.sqlTargetMemoryMb)} ·
					{s.resources?.systemMemoryState ?? '—'}
				</div>
			</div>

			<div class="card">
				<h3>Servisler ve çalıştıkları hesap</h3>
				{#if s.services.length === 0}
					<p class="muted">
						Servis bilgisi okunamadı. VIEW SERVER STATE izni gerekir; Linux üzerinde bazı
						servisler listelenmez.
					</p>
				{:else}
					<div class="scroll-x">
						<table>
							<thead>
								<tr>
									<SortHeader sorter={serviceSort} column="name" label="Servis" />
									<SortHeader sorter={serviceSort} column="account" label="Hesap" />
									<SortHeader sorter={serviceSort} column="status" label="Durum" />
									<SortHeader sorter={serviceSort} column="startup" label="Başlangıç" />
								</tr>
							</thead>
							<tbody>
								{#each serviceSort.apply(s.services) as svc (svc.serviceName)}
									<tr>
										<td>{svc.serviceName}</td>
										<td class="mono">{svc.serviceAccount ?? '—'}</td>
										<td>{svc.statusDescription ?? '—'}</td>
										<td>{svc.startupType ?? '—'}</td>
									</tr>
								{/each}
							</tbody>
						</table>
					</div>
				{/if}
			</div>

			<p class="muted">Toplama süresi {s.collectionMs} ms · ölçüm {clock(s.capturedAt)}</p>
		{/if}
	{/if}
</div>

<style>
	.sql {
		background: var(--surface-2);
		border-radius: 8px;
		padding: 0.5rem;
		margin: 0.3rem 0 0;
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
		font-size: 0.78rem;
		white-space: pre-wrap;
		word-break: break-word;
		max-height: 12rem;
		overflow-y: auto;
	}

	tr.blocked td {
		background: color-mix(in srgb, var(--warn) 10%, transparent);
	}

	tr.blocker td {
		background: color-mix(in srgb, var(--crit) 10%, transparent);
	}

	td.stale {
		color: var(--warn);
	}

	h1 {
		font-size: 1.1rem;
	}

	/* Arama, gruplama ve sutun secici tek satirda; dar ekranda alt alta sarar. */
	.toolbar {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		flex-wrap: wrap;
		margin-bottom: 0.5rem;
	}

	.search {
		flex: 1 1 14rem;
		min-width: 0;
	}

	.group-pick {
		display: inline-flex;
		align-items: center;
		gap: 0.35rem;
	}

	.toolbar .count {
		font-variant-numeric: tabular-nums;
	}

	/* Grup basligi bir veri satiri degil; zemini ayirir ama renk tasimaz — renk bu tabloda
	   durum demek. */
	.group-row td {
		background: var(--surface-2, rgba(127, 127, 127, 0.08));
		padding: 0;
	}

	.group-toggle {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		width: 100%;
		padding: 0.4rem 0.6rem;
		background: none;
		border: 0;
		color: inherit;
		font: inherit;
		text-align: left;
		cursor: pointer;
	}

	.caret {
		display: inline-block;
		transition: transform 0.12s ease;
	}

	.caret.collapsed {
		transform: rotate(-90deg);
	}

	@media (prefers-reduced-motion: reduce) {
		.caret {
			transition: none;
		}
	}

	/* Surum satiri: bilgi, olcum degil — o yuzden sessiz. */
	.edition {
		font-size: 0.78rem;
		opacity: 0.85;
	}

	.chip {
		border: 1px solid var(--border);
		background: var(--surface-2);
		color: inherit;
		border-radius: 999px;
		padding: 0.15rem 0.6rem;
		font-size: 0.78rem;
		cursor: pointer;
	}

	/* Seçili çipteki rakam kaçıncı seviye olduğunu söylüyor — sıra burada anlam taşıyor. */
	.chip.on {
		border-color: var(--accent);
		color: var(--accent);
	}

	.chip .order {
		display: inline-block;
		margin-right: 0.3rem;
		font-variant-numeric: tabular-nums;
		opacity: 0.8;
	}

	.chip.clear {
		border-style: dashed;
		opacity: 0.75;
	}

	/* Hangi alana göre gruplandığı başlıkta yazıyor: iç içe iki seviyede "MUHASEBE-PC" tek
	   başına hangi soruya cevap verdiğini söylemiyor. */
	.level {
		font-size: 0.72rem;
		text-transform: lowercase;
	}

	/* Tam ekran: veriye odaklanmak için her şeyin üstünü kapatır. */
	.fullscreen {
		position: fixed;
		inset: 0;
		z-index: 50;
		padding: 0.9rem;
		background: var(--bg);
		overflow: auto;
	}
</style>
