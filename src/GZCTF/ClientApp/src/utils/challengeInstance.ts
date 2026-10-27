/** The API serializes DateTimeOffset as Unix milliseconds. Accept ISO for older deployments. */
export const instanceSecondsRemaining = (
  expiry: number | string | null | undefined,
  now = Date.now()
): number | null => {
  const end = typeof expiry === 'number' ? expiry : typeof expiry === 'string' ? Date.parse(expiry) : NaN
  return Number.isFinite(end) ? Math.max(0, Math.ceil((end - now) / 1000)) : null
}

export const formatRemaining = (seconds: number): string =>
  [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60]
    .map((value) => String(value).padStart(2, '0'))
    .join(':')

export const instanceConnection = (
  host: string | null | undefined,
  port: number | null | undefined,
  category: string
) => {
  if (!host) return { address: '', href: undefined }
  const hasScheme = /^https?:\/\//i.test(host)
  const hostname = !hasScheme && host.includes(':') && !host.startsWith('[') ? `[${host}]` : host
  const address = `${hostname}${port ? `:${port}` : ''}`
  // Proxy mode returns an opaque container ID, not a public HTTP hostname.
  const opaqueProxy = !port && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(host)
  if (category.toLowerCase() !== 'web' || opaqueProxy) return { address, href: undefined }
  try {
    if (/\s/.test(host) || host.startsWith('//')) return { address, href: undefined }
    const url = new URL(hasScheme ? host : `http://${hostname}`)
    if (
      !['http:', 'https:'].includes(url.protocol) ||
      url.username ||
      url.password ||
      url.pathname !== '/' ||
      url.search ||
      url.hash
    )
      return { address, href: undefined }
    if (port) url.port = String(port)
    return { address: hasScheme ? url.href : address, href: url.href }
  } catch {
    return { address, href: undefined }
  }
}

/** Clipboard API requires HTTPS. Keep copying available on the plain HTTP intranet. */
export const copyInstanceAddress = async (address: string): Promise<void> => {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(address)
      return
    } catch {
      /* Try the HTTP fallback. */
    }
  }
  const active = document.activeElement as HTMLElement | null
  const input = document.createElement('textarea')
  input.value = address
  input.setAttribute('readonly', '')
  input.style.cssText = 'position:fixed;left:-9999px;top:0'
  // Keep focus inside a Mantine modal's focus trap.
  ;(active?.closest('[role="dialog"]') ?? document.body).appendChild(input)
  try {
    input.select()
    if (!document.execCommand('copy')) throw new Error('Clipboard unavailable')
  } finally {
    input.remove()
    active?.focus({ preventScroll: true })
  }
}
