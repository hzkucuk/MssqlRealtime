import type { UiModule } from '../registry';
import HttpHome from './HttpHome.svelte';
import HttpTargetView from './HttpTargetView.svelte';
import HttpTargetForm from './HttpTargetForm.svelte';

export const httpModule: UiModule = {
	id: 'http',
	home: HttpHome,
	target: HttpTargetView,
	targetSettings: HttpTargetForm,
	createTarget: HttpTargetForm
};
