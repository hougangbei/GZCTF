import { useEffect } from 'react'

/** Browser back/forward does not dispatch a link click. Keep unsaved SPA sessions on cancel. */
export const useHistoryLeaveGuard = (dirty: boolean, message: string) => {
  useEffect(() => {
    if (!dirty) return
    const previousIndex = window.history.state?.idx as number | undefined
    let restoring = false
    const pop = (event: PopStateEvent) => {
      if (restoring) {
        restoring = false
        event.stopImmediatePropagation()
        return
      }
      const nextIndex = event.state?.idx as number | undefined
      if (typeof previousIndex !== 'number' || typeof nextIndex !== 'number' || previousIndex === nextIndex) return
      if (!window.confirm(message)) {
        event.stopImmediatePropagation()
        restoring = true
        window.history.go(previousIndex - nextIndex)
      }
    }
    window.addEventListener('popstate', pop, true)
    return () => window.removeEventListener('popstate', pop, true)
  }, [dirty, message])
}
