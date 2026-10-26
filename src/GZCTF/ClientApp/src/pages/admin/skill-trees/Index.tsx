import { Box, Button, Group, Stack, Title } from '@mantine/core'
import { mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import classes from '@Components/admin/workspace/AdminWorkspace.module.css'
import { CategoriesPanel } from '@Components/admin/workspace/CategoriesPanel'
import { ChallengesPanel } from '@Components/admin/workspace/ChallengesPanel'
import { InstancesPanel } from '@Components/admin/workspace/InstancesPanel'
import { MembersPanel } from '@Components/admin/workspace/MembersPanel'
import { SkillTreesPanel } from '@Components/admin/workspace/SkillTreesPanel'
import { WriteupsPanel } from '@Components/admin/workspace/WriteupsPanel'
import { usePageTitle } from '@Hooks/usePageTitle'
import { Role } from '@Api'
import { UsersPanel } from '../Users'

type AdminTab =
  'trees' | 'categories' | 'challenges' | 'members' | 'users' | 'writeups' | 'writeupManagement' | 'instances'

const isKnownTab = (value: string | null): value is AdminTab =>
  value === 'trees' ||
  value === 'categories' ||
  value === 'challenges' ||
  value === 'members' ||
  value === 'users' ||
  value === 'writeups' ||
  value === 'writeupManagement' ||
  value === 'instances'

const AdminWorkspace = () => {
  const { t } = useTranslation(['skillTrees', 'admin'])
  const [searchParams] = useSearchParams()
  const requested = searchParams.get('tab')
  const tab: AdminTab = isKnownTab(requested) ? requested : 'trees'
  const [createOpen, setCreateOpen] = useState(false)
  useEffect(() => setCreateOpen(false), [tab])

  const tabLabels: Record<AdminTab, string> = {
    trees: t('list.title'),
    categories: t('category.title'),
    challenges: t('workspace.tabs.challenges'),
    members: t('workspace.tabs.members'),
    users: t('admin:tab.users'),
    writeups: t('writeups.reviewMenu'),
    writeupManagement: t('writeups.manageMenu'),
    instances: t('instances.menu'),
  }
  usePageTitle(tabLabels[tab])

  const openCreate = () => setCreateOpen(true)
  const closeCreate = () => setCreateOpen(false)

  const createLabels: Partial<Record<AdminTab, string>> = {
    trees: t('list.createTitle'),
    categories: t('category.createTitle'),
    challenges: t('workspace.createChallenge'),
    members: t('workspace.createCohort'),
  }

  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>
        <Stack gap="md" className={classes.workspace}>
          <Title order={1}>{tabLabels[tab]}</Title>

          <Group justify="space-between" align="flex-start" gap="md" wrap="wrap" className={classes.toolbar}>
            <Group gap="xs">
              {createLabels[tab] && (
                <Button
                  leftSection={<Icon path={mdiPlus} size={0.8} aria-hidden="true" />}
                  className={classes.primaryAction}
                  onClick={openCreate}
                >
                  {createLabels[tab]}
                </Button>
              )}
            </Group>
          </Group>

          <Box className={classes.content}>
            {tab === 'trees' && <SkillTreesPanel createOpen={createOpen} onClose={closeCreate} onOpen={openCreate} />}
            {tab === 'categories' && (
              <CategoriesPanel createOpen={createOpen} onClose={closeCreate} onOpen={openCreate} />
            )}
            {tab === 'challenges' && (
              <ChallengesPanel createOpen={createOpen} onClose={closeCreate} onOpen={openCreate} />
            )}
            {tab === 'members' && <MembersPanel createOpen={createOpen} onClose={closeCreate} onOpen={openCreate} />}
            {tab === 'users' && <UsersPanel />}
            {tab === 'writeups' && <WriteupsPanel />}
            {tab === 'writeupManagement' && <WriteupsPanel mode="management" />}
            {tab === 'instances' && <InstancesPanel />}
          </Box>
        </Stack>
      </WithNavBar>
    </WithRole>
  )
}

export default AdminWorkspace
