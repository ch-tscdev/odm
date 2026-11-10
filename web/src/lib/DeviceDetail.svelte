<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type DeviceDto, type DeviceGroupRefDto, type DeviceInventoryDto, type InstallationDto, type InstalledProgramDto } from './api'
  import { auth } from './auth.svelte'
  import { router } from './router.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'
  import DeviceProfiles from './DeviceProfiles.svelte'
  import UpdateRuns from './UpdateRuns.svelte'
  import UpdateHistory from './UpdateHistory.svelte'
  import MessageDialog from './MessageDialog.svelte'
  import { formatSize } from './format'
  import { isOnline } from './status'

  let { id }: { id: string } = $props()

  let device = $state<DeviceDto | null>(null)
  let groups = $state<DeviceGroupRefDto[]>([])
  let installations = $state<InstallationDto[]>([])
  let inventory = $state<DeviceInventoryDto | null>(null)
  let loading = $state(true)
  let notFound = $state(false)
  let failed = $state(false)

  let messageDialog: MessageDialog
  let certMessage = $state('')
  let certBusy = $state(false)

  async function resetCertificate() {
    if (!device || !confirm(i18n.t('dev.detail.certConfirm', { name: device.name }))) return
    certBusy = true
    certMessage = ''
    try {
      await api.resetDeviceCertificate(device.id)
      certMessage = i18n.t('dev.detail.certDone')
    } catch {
      certMessage = i18n.t('devices.error')
    } finally {
      certBusy = false
    }
  }

  let deviceError = $state<string | null>(null)

  /** Device actions for operators (approval, reinstall, adopt reported name); shows the server's reason on errors. */
  async function deviceAction(action: () => Promise<DeviceDto>, confirmText?: string) {
    if (confirmText && !confirm(confirmText)) return
    deviceError = null
    try {
      device = await action()
      groups = await api.deviceGroups(id)
    } catch (e) {
      deviceError = (e instanceof ApiError && e.detail) || i18n.t('common.saveFailed')
    }
  }

  async function removeDevice() {
    if (!device || !confirm(i18n.t('dev.delete.confirm', { name: device.name }))) return
    deviceError = null
    try {
      await api.deleteDevice(id)
      router.navigate('/devices')
    } catch (e) {
      deviceError = (e instanceof ApiError && e.detail) || i18n.t('common.saveFailed')
    }
  }

  const statusClass = { Pending: '', Running: 'run', Installed: 'ok', Failed: 'err', Removed: '' } as const

  async function load() {
    try {
      const [d, g, i, inv] = await Promise.all([
        api.device(id),
        api.deviceGroups(id),
        api.deviceInstallations(id),
        api.deviceInventory(id).catch(() => null),
      ])
      device = d
      groups = g
      installations = i
      inventory = inv
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) notFound = true
      else failed = true
    } finally {
      loading = false
    }
  }

  onMount(load)

  async function retry(installation: InstallationDto) {
    await api.retryInstallation(installation.id)
    installations = await api.deviceInstallations(id)
  }

  const groupColumns = $derived<Column<DeviceGroupRefDto>[]>([
    { key: 'name', label: i18n.t('groups.name'), value: (g) => g.name, cell: groupName, strong: true },
    { key: 'desc', label: i18n.t('groups.description'), value: (g) => g.description, muted: true },
  ])

  const softwareColumns = $derived<Column<InstallationDto>[]>([
    { key: 'package', label: i18n.t('sw.name'), value: (i) => i.packageName, cell: packageName, strong: true },
    { key: 'status', label: i18n.t('sw.status'), value: (i) => i18n.t(`sw.status.${i.status}` as MessageKey), cell: status, filter: 'select' },
    { key: 'target', label: i18n.t('dev.detail.target'), value: (i) => i.targetVersion },
    { key: 'installed', label: i18n.t('dev.detail.installed'), value: (i) => i.installedVersion },
    { key: 'attempts', label: i18n.t('dev.detail.attempts'), value: (i) => i.attempts, align: 'right', filter: 'none' },
    { key: 'updated', label: i18n.t('dev.detail.updated'), value: (i) => i.updatedAt, cell: updated, filter: 'none' },
    { key: 'message', label: i18n.t('dev.detail.message'), value: (i) => i.message, cell: message, muted: true },
  ])

  const programColumns = $derived<Column<InstalledProgramDto>[]>([
    { key: 'name', label: i18n.t('inv.name'), value: (p) => p.name, strong: true },
    { key: 'version', label: i18n.t('inv.version'), value: (p) => p.version },
    { key: 'publisher', label: i18n.t('inv.publisher'), value: (p) => p.publisher, muted: true },
  ])

  /** Uptime from the boot time of the last report; the age of the report is included. */
  function uptimeText(bootTime: string) {
    const minutes = Math.max(0, Math.floor((Date.now() - new Date(bootTime).getTime()) / 60000))
    const days = Math.floor(minutes / 1440)
    const hours = Math.floor((minutes % 1440) / 60)
    const rest = minutes % 60
    return days > 0
      ? i18n.t('inv.uptime.dhm', { d: days, h: hours, m: rest })
      : i18n.t('inv.uptime.hm', { h: hours, m: rest })
  }
