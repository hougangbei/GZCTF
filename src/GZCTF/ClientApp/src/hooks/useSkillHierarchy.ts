import useSWR, { useSWRConfig } from 'swr'
import type { AdminSkill, TreeProgress } from '@Utils/skillHierarchy'
import { useUser } from '@Hooks/useUser'
import { fetcher } from '@Api'

export const useTreeProgress = (treeId?: string) => {
  const { user } = useUser()
  return useSWR<TreeProgress>(
    user && treeId ? `/api/my-learning/skill-trees/${treeId}` : null,
    async (path: string) => {
      const data = (await fetcher(path)) as TreeProgress
      if (
        !data ||
        !Array.isArray(data.categories) ||
        !Array.isArray(data.skills) ||
        !Array.isArray(data.completedLessonIds) ||
        !Array.isArray(data.solvedChallengeIds)
      )
        throw new Error('Invalid learning progress response')
      return data
    }
  )
}
export const useAdminSkills = () => useSWR<AdminSkill[]>('/api/admin/skills', fetcher)
export const useAdminSkill = (id?: string) => useSWR<AdminSkill>(id ? `/api/admin/skills/${id}` : null, fetcher)
export const hierarchyKey = (key: unknown) => {
  const path = Array.isArray(key) ? key[0] : key
  return (
    typeof path === 'string' &&
    /^\/api\/(admin\/(skills|skill-categories|skill-trees)|skill-trees|my-learning)/.test(path)
  )
}
export async function hierarchyRequest<T>(path: string, data?: unknown, method = 'PUT'): Promise<T> {
  const response = await fetch(path, {
    credentials: 'include',
    method,
    headers: data instanceof FormData ? undefined : { 'Content-Type': 'application/json' },
    body: data === undefined ? undefined : data instanceof FormData ? data : JSON.stringify(data),
  })
  if (!response.ok) {
    const body = await response.json().catch(() => ({}))
    throw new Error(
      response.status === 409
        ? '内容已被其他管理员修改，请重新加载后再编辑。'
        : (body.message ?? `操作失败 (${response.status})`)
    )
  }
  return response.status === 204 ? (undefined as T) : response.json()
}
export const useHierarchyMutations = () => {
  const { mutate } = useSWRConfig()
  return {
    invalidate: () => mutate(hierarchyKey),
    request: async <T>(path: string, data?: unknown, method?: string) => {
      const value = await hierarchyRequest<T>(path, data, method)
      await mutate(hierarchyKey)
      return value
    },
  }
}
