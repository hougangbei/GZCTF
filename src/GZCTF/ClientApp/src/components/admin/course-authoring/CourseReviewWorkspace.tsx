import { Alert, Button, Group, Skeleton, Stack, Text, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import type { CourseCheck, PublicationIssue } from '@Utils/courseAuthoring'
import { buildCourseReview, groupReviewIssues, reviewIssueHref } from '@Utils/courseReview'
import { courseRequest, useCourseWorkspace } from '@Hooks/useCourseAuthoring'
import { useCourseReview } from '@Hooks/useCourseReview'
import { PublishReview } from './PublishReview'

export function CourseReviewWorkspace({ id }: { id: string }) {
  const { data, error, mutate } = useCourseWorkspace(id)
  const { review, save, clear, markEdited } = useCourseReview(id)
  const [pending, setPending] = useState(false),
    [message, setMessage] = useState('')
  const lock = useRef(false),
    navigate = useNavigate()
  const current = !!data && !!review && !review.stale && review.check.version === data.version
  const run = async (work: () => Promise<void>) => {
    if (lock.current) return
    lock.current = true
    setPending(true)
    setMessage('')
    try {
      await work()
    } catch (e) {
      setMessage((e as Error).message)
      if ((e as { status?: number }).status === 409) markEdited()
    } finally {
      lock.current = false
      setPending(false)
    }
  }
  const inspect = () =>
    run(async () => {
      const latest = await mutate()
      if (!latest) throw new Error('课程加载失败，请重试。')
      const checked = await courseRequest<CourseCheck>(
        `/api/admin/skill-trees/${id}/workspace/check`,
        { version: latest.version },
        'POST'
      )
      save(buildCourseReview(id, latest.document, checked))
    })
  const publish = () =>
    run(async () => {
      if (!current || !review || review.check.issues.some((i) => i.severity === 'error'))
        throw new Error('请先统一复查并处理阻止发布的问题。')
      await courseRequest(`/api/admin/skill-trees/${id}/workspace/publish`, { version: review.check.version }, 'POST')
      clear()
      showNotification({ color: 'green', message: '课程已统一发布。' })
      navigate(`/admin/skill-trees/${id}`)
    })
  const locate = (issue: PublicationIssue) => {
    if (!review) return
    const group = groupReviewIssues(review).find((g) => g.issues.includes(issue))
    if (group) navigate(reviewIssueHref(id, group))
  }
  return (
    <Stack gap="lg">
      <div>
        <Text size="sm" c="dimmed">
          课程发布
        </Text>
        <Title order={1}>逐题修复</Title>
        <Text c="dimmed">{data?.document.name ?? review?.document.name}</Text>
      </div>
      <Alert>左侧选择问题题目，右侧完整编辑。可以连续保存多题，最后统一复查；已修改不代表已通过。</Alert>
      <Group>
        <Button loading={pending} disabled={!!error || !data} onClick={() => void inspect()}>
          统一复查全部问题
        </Button>
        <Text size="sm" c="dimmed">
          {review
            ? `${groupReviewIssues(review).length} 个问题项 · ${review.changedKeys.length} 项已修改待复查`
            : '尚未检查'}
        </Text>
      </Group>
      {!current && review && (
        <Alert color="yellow">草稿已修改或检查结果已过期，请统一复查后再发布。原问题清单会一直保留。</Alert>
      )}
      {(message || error) && (
        <Alert color="red">
          {message || '课程加载失败。'}
          {error && (
            <Button variant="subtle" onClick={() => void mutate()}>
              重新加载
            </Button>
          )}
        </Alert>
      )}
      {!data && !error && <Skeleton h={160} />}
      {review && (
        <PublishReview
          check={review.check}
          document={review.document}
          pending={pending}
          embedded
          publishDisabled={!current}
          onClose={() => navigate(`/admin/skill-trees/${id}`)}
          onPublish={() => void publish()}
          onLocate={locate}
        />
      )}
    </Stack>
  )
}
