<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { mssql } from './store.svelte';
	import { ago, duration, mb, num, pct, statusText } from '$lib/format';

	// The summary screen: one card per customer server, sorted so the worst is first.
	onMount(() => mssql.start());
	onDestroy(() => mssql.stop());

	const servers = $derived(mssql.servers);
</script>

<div class="page">
	<div class="row between" style="margin-bottom:0.6rem">
		<h1>MSSQL İzleme</h1>
		<a class="btn btn-sm btn-primary" href="/m/mssql/yeni">+ Sunucu</a>
	</div>

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
		<a class="card server" href="/m/mssql/{s.serverId}">
			<div class="row between">
				<div class="row" style="min-width:0">
					<span class="dot sev-{s.summary.severity}"></span>
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
					</div>
					<div class="stat">
						<div class="value">{pct(s.summary.memoryUsedPercent, 1)}</div>
						<div class="label">Bellek</div>
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
	}

	.stat.bad .value {
		color: var(--crit);
	}
</style>
