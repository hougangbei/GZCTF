import surface from '@Components/skill-trees/TrainingSurface.module.css'
import { Button, Skeleton } from '@mantine/core'
import { useParams, useSearchParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useSkillTree } from '@Hooks/useSkillTrees'
import { SkillTreeOutline } from '@Components/skill-trees/SkillTreeOutline'
import { WithNavBar } from '@Components/WithNavbar'
import { usePageTitle } from '@Hooks/usePageTitle'
import classes from '@Components/skill-trees/SkillTreeOutline.module.css'

const SkillTreeDetail = () => {
  const { id } = useParams()
  const [searchParams] = useSearchParams()
  const { t } = useTranslation('skillTrees')
  const { data: tree, error, mutate } = useSkillTree(id)
  usePageTitle(tree?.name ?? t('list.title'))

  return (
    <WithNavBar className={surface.surface} minWidth={0} withFooter>
      {!tree && !error ? (
        <div className={classes.detailState} role="status" aria-busy="true">
          <span>{t('loading')}</span>
          <Skeleton height={28} width="40%" radius="sm" />
          <Skeleton height={18} width="70%" radius="sm" />
        </div>
      ) : error && !tree ? (
        <div className={classes.detailState} role="alert">
          <p>{t('errors.generic')}</p>
          <Button onClick={() => { void mutate() }}>{t('editor.reload')}</Button>
        </div>
      ) : (
        <SkillTreeOutline tree={tree!} selectedCategoryId={searchParams.get('category') ?? undefined} />
      )}
    </WithNavBar>
  )
}

export default SkillTreeDetail
