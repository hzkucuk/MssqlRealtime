import type { Component } from 'svelte';
import { httpModule } from './http';
import { mssqlModule } from './mssql';

/**
 * Front-end half of a tool.
 *
 * The backend says which tools exist (`GET /api/modules`); this registry says how to draw
 * them. A tool with no entry here still appears on the dashboard, greyed out — which is what
 * you want when the phone has not been updated yet but the server already has a new module.
 */
export type UiModule = {
	/** Must match IToolModule.Id on the server. */
	id: string;

	/** Full-screen view for the module. Receives no props; reads the route itself. */
	home: Component;

	/** Optional detail view for one target inside the module. */
	target?: Component;

	/** Optional settings view for one target. */
	targetSettings?: Component;

	/** Optional "add a target" view. */
	createTarget?: Component;
};

const modules: UiModule[] = [mssqlModule, httpModule];

export const uiModules = new Map(modules.map((m) => [m.id, m]));

export function findUiModule(id: string): UiModule | undefined {
	return uiModules.get(id);
}
