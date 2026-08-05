<script lang="ts" generics="T">
	import type { Sorter } from '$lib/sort.svelte';

	// A table header cell that sorts. Kept as a component so every table gets the same
	// affordance and the same touch target on a phone.
	let {
		sorter,
		column,
		label,
		align = 'left'
	}: {
		sorter: Sorter<T>;
		column: string;
		label: string;
		align?: 'left' | 'right';
	} = $props();

	const active = $derived(sorter.key === column);
</script>

<th style="text-align:{align}">
	<button class:active onclick={() => sorter.toggle(column)} style="justify-content:{align === 'right' ? 'flex-end' : 'flex-start'}">
		{label}
		<span class="arrow">{sorter.indicator(column)}</span>
	</button>
</th>

<style>
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
		/* Comfortable to hit with a thumb without making the header row tall. */
		min-height: 1.8rem;
	}

	button:hover {
		color: var(--text);
	}

	button.active {
		color: var(--accent);
	}

	.arrow {
		font-size: 0.6rem;
		line-height: 1;
	}
</style>
