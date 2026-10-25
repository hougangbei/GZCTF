import { Button, Skeleton } from '@mantine/core'
import { mdiArrowRight, mdiFileTreeOutline, mdiReload } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { SkillTreeCard } from '@Components/skill-trees/SkillTreeCard'
import { WithNavBar } from '@Components/WithNavbar'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useSkillTrees } from '@Hooks/useSkillTrees'
import classes from './SkillTreeIndex.module.css'

const SkillTreeIndex = () => {
  const { t } = useTranslation('skillTrees')
  const { data: trees, error, mutate } = useSkillTrees()
  usePageTitle(t('list.title'))

  return (
    <WithNavBar minWidth={0} withFooter>
      <div className={classes.page}>
        <header className={classes.header}>
          <h1>{t('list.title')}</h1>
          <p>{t('list.subtitle')}</p>
        </header>

        {!trees && !error && (
          <section className={classes.loading} role="status" aria-busy="true">
            <span className={classes.srOnly}>{t('loading')}</span>
            {[0, 1, 2].map(index => <div className={classes.skeletonCard} key={index}>
              <Skeleton height={46} width={46} radius="md" />
              <Skeleton height={25} width="65%" radius="sm" />
              <Skeleton height={14} width="85%" radius="sm" />
              <Skeleton height={14} width="75%" radius="sm" />
            </div>)}
          </section>
        )}

        {error && !trees && (
          <section className={classes.statePanel} role="alert">
            <span className={classes.stateIcon} aria-hidden="true"><Icon path={mdiFileTreeOutline} size={1.7} /></span>
            <h2>{t('errors.generic')}</h2>
            <Button leftSection={<Icon path={mdiReload} size={0.8} />} onClick={() => { void mutate() }}>
              {t('editor.reload')}
            </Button>
          </section>
        )}

        {trees?.length === 0 && (
          <section className={classes.statePanel} role="status">
            <span className={classes.stateIcon} aria-hidden="true"><Icon path={mdiFileTreeOutline} size={1.7} /></span>
            <h2>{t('list.emptyTitle')}</h2>
            <p>{t('list.emptyDescription')}</p>
            <Link className={classes.action} to="/posts">
              {t('list.explorePosts')}<Icon path={mdiArrowRight} size={0.8} aria-hidden="true" />
            </Link>
          </section>
        )}

        {trees && trees.length > 0 && (
          <div className={classes.grid} data-count={trees.length}>
            {trees.map(tree => <SkillTreeCard key={tree.skillTreeId} tree={tree} />)}
          </div>
        )}
      </div>
    </WithNavBar>
  )
}

export default SkillTreeIndex
