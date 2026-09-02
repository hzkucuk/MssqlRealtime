<script lang="ts">
	import { onMount } from 'svelte';
	import { api } from '$lib/api/client';

	// Sunucudaki adlar Türkçe ve stabil: enum numarası değişse bile kayıtlı ayar kaymaz.
	type StatementStorage = 'maskeli' | 'tam' | 'kapali';

	const OPTIONS: { value: StatementStorage; title: string; body: string; example: string }[] = [
		{
			value: 'maskeli',
			title: 'Maskeli — değerler saklanmaz',
			body:
				'Sorgunun şekli saklanır, içindeki değerler saklanmaz. Hangi sorgunun yavaş olduğu ' +
				'görülür; kimin TC kimlik numarasını aradığı görülmez.',
			example: "SELECT * FROM Musteri WHERE TCKimlik = ?"
		},
		{
			value: 'tam',
			title: 'Tam metin',
			body:
				'Sorgu olduğu gibi saklanır, değerler dahil. Teşhiste en açık olanı; ölçüm kayıtları ' +
				'iki yıl saklandığı için kişisel veri de o kadar süre diskte kalır.',
			example: "SELECT * FROM Musteri WHERE TCKimlik = '12345678901'"
		},
		{
			value: 'kapali',
			title: 'Saklanmasın',
			body:
				'Kayıtlara sorgu metni hiç yazılmaz. Süre ve sorguyu kimin çalıştırdığı yazılmaya ' +
				'devam eder — rapor "ne kadar sürdü, kim" sorusunu cevaplar, "hangi sorgu" sorusunu ' +
				'cevaplamaz.',
			example: '—'
		}
	];

	let current = $state<StatementStorage>('maskeli');
	let selected = $state<StatementStorage>('maskeli');
	let loading = $state(true);
	let busy = $state(false);
	let saved = $state(false);
	let error = $state<string | null>(null);

	onMount(async () => {
		try {
			const result = await api<{ sorguMetni: StatementStorage }>('/api/gizlilik');
			current = result.sorguMetni;
			selected = result.sorguMetni;
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		} finally {
			loading = false;
		}
	});

	async function save() {
		busy = true;
		saved = false;
		error = null;

		try {
			const result = await api<{ sorguMetni: StatementStorage }>('/api/gizlilik', {
				method: 'PUT',
				body: JSON.stringify({ sorguMetni: selected })
			});
			current = result.sorguMetni;
			selected = result.sorguMetni;
			saved = true;
		} catch (e) {
			// Kaydedilmediği söylenmezse kullanıcı ayarı değişmiş sanır — gizlilikte en kötüsü bu.
			error = e instanceof Error ? e.message : String(e);
		} finally {
			busy = false;
		}
	}
</script>

<div class="page">
	<h1>Gizlilik</h1>
	<p class="muted">
		Sorgu metni kişisel veri taşıyabilir. Bu ayar, panelin <strong>diske yazdığı</strong> metni
		belirler: ölçüm geçmişi (iki yıl saklanır) ve alarm kayıtları.
	</p>

	{#if error}<div class="error">{error}</div>{/if}
	{#if loading}<p class="muted">Yükleniyor…</p>{/if}

	{#if !loading}
		<div class="card">
			<h2>Saklanan sorgu metni</h2>

			{#each OPTIONS as option (option.value)}
				<label class="option" class:on={selected === option.value}>
					<input type="radio" value={option.value} bind:group={selected} />
					<span class="body">
						<strong>{option.title}</strong>
						<span class="muted">{option.body}</span>
						<code>{option.example}</code>
					</span>
				</label>
			{/each}

			<div class="row" style="gap:0.5rem;margin-top:0.6rem">
				<button
					class="btn btn-primary btn-sm"
					disabled={busy || selected === current}
					onclick={save}
				>
					{busy ? 'Kaydediliyor…' : 'Kaydet'}
				</button>
				{#if saved && selected === current}<span class="muted">kaydedildi</span>{/if}
				{#if selected !== current}<span class="muted">kaydedilmedi</span>{/if}
			</div>
		</div>

		<div class="card">
			<h2>Bu ayarın kapsamadıkları</h2>
			<ul class="muted">
				<li>
					<strong>Canlı ekran maskelenmez.</strong> Oturumlar, Çalışan ve Bloke sekmelerinde sorgu
					olduğu gibi görünür — o an sorunu çözen kişinin görmesi gereken şey budur. Bu metin
					diske yazılmaz, oturumla birlikte kaybolur.
				</li>
				<li>
					<strong>Sorguyu kimin çalıştırdığı maskelenmez</strong> (SPID, uygulama, login, makine,
					veritabanı): raporun cevapladığı asıl soru bu.
				</li>
				<li>
					<strong>Geçmiş kayıtlar değişmez.</strong> Ayar bugünden sonra yazılan kayıtlara işler;
					daha önce tam metinle yazılmış satırlar olduğu gibi kalır.
				</li>
				<li>
					<strong>Yorum satırları maskelenmez.</strong> Sorgunun içine yazılmış bir ad
					(<code>-- Ahmet'in raporu</code>) maskelemeden geçer. Maskeleme riski azaltır, sıfırlamaz.
				</li>
			</ul>
		</div>
	{/if}
</div>

<style>
	.option {
		display: flex;
		align-items: flex-start;
		gap: 0.6rem;
		padding: 0.6rem;
		border: 1px solid var(--border);
		border-radius: 10px;
		margin-top: 0.5rem;
		cursor: pointer;
	}

	/* Seçili olan zeminden ayrılır: üç seçenek de metin, tek fark hangisinin açık olduğu. */
	.option.on {
		border-color: var(--accent);
	}

	.option .body {
		display: flex;
		flex-direction: column;
		gap: 0.2rem;
		min-width: 0;
	}

	code {
		font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
		font-size: 0.78rem;
		background: var(--surface-2);
		border-radius: 6px;
		padding: 0.2rem 0.4rem;
		align-self: flex-start;
		max-width: 100%;
		/* Örneğin ayırt edici kısmı sonunda: telefonda yatay kaydırmaya bırakılırsa iki seçenek
		   de aynı görünür ve örnek işe yaramaz. */
		white-space: pre-wrap;
		word-break: break-word;
	}

	ul {
		margin: 0.4rem 0 0;
		padding-left: 1.1rem;
	}

	li + li {
		margin-top: 0.4rem;
	}
</style>
