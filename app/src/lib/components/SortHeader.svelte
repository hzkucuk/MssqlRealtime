<script lang="ts" generics="T">
	import type { Sorter } from '$lib/sort.svelte';
	import type { TableColumns } from '$lib/table.svelte';

	// A table header cell that sorts and, when given a TableColumns, can be resized by
	// dragging its right edge. Kept as one component so every table gets the same affordance
	// and the same touch target.
	let {
		sorter,
		column,
		label,
		align = 'left',
		columns,
		resizeKey
	}: {
		sorter: Sorter<T>;
		column: string;
		label: string;
		align?: 'left' | 'right';
		columns?: TableColumns;
		resizeKey?: string;
	} = $props();

	const active = $derived(sorter.key === column);
	const key = $derived(resizeKey ?? column);

	let dragging = $state(false);

	function startResize(event: PointerEvent) {
		if (!columns) return;

		// Stop the click reaching the sort button underneath.
		event.preventDefault();
		event.stopPropagation();

		const handle = event.currentTarget as HTMLElement;
		const startX = event.clientX;
		const startWidth = columns.width(key);

		dragging = true;
		// Pointer capture keeps the drag alive when the cursor leaves the thin handle —
		// without it a fast drag drops after a few pixels.
		handle.setPointerCapture(event.pointerId);

		const move = (e: PointerEvent) => columns.setWidth(key, startWidth + (e.clientX - startX));

		const end = () => {
			dragging = false;
			handle.releasePointerCapture(event.pointerId);
			handle.removeEventListener('pointermove', move);
			handle.removeEventListener('pointerup', end);
			handle.removeEventListener('pointercancel', end);
		};

		handle.addEventListener('pointermove', move);
		handle.addEventListener('pointerup', end);
		handle.addEventListener('pointercancel', end);
	}
</script>

<th
	style="text-align:{align}{columns ? `;width:${columns.width(key)}px` : ''}"
	class:resizable={!!columns}
>
	<button
		class:active
		onclick={() => sorter.toggle(column)}
		style="justify-content:{align === 'right' ? 'flex-end' : 'flex-start'}"
	>
		<span class="label">{label}</span>
		<span class="arrow">{sorter.indicator(column)}</span>
	</button>

	{#if columns}
		<span
			class="handle"
			class:dragging
			role="separator"
			aria-orientation="vertical"
			aria-label="{label} sütun genişliği"
			onpointerdown={startResize}
			ondblclick={() => columns.setWidth(key, 120)}
		></span>
	{/if}
</th>

<style>
	th {
		position: relative;
	}

	th.resizable {
		/* Widths only take effect with a fixed layout; otherwise the browser overrides them. */
		overflow: hidden;
	}

	button {
		display: flex;
		align-items: center;
		gap: 0.25rem;
		width: 100%;
		padding: 0.15rem 0;
		background: none;
		border: none;
		color: inherit;
		font: inherit;
		text-transform: inherit;
		letter-spacing: inherit;
		cursor: pointer;
		min-height: 1.8rem;
	}

	button:hover {
		color: var(--text);
	}

	button.active {
		color: var(--accent);
	}

	.label {
		overflow: hidden;
		text-overflow: ellipsis;
		white-space: nowrap;
	}

	.arrow {
		font-size: 0.6rem;
		line-height: 1;
		flex: none;
	}

	.handle {
		position: absolute;
		top: 0;
		right: -3px;
		width: 10px;
		height: 100%;
		cursor: col-resize;
		touch-action: none;
		z-index: 2;
	}

	.handle::after {
		content: '';
		position: absolute;
		top: 20%;
		left: 4px;
		width: 2px;
		height: 60%;
		background: var(--border);
		border-radius: 1px;
	}

	.handle:hover::after,
	.handle.dragging::after {
		background: var(--accent);
		top: 0;
		height: 100%;
	}
</style>
