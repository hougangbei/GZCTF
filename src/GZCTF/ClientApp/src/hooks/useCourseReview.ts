import { useMemo, useSyncExternalStore } from 'react'
import type { CourseSelection } from '@Utils/courseAuthoring'
import { markReviewChanged, parseCourseReview, reviewKey, type CourseReview } from '@Utils/courseReview'
import { useUser } from './useUser'

const event = 'gzctf-course-review'
const memory = new Map<string, string>()
// A readable stale storage value must not shadow a newer failed write or a cleared tombstone.
const memoryOnly = new Set<string>()
const read = (key: string | undefined) => {
  if (!key) return null
  if (memoryOnly.has(key)) return memory.get(key) ?? null
  try {
    return sessionStorage.getItem(key) ?? memory.get(key) ?? null
  } catch {
    return memory.get(key) ?? null
  }
}
function write(key: string, review: CourseReview | undefined) {
  const raw = review ? JSON.stringify(review) : null
  if (raw) {
    memory.set(key, raw)
    if (memory.size > 32) memory.delete(memory.keys().next().value!)
  } else memory.delete(key)
  try {
    raw ? sessionStorage.setItem(key, raw) : sessionStorage.removeItem(key)
    memoryOnly.delete(key)
  } catch {
    memoryOnly.add(key)
  }
  window.dispatchEvent(new Event(event))
}
const subscribe = (callback: () => void) => {
  window.addEventListener(event, callback)
  window.addEventListener('storage', callback)
  return () => {
    window.removeEventListener(event, callback)
    window.removeEventListener('storage', callback)
  }
}
export function markCourseReviewEdited(userId: string | undefined | null, courseId: string, node?: CourseSelection) {
  if (!userId) return
  const key = reviewKey(userId, courseId),
    review = parseCourseReview(read(key), courseId)
  if (review) write(key, markReviewChanged(review, node))
}
export function useCourseReview(courseId?: string) {
  const { user } = useUser()
  const key = user?.userId && courseId ? reviewKey(user.userId, courseId) : undefined
  const raw = useSyncExternalStore(
    subscribe,
    () => read(key),
    () => null
  )
  const review = useMemo(() => parseCourseReview(raw, courseId ?? ''), [raw, courseId])
  return {
    review,
    storageUnavailable: !!key && memoryOnly.has(key),
    markEdited: (node?: CourseSelection) => {
      if (user?.userId && courseId) markCourseReviewEdited(user.userId, courseId, node)
    },
    save: (next: CourseReview) => {
      if (key && next.courseId === courseId) write(key, next)
    },
    clear: () => {
      if (key) write(key, undefined)
    },
  }
}
