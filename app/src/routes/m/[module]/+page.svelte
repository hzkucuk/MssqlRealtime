<script lang="ts">
	import { page } from '$app/state';
	import { findUiModule } from '$lib/modules/registry';

	const module = $derived(findUiModule(page.params.module!));
</script>

{#if module}
	<module.home />
{:else}
	<div class="page">
		<p class="muted">Bu araç bu uygulamada tanımlı değil.</p>
	</div>
{/if}
