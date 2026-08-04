import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

// The same build is loaded three ways: inside Tauri on phones and desktops, and served as a
// static site by the .NET host for browsers. adapter-static + SPA fallback is the one output
// that satisfies all three.
export default defineConfig({
	plugins: [
		sveltekit({
			compilerOptions: {
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},
			adapter: adapter({ fallback: 'index.html' })
		})
	],

	// Tauri expects a fixed port and needs to see errors in the terminal.
	clearScreen: false,
	server: {
		port: 1420,
		strictPort: true,
		host: process.env.TAURI_DEV_HOST || false,
		hmr: process.env.TAURI_DEV_HOST
			? { protocol: 'ws', host: process.env.TAURI_DEV_HOST, port: 1421 }
			: undefined,
		watch: { ignored: ['**/src-tauri/**'] }
	}
});
