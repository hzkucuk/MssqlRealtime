<script lang="ts">
	import type { TableColumns } from '$lib/table.svelte';

	// Show/hide columns. On a phone this matters more than resizing: a nine-column table will
	// never fit, so choosing the four that matter beats shrinking all nine.
	let { columns, label = 'Sütunlar' }: { columns: TableColumns; label?: string } = $props();

	let open = $state(false);

	// Sürükleyerek sıralama. HTML5 drag&drop dokunmatikte çalışmıyor, o yüzden pointer
	// olaylarıyla yazıldı: aynı kod hem parmakla hem fareyle çalışır.
	let dragKey = $state<string | null>(null);
	let dragOverKey = $state<string | null>(null);

	function startDrag(event: PointerEvent, key: string) {
		// Sadece tutamaçtan başlar; satırın her yerinden sürüklemek onay kutusunu kullanılmaz
		// hale getirirdi.
		(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
		dragKey = key;
	}

	function moveDrag(event: PointerEvent) {
		if (!dragKey) return;

		// Parmağın altındaki satır hedeftir; listeyi tarayıp kimin üstünde olduğumuzu buluruz.
		const items = [...document.querySelectorAll<HTMLElement>('[data-col-key]')];
		const hit = items.find((el) => {
			const box = el.getBoundingClientRect();
			return event.clientY >= box.top && event.clientY <= box.bottom;
		});

		dragOverKey = hit?.dataset.colKey ?? null;
	}

	function endDrag() {
		if (dragKey && dragOverKey && dragKey !== dragOverKey) {
			const to = columns.columns.findIndex((c) => c.key === dragOverKey);
			if (to >= 0) columns.move(dragKey, to);
		}

		dragKey = null;
		dragOverKey = null;
	}
</script>

<div class="picker">
	<button class="btn btn-sm" onclick={() => (open = !open)}>
		⚙ {label}
		{#if columns.hiddenCount > 0}<span class="count">{columns.hiddenCount} gizli</span>{/if}
	</button>

	{#if open}
		<!-- Click-away layer: a menu that only closes via its own button is a trap on touch. -->
		<button
			class="backdrop"
			aria-label="Kapat"
			onclick={() => (open = false)}
			onkeydown={(e) => e.key === 'Escape' && (open = false)}
		></button>

		<div class="menu card">
			<p class="hint muted">Görünürlük için kutuyu işaretleyin, sırayı ⠿ tutamacından sürükleyin.</p>

			{#each columns.columns as column (column.key)}
				<div
					class="item"
					class:locked={column.required}
					class:dragging={dragKey === column.key}
					class:over={dragOverKey === column.key && dragKey !== column.key}
					data-col-key={column.key}
				>
					<button
						class="handle"
						aria-label="{column.label} sütununu taşı"
						onpointerdown={(e) => startDrag(e, column.key)}
						onpointermove={moveDrag}
						onpointerup={endDrag}
						onpointercancel={endDrag}
					>
						⠿
					</button>

					<label class="pick">
						<input
							type="checkbox"
							checked={!column.hidden}
							disabled={column.required}
							onchange={() => columns.toggle(column.key)}
						/>
						{column.label}
						{#if column.required}<span class="muted">(sabit)</span>{/if}
					</label>
				</div>
			{/each}

			<button
				class="btn btn-sm"
				style="width:100%;margin-top:0.4rem"
				onclick={() => {
					columns.reset();
					open = false;
				}}
			>
				Varsayılana dön
			</button>
		</div>
	{/if}
</div>

<style>
	.picker {
		position: relative;
		display: inline-block;
	}

	.count {
		margin-left: 0.3rem;
		font-size: 0.7rem;
		color: var(--accent);
	}

	.backdrop {
		position: fixed;
		inset: 0;
		background: transparent;
		border: none;
		cursor: default;
		z-index: 20;
	}

	.menu {
		position: absolute;
		right: 0;
		top: calc(100% + 0.3rem);
		z-index: 21;
		min-width: 200px;
		/* Ölçüldü 2026-08-07 (telefon): düğme sola yakınken `right: 0` + `min-width` menüyü
		   ekranın soluna taşırıyordu, etiketlerin yarısı kesiliyordu. Genişlik ekranı
		   aşamaz. */
		max-width: min(280px, calc(100vw - 1.5rem));
		max-height: 60vh;
		overflow-y: auto;
		margin: 0;
		padding: 0.5rem;
	}

	/* Dar ekranda açılır menü değil, alttan sayfa: konumu ekrana göre kendi belirlenir,
	   başparmağa yakın durur ve hiçbir kenardan taşamaz. */
	@media (max-width: 560px) {
		.menu {
			position: fixed;
			inset: auto 0.75rem 0.75rem 0.75rem;
			top: auto;
			max-width: none;
			max-height: 55vh;
			border-radius: 14px;
			box-shadow: 0 12px 40px rgb(0 0 0 / 45%);
		}

		/* Sayfa açıkken arkası hafifçe kararsın: nereye dokunulacağı belirsiz kalmasın. */
		.backdrop {
			background: rgb(0 0 0 / 35%);
		}

		.item {
			padding: 0.6rem 0.3rem;
		}
	}

	.item {
		display: flex;
		align-items: center;
		gap: 0.45rem;
		padding: 0.35rem 0.2rem;
		font-size: 0.88rem;
		cursor: pointer;
		border-radius: 6px;
	}

	.item:hover {
		background: var(--surface-2);
	}

	.item.locked {
		opacity: 0.6;
		cursor: default;
	}

	.item input {
		width: auto;
		margin: 0;
	}

	.hint {
		font-size: 0.72rem;
		margin: 0 0 0.4rem;
	}

	.handle {
		background: none;
		border: 0;
		color: var(--muted);
		cursor: grab;
		font-size: 1rem;
		line-height: 1;
		padding: 0.25rem;
		/* Parmak sürüklerken sayfa kaymasın: hareket tutamaca ait. */
		touch-action: none;
	}

	.pick {
		display: flex;
		align-items: center;
		gap: 0.45rem;
		flex: 1;
		cursor: pointer;
	}

	.item.dragging {
		opacity: 0.5;
	}

	/* Bırakılacak yer: üstte ince bir çizgi, satırın tamamını boyamaktan daha az gürültülü. */
	.item.over {
		box-shadow: inset 0 2px 0 var(--accent);
	}
</style>
