import type { UiModule } from '../registry';
import MssqlHome from './MssqlHome.svelte';
import MssqlTarget from './MssqlTarget.svelte';
import MssqlServerForm from './MssqlServerForm.svelte';

/**
 * The MSSQL tool's front end. Everything the shell needs to know about it is here — copy this
 * folder, change the id, and a new tool has screens.
 */
export const mssqlModule: UiModule = {
	id: 'mssql',
	home: MssqlHome,
	target: MssqlTarget,
	targetSettings: MssqlServerForm,
	createTarget: MssqlServerForm
};
