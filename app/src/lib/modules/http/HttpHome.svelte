<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { http } from './store.svelte';
	import { httpStatusText } from './types';
	import { ago, num, pct } from '$lib/format';

	onMount(() => http.start());
	onDestroy(() => http.stop());

	const checks = $derived(http.checks);
</script>

<div class="page">
	<div class="row between" style="margin-bottom:0.6rem">
		<h1>Site / API İzleme</h1>
		<a class="btn btn-sm btn-primary" href="/m/http/yeni">+ Adres</a>
	</div>

	{#if http.error}<div class="error">{http.error}</div>{/if}

	{#if checks.length === 0}
		<div class="card">
			<p class="muted" style="margin:0 0 0.6rem">
				İzlenen adres yok. Müşteri sitesi, API ucu ya da ödeme sağlayıcı geri dönüş adresi
				ekleyin — ayakta mı, ne kadar hızlı ve sertifikası ne zaman bitiyor takip edilir.
			</p>
			<a class="btn btn-primary" href="/m/http/yeni">Adres ekle</a>
		</div>
	{/if}

	{#each checks as c (c.targetId)}
		<a class="card block" href="/m/http/{c.targetId}">
			<div class="row between">
				<div class="row" style="min-width:0">
					<span class="dot sev-{c.severity}"></span>
					<div style="min-width:0">
						<strong>{c.targetName}</strong>
						<div class="muted url">{c.url}</div>
					</div>
				</div>
				<div style="text-align:right">
					<div class="muted">{httpStatusText[c.status]}</div>
					<div class="muted">{ago(c.checkedAt)}</div>
				</div>
			</div>

			{#if c.error}
				<div class="error" style="margin:0.6rem 0 0">{c.error}</div>
			{/if}

			<div class="grid" style="margin-top:0.6rem">
				<div class="stat">
					<div class="value">{num(c.responseTimeMs)} ms</div>
					<div class="label">Yanıt</div>
				</div>
				<div class="stat">
					<div class="value">{c.statusCode ?? '—'}</div>
					<div class="label">Durum kodu</div>
				</div>
				<div class="stat">
					<div class="value">{pct(c.uptimePercent, 1)}</div>
					<div class="label">Erişilebilirlik</div>
				</div>
				<div class="stat" class:warn={c.certificateDaysRemaining != null && c.certificateDaysRemaining <= 14}>
					<div class="value">
						{c.certificateDaysRemaining == null ? '—' : `${c.certificateDaysRemaining} gün`}
					</div>
					<div class="label">Sertifika</div>
				</div>
			</div>

			{#each c.activeAlerts as alert (alert.key)}
				<div class="badge {alert.severity === 2 ? 'badge-crit' : 'badge-warn'}" style="margin-top:0.4rem">
					{alert.message}
				</div>
			{/each}
		</a>
	{/each}
</div>

<style>
	.block {
		display: block;
	}

	.url {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.stat.warn .value {
		color: var(--warn);
	}
</style>
