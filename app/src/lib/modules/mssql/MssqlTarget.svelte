<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { realtime } from '$lib/api/realtime.svelte';
	import { mssql, MSSQL_MODULE_ID } from './store.svelte';
	import { Sorter } from '$lib/sort.svelte';
	import SortHeader from '$lib/components/SortHeader.svelte';
	import type { DatabaseInfo, RequestInfo, SessionInfo, SqlServiceInfo } from '$lib/types';
	import { ago, clock, dateTime, duration, mb, num, pct, statusText } from '$lib/format';

	const serverId = $derived(page.params.target!);
	const snapshot = $derived(mssql.snapshot(serverId));

	type Tab = 'ozet' | 'oturumlar' | 'calisan' | 'bloke' | 'veritabani' | 'sistem';
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
					<div class="muted">{s.customerName} · {statusText[s.status]} · {ago(s.capturedAt)}</div>
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
			{#each [['ozet', 'Özet'], ['oturumlar', `Oturumlar (${s.sessions.length})`], ['calisan', `Çalışan (${s.requests.length})`], ['bloke', `Bloke (${s.blocking.length})`], ['veritabani', 'Veritabanları'], ['sistem', 'Sistem']] as [key, label] (key)}
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
				</div>
				<div class="stat">
					<div class="value">{pct(s.resources?.sqlCpuPercent)}</div>
					<div class="label">SQL payı</div>
				</div>
				<div class="stat">
					<div class="value">{pct(s.resources?.memoryUsedPercent, 1)}</div>
					<div class="label">Bellek</div>
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
			<div class="card scroll-x">
				<table>
					<thead>
						<tr>
							<SortHeader sorter={sessionSort} column="sessionId" label="SPID" />
							<SortHeader sorter={sessionSort} column="program" label="Uygulama" />
							<SortHeader sorter={sessionSort} column="host" label="Makine / IP" />
							<SortHeader sorter={sessionSort} column="login" label="Kullanıcı" />
							<SortHeader sorter={sessionSort} column="status" label="Durum" />
							<SortHeader sorter={sessionSort} column="database" label="Veritabanı" />
							<SortHeader sorter={sessionSort} column="cpu" label="CPU" />
							<SortHeader sorter={sessionSort} column="idle" label="Boşta" />
							<th class="pinned"></th>
						</tr>
					</thead>
					<tbody>
						{#each sessionSort.apply(s.sessions) as x (x.sessionId)}
							<tr class:blocked={x.isBlocked} class:blocker={x.isBlocker}>
								<td class="mono">{x.sessionId}</td>
								<td class="clamp">{x.programName ?? '—'}</td>
								<td class="clamp">{x.hostName ?? '—'}<div class="muted mono">{x.clientAddress ?? ''}</div></td>
								<td>{x.loginName ?? '—'}</td>
								<td>
									{x.status ?? '—'}
									{#if x.isBlocker}<span class="badge badge-crit">engelliyor</span>{/if}
									{#if x.isBlocked}<span class="badge badge-warn">bloke</span>{/if}
									{#if x.openTransactionCount > 0}
										<span class="badge">{x.openTransactionCount} açık işlem</span>
									{/if}
								</td>
								<td>{x.databaseName ?? '—'}</td>
								<td class="mono">{num(x.cpuTimeMs)} ms</td>
								<td>{duration(x.idleSeconds)}</td>
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
						{/each}
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
			<div class="card scroll-x">
				<table>
					<thead>
						<tr>
							<SortHeader sorter={databaseSort} column="name" label="Veritabanı" />
							<SortHeader sorter={databaseSort} column="state" label="Durum" />
							<SortHeader sorter={databaseSort} column="recovery" label="Kurtarma" />
							<SortHeader sorter={databaseSort} column="data" label="Veri" />
							<SortHeader sorter={databaseSort} column="log" label="Log" />
							<SortHeader sorter={databaseSort} column="backup" label="Son yedek" />
						</tr>
					</thead>
					<tbody>
						{#each databaseSort.apply(s.databases) as d (d.name)}
							<tr>
								<td>{d.name}{#if d.isReadCommittedSnapshotOn}<span class="badge">RCSI</span>{/if}</td>
								<td>{d.state ?? '—'}</td>
								<td>{d.recoveryModel ?? '—'}</td>
								<td>{mb(d.dataSizeMb)}</td>
								<td>{mb(d.logSizeMb)}</td>
								<td class:stale={!d.lastFullBackup}>{dateTime(d.lastFullBackup)}</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
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
</style>
