<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { mssql } from './store.svelte';
	import type { ServerSnapshot } from '$lib/types';
	import { num, pct } from '$lib/format';

	// Used for both "add" and "edit"; the route decides which by supplying a target id.
	const serverId = $derived(page.params.target ?? null);

	let form = $state({
		name: '',
		customerName: '',
		host: '',
		port: 1433,
		initialCatalog: 'master',
		authMode: 0 as 0 | 1,
		username: 'sa',
		password: '',
		encryptConnection: true,
		trustServerCertificate: true,
		connectTimeoutSeconds: 5,
		commandTimeoutSeconds: 15,
		enabled: true,
		pollIntervalSeconds: 5,
		thresholds: {
			cpuPercent: 85 as number | null,
			memoryPercent: 90 as number | null,
			sqlProcessMemoryMb: null as number | null,
			blockedSessions: 1 as number | null,
			longRunningQuerySeconds: 30 as number | null,
			sessionCount: 500 as number | null,
			blockingDurationSeconds: 30 as number | null,
			runnableTasks: null as number | null,
			workerUtilizationPercent: 80 as number | null,
			consecutiveBreaches: 3,
			renotifyMinutes: 15,
			alertOnOffline: true
		}
	});

	let hasStoredPassword = $state(false);
	let busy = $state(false);
	let error = $state<string | null>(null);
	let testResult = $state<ServerSnapshot | null>(null);
	let restoredDraft = $state(false);
	let loaded = $state(false);

	const draftKey = $derived(`mr.draft.mssql.server.${serverId ?? 'yeni'}`);

	// What the form looked like before anyone typed in it. A draft is only worth keeping —
	// and only worth announcing — if it differs from this. Without the comparison, merely
	// opening a server's form left a draft behind, so the next visit greeted the user with
	// "yarım kalan form geri yüklendi" for a form nobody had touched.
	let baseline = $state<string | null>(null);

	// The password is never drafted, so it never takes part in the comparison either.
	function draftable(f: typeof form) {
		const { password: _password, ...rest } = f;
		return JSON.stringify(rest);
	}

	onMount(async () => {
		if (mssql.profiles.length === 0) await mssql.refresh();

		if (serverId) {
			const profile = mssql.profile(serverId);
			if (profile) {
				hasStoredPassword = profile.hasPassword;
				form = {
					...form,
					...profile,
					username: profile.username ?? '',
					password: '',
					thresholds: { ...form.thresholds, ...profile.thresholds }
				};
			}
		}

		baseline = draftable(form);

		// A refresh — or a server-side validation failure that redraws the page — must not
		// throw away what was typed. The password is deliberately never drafted.
		const draft = sessionStorage.getItem(draftKey);
		if (draft) {
			try {
				const restored = { ...form, ...JSON.parse(draft), password: '' };
				if (draftable(restored) === baseline) {
					// The previous visit only looked at the form. Nothing to restore — and a
					// stale copy would quietly override a profile edited somewhere else.
					sessionStorage.removeItem(draftKey);
				} else {
					form = restored;
					restoredDraft = true;
				}
			} catch {
				sessionStorage.removeItem(draftKey);
			}
		}

		loaded = true;
	});

	// Save the draft on every change, but only once the initial load has settled — and only
	// while it still differs from what was loaded.
	$effect(() => {
		if (!loaded) return;

		const current = draftable(form);
		if (current === baseline) sessionStorage.removeItem(draftKey);
		else sessionStorage.setItem(draftKey, current);
	});

	function payload() {
		return {
			...form,
			// Omitting the password entirely means "keep the stored one".
			password: form.password.length > 0 ? form.password : undefined
		};
	}

	async function test() {
		busy = true;
		error = null;
		testResult = null;

		try {
			const result = await mssql.test(payload());
			testResult = result.snapshot;
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}

	async function save() {
		busy = true;
		error = null;

		try {
			const saved = await mssql.save(payload(), serverId ?? undefined);
			// Only now is the draft safe to discard.
			sessionStorage.removeItem(draftKey);
			await goto(`/m/mssql/${saved.id}`);
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}

	async function remove() {
		if (!serverId) return;
		if (!confirm('Bu sunucu izlemeden kaldırılsın mı?')) return;

		busy = true;

		try {
			await mssql.remove(serverId);
			sessionStorage.removeItem(draftKey);
			await goto('/m/mssql');
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}
</script>

<div class="page">
	<h1>{serverId ? 'Sunucu ayarları' : 'Yeni sunucu'}</h1>

	{#if restoredDraft}
		<div class="notice">
			Yarım kalan form geri yüklendi.
			{#if hasStoredPassword}
				Parola alanı boş; boş bırakırsanız kayıtlı parola korunur.
			{:else}
				Parolayı yeniden girmeniz gerekir.
			{/if}
		</div>
	{/if}

	{#if error}<div class="error">{error}</div>{/if}

	{#if testResult}
		<div class="notice">
			✅ Bağlantı başarılı — {testResult.instance?.edition ?? 'SQL Server'}
			{testResult.instance?.productVersion ?? ''} ·
			{num(testResult.summary.totalSessions)} oturum ·
			işlemci {pct(testResult.summary.cpuPercent)} ·
			bellek {pct(testResult.summary.memoryUsedPercent, 1)}
		</div>
	{/if}

	<div class="card">
		<h3>Tanım</h3>
		<div class="field">
			<label for="name">Görünen ad</label>
			<input id="name" bind:value={form.name} placeholder="Merkez SQL" />
		</div>
		<div class="field">
			<label for="customer">Müşteri</label>
			<input id="customer" bind:value={form.customerName} placeholder="Acme Ltd." />
		</div>
	</div>

	<div class="card">
		<h3>Bağlantı</h3>
		<div class="field-row">
			<div class="field">
				<label for="host">Sunucu adresi</label>
				<input id="host" bind:value={form.host} placeholder="192.168.1.10" />
			</div>
			<div class="field">
				<label for="port">Port</label>
				<input id="port" type="number" bind:value={form.port} />
			</div>
		</div>

		<div class="field">
			<label for="auth">Kimlik doğrulama</label>
			<select id="auth" bind:value={form.authMode}>
				<option value={0}>SQL Server girişi</option>
				<option value={1}>Windows (entegre)</option>
			</select>
		</div>

		{#if form.authMode === 0}
			<div class="field-row">
				<div class="field">
					<label for="username">Kullanıcı</label>
					<input id="username" bind:value={form.username} autocomplete="off" />
				</div>
				<div class="field">
					<label for="password">
						Parola {#if hasStoredPassword}<span class="muted">(kayıtlı)</span>{/if}
					</label>
					<input
						id="password"
						type="password"
						bind:value={form.password}
						placeholder={hasStoredPassword ? 'Değiştirmek için yazın' : ''}
						autocomplete="new-password"
					/>
				</div>
			</div>
		{/if}

		<label class="check">
			<input type="checkbox" bind:checked={form.trustServerCertificate} />
			Sertifikaya güven (kendi imzalı sertifikalarda gerekir)
		</label>

		<div class="field-row" style="margin-top:0.5rem">
			<div class="field">
				<label for="interval">Sorgulama aralığı (sn)</label>
				<input id="interval" type="number" min="1" max="3600" bind:value={form.pollIntervalSeconds} />
			</div>
			<div class="field">
				<label for="timeout">Bağlantı zaman aşımı (sn)</label>
				<input id="timeout" type="number" min="1" max="120" bind:value={form.connectTimeoutSeconds} />
			</div>
		</div>

		<label class="check">
			<input type="checkbox" bind:checked={form.enabled} />
			İzleme açık
		</label>
	</div>

	<div class="card">
		<h3>Alarm sınırları</h3>
		<p class="muted" style="margin:0.2rem 0 0.6rem">
			Boş bırakılan sınır için alarm üretilmez. Alarm, sınır üst üste
			{form.thresholds.consecutiveBreaches} ölçümde aşılırsa bildirim gönderir — anlık
			sıçramalar telefonu uyandırmaz.
		</p>

		<div class="field-row">
			<div class="field">
				<label for="cpu">İşlemci sınırı (%)</label>
				<input id="cpu" type="number" min="1" max="100" bind:value={form.thresholds.cpuPercent} />
			</div>
			<div class="field">
				<label for="mem">Bellek sınırı (%)</label>
				<input id="mem" type="number" min="1" max="100" bind:value={form.thresholds.memoryPercent} />
			</div>
		</div>

		<div class="field-row">
			<div class="field">
				<label for="sqlmem">SQL Server belleği (MB)</label>
				<input id="sqlmem" type="number" min="1" bind:value={form.thresholds.sqlProcessMemoryMb} />
			</div>
			<div class="field">
				<label for="blocked">Bloke oturum sayısı</label>
				<input id="blocked" type="number" min="1" bind:value={form.thresholds.blockedSessions} />
			</div>
		</div>

		<div class="field-row">
			<div class="field">
				<label for="long">Uzun sorgu (sn)</label>
				<input id="long" type="number" min="1" bind:value={form.thresholds.longRunningQuerySeconds} />
			</div>
			<div class="field">
				<label for="sessions">Oturum sayısı</label>
				<input id="sessions" type="number" min="1" bind:value={form.thresholds.sessionCount} />
			</div>
		</div>

		<p class="muted" style="margin:0.2rem 0 0.6rem">
			Oturum sayısı boşta duran bağlantı havuzu oturumlarını da içerir; darboğazı aşağıdaki
			üç sınır çok daha erken yakalar.
		</p>

		<div class="field-row">
			<div class="field">
				<label for="blockdur">Kilit süresi (sn)</label>
				<input
					id="blockdur"
					type="number"
					min="1"
					bind:value={form.thresholds.blockingDurationSeconds}
				/>
			</div>
			<div class="field">
				<label for="workers">Worker doluluğu (%)</label>
				<input
					id="workers"
					type="number"
					min="1"
					max="100"
					bind:value={form.thresholds.workerUtilizationPercent}
				/>
			</div>
		</div>

		<div class="field-row">
			<div class="field">
				<label for="runnable">İşlemci sırası (görev)</label>
				<input id="runnable" type="number" min="1" bind:value={form.thresholds.runnableTasks} />
			</div>
			<div class="field"></div>
		</div>

		<p class="muted" style="margin:0.2rem 0 0.6rem">
			İşlemci sırası varsayılan olarak kapalıdır: sağlıklı değer çekirdek sayısına bağlı.
			Sunucunun zamanlayıcı sayısını ölçüp ona göre bir sayı verin.
		</p>

		<div class="field-row">
			<div class="field">
				<label for="breaches">Ardışık ihlal sayısı</label>
				<input id="breaches" type="number" min="1" max="60" bind:value={form.thresholds.consecutiveBreaches} />
			</div>
			<div class="field">
				<label for="renotify">Tekrar bildirim (dk)</label>
				<input id="renotify" type="number" min="1" max="1440" bind:value={form.thresholds.renotifyMinutes} />
			</div>
		</div>

		<label class="check">
			<input type="checkbox" bind:checked={form.thresholds.alertOnOffline} />
			Sunucuya erişilemediğinde bildir
		</label>
	</div>

	<div class="row" style="gap:0.5rem">
		<button class="btn" onclick={test} disabled={busy}>Bağlantıyı sına</button>
		<button class="btn btn-primary" onclick={save} disabled={busy} style="flex:1">
			{busy ? 'Kaydediliyor…' : 'Kaydet'}
		</button>
		{#if serverId}
			<button class="btn btn-danger" onclick={remove} disabled={busy}>Sil</button>
		{/if}
	</div>
</div>

<style>
	.check {
		display: flex;
		align-items: center;
		gap: 0.45rem;
		font-size: 0.9rem;
		color: var(--muted);
	}

	.check input {
		width: auto;
	}
</style>
