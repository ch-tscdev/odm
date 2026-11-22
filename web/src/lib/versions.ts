import { i18n } from './i18n.svelte'

/** Server marker for repo packages without a pinned version: the package manager installs the newest one. */
const LATEST = 'latest'

/** Version as shown to users: "latest" translated, everything else unchanged. */
export function versionText(version: string | null | undefined): string | null {
  if (version == null) return null
  return version === LATEST ? i18n.t('sw.version.latest') : version
}

const revisionPattern = /^(.*) \(r(\d+)\)$/

/** Internal revision label of a repo package ("latest (r5)") as "Stand 5 · neueste". */
export function revisionText(label: string): string {
  const match = revisionPattern.exec(label)
  if (!match) return label
  return i18n.t('sw.revision', { n: match[2], version: versionText(match[1]) ?? match[1] })
}
