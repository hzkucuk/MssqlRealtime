<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import { ago, dateTime, duration } from '$lib/format';
	import type { AlertHistoryEntry } from '$lib/types';

	// Persisted history: this is what answers "what happened while I was asleep" — the in-app
	// alert list only holds what arrived while the app was open.
	let entries = $state<AlertHistoryEntry[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let onlyActive = $state(false);

	const shown = $derived(onlyActive ? entries.filter((e) => e.isActive) : entries);
	const activeCount = $derived(entries.filter((e) => e.isActive).length);

	onMount(async () => {
		try {
			entries = await api<AlertHistoryEntry[]>('/api/alerts?limit=200');
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
		}
	});

	function lasted(entry: AlertHistoryEntry): string {
		const end = entry.clearedAtUtc ? new Date(entry.clearedAtUtc) : new Date();
		return duration((end.getTime() - new Date(entry.raisedAtUtc).getTime()) / 1000);
	}
</script>

<div class="page">
	<div class="row between" style="margin-bottom:0.6rem">
		<h1>Alarm geçmişi</h1>
		<label class="check">
			<input type="checkbox" bind:checked={onlyActive} />
			Yalnız süren ({activeCount})
		</label>
	</div>

	{#if error}<div class="error">{error}</div>{/if}
	{#if loading}<p class="muted">Yükleniyor…</p>{/if}
	{#if !loading && shown.length === 0}
		<p class="muted">Kayıt yok. Bu iyi haber.</p>
	{/if}

	{#each shown as entry (entry.id)}
		<div class="card">
			<div class="row between">
				<div class="row" style="min-width:0">
					<span class="dot sev-{entry.severity}"></span>
					<div style="min-width:0">
						<strong>{entry.targetName}</strong>
						<div class="muted">{entry.groupName ?? '—'} · {entry.ruleTitle}</div>
					</div>
				</div>
				<div style="text-align:right">
					{#if entry.isActive}
						<span class="badge badge-crit">sürüyor</span>
					{:else}
						<span class="badge">bitti</span>
					{/if}
					<div class="muted">{ago(entry.raisedAtUtc)}</div>
				</div>
			</div>

			<div style="margin-top:0.35rem">{entry.message}</div>

			<div class="muted" style="margin-top:0.3rem">
				{dateTime(entry.raisedAtUtc)} · {lasted(entry)} sürdü
				{#if entry.clearedAtUtc}· bitiş {dateTime(entry.clearedAtUtc)}{/if}
			</div>
		</div>
	{/each}
</div>

<style>
	.check {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		font-size: 0.85rem;
		color: var(--muted);
	}

	.check input {
		width: auto;
	}
</style>
