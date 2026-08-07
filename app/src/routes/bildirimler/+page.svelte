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

	// --- Sessiz saatler ---
	type Schedule = {
		enabled: boolean;
		workDays: number[];
		start: string;
		end: string;
		quietOnHolidays: boolean;
		extraHolidays: string[];
		criticalAlwaysLoud: boolean;
	};

	const DAYS: [number, string][] = [
		[1, 'Pzt'],
		[2, 'Sal'],
		[3, 'Çar'],
		[4, 'Per'],
		[5, 'Cum'],
		[6, 'Cmt'],
		[0, 'Paz']
	];

	let schedule = $state<Schedule | null>(null);
	let scheduleBusy = $state(false);
	let scheduleSaved = $state(false);
	let holidays = $state<string[]>([]);
	let newHoliday = $state('');

	async function loadSchedule() {
		schedule = await api<Schedule>('/api/notifications/zamanlama');
		const list = await api<{ gunler: string[] }>('/api/notifications/tatiller');
		holidays = list.gunler;
	}

	async function saveSchedule() {
		if (!schedule) return;

		scheduleBusy = true;
		scheduleSaved = false;

		try {
			schedule = await api<Schedule>('/api/notifications/zamanlama', {
				method: 'PUT',
				body: JSON.stringify(schedule)
			});
			scheduleSaved = true;
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			scheduleBusy = false;
		}
	}

	function toggleDay(day: number) {
		if (!schedule) return;
		schedule.workDays = schedule.workDays.includes(day)
			? schedule.workDays.filter((d) => d !== day)
			: [...schedule.workDays, day];
	}

	function addHoliday() {
		if (!schedule || !newHoliday) return;
		if (!schedule.extraHolidays.includes(newHoliday)) {
			schedule.extraHolidays = [...schedule.extraHolidays, newHoliday].sort();
		}
		newHoliday = '';
	}

	onMount(async () => {
		await load();
		try {
			await loadSchedule();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		}
	});

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

	{#if schedule}
		<div class="card">
			<div class="row between">
				<h2>Sessiz saatler</h2>
				<label class="check">
					<input type="checkbox" bind:checked={schedule.enabled} />
					Açık
				</label>
			</div>

			<p class="muted">
				Mesai dışında bildirim <strong>kesilmez</strong>, sessiz gönderilir: mesaj ve alarm
				geçmişi eksilmez, telefon yalnız ses çıkarmaz ve titremez. Kesmek, gelmeyen alarm
				demektir.
			</p>

			{#if schedule.enabled}
				<div class="field">
					<span class="label">Çalışma günleri</span>
					<div class="chips">
						{#each DAYS as [day, label] (day)}
							<button
								class="chip"
								class:on={schedule.workDays.includes(day)}
								onclick={() => toggleDay(day)}
							>
								{label}
							</button>
						{/each}
					</div>
				</div>

				<div class="row" style="gap:0.75rem;flex-wrap:wrap">
					<label class="field">
						<span class="label">Başlangıç</span>
						<input type="time" bind:value={schedule.start} />
					</label>
					<label class="field">
						<span class="label">Bitiş</span>
						<input type="time" bind:value={schedule.end} />
					</label>
				</div>

				<label class="check">
					<input type="checkbox" bind:checked={schedule.quietOnHolidays} />
					Resmî tatil ve bayramlarda da sessiz
				</label>

				<label class="check">
					<input type="checkbox" bind:checked={schedule.criticalAlwaysLoud} />
					Kritik alarmları mesai dışında da sesli gönder
				</label>

				<div class="field">
					<span class="label">Ek tatil günleri</span>
					<div class="row" style="gap:0.4rem">
						<input type="date" bind:value={newHoliday} />
						<button class="btn btn-sm" onclick={addHoliday}>Ekle</button>
					</div>
					{#if schedule.extraHolidays.length > 0}
						<div class="chips" style="margin-top:0.4rem">
							{#each schedule.extraHolidays as day (day)}
								<button
									class="chip on"
									onclick={() =>
										(schedule!.extraHolidays = schedule!.extraHolidays.filter((d) => d !== day))}
								>
									{day} ✕
								</button>
							{/each}
						</div>
					{/if}
				</div>

				{#if holidays.length > 0}
					<details>
						<summary class="muted">Bu yılın tatilleri ({holidays.length} gün)</summary>
						<p class="muted small">
							{holidays.join(' · ')}
						</p>
						<p class="muted small">
							Bayram tarihleri ay takvimiyle hesaplanır; Diyanet takviminden bir gün
							şaşabilir. Şaşarsa doğru günü yukarıdan ekleyin.
						</p>
					</details>
				{/if}
			{/if}

			<div class="row" style="gap:0.5rem;margin-top:0.6rem">
				<button class="btn btn-primary btn-sm" disabled={scheduleBusy} onclick={saveSchedule}>
					{scheduleBusy ? 'Kaydediliyor…' : 'Kaydet'}
				</button>
				{#if scheduleSaved}<span class="muted">kaydedildi</span>{/if}
			</div>
		</div>
	{/if}

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

	.chips {
		display: flex;
		gap: 0.35rem;
		flex-wrap: wrap;
	}

	.chip {
		border: 1px solid var(--border);
		background: var(--surface-2);
		color: inherit;
		border-radius: 999px;
		padding: 0.2rem 0.7rem;
		font-size: 0.8rem;
		cursor: pointer;
	}

	.chip.on {
		border-color: var(--accent);
		color: var(--accent);
	}

	.small {
		font-size: 0.75rem;
		overflow-wrap: anywhere;
	}
</style>
