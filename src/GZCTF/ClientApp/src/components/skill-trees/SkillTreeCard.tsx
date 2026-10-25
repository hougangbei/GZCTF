import { mdiArrowRight } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { skillTreeIcons, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import type { SkillTreeSummaryResponse } from '@Api'
import classes from './SkillTreeCard.module.css'

type SkillTreeCardProps = {
  tree: SkillTreeSummaryResponse
}

export const SkillTreeCard = ({ tree }: SkillTreeCardProps) => {
  const { t } = useTranslation('skillTrees')
  const icon = skillTreeIcons[(tree.iconKey as SkillTreeIconKey) ?? 'flag']

  return <Link className={classes.card} to={`/skill-trees/${tree.skillTreeId}`}>
    <span className={classes.icon} aria-hidden="true">{icon}</span>
    <h2>{tree.name}</h2>
    {tree.summary && <p className={classes.summary}>{tree.summary}</p>}
    <div className={classes.meta}>
      <span>{t('list.categoryCount', { count: tree.categoryCount ?? 0 })}</span>
      <span>{t('list.challengeCount', { count: tree.challengeCount ?? 0 })}</span>
      <span>{t('list.lessonCount', { count: tree.lessonCount ?? 0 })}</span>
    </div>
    <span className={classes.open}>{t('list.open')}<Icon path={mdiArrowRight} size={0.8} aria-hidden="true" /></span>
  </Link>
}
