export const workspaceTabs = [
  'trees',
  'categories',
  'challenges',
  'members',
  'users',
  'writeups',
  'writeupManagement',
  'instances',
] as const
export type WorkspaceTab = (typeof workspaceTabs)[number]
export const isWorkspaceTab = (value: string | null): value is WorkspaceTab =>
  workspaceTabs.some((tab) => tab === value)

export const getAdminSection = (pathname: string, search: string): string => {
  const path = pathname.replace(/\/$/, '')
  if (path === '/admin/skill-trees') {
    const tab = new URLSearchParams(search).get('tab')
    return isWorkspaceTab(tab) ? tab : 'trees'
  }
  if (path.startsWith('/admin/skill-trees/')) return 'trees'
  if (path.startsWith('/admin/skill-categories')) return 'categories'
  if (path.startsWith('/admin/library/challenges')) return 'challenges'
  if (path.startsWith('/admin/library/lessons')) return 'lessons'
  if (path.startsWith('/admin/cohorts')) return 'members'
  if (path === '/admin/instances') return 'gameInstances'
  return path.split('/')[2] ?? 'trees'
}
