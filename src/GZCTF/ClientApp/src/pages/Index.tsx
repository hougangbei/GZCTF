import { mdiArrowRight, mdiChartLine, mdiFileTreeOutline, mdiNoteTextOutline, mdiSchoolOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { Link } from 'react-router'
import { useTranslation } from 'react-i18next'
import { PostCard } from '@Components/PostCard'
import { getCollectionStatus, PageState } from '@Components/PageState'
import { WithNavBar } from '@Components/WithNavbar'
import { MobilePostCard } from '@Components/mobile/PostCard'
import { showErrorMsg } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { usePageTitle } from '@Hooks/usePageTitle'
import { getPlatformName, useConfig } from '@Hooks/useConfig'
import api, { PostInfoModel } from '@Api'
import classes from '@Styles/Index.module.css'

const features = [
  { title: 'skillTitle', description: 'skillDescription', href: '/skill-trees', icon: mdiFileTreeOutline },
  { title: 'learningTitle', description: 'learningDescription', href: '/learn', icon: mdiSchoolOutline },
  { title: 'leaderboardTitle', description: 'leaderboardDescription', href: '/leaderboard', icon: mdiChartLine },
  { title: 'postsTitle', description: 'postsDescription', href: '/posts', icon: mdiNoteTextOutline },
] as const

const Home = () => {
  const { t } = useTranslation(['translation', 'home'])
  const { config } = useConfig()
  const platformName = getPlatformName(config)
  const { data: posts, error, mutate } = api.info.useInfoGetLatestPosts({ refreshInterval: 5 * 60 * 1000 })
  const isMobile = useIsMobile(900)
  usePageTitle()

  const onTogglePinned = async (post: PostInfoModel, setDisabled: (value: boolean) => void) => {
    setDisabled(true)
    try {
      const res = await api.editPosts.editPostsUpdate(post.id, { isPinned: !post.isPinned })
      if (post.isPinned) {
        mutate([
          ...(posts?.filter(p => p.id !== post.id && p.isPinned) ?? []),
          { ...res.data },
          ...(posts?.filter(p => p.id !== post.id && !p.isPinned) ?? []),
        ])
      } else {
        mutate([
          { ...res.data },
          ...(posts?.filter(p => p.id !== post.id && p.isPinned) ?? []),
          ...(posts?.filter(p => p.id !== post.id && !p.isPinned) ?? []),
        ])
      }
      api.info.mutateInfoGetPosts()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <WithNavBar minWidth={0} withFooter>
      <div className={classes.landing}>
        <section className={classes.hero} aria-labelledby="home-title">
          <div className={classes.heroCopy}>
            <span className={classes.badge}><span className={classes.badgeDot} aria-hidden="true" />{t('home:eyebrow')}</span>
            <h1 id="home-title" className={classes.heroTitle}>{t('home:line1')}<br /><em>{t('home:line2')}</em></h1>
            <p className={classes.description}>{t('home:description')}</p>
            <div className={classes.heroLinks}>
              <Link to="/skill-trees" className={classes.primaryLink}>{t('home:start')}<Icon path={mdiArrowRight} size={0.8} aria-hidden="true" /></Link>
              <Link to="/posts" className={classes.secondaryLink}>{t('home:posts')}</Link>
            </div>
          </div>
          <div className={classes.terminal} aria-hidden="true">
            <div className={classes.terminalBar}><span className={classes.terminalDots}><i /><i /><i /></span><span>{platformName} — {t('home:terminalTitle')}</span></div>
            <div className={classes.terminalBody}>
              <p><span>›</span> {platformName}</p>
              <p className={classes.terminalLine}>01 &nbsp; {t('home:skillTitle')}</p>
              <p className={classes.terminalLine}>02 &nbsp; {t('home:learningTitle')}</p>
              <p className={classes.terminalLine}>03 &nbsp; {t('home:leaderboardTitle')}</p>
              <span className={classes.cursor} />
            </div>
          </div>
        </section>

        <section className={classes.section} aria-labelledby="explore-title">
          <div className={classes.sectionHeading}>
            <h2 id="explore-title">{t('home:explore')}</h2>
            <p>{t('home:exploreDescription')}</p>
          </div>
          <div className={classes.featureGrid}>
            {features.map((feature, index) => (
              <Link key={feature.href} to={feature.href} className={classes.featureCard}>
                <span className={classes.featureNumber}>{String(index + 1).padStart(2, '0')}</span>
                <span className={classes.featureText}><strong>{t(`home:${feature.title}`)}</strong><span>{t(`home:${feature.description}`)}</span></span>
                <Icon path={feature.icon} size={1} className={classes.featureIcon} aria-hidden="true" />
              </Link>
            ))}
          </div>
        </section>

        <section className={classes.section} aria-labelledby="latest-title">
          <div className={classes.sectionHeading}>
            <h2 id="latest-title">{t('home:latest')}</h2>
            <p>{t('home:latestDescription')}</p>
          </div>
          <PageState
            status={getCollectionStatus(posts, error)}
            loadingMessage={t('learning:loading')}
            emptyMessage={t('common.content.no_data')}
            errorMessage={t('common.error.encountered')}
            retryLabel={t('common.button.try_again')}
            onRetry={() => { void mutate() }}
          >
            <div className={classes.posts}>
              {posts?.map(post => isMobile
                ? <MobilePostCard key={post.id} post={post} onTogglePinned={onTogglePinned} />
                : <PostCard key={post.id} post={post} onTogglePinned={onTogglePinned} />)}
            </div>
          </PageState>
        </section>
      </div>
    </WithNavBar>
  )
}

export default Home
