import { expect, it, vi } from 'vitest'
import { recoverChunkLoad } from './ChunkRecovery'

const setup = () => {
  const data = new Map<string, string>()
  return {
    storage: {
      getItem: (key: string) => data.get(key) ?? null,
      setItem: (key: string, value: string) => {
        data.set(key, value)
      },
    },
    reload: vi.fn(),
    online: true,
    now: 100000,
  }
}
it('recovers an obsolete module once without clearing user preferences', () => {
  const env = setup()
  env.storage.setItem('theme', 'custom')
  const error = new TypeError('Failed to fetch dynamically imported module: http://localhost/static/old.js')
  expect(recoverChunkLoad(error, env)).toBe(true)
  expect(env.reload).toHaveBeenCalledOnce()
  expect(recoverChunkLoad(error, env)).toBe(false)
  expect(env.storage.getItem('theme')).toBe('custom')
})
it('does not reload for normal bugs, offline clients or unavailable storage', () => {
  const env = setup()
  expect(recoverChunkLoad(new Error('render failed'), env)).toBe(false)
  const error = new TypeError('Importing a module script failed.')
  expect(recoverChunkLoad(error, { ...env, online: false })).toBe(false)
  expect(
    recoverChunkLoad(error, {
      ...env,
      storage: {
        getItem: () => {
          throw new Error('denied')
        },
        setItem: vi.fn(),
      },
    })
  ).toBe(false)
  expect(env.reload).not.toHaveBeenCalled()
})
it('allows a later update to recover after a cooldown', () => {
  const env = setup()
  const error = new Error('Unable to preload CSS for /static/old.css')
  expect(recoverChunkLoad(error, env)).toBe(true)
  expect(recoverChunkLoad(error, { ...env, now: env.now + 61000 })).toBe(true)
})
