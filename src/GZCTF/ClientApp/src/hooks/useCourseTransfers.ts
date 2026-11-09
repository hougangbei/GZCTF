import useSWR from 'swr'
import type { ResourceRow } from '@Utils/courseTransfer'
import { useUser } from '@Hooks/useUser'
import { fetcher } from '@Api'

export const useCourseResources = (kind: string, enabled = true) => {
  const { user } = useUser()
  return useSWR<ResourceRow[]>(
    enabled && user?.userId ? [`/api/admin/course-resources?kind=${encodeURIComponent(kind)}`, user.userId] : null,
    ([path]: [string, string]) => fetcher(path),
    { keepPreviousData: false, revalidateOnFocus: true }
  )
}
