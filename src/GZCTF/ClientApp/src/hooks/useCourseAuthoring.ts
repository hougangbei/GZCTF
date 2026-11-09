import useSWR from 'swr'
import type { CourseWorkspace } from '@Utils/courseAuthoring'
import { useUser } from '@Hooks/useUser'
import { fetcher } from '@Api'

export class CourseRequestError extends Error {
  constructor(
    message: string,
    public status: number,
    public code?: string
  ) {
    super(message)
  }
}
export async function courseRequest<T>(path: string, data?: unknown, method = 'PUT'): Promise<T> {
  const response = await fetch(path, {
    credentials: 'include',
    method,
    headers: data instanceof FormData ? undefined : { 'Content-Type': 'application/json' },
    body: data === undefined ? undefined : data instanceof FormData ? data : JSON.stringify(data),
  })
  if (!response.ok) {
    const result = await response.json().catch(() => ({}))
    throw new CourseRequestError(
      response.status === 409
        ? '草稿版本已变化，请重新加载并核对修改后重试。'
        : (result.message ?? result.detail ?? '操作失败，请保留修改后重试。'),
      response.status,
      result.code
    )
  }
  return response.status === 204 ? (undefined as T) : response.json()
}
export const useCourseWorkspace = (id: string) => {
  const { user } = useUser()
  return useSWR<CourseWorkspace>(
    user?.userId && id ? [`/api/admin/skill-trees/${id}/workspace`, user.userId] : null,
    ([path]: [string, string]) => fetcher(path),
    {
      keepPreviousData: false,
      revalidateOnFocus: false,
      revalidateOnMount: true,
      dedupingInterval: 0,
    }
  )
}
