import { afterEach, expect, it, vi } from 'vitest'
import { ChallengeInstanceStatus } from '@Api'
import { AdminTestSession, createEditorSessionId } from './adminTestSession'

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})
const instance = { id: 'instance-1', status: ChallengeInstanceStatus.Running }
const json = (value: unknown, status = 200) => new Response(status === 204 ? null : JSON.stringify(value), { status })

it('starts with its session and course then renews and releases only its exact instance', async () => {
  vi.useFakeTimers()
  const request = vi.fn().mockResolvedValue(json(instance))
  const notify = vi.fn()
  const session = new AdminTestSession('challenge-1', notify, request, 'session-1')
  await session.start('course-1')
  expect(request.mock.calls[0][0]).toContain('testSessionId=session-1')
  expect(request.mock.calls[0][0]).toContain('course=course-1')
  request.mockResolvedValue(json(instance))
  await vi.advanceTimersByTimeAsync(20_000)
  expect(request.mock.calls[1][0]).toBe('/api/challenges/challenge-1/instances/test-heartbeat')
  expect(JSON.parse(request.mock.calls[1][1].body)).toEqual({ instanceId: 'instance-1', testSessionId: 'session-1' })
  request.mockResolvedValue(json(null, 204))
  session.dispose()
  expect(request.mock.calls[2][0]).toBe('/api/challenges/challenge-1/instances/test-stop')
  expect(request.mock.calls[2][1].keepalive).toBe(true)
  expect(JSON.parse(request.mock.calls[2][1].body).instanceId).toBe('instance-1')
  const count = request.mock.calls.length
  await vi.advanceTimersByTimeAsync(60_000)
  expect(request).toHaveBeenCalledTimes(count)
})

it('releases a test whose startup finishes after leaving the editor', async () => {
  let resolve!: (value: Response) => void
  const request = vi
    .fn()
    .mockImplementationOnce(
      () =>
        new Promise<Response>((done) => {
          resolve = done
        })
    )
    .mockResolvedValue(json(null, 204))
  const notify = vi.fn()
  const session = new AdminTestSession('challenge', notify, request, 'session')
  const pending = session.start()
  session.dispose()
  resolve(json(instance))
  expect(await pending).toBeUndefined()
  expect(notify).not.toHaveBeenCalledWith(instance)
  expect(request.mock.calls[1][0]).toContain('/test-stop')
  expect(request.mock.calls[1][1].keepalive).toBe(true)
})

it('a failed explicit stop can be retried and does not lose the owned instance', async () => {
  const request = vi
    .fn()
    .mockResolvedValueOnce(json(instance))
    .mockResolvedValueOnce(json({ code: 'failed' }, 502))
    .mockResolvedValue(json(null, 204))
  const notify = vi.fn()
  const session = new AdminTestSession('challenge', notify, request, 'session')
  await session.start()
  await expect(session.stop()).rejects.toThrow()
  await session.stop()
  expect(JSON.parse(request.mock.calls[2][1].body).instanceId).toBe('instance-1')
  expect(notify).toHaveBeenLastCalledWith(undefined)
  session.dispose()
})

it('expired leases clear the editor status without reviving the test', async () => {
  vi.useFakeTimers()
  const request = vi
    .fn()
    .mockResolvedValueOnce(json(instance))
    .mockResolvedValue(json({ code: 'challenge.instance_not_found' }, 404))
  const notify = vi.fn()
  const session = new AdminTestSession('challenge', notify, request, 'session')
  await session.start()
  await vi.advanceTimersByTimeAsync(20_000)
  expect(notify).toHaveBeenLastCalledWith(undefined)
  const count = request.mock.calls.length
  await vi.advanceTimersByTimeAsync(40_000)
  expect(request).toHaveBeenCalledTimes(count)
  session.dispose()
})

it('intranet HTTP session IDs use cryptographic random bytes when randomUUID is unavailable', () => {
  vi.stubGlobal('crypto', {
    getRandomValues: (bytes: Uint8Array) => {
      bytes.fill(7)
      return bytes
    },
  })
  expect(createEditorSessionId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
})

it('a restored page rotates its session so a delayed old release cannot target the new test', async () => {
  const request = vi.fn().mockImplementation(() => Promise.resolve(json(instance)))
  const session = new AdminTestSession('challenge', vi.fn(), request, 'old-session')
  await session.start()
  session.close()
  await session.start()
  expect(JSON.parse(request.mock.calls[1][1].body).testSessionId).toBe('old-session')
  expect(new URL(request.mock.calls[2][0], 'http://fixture').searchParams.get('testSessionId')).not.toBe('old-session')
  session.dispose()
})

it('a suspended editor refuses a save continuation that tries to start after pagehide', async () => {
  const request = vi.fn()
  const session = new AdminTestSession('challenge', vi.fn(), request, 'old-session')
  session.suspend()
  expect(await session.start()).toBeUndefined()
  expect(request).not.toHaveBeenCalled()
  session.resume()
  request.mockResolvedValue(json(instance))
  await session.start()
  expect(request).toHaveBeenCalledTimes(1)
  session.dispose()
})

it('a save continuation from before pagehide cannot start a new test after BFCache restore', async () => {
  const request = vi.fn()
  const session = new AdminTestSession('challenge', vi.fn(), request, 'old-session')
  const revision = session.revision
  session.suspend()
  session.resume()
  expect(await session.start(undefined, revision)).toBeUndefined()
  expect(request).not.toHaveBeenCalled()
  session.dispose()
})