</script>

{#snippet groupName(g: DeviceGroupRefDto)}
  <a href={`#/groups/${g.id}`}>{g.name}</a>
{/snippet}

{#snippet packageName(i: InstallationDto)}
  <a href={`#/software/${i.packageId}`}>{i.packageName}</a>
{/snippet}

{#snippet status(i: InstallationDto)}
  <span class="badge {statusClass[i.status]}">{i.removal && (i.status === 'Pending' || i.status === 'Running' || i.status === 'Failed') ? i18n.t(`sw.removal.${i.status}` as MessageKey) : i18n.t(`sw.status.${i.status}` as MessageKey)}</span>
{/snippet}

{#snippet updated(i: InstallationDto)}
  {i18n.formatDate(i.updatedAt)}
{/snippet}

{#snippet message(i: InstallationDto)}
  {#if i.waitingFor && i.waitingFor.length > 0}
    <span class="msg">{i18n.t('dev.detail.waitingFor', { names: i.waitingFor.join(', ') })}</span>
  {:else}
    <span class="msg" title={i18n.server(i.message)}>{i18n.server(i.message) || '–'}</span>
  {/if}
{/snippet}

{#snippet softwareActions(i: InstallationDto)}
  {#if i.message}
    <button class="ghost" onclick={() => messageDialog.show(`${i.packageName} – ${i18n.t('msg.title')}`, i18n.server(i.message))}>
      {i18n.t('msg.show')}
    </button>
  {/if}
  {#if auth.canEdit && i.status === 'Failed'}
    <button class="ghost" onclick={() => retry(i)}>{i18n.t('sw.retry')}</button>
  {/if}
{/snippet}

<section>
  <p><a href="#/devices">← {i18n.t('devices.title')}</a></p>

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if notFound}
    <p class="muted">{i18n.t('dev.detail.notFound')}</p>
  {:else if failed || !device}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else}
    <header class="page-head">
      <h1>{device.name}</h1>
      <span class="badge" class:ok={isOnline(device)}>
        {isOnline(device) ? i18n.t('devices.online') : i18n.t('devices.offline')}
      </span>
      {#if device.approvalPending}<span class="badge err">{i18n.t('dev.approval.badge')}</span>{/if}
    </header>

    {#if device.approvalPending}
      <div class="card notice warn">
        <strong>{i18n.t('dev.approval.title')}</strong>
        <p>{i18n.t('dev.approval.text')}</p>
        {#if auth.canEdit}
          <div>
            <button class="primary" onclick={() => deviceAction(() => api.approveDevice(id), i18n.t('dev.approval.confirm', { name: device!.name }))}>
              {i18n.t('dev.approval.approve')}
            </button>
          </div>
        {/if}
      </div>
    {/if}

    {#if device.reportedName}
      <div class="card notice">
        <strong>{i18n.t('dev.rename.title', { name: device.reportedName })}</strong>
        <p>{i18n.t('dev.rename.text')}</p>
        {#if auth.canEdit}
          <div>
            <button onclick={() => deviceAction(() => api.acceptDeviceName(id), i18n.t('dev.rename.confirm', { from: device!.name, to: device!.reportedName ?? '' }))}>
              {i18n.t('dev.rename.accept')}
            </button>
          </div>
        {/if}
      </div>
    {/if}

    {#if deviceError}<p class="error" role="alert">{deviceError}</p>{/if}

    <dl class="card facts">
      <div><dt>{i18n.t('devices.os')}</dt><dd>{device.osName} {device.osVersion}</dd></div>
      <div><dt>{i18n.t('dev.detail.agent')}</dt><dd>{device.agentVersion ?? '–'}</dd></div>
      <div><dt>{i18n.t('devices.lastSeen')}</dt><dd>{device.lastSeenAt ? i18n.formatDate(device.lastSeenAt) : '–'}</dd></div>
      <div><dt>{i18n.t('dev.detail.registered')}</dt><dd>{i18n.formatDate(device.registeredAt)}</dd></div>
      <div><dt>{i18n.t('devices.primaryUser')}</dt><dd>{device.primaryUser ?? '–'}</dd></div>
      <div><dt>{i18n.t('devices.orgUnit')}</dt><dd>{device.orgUnit ?? '–'}</dd></div>
      <div><dt>{i18n.t('devices.costCenter')}</dt><dd>{device.costCenter ?? '–'}</dd></div>
    </dl>

    {#if auth.canEdit}
      <div class="cert">
        {#if device.reinstallAllowedUntil}
          <button class="ghost" onclick={() => deviceAction(() => api.setReinstall(id, false))}>{i18n.t('dev.reinstall.cancel')}</button>
          <span class="muted">{i18n.t('dev.reinstall.open', { until: i18n.formatDate(device.reinstallAllowedUntil) })}</span>
        {:else}
          <button class="ghost" onclick={() => deviceAction(() => api.setReinstall(id, true), i18n.t('dev.reinstall.confirm', { name: device!.name }))}>
            {i18n.t('dev.reinstall.allow')}
          </button>
          <span class="muted">{i18n.t('dev.reinstall.hint')}</span>
        {/if}
      </div>
      <div class="cert">
        <button class="ghost danger" onclick={removeDevice}>{i18n.t('dev.delete.button')}</button>
        <span class="muted">{i18n.t('dev.delete.hint')}</span>
      </div>
    {/if}

    {#if auth.isAdmin}
      <div class="cert">
        <button class="ghost" disabled={certBusy} onclick={resetCertificate}>{i18n.t('dev.detail.certReset')}</button>
        <span class="muted">{certMessage || i18n.t('dev.detail.certResetHint')}</span>
      </div>
    {/if}

    <h2>{i18n.t('inv.hardware')}</h2>
    {#if inventory?.hardware}
      {@const h = inventory.hardware}
      <dl class="card facts">
        <div><dt>{i18n.t('inv.model')}</dt><dd>{[h.manufacturer, h.model].filter(Boolean).join(' ') || '–'}</dd></div>
        <div><dt>{i18n.t('inv.cpu')}</dt><dd>{h.cpu ?? '–'}{h.cpuCores ? ` (${h.cpuCores})` : ''}</dd></div>
        <div><dt>{i18n.t('inv.arch')}</dt><dd>{h.architecture ?? '–'}</dd></div>
        <div><dt>{i18n.t('inv.memory')}</dt><dd>{h.memoryBytes ? formatSize(h.memoryBytes) : '–'}</dd></div>
        <div>
          <dt>{i18n.t('inv.disk')}</dt>
          <dd>{h.diskTotalBytes ? `${formatSize(h.diskFreeBytes ?? 0)} / ${formatSize(h.diskTotalBytes)}` : '–'}</dd>
        </div>
        <div>
          <dt>{i18n.t('inv.lastUser')}</dt>
          <dd>
            {h.lastUser ?? '–'}
            {#if h.lastUser && h.lastLoginAt}<span class="muted"> · {i18n.formatDate(h.lastLoginAt)}</span>{/if}
          </dd>
        </div>
        <div>
          <dt>{i18n.t('inv.uptime')}</dt>
          <dd>{h.bootTime ? uptimeText(h.bootTime) : '–'}</dd>
        </div>
        <div><dt>{i18n.t('inv.collected')}</dt><dd>{i18n.formatDate(h.collectedAt)}</dd></div>
      </dl>

      {#if h.network?.length}
        <h3>{i18n.t('inv.network')}</h3>
        <dl class="card facts">
          {#each h.network as adapter (adapter.name)}
            <div>
              <dt>{adapter.name}{adapter.macAddress ? ` · ${adapter.macAddress}` : ''}</dt>
              <dd>{#each adapter.addresses as address}<div>{address}</div>{/each}</dd>
            </div>
          {/each}
        </dl>
      {/if}

      {#if h.disks?.length}
        <h3>{i18n.t('inv.volumes')}</h3>
        <dl class="card facts">
          {#each h.disks as disk (disk.name)}
            <div>
              <dt>{disk.name}</dt>
              <dd>{formatSize(disk.freeBytes)} / {formatSize(disk.totalBytes)} {i18n.t('inv.free')}</dd>
            </div>
          {/each}
        </dl>
      {/if}
    {:else}
      <p class="muted">{i18n.t('inv.none')}</p>
    {/if}

    <h2>{i18n.t('dev.detail.groups')} ({groups.length})</h2>
    <DataTable rows={groups} columns={groupColumns} rowKey={(g) => g.id} emptyText={i18n.t('dev.detail.groupsEmpty')} pageSize={25} />

    <h2>{i18n.t('dev.detail.software')} ({installations.length})</h2>
    <DataTable
      rows={installations}
      columns={softwareColumns}
      rowKey={(i) => i.id}
      actions={softwareActions}
      emptyText={i18n.t('dev.detail.softwareEmpty')}
      pageSize={25}
    />

    <DeviceProfiles deviceId={device.id} />

    <h2>{i18n.t('os.runs')}</h2>
    <UpdateRuns deviceId={device.id} />

    <h2>{i18n.t('os.history')}</h2>
    <UpdateHistory deviceId={device.id} />

    <h2>{i18n.t('inv.programs')} ({inventory?.software.length ?? 0})</h2>
    <DataTable
      rows={inventory?.software ?? []}
      columns={programColumns}
      rowKey={(p) => `${p.name}\u0000${p.version ?? ''}`}
      emptyText={i18n.t('inv.none')}
      pageSize={25}
    />
  {/if}
</section>

<MessageDialog bind:this={messageDialog} />

<style>
  .danger {
    color: var(--danger);
  }
  .notice {
    display: grid;
    gap: 0.4rem;
    margin-bottom: 1rem;
    border-left: 3px solid var(--accent);
  }
  .notice.warn {
    border-left-color: var(--danger);
  }
  .notice p {
    margin: 0;
    color: var(--muted);
    font-size: 0.9rem;
  }

  h2 {
    font-size: 1rem;
    margin: 1.5rem 0 0.5rem;
  }
  h3 {
    font-size: 0.9rem;
    margin: 1rem 0 0.4rem;
    color: var(--muted);
  }
  .facts {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr));
    gap: 0.9rem 1.5rem;
    margin: 0;
  }
  .facts div {
    min-width: 0;
  }
  dt {
    font-size: 0.75rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--muted);
  }
  dd {
    margin: 0.1rem 0 0;
    font-weight: 600;
    overflow-wrap: anywhere;
  }
  .msg {
    display: inline-block;
    max-width: 24rem;
    overflow: hidden;
    text-overflow: ellipsis;
    vertical-align: bottom;
  }
  .cert {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem;
    margin-top: 1rem;
  }
  .error {
    color: var(--danger);
  }
</style>
