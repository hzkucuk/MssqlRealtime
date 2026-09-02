<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import { findUiModule } from '$lib/modules/registry';
	import type { ToolDescriptor } from '$lib/types';

	// The dashboard is generated from what the server reports, so a tool added on the backend
	// shows up here without touching this page.
	let modules = $state<ToolDescriptor[]>([]);
	let error = $state<string | null>(null);
	let loading = $state(true);

	onMount(async () => {
		try {
			modules = await api<ToolDescriptor[]>('/api/modules');
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
		}
	});
</script>

<div class="page">
	<h1>Araçlar</h1>
	<p class="muted">Bu sunucuda kurulu araçlar.</p>

	{#if error}<div class="error">{error}</div>{/if}
	{#if loading}<p class="muted">Yükleniyor…</p>{/if}

	{#each modules as module (module.id)}
		{@const ui = findUiModule(module.id)}
		{#if ui}
			<a class="card tool" href="/m/{module.id}">
				<span class="icon">{module.icon}</span>
				<span style="flex:1;min-width:0">
					<strong>{module.title}</strong>
					{#if module.description}<div class="muted">{module.description}</div>{/if}
				</span>
				<span class="muted">›</span>
			</a>
		{:else}
			<div class="card tool disabled">
				<span class="icon">{module.icon}</span>
				<span style="flex:1;min-width:0">
					<strong>{module.title}</strong>
					<div class="muted">
						Bu araç sunucuda var ama uygulamanın bu sürümünde ekranı yok — uygulamayı güncelleyin.
					</div>
				</span>
			</div>
		{/if}
	{/each}

	{#if !loading && modules.length === 0 && !error}
		<p class="muted">Kurulu araç yok.</p>
	{/if}

	<h2 style="margin-top:1.2rem">Yönetim</h2>
	<a class="card tool" href="/bildirimler">
		<span class="icon">🔔</span>
		<span style="flex:1;min-width:0">
			<strong>Bildirimler</strong>
			<div class="muted">Telegram, e-posta, webhook</div>
		</span>
		<span class="muted">›</span>
	</a>
	<a class="card tool" href="/gizlilik">
		<span class="icon">🔒</span>
		<span style="flex:1;min-width:0">
			<strong>Gizlilik</strong>
			<div class="muted">Sorgu metni nasıl saklansın</div>
		</span>
		<span class="muted">›</span>
	</a>
	<a class="card tool" href="/alarmlar">
		<span class="icon">📋</span>
		<span style="flex:1;min-width:0">
			<strong>Alarm geçmişi</strong>
			<div class="muted">Uygulama kapalıyken olanlar dahil</div>
		</span>
		<span class="muted">›</span>
	</a>
</div>

<style>
	.tool {
		display: flex;
		align-items: center;
		gap: 0.7rem;
	}

	.icon {
		font-size: 1.6rem;
		line-height: 1;
	}

	.disabled {
		opacity: 0.55;
	}
</style>
