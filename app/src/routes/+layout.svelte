<script lang="ts">
	import '../app.css';
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import {
		appVersion,
		compareVersions,
		fetchHubUpdate,
		fetchServerVersion,
		startHubUpdate,
		type HubUpdate,
		getTokens,
		logout,
		releasePageUrl
	} from '$lib/api/client';

	const UPDATE_DISMISSED_KEY = 'mr.updateDismissed';
	import { activePanel } from '$lib/api/panel.svelte';
	import { realtime } from '$lib/api/realtime.svelte';
	import { ensureNotificationPermission } from '$lib/notify';
	import { ago } from '$lib/format';

	let { children } = $props();

	let showAlerts = $state(false);

	// Which customer's panel is on screen — with one hub per customer, this is the first
	// thing you need to know before reading any number on it. Reactive state rather than a
	// read of localStorage: see the note in panel.svelte.ts for what that cost.
	const activeServer = $derived(activePanel.current);

	// Which build answers on the other end. Worth showing next to the address: with one hub
	// per customer, "hangi sürüm bu müşteride?" is the first question of every support call.
	let serverVersion = $state<string | null>(null);

	// The phone app is updated by hand, so it can lag behind the hub. Say so when it does —
	// a screen that hides the difference turns "eski uygulama" into an hour of debugging.
	const mismatched = $derived(serverVersion !== null && serverVersion !== appVersion);
	const versionTitle = $derived(
		mismatched
			? `Panel v${serverVersion} · uygulama v${appVersion}`
			: `Panel ve uygulama v${appVersion}`
	);

	// Two different situations, and only one of them is the phone's problem:
	//   app older than hub  → this device can fix it, so offer the download;
	//   hub older than app  → the server needs upgrading, which nobody does from a phone.
	// In a browser the bundle is served by the hub itself, so the versions always agree and
	// this never fires — no platform check needed, the mismatch is the signal.
	const updateAvailable = $derived(
		serverVersion !== null && compareVersions(serverVersion, appVersion) > 0
	);

	// Dismissal lasts the session: a nudge that returns tomorrow is a reminder, one that
	// never returns is a missed update, and localStorage is not used in this product.
	let dismissedVersion = $state<string | null>(null);
	const showUpdate = $derived(updateAvailable && dismissedVersion !== serverVersion);

	function dismissUpdate() {
		dismissedVersion = serverVersion;
		if (serverVersion) sessionStorage.setItem(UPDATE_DISMISSED_KEY, serverVersion);
	}

	// --- panelin kendi güncellemesi ---------------------------------------------------------
	// Elle tetiklenir. Zamanlanmış bir güncelleme yok: bozuk bir sürüm izlemeyi sessizce
	// körleştirir ve bunu kimse fark etmez, o yüzden "ne zaman" kararı operatörde kalır.
	let hub = $state<HubUpdate | null>(null);
	let guncelleniyor = $state(false);
	let guncellemeNotu = $state<string | null>(null);

	const hubUpdate = $derived(hub?.available && hub.supported ? hub : null);

	async function guncelle() {
		if (!hub?.latest) return;

		const uyari =
			`Panel v${hub.latest} sürümüne güncellenecek.\n\n` +
			'Kurulum sırasında servis birkaç dakika duracak; o süre boyunca izleme yapılmaz ' +
			've bu ekranın bağlantısı kopacak.\n\n' +
			(hub.canRollback
				? 'Yeni sürüm açılmazsa otomatik olarak eskisine dönülür.'
				: '⚠ Bu sürüm için geri dönüş paketi bulunamadı: yeni sürüm açılmazsa elle müdahale gerekir.') +
			'\n\nDevam edilsin mi?';

		if (!confirm(uyari)) return;

		guncelleniyor = true;
		guncellemeNotu = null;
		try {
			const r = await startHubUpdate();
			// Buradan sonra servis kapanacağı için ekrandan başka haber gelmez: ne olacağını
			// şimdi söyle. Bağlantı göstergesi zaten "bağlı değil"e düşecek, sonra dönecek.
			guncellemeNotu =
				`Güncelleme başladı (v${r.version}). Panel birkaç dakika içinde yeniden açılacak; ` +
				'bağlantı geri geldiğinde sürüm başlıkta görünür.';
		} catch (e) {
			guncelleniyor = false;
			guncellemeNotu = e instanceof Error ? e.message : String(e);
		}
	}

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

		dismissedVersion = sessionStorage.getItem(UPDATE_DISMISSED_KEY);

		activePanel.refresh();

		// Sürüm listesi dış bir servisten geliyor; açılışı bekletmesin.
		void fetchHubUpdate().then((u) => (hub = u)).catch(() => (hub = null));

		// Ask before the first alert arrives, not with it.
		await ensureNotificationPermission();
		await realtime.start();
	});

	// The badge describes one hub, so it has to be asked again whenever which hub is on the
	// other end can have changed: the panel was switched, or the link went away and came back.
	// Fetched only on mount, it used to keep showing the previous panel's version — or stay
	// missing for the whole session after one unreachable start.
	$effect(() => {
		const url = activeServer?.url;
		// Read so the effect re-runs when the link recovers; the value itself is not needed.
		void realtime.state;
		if (!url) return;

		void fetchServerVersion(url).then((version) => {
			serverVersion = version;
		});
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
				{#if activeServer}
					<div class="host">
						{activeServer.url.replace(/^https?:\/\//, '')}
						{#if serverVersion}<span class="ver" title={versionTitle}>v{serverVersion}</span>{/if}
						<!-- Only when the banner is not already saying it: the same fact twice reads
					     as two facts. Survives the banner's dismissal, and covers the case the
					     banner does not — a hub older than the app. -->
					{#if mismatched && !showUpdate}
						<span class="ver warn" title={versionTitle}>≠ v{appVersion}</span>
					{/if}
					</div>
				{/if}
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
			<!-- Ölçüldü 2026-08-07 (Android): ⏻ (U+23FB) telefonun yazı tipinde yok ve boş
			     kutu olarak çıkıyordu. Simge yazı tipine bağlı olmasın diye SVG çizildi. -->
			<button class="btn btn-sm" onclick={signOut} title="Çıkış" aria-label="Çıkış">
				<svg class="icon" viewBox="0 0 24 24" aria-hidden="true">
					<path d="M12 3v9" />
					<path d="M6.3 6.3a8 8 0 1 0 11.4 0" />
				</svg>
			</button>
		</div>
	</header>

	{#if showUpdate}
		<!-- Below the header, above the data: an update is worth telling, never worth hiding
		     a measurement behind. Accent colour, not an alert colour — nothing is wrong. -->
		<div class="update">
			<span>
				Panel <strong>v{serverVersion}</strong> sürümünde, uygulamanız v{appVersion}.
			</span>
			<span class="row" style="gap:0.4rem">
				<a class="btn btn-sm" href={releasePageUrl(serverVersion!)} target="_blank" rel="noreferrer">
					İndir
				</a>
				<button class="btn btn-sm" onclick={dismissUpdate} title="Bu oturumda bir daha sorma">
					✕
				</button>
			</span>
		</div>
	{/if}

	{#if hubUpdate || guncellemeNotu}
		<div class="update">
			{#if guncellemeNotu}
				<span>{guncellemeNotu}</span>
			{:else if hubUpdate}
				<span>
					Panel <strong>v{hubUpdate.current}</strong> çalışıyor,
					<strong>v{hubUpdate.latest}</strong> yayınlandı.
					{#if !hubUpdate.canRollback}
						<span class="uyari">⚠ geri dönüş paketi yok</span>
					{/if}
				</span>
				<button class="btn btn-sm" onclick={guncelle} disabled={guncelleniyor}>
					{guncelleniyor ? 'Başlatılıyor…' : 'Güncelle'}
				</button>
			{/if}
		</div>
	{/if}

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

	/* Deliberately quiet: the version is reference material, not a reading. It sits next to
	   the address because that is the pair support asks for — which panel, which build. */
	.ver {
		margin-left: 0.35rem;
		opacity: 0.75;
		font-variant-numeric: tabular-nums;
	}

	/* A lagging app is worth noticing, but it is not an alarm — no alert colour, no pulse. */
	.ver.warn {
		color: var(--warn);
		opacity: 1;
	}

	.update {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.75rem;
		flex-wrap: wrap;
		padding: 0.55rem 0.9rem;
		font-size: 0.82rem;
		border-bottom: 1px solid var(--line);
		background: color-mix(in srgb, var(--accent) 12%, transparent);
	}

	.uyari {
		color: var(--sev-1, #e0a63a);
		font-weight: 600;
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

	/* currentColor: düğme metniyle aynı renk, tema değişince kendiliğinden uyar. */
	.icon {
		width: 1em;
		height: 1em;
		display: block;
		fill: none;
		stroke: currentColor;
		stroke-width: 2.2;
		stroke-linecap: round;
	}
</style>
