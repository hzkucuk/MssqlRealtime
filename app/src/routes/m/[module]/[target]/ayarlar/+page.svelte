<script lang="ts">
	import { page } from '$app/state';
	import { findUiModule } from '$lib/modules/registry';

	const module = $derived(findUiModule(page.params.module!));
</script>

{#if module?.targetSettings}
	<module.targetSettings />
{:else}
	<div class="page">
		<p class="muted">Bu araç için ayar ekranı yok.</p>
	</div>
{/if}
