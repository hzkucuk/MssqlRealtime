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

	// 🔴 Ölçüldü 2026-08-07: menüyü açtıktan sonra ölçüp ekrana sığdırmak iki kez denendi
	// (effect içinde ve iki kare sonra) ve ikisinde de menü sağ kenardan taştı — ölçüm her
	// zaman son boyuttan dar çıkıyor. Ölçüm bırakıldı: imleç ekranın sağ yarısındaysa menü
	// SOLA doğru açılıyor, alt yarısındaysa YUKARI. Bu, hiçbir ölçüme bağlı değil ve
	// masaüstü menülerinin de yaptığı şey.
	const flipX = $derived(typeof window !== 'undefined' && x > window.innerWidth * 0.55);
	const flipY = $derived(typeof window !== 'undefined' && y > window.innerHeight * 0.6);

	const transform = $derived(
		`translate(${flipX ? '-100%' : '0'}, ${flipY ? '-100%' : '0'})`
	);
</script>

<svelte:window
	onkeydown={(e) => e.key === 'Escape' && onclose()}
	onresize={onclose}
	onscroll={onclose}
/>

<!-- Tıklama yakalayıcı: menünün dışına dokunmak kapatır. -->
<button class="backdrop" aria-label="Menüyü kapat" onclick={onclose} oncontextmenu={(e) => { e.preventDefault(); onclose(); }}
></button>

<div class="menu card" style="left:{x}px; top:{y}px; transform:{transform}" role="menu">
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
