<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import type { NotificationChannelInfo } from '$lib/types';

	// The whole form is generated from what the server reports, so a channel added on the
	// backend later shows up here without an app update.
	let channels = $state<NotificationChannelInfo[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let busy = $state<string | null>(null);
	let result = $state<Record<string, string>>({});

	// Edited values per channel; only the keys actually typed into are sent, so an untouched
	// secret keeps whatever is stored.
	let edits = $state<Record<string, Record<string, string>>>({});

	onMount(load);

	async function load() {
		loading = true;
		try {
			channels = await api<NotificationChannelInfo[]>('/api/notifications/channels');
			error = null;
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
		}
	}

	function edit(channelId: string, key: string, value: string) {
		edits[channelId] = { ...(edits[channelId] ?? {}), [key]: value };
	}

	async function save(channel: NotificationChannelInfo) {
		busy = channel.id;
		result = { ...result, [channel.id]: '' };

		try {
			await api(`/api/notifications/channels/${channel.id}`, {
				method: 'PUT',
				body: JSON.stringify({
					enabled: channel.enabled,
					minimumSeverity: channel.minimumSeverity,
					sendRecoveries: channel.sendRecoveries,
					values: edits[channel.id] ?? {}
				})
			});

			edits[channel.id] = {};
			result = { ...result, [channel.id]: 'Kaydedildi.' };
			await load();
		} catch (e) {
			result = { ...result, [channel.id]: e instanceof Error ? e.message : String(e) };
		} finally {
			busy = null;
		}
	}

	async function test(channel: NotificationChannelInfo) {
		busy = channel.id;
		result = { ...result, [channel.id]: '' };

		try {
			await api(`/api/notifications/channels/${channel.id}/test`, { method: 'POST' });
			result = { ...result, [channel.id]: '✅ Test mesajı gönderildi.' };
		} catch (e) {
			result = { ...result, [channel.id]: `❌ ${e instanceof Error ? e.message : String(e)}` };
		} finally {
			busy = null;
		}
	}
</script>

<div class="page">
	<h1>Bildirimler</h1>
	<p class="muted">
		Uygulama kapalıyken de haber almanın yolu. Sunucu alarmı kendisi gönderir; telefonun
		bağlı olması gerekmez.
	</p>

	{#if error}<div class="error">{error}</div>{/if}
	{#if loading}<p class="muted">Yükleniyor…</p>{/if}

	{#each channels as channel (channel.id)}
		<div class="card">
			<div class="row between">
				<h2>{channel.title}</h2>
				<label class="check">
					<input type="checkbox" bind:checked={channel.enabled} />
					Açık
				</label>
			</div>

			{#each channel.fields as field (field.key)}
				<div class="field">
					<label for="{channel.id}-{field.key}">
						{field.label}
						{#if !field.isRequired}<span class="muted">(isteğe bağlı)</span>{/if}
						{#if field.isSecret && field.hasValue}<span class="badge">kayıtlı</span>{/if}
					</label>

					<input
						id="{channel.id}-{field.key}"
						type={field.isSecret ? 'password' : 'text'}
						placeholder={field.isSecret && field.hasValue
							? 'Değiştirmek için yazın'
							: (field.placeholder ?? '')}
						value={edits[channel.id]?.[field.key] ?? (field.isSecret ? '' : (field.value ?? ''))}
						oninput={(e) => edit(channel.id, field.key, e.currentTarget.value)}
						autocomplete="off"
					/>

					{#if field.help}<div class="muted help">{field.help}</div>{/if}
				</div>
			{/each}

			<div class="field-row">
				<div class="field">
					<label for="{channel.id}-sev">En düşük seviye</label>
					<select id="{channel.id}-sev" bind:value={channel.minimumSeverity}>
						<option value={0}>Tümü</option>
						<option value={1}>Uyarı ve üstü</option>
						<option value={2}>Yalnız kritik</option>
					</select>
				</div>
				<div class="field" style="display:flex;align-items:flex-end">
					<label class="check">
						<input type="checkbox" bind:checked={channel.sendRecoveries} />
						"Normale döndü" mesajı da gönder
					</label>
				</div>
			</div>

			{#if result[channel.id]}
				<div class={result[channel.id].startsWith('❌') ? 'error' : 'notice'}>
					{result[channel.id]}
				</div>
			{/if}

			<div class="row" style="gap:0.5rem">
				<button class="btn" onclick={() => test(channel)} disabled={busy === channel.id}>
					Test gönder
				</button>
				<button
					class="btn btn-primary"
					style="flex:1"
					onclick={() => save(channel)}
					disabled={busy === channel.id}
				>
					{busy === channel.id ? 'Kaydediliyor…' : 'Kaydet'}
				</button>
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
		margin: 0;
	}

	.check input {
		width: auto;
	}

	.help {
		margin-top: 0.2rem;
		font-size: 0.78rem;
		line-height: 1.35;
	}
</style>
