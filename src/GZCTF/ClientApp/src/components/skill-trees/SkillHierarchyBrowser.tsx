import { Button, Drawer } from '@mantine/core'
import { useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate } from 'react-router'
import {
  contentCompleted,
  orderedSkills,
  progressPercent,
  skillHref,
  type Counts,
  type HierarchyTree,
} from '@Utils/skillHierarchy'
import { skillTreeIcons, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import { useTreeProgress } from '@Hooks/useSkillHierarchy'
import { useUser } from '@Hooks/useUser'
import classes from './SkillHierarchyBrowser.module.css'

export const HierarchySteps = ({ treeId, categoryId }: { treeId?: string; categoryId?: string }) => (
  <nav className={classes.steps} aria-label="学习路径选择">
    <Link to="/skill-trees" aria-current={!treeId ? 'step' : undefined}>
      <b>1</b>选择路线
    </Link>
    <span />
    <Link
      to={treeId ? `/skill-trees/${treeId}` : '/skill-trees'}
      aria-current={treeId && !categoryId ? 'step' : undefined}
    >
      <b>2</b>选择类别
    </Link>
    <span />
    <Link
      to={
        treeId && categoryId
          ? `/skill-trees/${treeId}/${categoryId}/skills`
          : treeId
            ? `/skill-trees/${treeId}`
            : '/skill-trees'
      }
      aria-current={categoryId ? 'step' : undefined}
    >
      <b>3</b>选择技能
    </Link>
  </nav>
)

export const LearningProgress = ({ counts }: { counts?: Counts }) =>
  counts ? (
    <div className={classes.progress}>
      <span>
        个人进度{' '}
        <strong>
          {counts.completedCount} / {counts.totalCount}
        </strong>
      </span>
      <div
        role="progressbar"
        aria-label="个人进度"
        aria-valuenow={Math.round(progressPercent(counts))}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <i style={{ width: `${progressPercent(counts)}%` }} />
      </div>
    </div>
  ) : null

const DirectoryGroup = ({
  identity,
  active,
  title,
  children,
}: {
  identity: string
  active: boolean
  title?: string
  children: ReactNode
}) => {
  const [open, setOpen] = useState(() => {
    try {
      const saved = sessionStorage.getItem(`skill-directory:${identity}`)
      return active || saved === 'true'
    } catch {
      return active
    }
  })
  useEffect(() => {
    if (active) setOpen(true)
  }, [active])
  return (
    <details
      open={open}
      onToggle={(event) => {
        const value = event.currentTarget.open
        setOpen(value)
        try {
          sessionStorage.setItem(`skill-directory:${identity}`, String(value))
        } catch {}
      }}
    >
      <summary>{title}</summary>
      {children}
    </details>
  )
}
export const ProgressNotice = ({ treeId }: { treeId?: string }) => {
  const { user } = useUser()
  const { error, mutate } = useTreeProgress(treeId)
  return !user ? (
    <p className={classes.notice}>
      <Link to="/account/login">登录后查看个人学习进度</Link>
    </p>
  ) : error ? (
    <p className={classes.notice} role="alert">
      个人进度加载失败。
      <Button
        variant="subtle"
        size="xs"
        onClick={() => {
          void mutate()
        }}
      >
        重试
      </Button>
    </p>
  ) : null
}

export const SkillDirectory = ({
  tree,
  categoryId,
  skillId,
  contentId,
  onNavigate,
}: {
  tree: HierarchyTree
  categoryId?: string
  skillId?: string
  contentId?: string
  onNavigate?: () => void
}) => {
  const { data: progress } = useTreeProgress(tree.skillTreeId)
  const { t } = useTranslation('lessonWorkspace')
  return (
    <nav aria-label={t('courseDirectory')} className={classes.directory}>
      <Link to={`/skill-trees/${tree.skillTreeId}`} onClick={onNavigate}>
        <strong>{tree.name}</strong>
      </Link>
      {tree.categories?.map((category) => (
        <DirectoryGroup
          key={category.categoryId}
          identity={`${tree.skillTreeId}:${category.categoryId}`}
          active={category.categoryId === categoryId}
          title={category.name}
        >
          {orderedSkills(category).map((skill) => (
            <DirectoryGroup
              key={skill.skillId}
              identity={`${tree.skillTreeId}:${category.categoryId}:${skill.skillId}`}
              active={category.categoryId === categoryId && skill.skillId === skillId}
              title={skill.name}
            >
              <Link to={skillHref(tree.skillTreeId!, category.categoryId!, skill.skillId)} onClick={onNavigate}>
                技能概览
              </Link>
              {skill.contents.map((item) => (
                <Link
                  key={`${item.kind}-${item.contentId}`}
                  aria-current={
                    category.categoryId === categoryId && skill.skillId === skillId && item.contentId === contentId
                      ? 'page'
                      : undefined
                  }
                  to={`${skillHref(tree.skillTreeId!, category.categoryId!, skill.skillId)}/${item.kind}/${item.contentId}`}
                  onClick={onNavigate}
                >
                  <span aria-hidden>
                    {contentCompleted(progress, item.kind, item.contentId) ? '✓' : item.kind === 'lesson' ? '▤' : '◇'}
                  </span>{' '}
                  {item.title}
                </Link>
              ))}
            </DirectoryGroup>
          ))}
        </DirectoryGroup>
      ))}
    </nav>
  )
}

export const SkillHierarchyBrowser = ({
  tree,
  categoryId,
  skillId,
}: {
  tree: HierarchyTree
  categoryId?: string
  skillId?: string
}) => {
  const { data: progress } = useTreeProgress(tree.skillTreeId)
  const [opened, setOpened] = useState(false)
  const category = tree.categories?.find((item) => item.categoryId === categoryId)
  const skill = orderedSkills(category).find((item) => item.skillId === skillId)
  useEffect(() => {
    if (sessionStorage.getItem('skill-directory-focus') === 'true') {
      sessionStorage.removeItem('skill-directory-focus')
      document.querySelector<HTMLElement>('main h1, article h1, h1')?.focus()
    }
  }, [skillId, categoryId])
  if ((categoryId && !category) || (skillId && !skill)) return <Navigate to="/404" replace />
  const items = skill
    ? skill.contents.map((content) => ({
        id: `${content.kind}-${content.contentId}`,
        name: content.title,
        summary: content.summary,
        iconKey: content.kind === 'lesson' ? 'book' : 'flag',
        count: undefined,
        href: `${skillHref(tree.skillTreeId!, categoryId!, skill.skillId)}/${content.kind}/${content.contentId}`,
        completed: contentCompleted(progress, content.kind, content.contentId),
        counts: undefined,
        detail: `${content.kind === 'lesson' ? '课节' : '题目'}${content.difficulty ? ` · ${content.difficulty}` : ''}${content.expectedMinutes ? ` · ${content.expectedMinutes} 分钟` : ''}`,
      }))
    : category
      ? orderedSkills(category).map((item) => ({
          id: item.skillId,
          name: item.name,
          summary: item.summary,
          iconKey: item.iconKey,
          count: item.challengeCount + item.lessonCount,
          href: skillHref(tree.skillTreeId!, categoryId!, item.skillId),
          completed: false,
          detail: `${item.challengeCount} 道题目 · ${item.lessonCount} 篇课节}`,
          counts: progress?.skills.find((value) => value.skillId === item.skillId),
        }))
      : (tree.categories ?? []).map((item) => ({
          id: item.categoryId!,
          name: item.name,
          summary: item.summary,
          iconKey: item.iconKey,
          count: item.skills?.length ?? 0,
          href: `/skill-trees/${tree.skillTreeId}/${item.categoryId}/skills`,
          completed: false,
          detail: `${item.contents?.filter((content) => content.kind === 'challenge').length ?? 0} 道题目 · ${item.contents?.filter((content) => content.kind === 'lesson').length ?? 0} 篇课节`,
          counts: progress?.categories.find((value) => value.categoryId === item.categoryId),
        }))
  const directory = (
    <SkillDirectory
      tree={tree}
      categoryId={categoryId}
      skillId={skillId}
      onNavigate={() => {
        if (opened) {
          sessionStorage.setItem('skill-directory-focus', 'true')
        }
        setOpened(false)
      }}
    />
  )
  return (
    <div className={classes.page}>
      <HierarchySteps treeId={tree.skillTreeId} categoryId={categoryId} />
      <header className={classes.header}>
        <Link
          to={
            skill
              ? `/skill-trees/${tree.skillTreeId}/${categoryId}/skills`
              : category
                ? `/skill-trees/${tree.skillTreeId}`
                : '/skill-trees'
          }
        >
          ← 返回{skill ? '技能列表' : category ? '类别列表' : '路线列表'}
        </Link>
        <h1 tabIndex={-1}>{skill?.name ?? category?.name ?? tree.name}</h1>
        <p>{skill?.summary ?? category?.summary ?? tree.summary}</p>
        <LearningProgress
          counts={
            skill
              ? progress?.skills.find((item) => item.skillId === skillId)
              : category
                ? progress?.categories.find((item) => item.categoryId === categoryId)
                : progress
          }
        />
        <ProgressNotice treeId={tree.skillTreeId} />
        {skill && (
          <Button className={classes.mobileButton} variant="light" onClick={() => setOpened(true)}>
            课程目录
          </Button>
        )}
      </header>
      <div className={skill ? classes.workspace : undefined}>
        {skill && <aside className={classes.sidebar}>{directory}</aside>}
        <section aria-label={skill ? '技能内容' : category ? '选择具体技能' : '选择类别'}>
          <h2>{skill ? '开始学习与练习' : category ? '选择具体技能' : '选择类别'}</h2>
          <div className={classes.grid} key={skillId ?? categoryId ?? tree.skillTreeId}>
            {items.map((item, index) => (
              <Link
                className={classes.card}
                key={item.id}
                to={item.href}
                style={{ animationDelay: `${Math.min(index * 24, 120)}ms` }}
              >
                <span className={classes.icon} aria-hidden>
                  {skillTreeIcons[(item.iconKey ?? 'flag') as SkillTreeIconKey] ?? '◇'}
                </span>
                <h3>{item.name}</h3>
                {item.summary && <p>{item.summary}</p>}
                <p className={classes.detail}>{item.detail}</p>
                {category && !skill && (
                  <span className={classes.status}>
                    {item.count === 0
                      ? '暂无内容'
                      : item.counts
                        ? item.counts.completedCount === item.counts.totalCount && item.counts.totalCount > 0
                          ? '已完成'
                          : item.counts.completedCount > 0
                            ? '学习中'
                            : '未开始'
                        : '可开始学习'}
                  </span>
                )}
                <div className={classes.meta}>
                  {item.count !== undefined && (
                    <span>
                      {item.count} {category ? '项内容' : '个技能'}
                    </span>
                  )}
                  {item.completed && <span>✓ 已完成</span>}
                  <span>进入 →</span>
                </div>
                <LearningProgress counts={item.counts} />
              </Link>
            ))}
          </div>
          {!items.length && <p className={classes.empty}>内容正在准备中，请稍后再来。</p>}
        </section>
      </div>
      <Drawer opened={opened} onClose={() => setOpened(false)} title="课程目录" size={320}>
        {directory}
      </Drawer>
    </div>
  )
}
