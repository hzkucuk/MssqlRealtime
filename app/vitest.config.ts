import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vitest/config';

/**
 * Ön yüz testleri.
 *
 * Neden ayrı bir dosya: uygulama derlemesi Tauri'ye özel sunucu ayarları ve statik adapter
 * taşıyor; testin bunların hiçbirine ihtiyacı yok, ama `.svelte.ts` dosyalarındaki rune'ları
 * derleyecek Svelte eklentisine ihtiyacı var — store'lar `$state` kullanıyor.
 *
 * Neden bugün eklendi: 2026-08-09/10 gecesinde ön yüzde dört ayrı hata bulundu ve hepsi
 * geçici Playwright düzenekleriyle ölçüldü; hiçbiri depoda kalmadı, yani hiçbiri korunmuyordu.
 * `npm run check` yalnız tipleri görür, davranışı görmez.
 */
export default defineConfig({
	// Uygulama derlemesiyle aynı derleyici ayarı: testin ürünün derlendiği şekilde
	// derlenmesi gerekir, yoksa test başka bir şeyi ölçer.
	plugins: [
		sveltekit({
			compilerOptions: {
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			}
		})
	],

	// Svelte 5, istemci tarafı kodunu test ederken tarayıcı koşulunu ister.
	resolve: { conditions: ['browser'] },

	// Uygulama derlemesinde bu sürüm Directory.Build.props'tan okunur; testte önemi yok.
	define: { __APP_VERSION__: '"test"' },

	test: {
		environment: 'jsdom',
		include: ['src/**/*.test.ts']
	}
});
