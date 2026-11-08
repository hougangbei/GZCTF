import { ChallengeInstanceStatus, type ChallengeInstanceResponse } from '@Api'

export const createEditorSessionId = (): string => {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 15) | 64
  bytes[8] = (bytes[8] & 63) | 128
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

/** An editor owns one exact administrator test, never the user's general learner instance. */
export class AdminTestSession {
  private instance?: ChallengeInstanceResponse
  private timer?: ReturnType<typeof setInterval>
  private disposed = false
  private suspended = false
  private generation = 0
  private heartbeatPending = false
  private readonly path: string

  constructor(
    challengeId: string,
    private readonly notify: (instance: ChallengeInstanceResponse | undefined) => void,
    private readonly request: typeof fetch = (input, init) => fetch(input, init),
    private sessionId = createEditorSessionId()
  ) {
    this.path = `/api/challenges/${encodeURIComponent(challengeId)}/instances`
  }

  private async post(path: string, body?: unknown, keepalive = false): Promise<ChallengeInstanceResponse | undefined> {
    const response = await this.request(path, {
      method: 'POST',
      credentials: 'include',
      keepalive,
      ...(body ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {}),
    })
    const data = await response.json().catch(() => undefined)
    if (!response.ok) {
      throw Object.assign(new Error(data?.message ?? 'Administrator test request failed.'), {
        response: { status: response.status, data },
      })
    }
    return data as ChallengeInstanceResponse | undefined
  }

  private body(instance: ChallengeInstanceResponse) {
    return { instanceId: instance.id, testSessionId: this.sessionId }
  }

  private release(instance: ChallengeInstanceResponse) {
    return this.post(`${this.path}/test-stop`, this.body(instance), true)
  }

  get revision() {
    return this.generation
  }

  async start(courseId?: string, expectedRevision = this.generation): Promise<ChallengeInstanceResponse | undefined> {
    if (this.disposed || this.suspended || expectedRevision !== this.generation) return undefined
    if (this.instance) return this.instance
    const generation = this.generation
    const sessionId = this.sessionId
    const query = new URLSearchParams({ testSessionId: sessionId })
    if (courseId) query.set('course', courseId)
    const instance = await this.post(`${this.path}?${query}`)
    if (!instance?.id || instance.status !== ChallengeInstanceStatus.Running)
      throw new Error('The server did not return a running test instance.')
    if (this.disposed || generation !== this.generation) {
      await this.post(`${this.path}/test-stop`, { instanceId: instance.id, testSessionId: sessionId }, true).catch(
        () => undefined
      )
      return undefined
    }
    this.instance = instance
    this.notify(instance)
    this.timer = setInterval(() => void this.heartbeat(), 20_000)
    return instance
  }

  private async heartbeat() {
    const instance = this.instance
    if (!instance || this.disposed || this.heartbeatPending) return
    this.heartbeatPending = true
    try {
      const updated = await this.post(`${this.path}/test-heartbeat`, this.body(instance))
      if (!this.disposed && this.instance?.id === instance.id && updated?.id === instance.id) {
        this.instance = updated
        this.notify(updated)
      }
    } catch (error) {
      if (
        !this.disposed &&
        this.instance?.id === instance.id &&
        [401, 403, 404].includes((error as { response?: { status?: number } }).response?.status ?? 0)
      ) {
        this.clear()
        this.notify(undefined)
      }
      // Transient network failures do not extend the server lease. Its expiry remains the final fallback.
    } finally {
      this.heartbeatPending = false
    }
  }

  async stop() {
    const instance = this.instance
    if (!instance) return
    await this.post(`${this.path}/test-stop`, this.body(instance))
    if (this.instance?.id === instance.id) {
      this.clear()
      this.notify(undefined)
    }
  }

  private clear() {
    if (this.timer !== undefined) clearInterval(this.timer)
    this.timer = undefined
    this.instance = undefined
  }

  close(notify = true) {
    this.generation++
    const instance = this.instance
    this.clear()
    if (notify && !this.disposed) this.notify(undefined)
    if (instance) void this.release(instance).catch(() => undefined)
    this.sessionId = createEditorSessionId()
  }

  suspend() {
    this.suspended = true
    this.close()
  }

  resume() {
    if (!this.disposed) this.suspended = false
  }

  dispose() {
    this.disposed = true
    this.close(false)
  }
}
