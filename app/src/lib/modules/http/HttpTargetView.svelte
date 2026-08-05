<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { realtime } from '$lib/api/realtime.svelte';
	import { http, HTTP_MODULE_ID } from './store.svelte';
	import { httpStatusText } from './types';
	import { ago, dateTime, num, pct } from '$lib/format';

	const targetId = $derived(page.params.target!);
	const check = $derived(http.check(targetId));
	const target = $derived(http.target(targetId));

	onMount(async () => {
		await http.start();
		await realtime.subscribeTarget(HTTP_MODULE_ID, targetId.replace(/-/g, ''));
	});

	onDestroy(() => realtime.unsubscribeTarget(HTTP_MODULE_ID, targetId.replace(/-/g, '')));
</script>

<div class="page">
	{#if !check}
		<p class="muted">Bu adres için henüz ölçüm yok.</p>
	{:else}
		<div class="row between" style="margin-bottom:0.5rem">
			<div class="row" style="min-width:0">
				<span class="dot sev-{check.severity}"></span>
				<div style="min-width:0">
					<h1>{check.targetName}</h1>
					<div class="muted">{check.groupName || '—'} · {httpStatusText[check.status]} · {ago(check.checkedAt)}</div>
				</div>
			</div>
			<a class="btn btn-sm" href="/m/http/{targetId}/ayarlar">Ayarlar</a>
		</div>

		<div class="card">
			<div class="mono" style="word-break:break-all">{check.url}</div>
		</div>

		{#if check.error}<div class="error">{check.error}</div>{/if}

		{#each check.activeAlerts as alert (alert.key)}
			<div class="error" style="background:transparent">
				<strong>{alert.ruleTitle}:</strong> {alert.message}
				<span class="muted"> · {ago(alert.sinceUtc)} beri</span>
			</div>
		{/each}

		<div class="grid">
			<div class="stat">
				<div class="value">{num(check.responseTimeMs)} ms</div>
				<div class="label">Yanıt süresi</div>
			</div>
			<div class="stat">
				<div class="value">{check.statusCode ?? '—'}</div>
				<div class="label">Durum kodu</div>
			</div>
			<div class="stat">
				<div class="value">{pct(check.uptimePercent, 1)}</div>
				<div class="label">Son {check.recentChecks} ölçüm</div>
			</div>
			<div class="stat">
				<div class="value">
					{check.contentLength == null ? '—' : `${num(Math.round(check.contentLength / 1024))} KB`}
				</div>
				<div class="label">Gövde</div>
			</div>
		</div>

		<div class="card">
			<h3>TLS sertifikası</h3>
			{#if check.certificateDaysRemaining == null}
				<p class="muted" style="margin:0.3rem 0 0">
					Sertifika okunamadı (düz HTTP olabilir ya da el sıkışma başarısız).
				</p>
			{:else}
				<div class="muted" style="margin-top:0.3rem">
					{#if check.certificateDaysRemaining <= 0}
						<span style="color:var(--crit)">
							Süresi {Math.abs(check.certificateDaysRemaining)} gün önce doldu.
						</span>
					{:else}
						Bitmesine <strong>{check.certificateDaysRemaining} gün</strong> kaldı.
					{/if}
					{#if check.certificateSubject}
						<div class="mono" style="margin-top:0.3rem;word-break:break-all">
							{check.certificateSubject}
						</div>
					{/if}
				</div>
			{/if}
		</div>

		{#if target}
			<div class="card">
				<h3>Yapılandırma</h3>
				<div class="muted" style="margin-top:0.3rem">
					{target.method} · her {target.checkIntervalSeconds} sn · zaman aşımı {target.timeoutSeconds} sn<br />
					Beklenen durum: {target.expectedStatusCode === 0 ? 'herhangi bir 2xx' : target.expectedStatusCode}
					{#if target.expectedBodyContains}<br />Gövde içermeli: “{target.expectedBodyContains}”{/if}
					{#if target.ignoreCertificateErrors}<br />⚠️ Sertifika hataları yok sayılıyor{/if}
					<br />Son güncelleme: {dateTime(target.updatedAt)}
				</div>
			</div>
		{/if}
	{/if}
</div>

<style>
	h1 {
		font-size: 1.1rem;
	}
</style>
