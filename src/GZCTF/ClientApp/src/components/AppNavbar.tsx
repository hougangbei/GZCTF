import { mdiBookOpenPageVariantOutline, mdiChartLine, mdiFileTreeOutline, mdiHomeVariantOutline, mdiInformationOutline, mdiNoteTextOutline, mdiWrenchOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { useUser } from '@Hooks/useUser'
import { useConfig } from '@Hooks/useConfig'
import { getLaboratoryBlogUrl } from '@Utils/LaboratoryBlog'
import { Role } from '@Api'
import classes from '@Styles/AppNavbar.module.css'

type NavItem = { icon: string; label: string; link: string; admin?: boolean; external?: boolean }

interface AppNavbarProps {
  mobile: boolean
  mobileHidden: boolean
  onNavigate: () => void
}

export function AppNavbar({ mobile, mobileHidden, onNavigate }: AppNavbarProps) {
  const { t } = useTranslation(['translation', 'skillTrees'])
  const { user } = useUser()
  const { config } = useConfig()
  const blogUrl = getLaboratoryBlogUrl(config.laboratoryBlogUrl)
  const location = useLocation()
  const items: NavItem[] = [
    { icon: mdiHomeVariantOutline, label: 'common.tab.home', link: '/' },
    { icon: mdiFileTreeOutline, label: 'skillTrees:navigation.title', link: '/skill-trees' },
    { icon: mdiNoteTextOutline, label: 'common.tab.post', link: '/posts' },
    { icon: mdiChartLine, label: 'skillTrees:leaderboard.title', link: '/leaderboard' },
    { icon: mdiInformationOutline, label: 'common.tab.about', link: '/about' },
    ...(blogUrl ? [{ icon: mdiBookOpenPageVariantOutline, label: 'common.tab.laboratory_blog', link: blogUrl, external: true }] : []),
    { icon: mdiWrenchOutline, label: 'common.tab.admin', link: '/admin/skill-trees', admin: true },
  ]

  return (
    <nav className={classes.navbar} data-mobile={mobile || undefined} data-open={mobile && !mobileHidden || undefined} aria-label={t('common.accessibility.navigation')} aria-hidden={mobileHidden || undefined} inert={mobileHidden}>
      {items.filter(item => !item.admin || user?.role === Role.Admin).map(item => {
        if (item.external) {
          return (
            <a key={item.link} href={item.link} target="_blank" rel="noopener noreferrer" className={classes.link} onClick={onNavigate}>
              <Icon path={item.icon} size={0.8} aria-hidden="true" />
              <span>{t(item.label)}</span>
            </a>
          )
        }
        const selected = item.admin
          ? location.pathname.startsWith('/admin')
          : item.link === '/' ? location.pathname === '/' : location.pathname.startsWith(item.link)
        return (
          <Link key={item.link} to={item.link} className={classes.link} aria-current={selected ? 'page' : undefined} onClick={onNavigate}>
            <Icon path={item.icon} size={0.8} aria-hidden="true" />
            <span>{t(item.label)}</span>
          </Link>
        )
      })}
    </nav>
  )
}
