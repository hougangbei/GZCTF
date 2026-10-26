import { Button, Group, Stack, Text } from '@mantine/core'
import {
  mdiAccountCogOutline,
  mdiAccountGroupOutline,
  mdiBookOpenPageVariantOutline,
  mdiChevronDown,
  mdiFileCheckOutline,
  mdiFileDocumentOutline,
  mdiFileImportOutline,
  mdiFileTreeOutline,
  mdiFlagOutline,
  mdiFolderOutline,
  mdiInformationOutline,
  mdiMenu,
  mdiMonitorDashboard,
  mdiPackageVariantClosed,
  mdiSitemapOutline,
  mdiUpdate,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { type PropsWithChildren, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { getAdminSection } from '@Utils/adminNavigation'
import classes from './AdminShell.module.css'

type Item = { key: string; label: string; path: string; icon: string }
export const AdminShell = ({ children }: PropsWithChildren) => {
  const { t } = useTranslation(['admin', 'skillTrees', 'learning'])
  const location = useLocation()
  const [opened, setOpened] = useState(false)
  const toggle = useRef<HTMLButtonElement>(null)
  const active = getAdminSection(location.pathname, location.search)
  const workspace = (key: string) => (key === 'trees' ? '/admin/skill-trees' : `/admin/skill-trees?tab=${key}`)
  const groups: { label: string; items: Item[] }[] = [
    {
      label: t('navigation.content'),
      items: [
        { key: 'trees', label: t('skillTrees:list.title'), path: workspace('trees'), icon: mdiFileTreeOutline },
        {
          key: 'categories',
          label: t('skillTrees:category.title'),
          path: workspace('categories'),
          icon: mdiFolderOutline,
        },
        {
          key: 'challenges',
          label: t('skillTrees:workspace.tabs.challenges'),
          path: workspace('challenges'),
          icon: mdiFlagOutline,
        },
        {
          key: 'lessons',
          label: t('learning:adminLessonLibrary'),
          path: '/admin/library/lessons',
          icon: mdiBookOpenPageVariantOutline,
        },
      ],
    },
    {
      label: t('navigation.people'),
      items: [
        {
          key: 'members',
          label: t('skillTrees:workspace.tabs.members'),
          path: workspace('members'),
          icon: mdiAccountGroupOutline,
        },
        { key: 'users', label: t('tab.users'), path: workspace('users'), icon: mdiAccountCogOutline },
      ],
    },
    {
      label: t('navigation.operations'),
      items: [
        {
          key: 'writeups',
          label: t('skillTrees:writeups.reviewMenu'),
          path: workspace('writeups'),
          icon: mdiFileCheckOutline,
        },
        {
          key: 'writeupManagement',
          label: t('skillTrees:writeups.manageMenu'),
          path: workspace('writeupManagement'),
          icon: mdiFileDocumentOutline,
        },
        {
          key: 'instances',
          label: t('skillTrees:instances.menu'),
          path: workspace('instances'),
          icon: mdiPackageVariantClosed,
        },
        {
          key: 'gameInstances',
          label: t('navigation.gameInstances'),
          path: '/admin/instances',
          icon: mdiPackageVariantClosed,
        },
      ],
    },
    {
      label: t('navigation.system'),
      items: [
        {
          key: 'dashboards',
          label: t('learning:adminDashboards'),
          path: '/admin/dashboards',
          icon: mdiMonitorDashboard,
        },
        { key: 'imports', label: t('learning:adminImportReview'), path: '/admin/imports', icon: mdiFileImportOutline },
        { key: 'settings', label: t('tab.settings'), path: '/admin/settings', icon: mdiSitemapOutline },
        { key: 'logs', label: t('tab.logs'), path: '/admin/logs', icon: mdiFileDocumentOutline },
        { key: 'updates', label: t('tab.updates'), path: '/admin/updates', icon: mdiUpdate },
        { key: 'about', label: t('tab.about'), path: '/admin/about', icon: mdiInformationOutline },
      ],
    },
  ]
  useEffect(() => setOpened(false), [location.pathname, location.search])
  return (
    <div className={classes.shell}>
      <aside className={classes.sidebar}>
        <Group className={classes.mobileHeading} justify="space-between" wrap="nowrap">
          <Text fw={600}>{t('navigation.title')}</Text>
          <Button
            ref={toggle}
            variant="default"
            size="compact-sm"
            aria-expanded={opened}
            aria-controls="admin-navigation"
            onClick={() => setOpened((value) => !value)}
            leftSection={<Icon path={mdiMenu} size={0.75} aria-hidden="true" />}
            rightSection={<Icon path={mdiChevronDown} size={0.65} aria-hidden="true" />}
          >
            {t('navigation.menu')}
          </Button>
        </Group>
        <nav
          id="admin-navigation"
          aria-label={t('navigation.title')}
          className={classes.navigation}
          data-open={opened || undefined}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              setOpened(false)
              toggle.current?.focus()
            }
          }}
        >
          {groups.map((group) => (
            <section key={group.label} className={classes.group}>
              <Text className={classes.groupLabel} size="xs" fw={600}>
                {group.label}
              </Text>
              <Stack gap={2}>
                {group.items.map((item) => (
                  <Link
                    key={item.key}
                    to={item.path}
                    className={classes.link}
                    aria-current={active === item.key ? 'page' : undefined}
                    onClick={() => {
                      setOpened(false)
                      toggle.current?.focus()
                    }}
                  >
                    <Icon path={item.icon} size={0.8} aria-hidden="true" />
                    <span>{item.label}</span>
                  </Link>
                ))}
              </Stack>
            </section>
          ))}
        </nav>
      </aside>
      <div className={classes.content}>{children}</div>
    </div>
  )
}
