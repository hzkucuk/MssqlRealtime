<script lang="ts">
	import { goto } from '$app/navigation';
	import {
		appVersion,
		fetchCaptcha,
		getServers,
		getServerUrl,
		isCaptchaRequired,
		login,
		removeServer,
		setActiveServer,
		type CaptchaChallenge,
		type SavedServer
	} from '$lib/api/client';
	import { activePanel } from '$lib/api/panel.svelte';
	import { realtime } from '$lib/api/realtime.svelte';
	import { mssql } from '$lib/modules/mssql/store.svelte';
	import { http } from '$lib/modules/http/store.svelte';
	import { ago } from '$lib/format';

	// Everything on screen belongs to one customer's panel. Switching customers therefore
	// has to drop the old panel's data and re-point the live link before the shell renders
	// again — measured 2026-08-09: without this the socket stayed open against the previous
	// hub while the header already showed the new customer's name.
	async function enterActivePanel() {
		activePanel.refresh();
		mssql.reset();
		http.reset();
		await realtime.switchPanel();
		await goto('/');
	}

	// One hub per customer is the normal deployment, so signing in starts with "which
	// customer" rather than with an empty address box.
	let servers = $state<SavedServer[]>(getServers());
	let showForm = $state(getServers().length === 0);

	let label = $state('');
	let serverUrl = $state(getServerUrl() || 'https://');
	let email = $state('');
	let password = $state('');
	let busy = $state(false);
	let error = $state<string | null>(null);

	// Only shown once an address has failed a couple of times: a captcha on every sign-in
	// costs the operator — who is often on a phone, at 03:00, during an incident — and buys
	// nothing against a bot that has not started guessing yet.
	let captcha = $state<CaptchaChallenge | null>(null);
	let captchaAnswer = $state('');

	async function loadCaptcha() {
		try {
			captcha = await fetchCaptcha(serverUrl);
			captchaAnswer = '';
		} catch {
			captcha = null;
		}
	}

	async function submit(event: SubmitEvent) {
		event.preventDefault();
		busy = true;
		error = null;

		try {
			await login(
				serverUrl,
				email.trim(),
				password,
				label.trim(),
				captcha ? { token: captcha.token, answer: captchaAnswer } : undefined
			);
			await enterActivePanel();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);

			// A used or rejected challenge is spent — always issue a fresh one.
			if (await isCaptchaRequired(serverUrl)) {
				await loadCaptcha();
			}
		} finally {
			busy = false;
		}
	}

	async function use(server: SavedServer) {
		setActiveServer(server.url);

		// A stored session means straight in; otherwise the form opens pre-filled.
		if (server.tokens) {
			await enterActivePanel();
			return;
		}

		label = server.label;
		serverUrl = server.url;
		showForm = true;

		if (await isCaptchaRequired(server.url)) {
			await loadCaptcha();
		}
	}

	function forget(server: SavedServer, event: MouseEvent) {
		event.stopPropagation();
		if (!confirm(`${server.label} listeden kaldırılsın mı?`)) return;

		removeServer(server.url);
		servers = getServers();
		if (servers.length === 0) showForm = true;
	}

	function addNew() {
		label = '';
		serverUrl = 'https://';
		email = '';
		password = '';
		captcha = null;
		showForm = true;
	}
</script>

