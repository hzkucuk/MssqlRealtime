<script lang="ts">
	/**
	 * İmlecin bulunduğu yerde açılan komut menüsü.
	 *
	 * Neden sağ tık: bu komutların çoğu her satırda ve her sütunda anlamlı, ama hepsini
	 * düğme olarak koymak tabloyu komut çubuğuna çevirirdi. Menü yalnız istendiğinde
	 * görünür ve hiç yer kaplamaz.
	 *
	 * Dokunmatikte sağ tık yoktur; çağıran taraf uzun basmayı da bu menüye bağlar.
	 */
	export type MenuItem =
		| { kind?: 'item'; label: string; run: () => void; disabled?: boolean }
		| { kind: 'separator' };

	let {
		x,
		y,
		items,
		onclose
	}: { x: number; y: number; items: MenuItem[]; onclose: () => void } = $props();

	let menu = $state<HTMLDivElement | null>(null);

	// Ölçüm yapılana kadar imlecin olduğu yerde duruyor; effect içinde ekrana sığdırılıyor.
	let left = $state(0);
	let top = $state(0);

	// Menü ekranın dışına taşarsa kullanılamaz olur; ölçüp içeri çekiyoruz. Sütunlar
	// menüsünde tam olarak bu hata yaşandı (2026-08-07).
	$effect(() => {
		if (!menu) {
			return;
		}

		const box = menu.getBoundingClientRect();
		const pad = 8;

		left = Math.max(pad, Math.min(x, window.innerWidth - box.width - pad));
		top = Math.max(pad, Math.min(y, window.innerHeight - box.height - pad));
	});
</script>

<svelte:window
	onkeydown={(e) => e.key === 'Escape' && onclose()}
	onresize={onclose}
	onscroll={onclose}
/>

<!-- Tıklama yakalayıcı: menünün dışına dokunmak kapatır. -->
<button class="backdrop" aria-label="Menüyü kapat" onclick={onclose} oncontextmenu={(e) => { e.preventDefault(); onclose(); }}
></button>

<div class="menu card" bind:this={menu} style="left:{left}px; top:{top}px" role="menu">
	{#each items as item, i (i)}
		{#if item.kind === 'separator'}
			<hr />
		{:else}
			<button
				class="entry"
				role="menuitem"
				disabled={item.disabled}
				onclick={() => {
					item.run();
					onclose();
				}}
			>
				{item.label}
			</button>
		{/if}
	{/each}
</div>

<style>
	.backdrop {
		position: fixed;
		inset: 0;
		background: transparent;
		border: none;
		padding: 0;
		z-index: 60;
		cursor: default;
	}

	.menu {
		position: fixed;
		z-index: 61;
		min-width: 190px;
		max-width: min(280px, calc(100vw - 1rem));
		padding: 0.3rem;
		box-shadow: 0 12px 40px rgb(0 0 0 / 45%);
	}

	.entry {
		display: block;
		width: 100%;
		text-align: left;
		background: none;
		border: 0;
		color: inherit;
		font: inherit;
		font-size: 0.86rem;
		padding: 0.5rem 0.6rem;
		border-radius: 6px;
		cursor: pointer;
	}

	.entry:hover:not(:disabled) {
		background: var(--surface-2);
	}

	.entry:disabled {
		opacity: 0.45;
		cursor: default;
	}

	hr {
		border: 0;
		border-top: 1px solid var(--border);
		margin: 0.25rem 0.3rem;
	}
</style>
