import { Box, Button, Group, Stack, Tabs, Text, Title } from '@mantine/core'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import { Role } from '@Api'
import { CategoriesPanel } from '@Components/admin/workspace/CategoriesPanel'
import { ChallengesPanel } from '@Components/admin/workspace/ChallengesPanel'
import { MembersPanel } from '@Components/admin/workspace/MembersPanel'
import { InstancesPanel } from '@Components/admin/workspace/InstancesPanel'
import { SkillTreesPanel } from '@Components/admin/workspace/SkillTreesPanel'
import { WriteupsPanel } from '@Components/admin/workspace/WriteupsPanel'
import classes from '@Components/admin/workspace/AdminWorkspace.module.css'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { usePageTitle } from '@Hooks/usePageTitle'

type AdminTab = 'trees' | 'categories' | 'challenges' | 'members' | 'writeups' | 'writeupManagement' | 'instances'

const isKnownTab = (value: string | null): value is AdminTab =>
  value === 'trees' || value === 'categories' || value === 'challenges' || value === 'members' ||
  value === 'writeups' || value === 'writeupManagement' || value === 'instances'

const AdminWorkspace = () => {
  const { t } = useTranslation('skillTrees')
  const [searchParams, setSearchParams] = useSearchParams()
  const requested = searchParams.get('tab')
  const tab: AdminTab = isKnownTab(requested) ? requested : 'trees'
  const [createOpen, setCreateOpen] = useState(false)

  const tabLabels: Record<AdminTab, string> = {
    trees: t('list.title'),
    categories: t('category.title'),
    challenges: t('workspace.tabs.challenges'),
    members: t('workspace.tabs.members'),
    writeups: t('writeups.reviewMenu'),
    writeupManagement: t('writeups.manageMenu'),
    instances: t('instances.menu'),
  }
  usePageTitle(tabLabels[tab])

  const switchTab = (value: string | null) => {
    if (!value) return
    setCreateOpen(false)
    setSearchParams(value === 'trees' ? {} : { tab: value }, { replace: true })
  }

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
          <Stack gap={4}>
            <Title order={1}>{t('workspace.title')}</Title>
            <Text c="dimmed" size="sm">
              {t('workspace.subtitle')}
            </Text>
          </Stack>

          <Group justify="space-between" align="flex-start" gap="md" wrap="wrap" className={classes.toolbar}>
            <Tabs value={tab} onChange={switchTab} className={classes.tabs}>
              <Tabs.List className={classes.tabList}>
                <Tabs.Tab value="trees">{tabLabels.trees}</Tabs.Tab>
                <Tabs.Tab value="categories">{tabLabels.categories}</Tabs.Tab>
                <Tabs.Tab value="challenges">{tabLabels.challenges}</Tabs.Tab>
                <Tabs.Tab value="members">{tabLabels.members}</Tabs.Tab>
                <Tabs.Tab value="writeups">{tabLabels.writeups}</Tabs.Tab>
                <Tabs.Tab value="writeupManagement">{tabLabels.writeupManagement}</Tabs.Tab>
                <Tabs.Tab value="instances">{tabLabels.instances}</Tabs.Tab>
              </Tabs.List>
            </Tabs>
            <Group gap="xs">
              <Button component={Link} to="/admin/settings" variant="default">
                {t('admin:tab.settings')}
              </Button>
              {createLabels[tab] && <Button className={classes.primaryAction} onClick={openCreate}>
                {createLabels[tab]}
              </Button>}
            </Group>
          </Group>

          <Box className={classes.content}>
            {tab === 'trees' && (
              <SkillTreesPanel createOpen={createOpen} onClose={closeCreate} onOpen={openCreate} />
            )}
            {tab === 'categories' && (
              <CategoriesPanel createOpen={createOpen} onClose={closeCreate} onOpen={openCreate} />
            )}
            {tab === 'challenges' && (
              <ChallengesPanel createOpen={createOpen} onClose={closeCreate} onOpen={openCreate} />
            )}
            {tab === 'members' && (
              <MembersPanel createOpen={createOpen} onClose={closeCreate} onOpen={openCreate} />
            )}
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
