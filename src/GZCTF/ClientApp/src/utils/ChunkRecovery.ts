type RecoveryEnvironment = {
  storage: Pick<Storage, 'getItem' | 'setItem'>
  reload: () => void
  online: boolean
  now: number
}
const recoveryKey = 'gzctf:chunk-recovery'
const cooldown = 60000

export const isChunkLoadError = (error: unknown): boolean => {
  const message = error instanceof Error ? error.message : typeof error === 'string' ? error : ''
  return /Failed to fetch dynamically imported module|Importing a module script failed|error loading dynamically imported module|Unable to preload CSS for/i.test(
    message
  )
}

export const recoverChunkLoad = (error: unknown, env: RecoveryEnvironment): boolean => {
  if (!env.online || !isChunkLoadError(error)) return false
  try {
    const previous = Number(env.storage.getItem(recoveryKey))
    if (previous && env.now - previous < cooldown) return false
    // Persist before reloading; if storage is blocked, do not risk a reload loop.
    env.storage.setItem(recoveryKey, String(env.now))
    env.reload()
    return true
  } catch {
    return false
  }
}

export const recoverBrowserChunk = (error: unknown): boolean => {
  try {
    return recoverChunkLoad(error, {
      storage: window.sessionStorage,
      reload: () => window.location.reload(),
      online: navigator.onLine,
      now: Date.now(),
    })
  } catch {
    return false
  }
}

export const installChunkRecovery = () => {
  window.addEventListener('vite:preloadError', (event) => {
    const error = (event as Event & { payload?: unknown }).payload
    if (recoverBrowserChunk(error)) event.preventDefault()
  })
}
