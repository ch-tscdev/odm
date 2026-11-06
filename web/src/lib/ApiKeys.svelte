<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type ApiKeyCreatedDto, type ApiKeyDto } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n } from './i18n.svelte'

  let keys = $state<ApiKeyDto[]>([])
  let loading = $state(true)
  let failed = $state(false)

  let name = $state('')
  let validDays = $state<number | null>(365)
  let busy = $state(false)
  let error = $state<string | null>(null)
  let created = $state<ApiKeyCreatedDto | null>(null)
  let copied = $state(false)

  const example = $derived(
    `curl -sS -X POST ${location.origin}/api/agents/tokens \\\n` +
      `  -H 'X-Api-Key: ${created?.key ?? '<api-key>'}' -H 'Content-Type: application/json' \\\n` +
      `  -d '{"label":"ansible <host>","validHours":1,"maxUses":1}'`,
  )

  async function load() {
    try {
      keys = await api.apiKeys()
      failed = false
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }
  onMount(load)

  async function create(event: SubmitEvent) {
    event.preventDefault()
    busy = true
    error = null
    copied = false
    try {
      created = await api.createApiKey(name.trim(), validDays || null)
      name = ''
      await load()
    } catch (e) {
      error = (e instanceof ApiError && e.detail) || i18n.t('common.saveFailed')
    } finally {
      busy = false
    }
  }

  async function copy() {
    if (!created) return
    await navigator.clipboard.writeText(created.key)
    copied = true
  }

  async function remove(key: ApiKeyDto) {
    if (!confirm(i18n.t('apikeys.confirmDelete', { name: key.name }))) return
    try {
      await api.deleteApiKey(key.id)
      keys = keys.filter((k) => k.id !== key.id)
      if (created?.id === key.id) created = null
    } catch {
      alert(i18n.t('common.saveFailed'))
    }
  }

  const columns = $derived<Column<ApiKeyDto>[]>([
    { key: 'name', label: i18n.t('apikeys.name'), value: (k) => k.name, strong: true },
    { key: 'role', label: i18n.t('apikeys.role'), value: (k) => k.role, muted: true },
    { key: 'created', label: i18n.t('apikeys.created'), value: (k) => k.createdAt, cell: createdCell, filter: 'none' },
    { key: 'expires', label: i18n.t('apikeys.expires'), value: (k) => k.expiresAt, cell: expiresCell, filter: 'none' },
    { key: 'used', label: i18n.t('apikeys.lastUsed'), value: (k) => k.lastUsedAt, cell: usedCell, filter: 'none' },
  ])
</script>

{#snippet createdCell(k: ApiKeyDto)}
  {i18n.formatDate(k.createdAt)} <span class="muted">· {k.createdBy}</span>
{/snippet}

{#snippet expiresCell(k: ApiKeyDto)}
  {#if k.expired}
    <span class="badge err">{i18n.t('apikeys.expired')}</span>
  {:else if k.expiresAt}
    {i18n.formatDate(k.expiresAt)}
  {:else}
    <span class="muted">{i18n.t('apikeys.never')}</span>
  {/if}
{/snippet}

{#snippet usedCell(k: ApiKeyDto)}
  {#if k.lastUsedAt}{i18n.formatDate(k.lastUsedAt)}{:else}<span class="muted">–</span>{/if}
{/snippet}

{#snippet rowActions(k: ApiKeyDto)}
  <button class="ghost" onclick={() => remove(k)}>{i18n.t('common.delete')}</button>
{/snippet}

<section class="apikeys">
  <h2>{i18n.t('apikeys.title')}</h2>
  <p class="muted intro">{i18n.t('apikeys.intro')}</p>

  <form class="card form" onsubmit={create}>
    <div class="row">
      <label class="grow">
        <span>{i18n.t('apikeys.name')}</span>
        <input bind:value={name} required maxlength="64" placeholder="ansible" />
      </label>
      <label>
        <span>{i18n.t('apikeys.validDays')}</span>
        <input type="number" min="1" max="3650" step="1" bind:value={validDays} style="width: 8rem" />
      </label>
      <button class="primary" type="submit" disabled={busy}>{i18n.t('apikeys.create')}</button>
    </div>
    <p class="muted small">{i18n.t('apikeys.validHint')}</p>
    {#if error}<p class="error" role="alert">{error}</p>{/if}
  </form>

  {#if created}
    <div class="card created">
      <strong>{i18n.t('apikeys.createdTitle', { name: created.name })}</strong>
      <code class="token">{created.key}</code>
      <div class="row">
        <button onclick={copy}>{copied ? i18n.t('enroll.copied') : i18n.t('enroll.copy')}</button>
        <span class="muted small">{i18n.t('enroll.once')}</span>
      </div>
      <span class="muted small">{i18n.t('apikeys.usage')}</span>
      <code class="token pre">{example}</code>
    </div>
  {/if}

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if failed}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else}
    <DataTable rows={keys} {columns} rowKey={(k) => k.id} actions={rowActions} emptyText={i18n.t('apikeys.empty')} />
  {/if}
</section>

<style>
  .apikeys {
    margin-top: 2rem;
  }
  h2 {
    font-size: 1.1rem;
    margin: 0 0 0.5rem;
  }
  .intro {
    margin: 0 0 1rem;
    max-width: 46rem;
  }
  .form,
  .created {
    display: grid;
    gap: 0.75rem;
    margin-bottom: 1rem;
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    align-items: end;
  }
  .grow {
    flex: 1;
    min-width: 12rem;
  }
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
  .small {
    font-size: 0.85rem;
    margin: 0;
  }
  .token {
    display: block;
    padding: 0.6rem 0.8rem;
    background: var(--bg);
    border: 1px solid var(--border);
    border-radius: 0.45rem;
    word-break: break-all;
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  }
  .pre {
    white-space: pre-wrap;
    font-size: 0.82rem;
  }
  .error {
    color: var(--danger);
    margin: 0;
  }
</style>
