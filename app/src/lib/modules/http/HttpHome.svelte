<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { http } from './store.svelte';
	import { httpStatusText } from './types';
	import { ago, num, pct } from '$lib/format';
	import { Sorter } from '$lib/sort.svelte';
	import type { CheckCard } from './store.svelte';

	onMount(() => http.start());
	onDestroy(() => http.stop());

	// Ad ve grup hedeften (her zaman var), sayilar olcumden (olmayabilir).
	// Olcumu olmayan adres siralamada UYARI sayilir: sessizlik saglik degildir.
	const sorter = new Sorter<CheckCard>(
		{
			severity: (c) => c.result?.severity ?? 1,
			name: (c) => c.name,
			group: (c) => c.groupName,
			response: (c) => c.result?.responseTimeMs ?? null,
			uptime: (c) => c.result?.uptimePercent ?? null,
			certificate: (c) => c.result?.certificateDaysRemaining ?? null
		},
		'severity'
	);

	const checks = $derived(sorter.apply(http.checks));
</script>

<div class="page">
	<div class="row between" style="margin-bottom:0.6rem">
		<h1>Site / API İzleme</h1>
		<a class="btn btn-sm btn-primary" href="/m/http/yeni">+ Adres</a>
	</div>

	{#if checks.length > 0}
		<div class="row" style="gap:0.4rem;margin-bottom:0.6rem;flex-wrap:wrap">
			<span class="muted">Sırala:</span>
			{#each [['severity', 'Durum'], ['name', 'Ad'], ['response', 'Yanıt'], ['uptime', 'Erişilebilirlik'], ['certificate', 'Sertifika']] as [key, label] (key)}
				<button class="tab" class:active={sorter.key === key} onclick={() => sorter.toggle(key)}>
					{label}
					{sorter.indicator(key)}
				</button>
			{/each}
		</div>
	{/if}

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

	{#each checks as card (card.id)}
		{@const c = card.result}
		<a class="card block" href="/m/http/{card.id}">
			<div class="row between">
				<div class="row" style="min-width:0">
					<span class="dot sev-{c ? c.severity : 1}"></span>
					<div style="min-width:0">
						<strong>{card.name}</strong>
						<div class="muted url">{card.url}</div>
					</div>
				</div>
				<div style="text-align:right">
					<div class="muted">
						{c ? httpStatusText[c.status] : card.enabled ? 'ölçüm bekleniyor' : 'kapalı'}
					</div>
					{#if c}<div class="muted">{ago(c.checkedAt)}</div>{/if}
				</div>
			</div>

			{#if !c}
				<!-- Kayit var, olcum yok: gizlenmez, soylenir. -->
				<div class="muted" style="margin:0.6rem 0 0">
					{card.enabled
						? 'Henüz ölçüm gelmedi. Yeni eklendiyse ilk kontrol birazdan yapılır.'
						: 'Zamanlayıcı bu adres için kapalı; kontrol edilmiyor.'}
				</div>
			{:else}
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
			{/if}
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
