<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';
	import { ago, dateTime } from '$lib/format';

	type AgentInfo = {
		id: string;
		name: string;
		machineName?: string | null;
		version?: string | null;
		operatingSystem?: string | null;
		isConnected: boolean;
		lastSeenUtc?: string | null;
		registeredAtUtc?: string | null;
		assignedTargets: number;
		hasEverConnected: boolean;
	};

	let agents = $state<AgentInfo[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let newName = $state('');
	let busy = $state(false);

	// Shown exactly once, right after creation — the server stores only a hash.
	let issuedKey = $state<{ name: string; key: string } | null>(null);

	onMount(load);

	async function load() {
		loading = true;
		try {
			agents = await api<AgentInfo[]>('/api/agents');
			error = null;
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
		}
	}

	async function create() {
		if (!newName.trim()) return;
		busy = true;

		try {
			const result = await api<{ name: string; enrollmentKey: string }>('/api/agents', {
				method: 'POST',
				body: JSON.stringify({ name: newName.trim() })
			});

			issuedKey = { name: result.name, key: result.enrollmentKey };
			newName = '';
			await load();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}

	async function rotate(agent: AgentInfo) {
		if (!confirm(`${agent.name} için yeni anahtar üretilsin mi? Eski anahtar geçersiz olur.`)) return;

		busy = true;
		try {
			const result = await api<{ enrollmentKey: string }>(`/api/agents/${agent.id}/rotate-key`, {
				method: 'POST'
			});
			issuedKey = { name: agent.name, key: result.enrollmentKey };
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}

	async function remove(agent: AgentInfo) {
		if (!confirm(`${agent.name} silinsin mi? Bu agent'a atanmış sunucular merkeze geri döner.`)) return;

		busy = true;
		try {
			await api(`/api/agents/${agent.id}`, { method: 'DELETE' });
			await load();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}
</script>

<div class="page">
	<h1>Agent'lar</h1>
	<p class="muted">
		Müşteri sunucusuna erişemediğin durumlar için: oraya kurulan küçük bir servis <em>dışarı
		doğru</em> bağlanır, yerelde ölçer ve sonucu buraya gönderir. Müşteri güvenlik duvarında
		hiçbir port açılmaz.
	</p>

	{#if error}<div class="error">{error}</div>{/if}

	{#if issuedKey}
		<div class="card key-box">
			<h3>🔑 {issuedKey.name} — kayıt anahtarı</h3>
			<p class="muted" style="margin:0.3rem 0">
				Bu anahtar <strong>bir daha gösterilmeyecek</strong>. Agent'ın
				<code>appsettings.json</code> dosyasına şimdi kaydedin.
			</p>
			<pre class="key">{issuedKey.key}</pre>
			<button class="btn btn-sm" onclick={() => (issuedKey = null)}>Kaydettim, kapat</button>
		</div>
	{/if}

	<div class="card">
		<h3>Yeni agent</h3>
		<div class="field">
			<label for="name">Ad</label>
			<input id="name" bind:value={newName} placeholder="Acme Ltd. — SQL sunucusu" />
		</div>
		<button class="btn btn-primary" onclick={create} disabled={busy || !newName.trim()}>
			Oluştur ve anahtar üret
		</button>
	</div>

	{#if loading}<p class="muted">Yükleniyor…</p>{/if}

	{#each agents as agent (agent.id)}
		<div class="card">
			<div class="row between">
				<div class="row" style="min-width:0">
					<span class="dot {agent.isConnected ? 'sev-0' : 'sev-2'}"></span>
					<div style="min-width:0">
						<strong>{agent.name}</strong>
						<div class="muted">
							{#if agent.isConnected}
								bağlı · {agent.machineName ?? '—'}
							{:else if agent.hasEverConnected}
								bağlı değil · son görülme {ago(agent.lastSeenUtc)}
							{:else}
								<span style="color:var(--warn)">henüz hiç bağlanmadı</span>
							{/if}
						</div>
					</div>
				</div>
				<div style="text-align:right" class="muted">
					{agent.assignedTargets} sunucu
					{#if agent.version}<div>v{agent.version}</div>{/if}
				</div>
			</div>

			{#if agent.operatingSystem}
				<div class="muted" style="margin-top:0.3rem;font-size:0.78rem">{agent.operatingSystem}</div>
			{/if}

			{#if !agent.hasEverConnected}
				<div class="notice" style="margin-top:0.5rem">
					Agent'ı müşteri sunucusuna kurun, <code>appsettings.json</code> içine bu sunucunun
					adresini ve kayıt anahtarını yazın, servisi başlatın.
				</div>
			{/if}

			<div class="row" style="gap:0.5rem;margin-top:0.5rem">
				<button class="btn btn-sm" onclick={() => rotate(agent)} disabled={busy}>
					Yeni anahtar
				</button>
				<button class="btn btn-sm btn-danger" onclick={() => remove(agent)} disabled={busy}>
					Sil
				</button>
			</div>
		</div>
	{/each}

	{#if !loading && agents.length === 0}
		<p class="muted">
			Henüz agent yok. Merkezden erişilebilen sunucular için gerekmez — yalnız NAT veya
			güvenlik duvarı arkasındakiler için.
		</p>
	{/if}
</div>

<style>
	.key-box {
		border-color: var(--accent);
	}

	.key {
		background: var(--surface-2);
		border-radius: 8px;
		padding: 0.6rem;
		margin: 0.4rem 0;
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
		font-size: 0.8rem;
		word-break: break-all;
		white-space: pre-wrap;
		user-select: all;
	}

	code {
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
		font-size: 0.85em;
	}
</style>
