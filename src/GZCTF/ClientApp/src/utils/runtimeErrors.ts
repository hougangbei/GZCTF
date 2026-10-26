const runtimeErrorKeys: Record<string, string> = {
  'challenge.runtime_unavailable': 'translation:common.error.challenge_runtime_unavailable',
  'challenge.container_start_failed': 'translation:common.error.challenge_container_start_failed',
  'challenge.container_stop_failed': 'translation:common.error.challenge_container_stop_failed',
}

export const runtimeErrorKey = (error: unknown): string | undefined => {
  if (!error || typeof error !== 'object') return undefined
  const value = error as { code?: unknown; response?: { data?: { code?: unknown } } }
  const code = value.response?.data?.code ?? value.code
  return typeof code === 'string' ? runtimeErrorKeys[code] : undefined
}