<div class="page login">
	<!-- The one screen in the product with no data on it, so the one place a mark can breathe.
	     The trace is the same motif as the favicon: a pulse. It draws itself once, on arrival —
	     the product's whole promise in one gesture — then stops. It does not loop, because a
	     loop would compete with the alarm pulse that actually means something. -->
	<header class="hero">
		<svg class="mark" viewBox="0 0 220 56" role="img" aria-label="Sunucu İzleme">
			<defs>
				<linearGradient id="trace" x1="0" y1="0" x2="1" y2="0">
					<stop offset="0%" stop-color="var(--accent-soft)" />
					<stop offset="100%" stop-color="var(--accent)" />
				</linearGradient>
			</defs>
			<path
				class="baseline"
				d="M2 28 H70 l10 0 M150 28 H218"
				fill="none"
				stroke="var(--border)"
				stroke-width="1.5"
			/>
			<path
				class="pulse"
				d="M70 28 h12 l7 -18 l9 36 l8 -26 l7 8 h37"
				fill="none"
				stroke="url(#trace)"
				stroke-width="2.5"
				stroke-linecap="round"
				stroke-linejoin="round"
			/>
		</svg>

		<h1>Sunucu İzleme</h1>
		<p class="tagline">Sunucularınız ne yapıyor — şu anda.</p>
		<!-- The build in your hand. The hub's own version shows in the header after sign-in;
		     before that there is no hub to ask. -->
		<p class="build">v{appVersion}</p>
	</header>

	{#if error}<div class="error">{error}</div>{/if}

	{#if servers.length > 0}
		<p class="muted">Kayıtlı paneller</p>

		{#each servers as server (server.url)}
			<button class="card entry" onclick={() => use(server)}>
				<span style="flex:1;min-width:0">
					<strong>{server.label}</strong>
					<div class="muted url">{server.url}</div>
					<div class="muted">
						{#if server.tokens}
							oturum açık · {ago(new Date(server.lastUsedAt).toISOString())}
						{:else}
							giriş gerekiyor
						{/if}
					</div>
				</span>
				<span
					class="forget"
					role="button"
					tabindex="0"
					onclick={(e) => forget(server, e)}
					onkeydown={(e) => e.key === 'Enter' && forget(server, e as unknown as MouseEvent)}
					title="Listeden kaldır"
				>
					✕
				</span>
			</button>
		{/each}

		{#if !showForm}
			<button class="btn" style="width:100%;margin-top:0.5rem" onclick={addNew}>
				+ Başka bir panel ekle
			</button>
		{/if}
	{/if}

	{#if showForm}
		<form onsubmit={submit} class="card" style="margin-top:0.7rem">
			<h3>{servers.length > 0 ? 'Yeni panel' : 'Bağlan'}</h3>

			<div class="field">
				<label for="label">Müşteri / etiket</label>
				<input id="label" bind:value={label} placeholder="Acme Ltd." />
			</div>

			<div class="field">
				<label for="url">Panel adresi</label>
				<input id="url" bind:value={serverUrl} placeholder="https://izleme.firma.com" required />
			</div>

			<div class="field">
				<label for="email">Kullanıcı</label>
				<input id="email" type="email" bind:value={email} autocomplete="username" required />
			</div>

			<div class="field">
				<label for="password">Parola</label>
				<!-- Never drafted to storage, unlike other forms in the app. -->
				<input
					id="password"
					type="password"
					bind:value={password}
					autocomplete="current-password"
					required
				/>
			</div>

			{#if captcha}
				<div class="field">
					<label for="captcha">Güvenlik kodu</label>
					<div class="captcha">
						<!-- eslint-disable-next-line svelte/no-at-html-tags -->
						<div class="image">{@html captcha.svg}</div>
						<button type="button" class="btn btn-sm" onclick={loadCaptcha} title="Yenile">↻</button>
					</div>
					<input
						id="captcha"
						bind:value={captchaAnswer}
						placeholder="Yukarıdaki kodu yazın"
						autocomplete="off"
						autocapitalize="characters"
						spellcheck="false"
						required
					/>
					<div class="muted help">
						Birkaç başarısız denemeden sonra istenir. Büyük/küçük harf farkı yok.
					</div>
				</div>
			{/if}

			<button class="btn btn-primary" style="width:100%" disabled={busy}>
				{busy ? 'Bağlanılıyor…' : 'Giriş yap'}
			</button>
		</form>
	{/if}
</div>

<style>
	.login {
		max-width: 420px;
		padding-top: 2.5rem;
	}

	.hero {
		text-align: center;
		margin-bottom: 1.6rem;
	}

	.mark {
		width: 200px;
		height: 52px;
		margin: 0 auto 0.5rem;
		display: block;
		/* The glow is on the mark only — it is what makes this feel like a title screen
		   rather than a form, and it costs nothing on any screen that shows data. */
		filter: drop-shadow(0 0 14px color-mix(in srgb, var(--accent) 35%, transparent));
	}

	.pulse {
		stroke-dasharray: 190;
		stroke-dashoffset: 190;
		animation: draw 1.1s cubic-bezier(0.22, 1, 0.36, 1) 0.15s forwards;
	}

	@keyframes draw {
		to {
			stroke-dashoffset: 0;
		}
	}

	.login h1 {
		margin: 0 0 0.25rem;
		font-size: 1.6rem;
		letter-spacing: -0.02em;
		/* Gradient ink on the wordmark alone. Applied to body text it would hurt contrast;
		   on 1.6rem semibold at this pair's lightness it stays legible. */
		background: linear-gradient(100deg, var(--text) 20%, var(--accent) 130%);
		-webkit-background-clip: text;
		background-clip: text;
		color: transparent;
	}

	.tagline {
		margin: 0;
		color: var(--muted);
		font-size: 0.88rem;
	}

	/* Faint on purpose: on the cover screen the version is a footnote, not a headline. */
	.build {
		margin: 0.35rem 0 0;
		color: var(--muted);
		opacity: 0.6;
		font-size: 0.72rem;
		font-variant-numeric: tabular-nums;
	}

	@media (prefers-reduced-motion: reduce) {
		.pulse {
			animation: none;
			stroke-dashoffset: 0;
		}
	}

	.entry {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		width: 100%;
		text-align: left;
		cursor: pointer;
		font: inherit;
		color: inherit;
	}

	.url {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
		font-size: 0.8rem;
	}

	.forget {
		color: var(--muted);
		padding: 0.3rem 0.5rem;
		border-radius: 6px;
		flex: none;
	}

	.forget:hover {
		color: var(--crit);
		background: var(--surface-2);
	}

	.captcha {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		margin-bottom: 0.4rem;
	}

	.image {
		line-height: 0;
		border-radius: 8px;
		overflow: hidden;
		/* The SVG is fixed-size; let it shrink on narrow phones rather than overflow. */
		max-width: 100%;
	}

	.image :global(svg) {
		max-width: 100%;
		height: auto;
	}

	.help {
		margin-top: 0.25rem;
		font-size: 0.78rem;
	}
</style>
