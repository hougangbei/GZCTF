import { Alert, Badge, Button, Drawer, Group, Stack, Text, Title } from '@mantine/core'
import { type PropsWithChildren, useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router'
import { courseReviewHref, groupReviewIssues, reviewIssueHref, reviewKey } from '@Utils/courseReview'
import { useCourseReview } from '@Hooks/useCourseReview'
import { useUser } from '@Hooks/useUser'
import classes from './CourseReview.module.css'
import { publicationIssueMessage } from './PublishReview'

const scrollMemory = new Map<string, number>()

export function CourseReviewShell({ courseId, children }: PropsWithChildren<{ courseId: string }>) {
  const { review, storageUnavailable } = useCourseReview(courseId)
  const [opened, setOpened] = useState(false)
  const toggle = useRef<HTMLButtonElement>(null)
  const location = useLocation()
  useEffect(() => {
    setOpened(false)
  }, [location.pathname, location.search])
  const { user } = useUser()
  const desktop = useRef<HTMLDivElement>(null),
    mobile = useRef<HTMLDivElement>(null)
  const scrollKey = user?.userId ? `${reviewKey(user.userId, courseId)}:scroll` : undefined
  const rememberScroll = (side: 'desktop' | 'mobile', element: HTMLElement) => {
    if (!scrollKey) return
    const key = `${scrollKey}:${side}`
    scrollMemory.set(key, element.scrollTop)
    if (scrollMemory.size > 128) scrollMemory.delete(scrollMemory.keys().next().value!)
    try {
      sessionStorage.setItem(key, String(element.scrollTop))
    } catch {}
  }
  const restoreScroll = useCallback(
    (side: 'desktop' | 'mobile', element: HTMLDivElement) => {
      let saved = scrollKey ? (scrollMemory.get(`${scrollKey}:${side}`) ?? 0) : 0
      if (scrollKey)
        try {
          const raw = sessionStorage.getItem(`${scrollKey}:${side}`)
          if (raw !== null && Number.isFinite(Number(raw))) saved = Math.max(0, Number(raw))
        } catch {}
      element.scrollTop = saved
    },
    [scrollKey]
  )
  const mountMobile = useCallback(
    (element: HTMLDivElement | null) => {
      mobile.current = element
      if (element) restoreScroll('mobile', element)
    },
    [restoreScroll]
  )
  useLayoutEffect(() => {
    if (desktop.current) restoreScroll('desktop', desktop.current)
    if (mobile.current) restoreScroll('mobile', mobile.current)
    // Restore manual position only; selection must never scroll the list or page.
  }, [location.pathname, location.search, opened, restoreScroll, review?.check.version])
  const groups = review ? groupReviewIssues(review) : []
  const list = (
    <Stack gap="md">
      <div>
        <Text size="xs" c="dimmed">
          发布前逐题修复
        </Text>
        <Title order={2} size="h4">
          问题清单
        </Title>
        <Text size="sm" c="dimmed">
          {review?.document.name ?? '当前课程'}
        </Text>
      </div>
      <Button component={Link} to={courseReviewHref(courseId)} variant="light">
        检查摘要 / 统一复查
      </Button>
      <Text size="xs" c="dimmed">
        点击题目在右侧编辑。保存只标记待复查，不会自动移除问题。
      </Text>
      {storageUnavailable && (
        <Alert color="yellow">浏览器暂时无法保存清单。当前页面可继续处理，刷新后可能回到旧记录，请重新复查。</Alert>
      )}
      <nav aria-label="发布问题清单" className={classes.items}>
        {groups.map((group) => {
          const href = reviewIssueHref(courseId, group),
            active = location.pathname === href.split('?')[0] && !!group.node?.contentId
          const error = group.issues.some((i) => i.severity === 'error'),
            changed = review?.changedKeys.includes(group.key)
          return (
            <Link key={group.key} to={href} className={classes.item} aria-current={active ? 'page' : undefined}>
              <Text size="sm" fw={600}>
                {group.title}
              </Text>
              <Text size="xs" c="dimmed" lineClamp={2} title={group.issues.map(publicationIssueMessage).join('；')}>
                {group.issues.map(publicationIssueMessage).join('；')}
              </Text>
              <Group gap="xs">
                <Badge size="xs" color={changed ? 'blue' : error ? 'red' : 'yellow'} variant="light">
                  {changed ? '已修改 · 待复查' : error ? '阻止发布' : '提示'}
                </Badge>
                <Text size="xs" c="dimmed">
                  {group.issues.length} 项
                </Text>
              </Group>
            </Link>
          )
        })}
      </nav>
      {!groups.length && (
        <Alert color={review ? 'green' : 'blue'}>
          {review ? '当前检查没有发现问题。' : '还没有检查记录，请先检查课程。'}
        </Alert>
      )}
      {review?.stale && (
        <Text size="xs" c="dimmed">
          清单来自上次检查，当前草稿已修改。处理完后统一复查。
        </Text>
      )}
      <Button component={Link} to={`/admin/skill-trees/${encodeURIComponent(courseId)}`} variant="default">
        返回课程编排
      </Button>
    </Stack>
  )
  return (
    <div className={classes.shell}>
      <aside className={classes.sidebar}>
        <div ref={desktop} className={classes.sticky} onScroll={(e) => rememberScroll('desktop', e.currentTarget)}>
          {list}
        </div>
      </aside>
      <div className={classes.main}>
        <Group className={classes.mobileBar} justify="space-between">
          <Text size="sm">{groups.length} 个问题项</Text>
          <Button ref={toggle} variant="light" onClick={() => setOpened(true)}>
            打开问题清单
          </Button>
        </Group>
        {children}
      </div>
      <Drawer
        opened={opened}
        onClose={() => {
          setOpened(false)
          toggle.current?.focus()
        }}
        title="问题清单"
        size="sm"
      >
        <div
          ref={mountMobile}
          className={classes.drawerList}
          onScroll={(e) => rememberScroll('mobile', e.currentTarget)}
        >
          {list}
        </div>
      </Drawer>
    </div>
  )
}
