import { mdiArrowRight } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import type { HierarchyTreeSummary } from '@Utils/skillHierarchy'
import { skillTreeIcons, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import { useTreeProgress } from '@Hooks/useSkillHierarchy'
import { LearningProgress } from './SkillHierarchyBrowser'
import classes from './SkillTreeCard.module.css'

type SkillTreeCardProps = {
  tree: HierarchyTreeSummary
}

export const SkillTreeCard = ({ tree }: SkillTreeCardProps) => {
  const { t } = useTranslation('skillTrees')
  const { data: progress, error: progressError } = useTreeProgress(tree.skillTreeId)
  const icon = skillTreeIcons[(tree.iconKey as SkillTreeIconKey) ?? 'flag']

  return (
    <Link className={classes.card} to={`/skill-trees/${tree.skillTreeId}`}>
      <div className={classes.cover}>
        <img
          src={tree.coverUrl || '/images/skill-route.svg'}
          alt=""
          onError={(event) => {
            event.currentTarget.onerror = null
            event.currentTarget.src = '/images/skill-route.svg'
          }}
        />
        <span className={classes.icon} aria-hidden="true">
          {icon}
        </span>
      </div>
      <h2>{tree.name}</h2>
      {tree.summary && <p className={classes.summary}>{tree.summary}</p>}
      <div className={classes.meta}>
        <span>{tree.skillCount ?? 0} 个技能</span>
        <span>{t('list.categoryCount', { count: tree.categoryCount ?? 0 })}</span>
        <span>{t('list.challengeCount', { count: tree.challengeCount ?? 0 })}</span>
        <span>{t('list.lessonCount', { count: tree.lessonCount ?? 0 })}</span>
      </div>
      <LearningProgress counts={progress} />
      {progressError && <span role="status">个人进度加载失败，进入路线后重试</span>}
      <span className={classes.open}>
        {t('list.open')}
        <Icon path={mdiArrowRight} size={0.8} aria-hidden="true" />
      </span>
    </Link>
  )
}
