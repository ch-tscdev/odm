<script lang="ts">
  import { api, ApiError } from './api'
  import { i18n } from './i18n.svelte'

  let { onsaved }: { onsaved?: () => void } = $props()

  let dialog: HTMLDialogElement
  let name = $state('')
  let value = $state('')
  let repeat = $state('')
  let busy = $state(false)
  let error = $state<string | null>(null)

  /** Opens the dialog to set a new value for an existing secret. The old value is never shown. */
  export function open(secretName: string) {
    name = secretName
    value = repeat = ''
    error = null
    dialog.showModal()
  }

  async function submit(event: SubmitEvent) {
    event.preventDefault()
    error = null
    if (value !== repeat) {
      error = i18n.t('sw.secrets.mismatch')
      return
    }
    busy = true
    try {
      await api.setSecret(name, value)
      value = repeat = ''
      dialog.close()
      onsaved?.()
    } catch (e) {
      error = (e instanceof ApiError && e.detail) || i18n.t('common.saveFailed')
    } finally {
      busy = false
    }
  }
</script>

<dialog bind:this={dialog} onclose={() => (value = repeat = '')}>
  <form class="dialog-form" onsubmit={submit}>
    <h2>{i18n.t('sw.secrets.changeTitle', { name })}</h2>
    <label>
      <span>{i18n.t('sw.secrets.newValue')}</span>
      <input type="password" autocomplete="new-password" bind:value required maxlength="4000" />
    </label>
    <label>
      <span>{i18n.t('sw.secrets.repeatValue')}</span>
      <input type="password" autocomplete="new-password" bind:value={repeat} required maxlength="4000" />
    </label>
    <p class="muted">{i18n.t('sw.secrets.changeHint')}</p>
    {#if error}<p class="error" role="alert">{error}</p>{/if}
    <div class="buttons">
      <button type="button" class="ghost" onclick={() => dialog.close()}>{i18n.t('common.cancel')}</button>
      <button type="submit" class="primary" disabled={busy}>{i18n.t('common.save')}</button>
    </div>
  </form>
</dialog>

<style>
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
</style>
