<script lang="ts">
	import { goto } from '$app/navigation';
	import {
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
	import { ago } from '$lib/format';

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
			await goto('/');
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
			await goto('/');
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
	<h1>Sunucu İzleme</h1>

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

	.login h1 {
		margin-bottom: 0.8rem;
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
