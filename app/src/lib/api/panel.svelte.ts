import { getActiveServer, type SavedServer } from './client';

/**
 * The active panel as reactive state.
 *
 * `getActiveServer()` reads localStorage, which Svelte cannot track. A `$derived` over it
 * has no dependency to invalidate, so it is computed once and then quietly goes stale —
 * measured 2026-08-09 18:0x: after switching customers the header kept naming the previous
 * one, and whether it happened to update at all came down to render timing.
 *
 * Anything that changes which panel is active calls `refresh()`.
 */
class ActivePanel {
	current = $state<SavedServer | null>(null);

	constructor() {
		this.refresh();
	}

	refresh(): void {
		this.current = getActiveServer();
	}
}

export const activePanel = new ActivePanel();
