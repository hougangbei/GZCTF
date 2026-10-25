import { mdiArrowLeft, mdiArrowRight, mdiBookOpenPageVariantOutline, mdiPuzzleOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import type { SkillTreeDetailResponse } from '@Api'
import { skillTreeIcons, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import classes from './SkillTreeOutline.module.css'

type SkillTreeOutlineProps = {
  tree: SkillTreeDetailResponse
  selectedCategoryId?: string
}

export const SkillTreeOutline = ({ tree, selectedCategoryId }: SkillTreeOutlineProps) => {
  const { t } = useTranslation('skillTrees')
  const selected = tree.categories?.find(category => category.categoryId === selectedCategoryId)
  const treeHref = `/skill-trees/${tree.skillTreeId}`
  const icon = skillTreeIcons[(tree.iconKey as SkillTreeIconKey) ?? 'flag']

  return <div className={classes.outline}>
    <Link className={classes.back} to="/skill-trees">
      <Icon path={mdiArrowLeft} size={0.75} aria-hidden="true" />{t('list.backToList')}
    </Link>
    <header className={classes.hero}>
      <span className={classes.heroIcon} aria-hidden="true">{icon}</span>
      <div>
        <h1>{tree.name}</h1>
        {tree.summary && <p>{tree.summary}</p>}
      </div>
    </header>

    {!tree.categories?.length ? (
      <section className={classes.empty} role="status">{t('editor.emptyPreview')}</section>
    ) : selected ? (
      <section aria-labelledby="selected-category-title">
        <Link className={classes.categoryBack} to={treeHref}>
          <Icon path={mdiArrowLeft} size={0.75} aria-hidden="true" />{t('content.allCategories')}
        </Link>
        <div className={classes.sectionHeader}>
          <h2 id="selected-category-title">{selected.name}</h2>
          {selected.summary && <p>{selected.summary}</p>}
        </div>
        {selected.contents?.length ? (
          <div className={classes.contentList}>
            {selected.contents.map(content => <Link
              key={`${content.kind}-${content.contentId}`}
              className={classes.contentLink}
              to={`${treeHref}/${selected.categoryId}/${content.kind}/${content.contentId}`}
            >
              <span className={classes.contentIcon} aria-hidden="true">
                <Icon path={content.kind === 'challenge' ? mdiPuzzleOutline : mdiBookOpenPageVariantOutline} size={1} />
              </span>
              <span className={classes.contentText}>
                <strong>{content.title}</strong>
                {content.summary && <span>{content.summary}</span>}
              </span>
              <span className={classes.contentMeta}>
                {content.kind === 'challenge' && content.difficulty &&
                  <span className={classes.difficulty}>{t(`difficulty.${content.difficulty.toLowerCase()}`)}</span>}
                {content.kind === 'challenge' && content.expectedMinutes &&
                  <span>{t('content.expectedMinutes', { count: content.expectedMinutes })}</span>}
              </span>
              <Icon className={classes.arrow} path={mdiArrowRight} size={0.8} aria-hidden="true" />
            </Link>)}
          </div>
        ) : <div className={classes.empty} role="status">{t('editor.emptyPreview')}</div>}
      </section>
    ) : (
      <section aria-labelledby="all-categories-title">
        <div className={classes.sectionHeader}>
          <h2 id="all-categories-title">{t('content.allCategories')}</h2>
          <p>{t('list.categoryCount', { count: tree.categories.length })}</p>
        </div>
        <ol className={classes.categoryList}>
          {tree.categories.map((category, index) => <li key={category.categoryId}>
            <Link className={classes.categoryLink} to={`${treeHref}?category=${category.categoryId}`}>
              <span className={classes.order}>{String(index + 1).padStart(2, '0')}</span>
              <span className={classes.categoryIcon} aria-hidden="true">
                {skillTreeIcons[(category.iconKey as SkillTreeIconKey) ?? 'flag']}
              </span>
              <span className={classes.categoryText}>
                <strong>{category.name}</strong>
                {category.summary && <span>{category.summary}</span>}
              </span>
              <span className={classes.itemCount}>{t('content.itemCount', { count: category.contents?.length ?? 0 })}</span>
              <Icon className={classes.arrow} path={mdiArrowRight} size={0.8} aria-hidden="true" />
            </Link>
          </li>)}
        </ol>
      </section>
    )}
  </div>
}
