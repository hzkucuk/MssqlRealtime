<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { mssql } from './store.svelte';
	import { ago, duration, mb, num, pct, statusText } from '$lib/format';
	import { Sorter } from '$lib/sort.svelte';
	import Sparkline from '$lib/components/Sparkline.svelte';
	import type { ServerSnapshot } from '$lib/types';

	// The summary screen: one card per customer server, sorted so the worst is first.
	onMount(() => mssql.start());
	onDestroy(() => mssql.stop());

	const sorter = new Sorter<ServerSnapshot>(
		{
			severity: (s) => s.summary.severity,
			name: (s) => s.serverName,
			customer: (s) => s.customerName,
			cpu: (s) => s.summary.cpuPercent,
			memory: (s) => s.summary.memoryUsedPercent,
			sessions: (s) => s.summary.totalSessions,
			blocked: (s) => s.summary.blockedSessions,
			longest: (s) => s.summary.longestRunningSeconds
		},
		'severity'
	);

	const servers = $derived(sorter.apply(mssql.servers));

	// Renk tek başına durum anlatmaz: nokta her zaman bir metin etiketi taşır.
	const severityText = ['Normal', 'Uyarı', 'Kritik'];
</script>

<div class="page">
	<div class="row between" style="margin-bottom:0.6rem">
		<h1>MSSQL İzleme</h1>
		<a class="btn btn-sm btn-primary" href="/m/mssql/yeni">+ Sunucu</a>
	</div>

	{#if servers.length > 1}
		<div class="row" style="gap:0.4rem;margin-bottom:0.6rem;flex-wrap:wrap">
			<span class="muted">Sırala:</span>
			{#each [['severity', 'Durum'], ['name', 'Ad'], ['customer', 'Müşteri'], ['cpu', 'CPU'], ['memory', 'Bellek'], ['sessions', 'Oturum'], ['blocked', 'Bloke']] as [key, label] (key)}
				<button class="tab" class:active={sorter.key === key} onclick={() => sorter.toggle(key)}>
					{label}
					{sorter.indicator(key)}
				</button>
			{/each}
		</div>
	{/if}

	{#if mssql.error}<div class="error">{mssql.error}</div>{/if}

	{#if servers.length === 0}
		<div class="card">
			<p class="muted" style="margin:0 0 0.6rem">
				Henüz izlenen sunucu yok. Bir müşteri sunucusu ekleyin — bağlantı bilgileri
				şifrelenerek saklanır.
			</p>
			<a class="btn btn-primary" href="/m/mssql/yeni">Sunucu ekle</a>
		</div>
	{/if}

	{#each servers as s (s.serverId)}
		<a class="card server sev-edge-{s.summary.severity}" href="/m/mssql/{s.serverId}">
			<div class="row between">
				<div class="row" style="min-width:0">
					<span
						class="dot sev-{s.summary.severity}"
						title={severityText[s.summary.severity]}
						aria-label={severityText[s.summary.severity]}
						role="img"
					></span>
					<div style="min-width:0">
						<strong>{s.serverName}</strong>
						<div class="muted">{s.customerName}</div>
					</div>
				</div>
				<div style="text-align:right">
					<div class="muted">{statusText[s.status]}</div>
					<div class="muted">{ago(s.capturedAt)}</div>
				</div>
			</div>

			{#if s.status !== 1}
				<div class="error" style="margin:0.6rem 0 0">{s.errorMessage ?? 'Erişilemiyor.'}</div>
			{:else}
				<div class="grid" style="margin-top:0.6rem">
					<div class="stat">
						<div class="value">{pct(s.summary.cpuPercent)}</div>
						<div class="label">İşlemci</div>
						<!-- A strip under the label, not beside it: beside it the tile grew wider than
						     its neighbours and broke the grid into a ragged row. -->
						<Sparkline values={mssql.metrics(s.serverId).cpu} max={100} height={16} fluid />
					</div>
					<div class="stat">
						<div class="value">{pct(s.summary.memoryUsedPercent, 1)}</div>
						<div class="label">Bellek</div>
						<Sparkline values={mssql.metrics(s.serverId).memory} max={100} height={16} fluid />
					</div>
					<div class="stat">
						<div class="value">{num(s.summary.totalSessions)}</div>
						<div class="label">Oturum</div>
					</div>
					<div class="stat">
						<div class="value">{num(s.summary.activeRequests)}</div>
						<div class="label">Çalışan</div>
					</div>
					<div class="stat" class:bad={s.summary.blockedSessions > 0}>
						<div class="value">{num(s.summary.blockedSessions)}</div>
						<div class="label">Bloke</div>
					</div>
					<div class="stat">
						<div class="value">{duration(s.summary.longestRunningSeconds)}</div>
						<div class="label">En uzun sorgu</div>
					</div>
				</div>

				{#if s.resources}
					<div class="muted" style="margin-top:0.45rem">
						{mb(s.resources.availablePhysicalMemoryMb)} boşta ·
						{num(s.summary.distinctApplications)} uygulama ·
						{num(s.summary.distinctHosts)} makine
						{#if s.summary.topWaitType}· bekleme: {s.summary.topWaitType}{/if}
					</div>
				{/if}

				{#each s.activeAlerts as alert (alert.key)}
					<div class="badge {alert.severity === 2 ? 'badge-crit' : 'badge-warn'}" style="margin-top:0.4rem">
						{alert.message}
					</div>
				{/each}
			{/if}
		</a>
	{/each}
</div>

<style>
	.server {
		display: block;
		position: relative;
		overflow: hidden;
	}

	.stat.bad .value {
		color: var(--crit);
	}
</style>
