<script lang="ts">
	/**
	 * A sparkline: the shape of the last N measurements, next to the current number.
	 *
	 * The number alone cannot answer the question you actually have at 03:00 — "is this
	 * climbing or is it always like this?" — and a full chart would not fit on a card. So:
	 * no axes, no grid, no legend, no tooltip. The current value is already displayed as
	 * text beside it; this only carries the trend.
	 *
	 * Colour is the accent, not a status colour: the card's status stripe and dot already
	 * say how bad things are, and repeating that in a third place is noise, not emphasis.
	 */
	let {
		values,
		width = 64,
		height = 22,
		/** Fixed upper bound (100 for a percentage) so the shape is comparable between cards. */
		max,
		/** Stretch to the container's width. The viewBox still uses `width` for the maths;
		 *  a non-scaling stroke keeps the line 1.75px however far it is stretched. */
		fluid = false
	}: {
		values: readonly (number | null | undefined)[];
		width?: number;
		height?: number;
		max?: number;
		fluid?: boolean;
	} = $props();

	const points = $derived(values.filter((v): v is number => typeof v === 'number'));

	const geometry = $derived.by(() => {
		if (points.length < 2) return null;

		const top = max ?? Math.max(...points);
		const bottom = max !== undefined ? 0 : Math.min(...points);
		// A flat series must not become a full-height band from floating-point noise.
		const span = Math.max(top - bottom, 0.001);

		const stepX = width / (points.length - 1);
		// 2px line at 1px stroke inset keeps the stroke inside the box at both extremes.
		const inset = 2;
		const usable = height - inset * 2;

		const coords = points.map((value, i) => {
			const x = i * stepX;
			const y = inset + usable - ((value - bottom) / span) * usable;
			return [x, y] as const;
		});

		return {
			line: coords.map(([x, y], i) => `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`).join(' '),
			area:
				`M0,${height} ` +
				coords.map(([x, y]) => `L${x.toFixed(1)},${y.toFixed(1)}`).join(' ') +
				` L${width},${height} Z`,
			last: coords[coords.length - 1]
		};
	});

	// One id per instance: two sparklines on a page must not share a gradient.
	const gradientId = `spark-${Math.random().toString(36).slice(2, 9)}`;
</script>

{#if geometry}
	<svg
		class="spark"
		class:fluid
		{width}
		{height}
		viewBox="0 0 {width} {height}"
		role="img"
		aria-label="Son {points.length} ölçümün seyri"
		preserveAspectRatio="none"
	>
		<defs>
			<linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
				<stop offset="0%" stop-color="var(--accent)" stop-opacity="0.16" />
				<stop offset="100%" stop-color="var(--accent)" stop-opacity="0" />
			</linearGradient>
		</defs>

		<path d={geometry.area} fill="url(#{gradientId})" />
		<path
			d={geometry.line}
			fill="none"
			stroke="var(--accent)"
			stroke-width="1.75"
			stroke-linejoin="round"
			stroke-linecap="round"
			vector-effect="non-scaling-stroke"
		/>
		<!-- The latest reading gets a dot; it is the one point the eye is looking for. -->
		<circle cx={geometry.last[0]} cy={geometry.last[1]} r="2.1" fill="var(--accent)" />
	</svg>
{:else}
	<!-- Reserve the space so a card does not resize once the second measurement lands. -->
	<span class="placeholder" style="width:{width}px;height:{height}px" aria-hidden="true"></span>
{/if}

<style>
	.spark {
		display: block;
		overflow: visible;
	}

	.spark.fluid {
		width: 100%;
	}

	.placeholder {
		display: block;
	}
</style>
