import { Alert, Badge, Button, Group, Modal, Stack, Text } from '@mantine/core'
import type { CourseCheck, CourseDocument, PublicationIssue } from '@Utils/courseAuthoring'

const publicationMessages: Record<string, string> = {
  missing_cover: '尚未设置课程封面，不影响发布。',
  disabled_challenge: '该题目已停用，发布后学员无法使用它；不影响其他内容发布。',
  challenge_invalid:
    '题目不存在、已退役，或内容不完整。请补全题目标题和各语言版本的正文；若是下载模板等辅助资料，可从课程目录移出。',
  challenge_identity_unavailable: '该题目已删除、退役或合并。请移出此引用，并从题目库添加有效的题目。',
  lesson_invalid: '课节不存在、已退役或缺少标题。请检查课节内容，或移出无效引用。',
  lesson_identity_unavailable: '该课节已删除或退役。请移出此引用，并从课节库添加有效的课节。',
}

export const publicationIssueMessage = (issue: PublicationIssue) => publicationMessages[issue.code] ?? issue.message

export const PublishReview = ({
  check,
  document,
  pending,
  onClose,
  onPublish,
  onLocate,
  onRepair,
  embedded = false,
  publishDisabled = false,
}: {
  check: CourseCheck
  document: CourseDocument
  pending: boolean
  onClose: () => void
  onPublish: () => void
  onRepair?: () => void
  embedded?: boolean
  publishDisabled?: boolean
  onLocate: (issue: PublicationIssue) => void
}) => {
  const errors = check.issues.filter((issue) => issue.severity === 'error')
  const body = (
    <Stack>
      <Text>发布后，学员才会看到本次修改。其他课程继续使用各自已发布的版本。</Text>
      <Group>
        {[
          [check.categoryCount, '个类别'],
          [check.skillCount, '个技能'],
          [check.lessonCount, '篇课节'],
          [check.challengeCount, '道题目'],
        ].map(([count, label]) => (
          <Badge key={String(label)} variant="light">
            {count} {label}
          </Badge>
        ))}
      </Group>
      {check.changes && (
        <Stack gap={4}>
          <Text fw={600}>目录变化</Text>
          {check.changes.added.map((name, i) => (
            <Text key={`add:${i}`} size="sm">
              新增 · {name}
            </Text>
          ))}
          {check.changes.removed.map((name, i) => (
            <Text key={`remove:${i}`} size="sm">
              移出 · {name}
            </Text>
          ))}
          {!check.changes.added.length && !check.changes.removed.length && (
            <Text size="sm" c="dimmed">
              没有新增或移出目录项；仍可能包含内容修改。
            </Text>
          )}
        </Stack>
      )}
      {check.impacts?.map((impact, i) => (
        <Alert key={`impact:${i}`} color="yellow">
          {impact}
        </Alert>
      ))}
      {check.runtimeChecks?.length ? (
        <Stack gap="xs">
          <Text fw={600}>题目配置</Text>
          {check.runtimeChecks.map((item) => (
            <Stack key={item.contentId} gap={2}>
              <Text size="sm" fw={600}>
                {item.title} · {item.score} 分
              </Text>
              {item.image && (
                <Text size="sm" style={{ overflowWrap: 'anywhere' }}>
                  镜像：{item.image} · 服务端口：{item.port ?? '未配置'}
                </Text>
              )}
            </Stack>
          ))}
          <Text size="xs" c="dimmed">
            配置检查不代表题目机已实际拉取或启动该镜像。
          </Text>
        </Stack>
      ) : null}
      {check.issues.map((issue, index) => {
        const categories = document.categories
        const skills = categories.flatMap((category) => category.skills)
        const name = issue.contentId
          ? skills.flatMap((skill) => skill.contents).find((content) => content.contentId === issue.contentId)?.title
          : issue.skillId
            ? skills.find((skill) => skill.skillId === issue.skillId)?.name
            : issue.categoryId
              ? categories.find((category) => category.categoryId === issue.categoryId)?.name
              : undefined
        return (
          <Alert
            key={`${issue.code}:${index}`}
            color={issue.severity === 'error' ? 'red' : 'yellow'}
            title={`${issue.severity === 'error' ? '阻止发布' : '请留意'}${name ? ` · ${name}` : ''}`}
          >
            <Stack gap="xs">
              <Text size="sm">{publicationIssueMessage(issue)}</Text>
              {(issue.categoryId || issue.skillId || issue.contentId) && (
                <Button size="compact-sm" variant="subtle" w="fit-content" onClick={() => onLocate(issue)}>
                  定位问题
                </Button>
              )}
            </Stack>
          </Alert>
        )
      })}
      {!check.issues.length && (
        <Alert color="green">检查通过，可以发布。容器配置校验不代表镜像已在题目机实际启动验证。</Alert>
      )}
      <Group justify="flex-end">
        {onRepair && check.issues.length > 0 && (
          <Button variant="light" disabled={pending} onClick={onRepair}>
            逐题修复
          </Button>
        )}
        <Button variant="default" disabled={pending} onClick={onClose}>
          继续编辑
        </Button>
        <Button disabled={errors.length > 0 || publishDisabled} loading={pending} onClick={onPublish}>
          确认统一发布
        </Button>
      </Group>
    </Stack>
  )
  return embedded ? (
    <section aria-label="发布检查摘要">{body}</section>
  ) : (
    <Modal opened onClose={onClose} closeOnClickOutside={!pending} title="发布检查" size="lg">
      {body}
    </Modal>
  )
}
