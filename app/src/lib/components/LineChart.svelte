<script lang="ts">
	/**
	 * Zaman serisi çizgi grafiği — inline SVG, dış kütüphane yok.
	 *
	 * Renkler `dataviz` doğrulayıcısıyla hesaplandı, göz kararı değil (2026-08-07):
	 *   koyu  #4f8ff0 / #d75f9e → en kötü CVD çifti ΔE 13,1 · normal görme 23,3
	 *   açık  #2570e8 / #c02d7d → en kötü CVD çifti ΔE 18,1 · normal görme 28,3
	 * Beş kontrolün beşi de geçiyor. Durum renkleri (yeşil/sarı/kırmızı) seri rengi olarak
	 * KULLANILMAZ: bu üründe onlar ölçülmüş durumu anlatır, seri kimliğini değil.
	 */
	type Series = {
		label: string;
		/** null = ölçüm yok. Sıfır değil — sunucuya ulaşılamadığı dakika boş kalmalı. */
		values: (number | null)[];
	};

	/**
	 * Çizgi / alan / sütun. Üçü de aynı veriyi anlatır; hangisinin doğru olduğu soruya bağlı:
	 * çizgi eğilim, alan hacim, sütun tek tek dönemleri karşılaştırma içindir.
	 */
	type ChartKind = 'line' | 'area' | 'bar';

	let {
		title,
		times,
		series,
		unit = '',
		max = null,
		height = 180,
		kind = 'line'
	}: {
		title: string;
		times: string[];
		series: Series[];
		unit?: string;
		/** Sabit tavan (yüzdelerde 100). null ise veriden hesaplanır. */
		max?: number | null;
		height?: number;
		kind?: ChartKind;
	} = $props();

	const PAD = { top: 8, right: 8, bottom: 20, left: 34 };

	let width = $state(320);
	let hover = $state<number | null>(null);

	const count = $derived(times.length);
	const hasData = $derived(series.some((s) => s.values.some((v) => v !== null)));

	const ceiling = $derived.by(() => {
		if (max !== null) return max;
		const values = series.flatMap((s) => s.values).filter((v): v is number => v !== null);
		const top = values.length ? Math.max(...values) : 0;
		// Tavanı biraz yukarı yuvarla: çizgi kutunun tepesine yapışmasın.
		return top <= 0 ? 1 : Math.ceil((top * 1.15) / 5) * 5;
	});

	const plotW = $derived(Math.max(10, width - PAD.left - PAD.right));
	const plotH = $derived(Math.max(10, height - PAD.top - PAD.bottom));

	function x(i: number): number {
		return PAD.left + (count <= 1 ? plotW / 2 : (i / (count - 1)) * plotW);
	}

	function y(value: number): number {
		return PAD.top + plotH - (value / ceiling) * plotH;
	}

	/** Ölçüm olmayan aralıkta çizgi kopar — boşluğu doğru düz çizgiyle örtmek yalan olurdu. */
	function path(values: (number | null)[]): string {
		let d = '';
		let pen = false;

		values.forEach((v, i) => {
			if (v === null) {
				pen = false;
				return;
			}

			d += `${pen ? 'L' : 'M'}${x(i).toFixed(1)} ${y(v).toFixed(1)} `;
			pen = true;
		});

		return d.trim();
	}

	/** Alan dolgusu: çizginin altını tabana kadar kapatır, boşluklarda kopar. */
	function areaPath(values: (number | null)[]): string {
		const base = PAD.top + plotH;
		let d = '';
		let start: number | null = null;

		values.forEach((v, i) => {
			if (v === null) {
				if (start !== null) d += `L${x(i - 1).toFixed(1)} ${base.toFixed(1)} Z `;
				start = null;
				return;
			}

			if (start === null) {
				start = i;
				d += `M${x(i).toFixed(1)} ${base.toFixed(1)} L${x(i).toFixed(1)} ${y(v).toFixed(1)} `;
			} else {
				d += `L${x(i).toFixed(1)} ${y(v).toFixed(1)} `;
			}
		});

		if (start !== null) d += `L${x(values.length - 1).toFixed(1)} ${base.toFixed(1)} Z`;
		return d.trim();
	}

	// Sütun genişliği: bir yılın günlerinde bile en az 1 piksel kalsın, aralarında 1 piksel
	// boşlukla — bitişik sütunlar tek blok gibi okunur.
	const barWidth = $derived(Math.max(1, plotW / Math.max(1, count) - 1));

	// Dört yatay çizgi yeter: ızgara okumaya yardım eder, veriyle yarışmaz.
	const gridLines = $derived([0, 0.25, 0.5, 0.75, 1].map((f) => ({
		value: ceiling * f,
		y: PAD.top + plotH - f * plotH
	})));

	// Eksen etiketi: ilk, orta ve son. Telefonda daha fazlası üst üste biner.
	const timeLabels = $derived.by(() => {
		if (count === 0) return [];
		const picks = count < 3 ? [0] : [0, Math.floor((count - 1) / 2), count - 1];
		return picks.map((i) => ({ i, label: shortTime(times[i]) }));
	});

	function shortTime(iso: string): string {
		const d = new Date(iso);
		const span = count > 1 ? new Date(times[count - 1]).getTime() - new Date(times[0]).getTime() : 0;
		const days = span / 86_400_000;

		// Bir günlük pencerede saat, daha genişinde tarih: eksende hangi bilginin ayırt edici
		// olduğu pencereye göre değişir.
		return days <= 2
			? d.toLocaleTimeString('tr', { hour: '2-digit', minute: '2-digit' })
			: d.toLocaleDateString('tr', { day: '2-digit', month: '2-digit' });
	}

	function onMove(event: PointerEvent) {
		const box = (event.currentTarget as SVGElement).getBoundingClientRect();
		const rel = ((event.clientX - box.left) / box.width) * width;
		const i = Math.round(((rel - PAD.left) / plotW) * (count - 1));
		hover = count === 0 ? null : Math.min(count - 1, Math.max(0, i));
	}

	function fmt(v: number | null): string {
		if (v === null) return '—';
		return `${v.toLocaleString('tr', { maximumFractionDigits: 1 })}${unit}`;
	}
