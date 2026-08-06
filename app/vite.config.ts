import adapter from '@sveltejs/adapter-static';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

// The version has exactly one source of truth — Directory.Build.props — and the front end
// reads it from there at build time rather than keeping a fourth copy in sync. The project
// has already shipped a release where two copies of the number disagreed.
const propsPath = fileURLToPath(new URL('../Directory.Build.props', import.meta.url));
const version =
	readFileSync(propsPath, 'utf8').match(/<VersionPrefix>([^<]+)<\/VersionPrefix>/)?.[1] ?? '0.0.0';

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

	define: { __APP_VERSION__: JSON.stringify(version) },

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
