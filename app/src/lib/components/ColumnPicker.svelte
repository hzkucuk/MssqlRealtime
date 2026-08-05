<script lang="ts">
	import type { TableColumns } from '$lib/table.svelte';

	// Show/hide columns. On a phone this matters more than resizing: a nine-column table will
	// never fit, so choosing the four that matter beats shrinking all nine.
	let { columns, label = 'Sütunlar' }: { columns: TableColumns; label?: string } = $props();

	let open = $state(false);
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
			{#each columns.columns as column (column.key)}
				<label class="item" class:locked={column.required}>
					<input
						type="checkbox"
						checked={!column.hidden}
						disabled={column.required}
						onchange={() => columns.toggle(column.key)}
					/>
					{column.label}
					{#if column.required}<span class="muted">(sabit)</span>{/if}
				</label>
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
		max-height: 60vh;
		overflow-y: auto;
		margin: 0;
		padding: 0.5rem;
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
</style>