</script>

<figure class="chart" bind:clientWidth={width}>
	<figcaption>
		<span class="title">{title}</span>

		<!-- İki ve daha fazla seride gösterge şart: kimlik yalnız renge bırakılmaz. -->
		{#if series.length > 1}
			<span class="legend">
				{#each series as s, i (s.label)}
					<span class="key"><i class="swatch s{i}"></i>{s.label}</span>
				{/each}
			</span>
		{/if}
	</figcaption>

	{#if !hasData}
		<p class="empty">Henüz veri yok — ölçümler dakikada bir birikiyor.</p>
	{:else}
		<svg
			viewBox="0 0 {width} {height}"
			style="height:{height}px"
			role="img"
			aria-label="{title}: {series.map((s) => `${s.label} ${fmt(s.values.at(-1) ?? null)}`).join(', ')}"
			onpointermove={onMove}
			onpointerleave={() => (hover = null)}
		>
			{#each gridLines as line (line.y)}
				<line class="grid" x1={PAD.left} x2={width - PAD.right} y1={line.y} y2={line.y} />
				<text class="tick" x={PAD.left - 6} y={line.y + 3} text-anchor="end">
					{Math.round(line.value)}
				</text>
			{/each}

			{#each timeLabels as t (t.i)}
				<!-- Uçtaki etiketler ortalanırsa yarısı çizim alanının dışında kalır; ilk sola,
				     son sağa yaslanır. -->
				<text
					class="tick"
					x={t.i === 0 ? PAD.left : t.i === count - 1 ? width - PAD.right : x(t.i)}
					y={height - 6}
					text-anchor={t.i === 0 ? 'start' : t.i === count - 1 ? 'end' : 'middle'}
				>
					{t.label}
				</text>
			{/each}

			{#each series as s, i (s.label)}
				{#if kind === 'bar'}
					{#each s.values as v, j (j)}
						{#if v !== null}
							<rect
								class="bar s{i}"
								x={x(j) - barWidth / 2 + (series.length > 1 ? (i - 0.5) * (barWidth / 2) : 0)}
								y={y(v)}
								width={series.length > 1 ? barWidth / 2 : barWidth}
								height={Math.max(0, PAD.top + plotH - y(v))}
							/>
						{/if}
					{/each}
				{:else}
					{#if kind === 'area'}
						<path class="area s{i}" d={areaPath(s.values)} />
					{/if}
					<path class="line s{i}" d={path(s.values)} />
				{/if}
			{/each}

			{#if hover !== null}
				<line class="cursor" x1={x(hover)} x2={x(hover)} y1={PAD.top} y2={PAD.top + plotH} />
				{#each series as s, i (s.label)}
					{#if s.values[hover] !== null && s.values[hover] !== undefined}
						<circle class="marker s{i}" cx={x(hover)} cy={y(s.values[hover]!)} r="4" />
					{/if}
				{/each}
			{/if}
		</svg>

		<!-- İmleç değerleri SVG'nin dışında: metin, metin belirteçlerini giyer; seri rengini
		     yalnız yanındaki nokta taşır. -->
		<div class="readout" class:visible={hover !== null}>
			{#if hover !== null}
				<span class="muted">{new Date(times[hover]).toLocaleString('tr')}</span>
				{#each series as s, i (s.label)}
					<span class="key"><i class="swatch s{i}"></i>{s.label}: <strong>{fmt(s.values[hover] ?? null)}</strong></span>
				{/each}
			{/if}
		</div>
	{/if}
</figure>

<style>
	.chart {
		margin: 0 0 0.75rem;
	}

	figcaption {
		display: flex;
		align-items: baseline;
		justify-content: space-between;
		flex-wrap: wrap;
		gap: 0.4rem;
		margin-bottom: 0.3rem;
	}

	.title {
		font-weight: 600;
		font-size: 0.9rem;
	}

	.legend,
	.readout {
		display: flex;
		gap: 0.75rem;
		flex-wrap: wrap;
		font-size: 0.75rem;
		color: var(--muted);
	}

	.key {
		display: inline-flex;
		align-items: center;
		gap: 0.3rem;
	}

	.swatch {
		width: 0.65rem;
		height: 0.65rem;
		border-radius: 2px;
		display: inline-block;
	}

	/* Doğrulanmış seri renkleri — koyu tema. */
	.swatch.s0 { background: #4f8ff0; }
	.swatch.s1 { background: #d75f9e; }
	:global(.line.s0) { stroke: #4f8ff0; }
	:global(.line.s1) { stroke: #d75f9e; }
	:global(.marker.s0) { fill: #4f8ff0; }
	:global(.marker.s1) { fill: #d75f9e; }

	/* Açık tema kendi adımlarını alır — otomatik çevirme değil, ayrı seçim. */
	@media (prefers-color-scheme: light) {
		.swatch.s0 { background: #2570e8; }
		.swatch.s1 { background: #c02d7d; }
		:global(.line.s0) { stroke: #2570e8; }
		:global(.line.s1) { stroke: #c02d7d; }
		:global(.marker.s0) { fill: #2570e8; }
		:global(.marker.s1) { fill: #c02d7d; }
	}

	svg {
		width: 100%;
		display: block;
		touch-action: pan-y;
	}

	:global(.chart .line) {
		fill: none;
		stroke-width: 2;
		stroke-linejoin: round;
		stroke-linecap: round;
	}

	:global(.chart .marker) {
		/* Yüzeyle 2px halka: çizgiler üst üste geldiğinde nokta kaybolmasın. */
		stroke: var(--surface);
		stroke-width: 2;
	}

	.grid {
		stroke: var(--border);
		stroke-width: 1;
	}

	.cursor {
		stroke: var(--border-strong);
		stroke-width: 1;
	}

	.tick {
		fill: var(--muted);
		font-size: 10px;
	}

	.readout {
		min-height: 1.1rem;
		margin-top: 0.2rem;
		visibility: hidden;
	}

	.readout.visible {
		visibility: visible;
	}

	.empty {
		color: var(--muted);
		font-size: 0.85rem;
		margin: 0.5rem 0 1rem;
	}

	:global(.chart .area) {
		stroke: none;
		fill-opacity: 0.16;
	}

	:global(.chart .bar) {
		stroke: none;
	}

	:global(.area.s0), :global(.bar.s0) { fill: #4f8ff0; }
	:global(.area.s1), :global(.bar.s1) { fill: #d75f9e; }

	@media (prefers-color-scheme: light) {
		:global(.area.s0), :global(.bar.s0) { fill: #2570e8; }
		:global(.area.s1), :global(.bar.s1) { fill: #c02d7d; }
	}
</style>
