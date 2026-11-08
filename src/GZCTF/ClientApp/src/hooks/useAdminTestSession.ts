import { useCallback, useEffect, useRef, useState } from 'react'
import { AdminTestSession } from '@Utils/adminTestSession'
import type { ChallengeInstanceResponse } from '@Api'

export const useAdminTestSession = (challengeId: string | undefined, userId: string | undefined, courseId?: string) => {
  const session = useRef<AdminTestSession | undefined>(undefined)
  const [instance, setInstance] = useState<ChallengeInstanceResponse>()
  useEffect(() => {
    if (!challengeId || !userId) return
    const owned = new AdminTestSession(challengeId, setInstance)
    session.current = owned
    const close = () => owned.suspend()
    const restore = () => owned.resume()
    window.addEventListener('pagehide', close)
    window.addEventListener('pageshow', restore)
    return () => {
      window.removeEventListener('pagehide', close)
      window.removeEventListener('pageshow', restore)
      if (session.current === owned) session.current = undefined
      owned.dispose()
    }
  }, [challengeId, userId, courseId])
  const prepareStart = useCallback(() => {
    const owned = session.current
    const revision = owned?.revision
    return () => owned?.start(courseId, revision)
  }, [courseId])
  const stop = useCallback(() => session.current?.stop(), [])
  return { instance, prepareStart, stop }
}
