<script lang="ts">
	import '../app.css';
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { getActiveServer, getTokens, logout } from '$lib/api/client';
	import { realtime } from '$lib/api/realtime.svelte';
	import { ensureNotificationPermission } from '$lib/notify';
	import { ago } from '$lib/format';

	let { children } = $props();

	let showAlerts = $state(false);

	// Which customer's panel is on screen — with one hub per customer, this is the first
	// thing you need to know before reading any number on it.
	const activeServer = $derived(getActiveServer());

	const isLogin = $derived(page.url.pathname === '/giris');
	const unread = $derived(realtime.alerts.filter((a) => !a.isCleared).length);

	const connectionLabel = $derived(
		{
			connected: 'canlı',
			connecting: 'bağlanıyor…',
			reconnecting: 'yeniden bağlanıyor…',
			disconnected: 'bağlı değil'
		}[realtime.state]
	);

	onMount(async () => {
		if (!getTokens()) {
			if (!isLogin) await goto('/giris');
			return;
		}

		// Ask before the first alert arrives, not with it.
		await ensureNotificationPermission();
		await realtime.start();
	});

	async function signOut() {
		await realtime.stop();
		logout();
		await goto('/giris');
	}
</script>

<svelte:head>
	<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
	<title>Sunucu İzleme</title>
</svelte:head>

{#if !isLogin}
	<header>
		<a href="/" class="brand">
			{#if page.url.pathname !== '/'}<span class="back" aria-hidden="true">‹</span>{/if}
			<span style="min-width:0">
				<strong>{activeServer?.label ?? 'Sunucu İzleme'}</strong>
				{#if activeServer}<div class="host">{activeServer.url.replace(/^https?:\/\//, '')}</div>{/if}
			</span>
		</a>

		<div class="row" style="gap:0.4rem">
			<span class="conn" class:live={realtime.state === 'connected'}>
				<span class="dot {realtime.state === 'connected' ? 'sev-0' : 'sev-1'}"></span>
				{connectionLabel}
			</span>

			<button class="btn btn-sm" onclick={() => (showAlerts = !showAlerts)}>
				🔔{#if unread > 0}<span class="count">{unread}</span>{/if}
			</button>

			<a class="btn btn-sm" href="/bildirimler" title="Bildirim ayarları">⚙️</a>
			<a class="btn btn-sm" href="/giris" title="Panel değiştir">🔀</a>
			<button class="btn btn-sm" onclick={signOut} title="Çıkış">⏻</button>
		</div>
	</header>

	{#if showAlerts}
		<div class="alerts">
			<a class="history-link" href="/alarmlar" onclick={() => (showAlerts = false)}>
				Tüm alarm geçmişi (uygulama kapalıyken olanlar dahil) ›
			</a>

			{#if realtime.alerts.length === 0}
				<p class="muted" style="padding:0.5rem">
					Bu oturumda alarm gelmedi. Daha öncekiler için geçmişe bakın.
				</p>
			{:else}
				{#each realtime.alerts as item (item.alert.key + item.raisedAtUtc)}
					<div class="alert-row">
						<span class="dot {item.isCleared ? 'sev-0' : `sev-${item.alert.severity}`}"></span>
						<div style="flex:1;min-width:0">
							<div class="row between">
								<strong>{item.alert.target.targetName}</strong>
								<span class="muted">{ago(item.raisedAtUtc)}</span>
							</div>
							<div class="muted">{item.body}</div>
						</div>
					</div>
				{/each}
			{/if}
		</div>
	{/if}
{/if}

{@render children()}

<style>
	header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.5rem;
		padding: 0.6rem 0.85rem;
		/* Yarı saydam + bulanık: altındaki içerik kaydıkça görünür, ama başlık okunur kalır.
		   Destekleyemeyen tarayıcıda düz yüzeye düşer. */
		background: color-mix(in srgb, var(--surface) 88%, transparent);
		backdrop-filter: blur(12px) saturate(140%);
		-webkit-backdrop-filter: blur(12px) saturate(140%);
		border-bottom: 1px solid var(--border);
		position: sticky;
		top: 0;
		z-index: 10;
	}

	@supports not (backdrop-filter: blur(1px)) {
		header {
			background: var(--surface);
		}
	}

	.brand {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		font-size: 1rem;
		min-width: 0;
		transition: opacity var(--speed) var(--ease);
	}

	.brand:hover {
		opacity: 0.85;
	}

	.back {
		font-size: 1.4rem;
		line-height: 1;
		color: var(--accent);
	}

	.host {
		font-size: 0.7rem;
		color: var(--muted);
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.conn {
		display: inline-flex;
		align-items: center;
		gap: 0.35rem;
		font-size: 0.72rem;
		color: var(--muted);
		padding: 0.2rem 0.5rem;
		border-radius: 999px;
		background: var(--surface-2);
		border: 1px solid var(--border);
		white-space: nowrap;
	}

	/* Canlı akış varken nokta yeşil ve sabit; yokken sarı. Metin de durumu söylüyor —
	   rengi tek başına bilgi taşıyıcı yapmıyoruz. */
	.conn.live {
		color: var(--ok);
		border-color: color-mix(in srgb, var(--ok) 30%, transparent);
		background: color-mix(in srgb, var(--ok) 8%, transparent);
	}

	.count {
		background: var(--crit);
		color: #fff;
		border-radius: 999px;
		padding: 0.05rem 0.32rem;
		margin-left: 0.25rem;
		font-size: 0.68rem;
		font-weight: 700;
		font-variant-numeric: tabular-nums;
	}

	.alerts {
		max-height: 45vh;
		overflow-y: auto;
		background: var(--surface);
		border-bottom: 1px solid var(--border);
		box-shadow: var(--shadow);
	}

	.alert-row {
		display: flex;
		gap: 0.55rem;
		align-items: flex-start;
		padding: 0.6rem 0.85rem;
		border-bottom: 1px solid var(--border);
		transition: background var(--speed) var(--ease);
	}

	.alert-row:hover {
		background: var(--surface-2);
	}

	.alert-row:last-child {
		border-bottom: none;
	}

	.alert-row .dot {
		margin-top: 0.4rem;
	}

	.history-link {
		display: block;
		padding: 0.5rem 0.75rem;
		font-size: 0.85rem;
		color: var(--accent);
		border-bottom: 1px solid var(--border);
	}
</style>
