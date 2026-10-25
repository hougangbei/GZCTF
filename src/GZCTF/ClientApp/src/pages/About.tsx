import { Button, Skeleton } from '@mantine/core'
import { mdiArrowRight, mdiInformationOutline, mdiPencilOutline, mdiReload } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import useSWR from 'swr'
import { Role, fetcher } from '@Api'
import { WithNavBar } from '@Components/WithNavbar'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import { AboutRenderer } from '../features/about/AboutRenderer'
import type { AboutDocument } from '../features/about/types'
import classes from './AboutPage.module.css'

type PublicAbout = { published: boolean; document?: AboutDocument }

const About = () => {
  const { t } = useTranslation()
  const { user } = useUser()
  const { data, error, mutate } = useSWR<PublicAbout>('/api/about', fetcher, {
    revalidateOnFocus: false,
    shouldRetryOnError: false,
  })
  const published = data?.published && data.document
  const title = published ? data.document!.title : t('common.title.about')
  usePageTitle(title)

  return <WithNavBar minWidth={0} withFooter>
    <div className={classes.page}>
      {user?.role === Role.Admin && <Link className={classes.edit} to="/admin/about">
        <Icon path={mdiPencilOutline} size={0.75} aria-hidden="true" />
        {t('common.content.about.edit')}
      </Link>}

      {published ? <>
        {error && <div className={classes.stale} role="alert">
          <span>{t('common.content.about.loadFailed')}</span>
          <Button size="compact-sm" variant="subtle" onClick={() => { void mutate() }}>
            {t('common.content.about.retry')}
          </Button>
        </div>}
        <AboutRenderer document={data.document!} subtitle={t('common.content.about.subtitle')} />
      </> : <>
        <header className={classes.header}>
          <h1>{t('common.title.about')}</h1>
          <p>{t('common.content.about.subtitle')}</p>
        </header>

        {!data && !error ? (
          <section className={classes.loading} role="status" aria-busy="true">
            <span className={classes.srOnly}>{t('common.content.about.subtitle')}</span>
            <Skeleton height={26} width="30%" radius="sm" />
            <Skeleton height={140} radius="md" />
          </section>
        ) : error ? (
          <section className={classes.statePanel} role="alert">
            <span className={classes.stateIcon} aria-hidden="true"><Icon path={mdiInformationOutline} size={1.7} /></span>
            <h2>{t('common.content.about.loadFailed')}</h2>
            <Button leftSection={<Icon path={mdiReload} size={0.8} />} onClick={() => { void mutate() }}>
              {t('common.content.about.retry')}
            </Button>
          </section>
        ) : (
          <section className={classes.statePanel} role="status">
            <span className={classes.stateIcon} aria-hidden="true"><Icon path={mdiInformationOutline} size={1.7} /></span>
            <h2>{t('common.content.about.emptyTitle')}</h2>
            <p>{t('common.content.about.emptyDescription')}</p>
            <Link className={classes.action} to="/posts">
              {t('common.content.about.explorePosts')}<Icon path={mdiArrowRight} size={0.8} aria-hidden="true" />
            </Link>
          </section>
        )}
      </>}
    </div>
  </WithNavBar>
}

export default About
