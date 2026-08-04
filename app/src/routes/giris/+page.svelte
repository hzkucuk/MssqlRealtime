<script lang="ts">
	import { goto } from '$app/navigation';
	import { getServerUrl, login } from '$lib/api/client';

	// The phone has to be told where the backend lives; the browser build defaults to its origin.
	let serverUrl = $state(getServerUrl() || 'http://localhost:5199');
	let email = $state('admin@local');
	let password = $state('');
	let busy = $state(false);
	let error = $state<string | null>(null);

	async function submit(event: SubmitEvent) {
		event.preventDefault();
		busy = true;
		error = null;

		try {
			await login(serverUrl.trim(), email.trim(), password);
			await goto('/');
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}
</script>

<div class="page login">
	<h1>Sunucu İzleme</h1>
	<p class="muted">Kendi izleme sunucunuza bağlanın.</p>

	{#if error}<div class="error">{error}</div>{/if}

	<form onsubmit={submit} class="card">
		<div class="field">
			<label for="url">İzleme sunucusu adresi</label>
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

		<button class="btn btn-primary" style="width:100%" disabled={busy}>
			{busy ? 'Bağlanılıyor…' : 'Giriş yap'}
		</button>
	</form>
</div>

<style>
	.login {
		max-width: 420px;
		padding-top: 3rem;
	}

	.login h1 {
		margin-bottom: 0.2rem;
	}

	.login p {
		margin: 0 0 1rem;
	}
</style>
