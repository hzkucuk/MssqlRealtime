<script lang="ts">
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { http } from './store.svelte';
	import { httpStatusText, type HttpCheckResult } from './types';
	import { num } from '$lib/format';

	const targetId = $derived(page.params.target ?? null);

	let form = $state({
		name: '',
		groupName: '',
		url: 'https://',
		method: 'GET',
		expectedStatusCode: 0,
		expectedBodyContains: '',
		enabled: true,
		checkIntervalSeconds: 60,
		timeoutSeconds: 10,
		ignoreCertificateErrors: false,
		alertOnDown: true,
		slowResponseMs: 3000 as number | null,
		certificateExpiryWarningDays: 14 as number | null,
		alertConsecutiveBreaches: 2,
		alertRenotifyMinutes: 15
	});

	let busy = $state(false);
	let error = $state<string | null>(null);
	let testResult = $state<HttpCheckResult | null>(null);
	let restoredDraft = $state(false);
	let loaded = $state(false);

	const draftKey = $derived(`mr.draft.http.target.${targetId ?? 'yeni'}`);

	onMount(async () => {
		if (http.targets.length === 0) await http.refresh();

		if (targetId) {
			const existing = http.target(targetId);
			if (existing) {
				form = { ...form, ...existing, expectedBodyContains: existing.expectedBodyContains ?? '' };
			}
		}

		// Same F5 protection as the MSSQL form: a refresh or a rejected save must not discard
		// what was typed.
		const draft = sessionStorage.getItem(draftKey);
		if (draft) {
			try {
				form = { ...form, ...JSON.parse(draft) };
				restoredDraft = true;
			} catch {
				sessionStorage.removeItem(draftKey);
			}
		}

		loaded = true;
	});

	$effect(() => {
		if (!loaded) return;
		sessionStorage.setItem(draftKey, JSON.stringify(form));
	});

	async function test() {
		busy = true;
		error = null;
		testResult = null;

		try {
			const result = await http.test(form);
			testResult = result.result;
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
			const saved = await http.save(form, targetId ?? undefined);
			sessionStorage.removeItem(draftKey);
			await goto(`/m/http/${saved.id}`);
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}

	async function remove() {
		if (!targetId || !confirm('Bu adres izlemeden kaldırılsın mı?')) return;

		busy = true;
		try {
			await http.remove(targetId);
			sessionStorage.removeItem(draftKey);
			await goto('/m/http');
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}
</script>

<div class="page">
	<h1>{targetId ? 'Adres ayarları' : 'Yeni adres'}</h1>

	{#if restoredDraft}<div class="notice">Yarım kalan form geri yüklendi.</div>{/if}
	{#if error}<div class="error">{error}</div>{/if}

	{#if testResult}
		<div class={testResult.status === 2 ? 'error' : 'notice'}>
			{testResult.status === 2 ? '❌' : '✅'}
			{httpStatusText[testResult.status]} · {testResult.statusCode ?? '—'} ·
			{num(testResult.responseTimeMs)} ms
			{#if testResult.certificateDaysRemaining != null}
				· sertifika {testResult.certificateDaysRemaining} gün
			{/if}
			{#if testResult.error}<div>{testResult.error}</div>{/if}
		</div>
	{/if}

	<div class="card">
		<h3>Tanım</h3>
		<div class="field">
			<label for="name">Görünen ad</label>
			<input id="name" bind:value={form.name} placeholder="Müşteri sitesi" />
		</div>
		<div class="field">
			<label for="group">Grup / müşteri</label>
			<input id="group" bind:value={form.groupName} placeholder="Acme Ltd." />
		</div>
		<div class="field">
			<label for="url">Adres</label>
			<input id="url" bind:value={form.url} placeholder="https://ornek.com/health" />
		</div>
		<div class="field-row">
			<div class="field">
				<label for="method">Yöntem</label>
				<select id="method" bind:value={form.method}>
					<option>GET</option>
					<option>HEAD</option>
					<option>POST</option>
				</select>
			</div>
			<div class="field">
				<label for="status">Beklenen durum kodu</label>
				<input id="status" type="number" min="0" max="599" bind:value={form.expectedStatusCode} />
				<div class="muted help">0 = herhangi bir 2xx</div>
			</div>
		</div>
		<div class="field">
			<label for="body">Gövde şunu içermeli (isteğe bağlı)</label>
			<input id="body" bind:value={form.expectedBodyContains} placeholder="Sipariş" />
			<div class="muted help">
				“200 OK” dönen ama hata sayfası gösteren siteleri yakalar.
			</div>
		</div>
	</div>

	<div class="card">
		<h3>Kontrol</h3>
		<div class="field-row">
			<div class="field">
				<label for="interval">Aralık (sn)</label>
				<input id="interval" type="number" min="5" bind:value={form.checkIntervalSeconds} />
			</div>
			<div class="field">
				<label for="timeout">Zaman aşımı (sn)</label>
				<input id="timeout" type="number" min="1" max="120" bind:value={form.timeoutSeconds} />
			</div>
		</div>
		<label class="check">
			<input type="checkbox" bind:checked={form.enabled} /> İzleme açık
		</label>
		<label class="check">
			<input type="checkbox" bind:checked={form.ignoreCertificateErrors} />
			Sertifika hatalarını yok say (iç ağdaki kendi imzalı sertifikalar)
		</label>
	</div>

	<div class="card">
		<h3>Alarm sınırları</h3>
		<label class="check">
			<input type="checkbox" bind:checked={form.alertOnDown} /> Erişilemediğinde bildir
		</label>
		<div class="field-row" style="margin-top:0.5rem">
			<div class="field">
				<label for="slow">Yavaş yanıt sınırı (ms)</label>
				<input id="slow" type="number" min="1" bind:value={form.slowResponseMs} />
			</div>
			<div class="field">
				<label for="cert">Sertifika uyarısı (gün)</label>
				<input id="cert" type="number" min="1" bind:value={form.certificateExpiryWarningDays} />
			</div>
		</div>
		<div class="field-row">
			<div class="field">
				<label for="breaches">Ardışık ihlal</label>
				<input id="breaches" type="number" min="1" max="60" bind:value={form.alertConsecutiveBreaches} />
			</div>
			<div class="field">
				<label for="renotify">Tekrar bildirim (dk)</label>
				<input id="renotify" type="number" min="1" max="1440" bind:value={form.alertRenotifyMinutes} />
			</div>
		</div>
	</div>

	<div class="row" style="gap:0.5rem">
		<button class="btn" onclick={test} disabled={busy}>Şimdi dene</button>
		<button class="btn btn-primary" style="flex:1" onclick={save} disabled={busy}>
			{busy ? 'Kaydediliyor…' : 'Kaydet'}
		</button>
		{#if targetId}
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

	.help {
		margin-top: 0.2rem;
		font-size: 0.78rem;
	}
</style>
