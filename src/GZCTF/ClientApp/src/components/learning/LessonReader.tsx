import { Button, Drawer, Group, Loader, Text } from '@mantine/core'
import { mdiBookOpenPageVariantOutline, mdiPuzzleOutline, mdiCheck } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Markdown } from '@Components/MarkdownRenderer'
import { useLanguage } from '@Utils/I18n'
import { getLessonHeadings } from '@Utils/lessonMarkdown'
import { useLesson, useLearningMutations } from '@Hooks/useChallengeLibraryAdmin'
import { useMyLearning } from '@Hooks/useSkillTrees'
import { useUser } from '@Hooks/useUser'
import type { SkillTreeDetailResponse } from '@Api'
import classes from './LessonReader.module.css'

type Props = { tree: SkillTreeDetailResponse; categoryId: string; lessonId: string }
export const LessonReader = ({ tree, categoryId, lessonId }: Props) => {
  const { t } = useTranslation('lessonWorkspace')
  const { locale } = useLanguage()
  const { user } = useUser()
  const { data: lesson, error } = useLesson(lessonId, locale, true)
  const { data: record } = useMyLearning(Boolean(user), locale)
  const { completeLesson } = useLearningMutations()
  const [pending, setPending] = useState(false)
  const [completed, setCompleted] = useState(false)
  const [message, setMessage] = useState('')
  const [directoryOpen, setDirectoryOpen] = useState(false)
  const [focused, setFocused] = useState(false)
  const headings = useMemo(() => getLessonHeadings(lesson?.body ?? ''), [lesson?.body])
  const base = `/skill-trees/${tree.skillTreeId}`
  const category = tree.categories?.find((item) => item.categoryId === categoryId)
  const items = (tree.categories ?? []).flatMap((category) =>
    (category.contents ?? []).map((item) => ({ ...item, categoryId: category.categoryId }))
  )
  const index = items.findIndex(
    (item) => item.kind === 'lesson' && item.contentId === lessonId && item.categoryId === categoryId
  )
  const href = (item: (typeof items)[number]) => `${base}/${item.categoryId}/${item.kind}/${item.contentId}`
  const isCompleted = completed || (record?.completedLessonIds ?? []).includes(lessonId)
  const complete = async () => {
    setPending(true)
    setMessage('')
    try {
      await completeLesson(lessonId)
      setCompleted(true)
    } catch {
      setMessage(t('saveFailed'))
    } finally {
      setPending(false)
    }
  }

  useEffect(() => {
    window.scrollTo(0, 0)
  }, [lessonId])

  const directory = (
    <nav aria-label={t('courseDirectory')} className={classes.directory}>
      <Link to={base} className={classes.treeName}>
        {tree.name}
      </Link>
      {tree.categories?.map((group) => (
        <section key={group.categoryId}>
          <h2>{group.name}</h2>
          {group.contents?.map((item) => (
            <Link
              key={`${item.kind}-${item.contentId}`}
              to={href({ ...item, categoryId: group.categoryId })}
              aria-current={
                item.kind === 'lesson' && item.contentId === lessonId && group.categoryId === categoryId
                  ? 'page'
                  : undefined
              }
              onClick={() => setDirectoryOpen(false)}
            >
              <Icon
                size={0.75}
                aria-hidden
                path={
                  item.kind === 'lesson' &&
                  ((record?.completedLessonIds ?? []).includes(item.contentId ?? '') ||
                    (item.contentId === lessonId && completed))
                    ? mdiCheck
                    : item.kind === 'lesson'
                      ? mdiBookOpenPageVariantOutline
                      : mdiPuzzleOutline
                }
              />
              <span>{item.title}</span>
            </Link>
          ))}
        </section>
      ))}
    </nav>
  )

  return (
    <div className={classes.reader} data-focused={focused || undefined}>
      <div className={classes.breadcrumb}>
        <Link to={`${base}?category=${categoryId}`}>← {t('backToTree')}</Link>
        <span>/</span>
        <span>{tree.name}</span>
        <span>/</span>
        <span>{category?.name}</span>
        <div className={classes.tools}>
          <Button size="xs" variant="light" onClick={() => setDirectoryOpen(true)}>
            {t('courseDirectory')}
          </Button>
          <Button
            size="xs"
            variant="subtle"
            className={classes.focusButton}
            onClick={() => setFocused((value) => !value)}
          >
            {t(focused ? 'showDirectory' : 'focusReading')}
          </Button>
        </div>
      </div>
      <aside className={classes.sidebar}>{directory}</aside>
      <article className={classes.article} aria-busy={!lesson && !error}>
        {!lesson && !error ? (
          <Loader />
        ) : error ? (
          <div role="alert">
            <Text c="red">{t('loadFailed')}</Text>
          </div>
        ) : (
          <>
            <div className={classes.kind}>{t('lesson')}</div>
            <h1>{lesson!.title}</h1>
            <p className={classes.meta}>
              {t('readingTime', { count: Math.max(1, Math.ceil(lesson!.body.length / 450)) })} ·{' '}
              {t(isCompleted ? 'completed' : 'notCompleted')}
            </p>
            <Markdown className={classes.markdown} source={lesson!.body} lessonHeadings />
            <footer className={classes.footer}>
              <div>
                {index > 0 && (
                  <Button component={Link} to={href(items[index - 1])} variant="default">
                    ← {t('previous')}
                  </Button>
                )}
              </div>
              <Group gap="xs">
                {user ? (
                  <Button loading={pending} disabled={isCompleted} onClick={complete}>
                    {t(isCompleted ? 'completed' : 'markComplete')}
                  </Button>
                ) : (
                  <Button component={Link} to="/account/login">
                    {t('signIn')}
                  </Button>
                )}
                {index >= 0 && index < items.length - 1 && (
                  <Button component={Link} to={href(items[index + 1])} variant="light">
                    {t('next')} →
                  </Button>
                )}
              </Group>
            </footer>
            {message && (
              <Text c="red" role="alert">
                {message}
              </Text>
            )}
          </>
        )}
      </article>
      <nav aria-label={t('pageOutline')} className={classes.toc}>
        <strong>{t('pageOutline')}</strong>
        {headings.length ? (
          headings.map((heading) => (
            <a
              key={heading.id}
              href={`#${heading.id}`}
              style={{ paddingInlineStart: Math.min(heading.level - 1, 3) * 8 }}
            >
              {heading.title}
            </a>
          ))
        ) : (
          <Text size="xs" c="dimmed">
            {t('noOutline')}
          </Text>
        )}
      </nav>
      <Drawer opened={directoryOpen} onClose={() => setDirectoryOpen(false)} title={t('courseDirectory')} size={320}>
        {directory}
      </Drawer>
    </div>
  )
}
