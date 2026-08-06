<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import { ago, dateTime, duration } from '$lib/format';
	import { Sorter } from '$lib/sort.svelte';
	import type { AlertHistoryEntry } from '$lib/types';

	// Persisted history: this is what answers "what happened while I was asleep" — the in-app
	// alert list only holds what arrived while the app was open.
	let entries = $state<AlertHistoryEntry[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let onlyActive = $state(false);

	const sorter = new Sorter<AlertHistoryEntry>(
		{
			raised: (e) => e.raisedAtUtc,
			severity: (e) => e.severity,
			target: (e) => e.targetName,
			group: (e) => e.groupName,
			rule: (e) => e.ruleTitle,
			duration: (e) =>
				(e.clearedAtUtc ? new Date(e.clearedAtUtc).getTime() : Date.now()) -
				new Date(e.raisedAtUtc).getTime()
		},
		'raised'
	);

	const shown = $derived(sorter.apply(onlyActive ? entries.filter((e) => e.isActive) : entries));
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

	<div class="row" style="gap:0.4rem;margin-bottom:0.6rem;flex-wrap:wrap">
		<span class="muted">Sırala:</span>
		{#each [['raised', 'Zaman'], ['severity', 'Seviye'], ['target', 'Sunucu'], ['rule', 'Kural'], ['duration', 'Süre']] as [key, label] (key)}
			<button class="tab" class:active={sorter.key === key} onclick={() => sorter.toggle(key)}>
				{label}
				{sorter.indicator(key)}
			</button>
		{/each}
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

			<!-- Alarmın sebebi: sayı sunucunun meşgul olduğunu söyler, bu satır ne yapılacağını.
			     Eski kayıtlarda yok — uydurmak yerine hiç göstermiyoruz. -->
			{#if entry.context}
				<div class="context">{entry.context}</div>
			{/if}

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

	/* Sebep satırı: mesajın altında, ölçüm değil bağlam olduğu için sessiz ve tek satır. */
	.context {
		margin-top: 0.3rem;
		padding: 0.3rem 0.5rem;
		border-left: 2px solid var(--line);
		font-size: 0.8rem;
		opacity: 0.9;
		overflow-wrap: anywhere;
	}
</style>
