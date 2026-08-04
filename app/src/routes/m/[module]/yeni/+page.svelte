<script lang="ts">
	import { page } from '$app/state';
	import { findUiModule } from '$lib/modules/registry';

	const module = $derived(findUiModule(page.params.module!));
</script>

{#if module?.createTarget}
	<module.createTarget />
{:else}
	<div class="page">
		<p class="muted">Bu araca yeni kayıt eklenemiyor.</p>
	</div>
{/if}
