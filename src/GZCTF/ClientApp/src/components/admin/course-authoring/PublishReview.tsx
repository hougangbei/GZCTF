import { Alert, Badge, Button, Group, Modal, Stack, Text } from '@mantine/core'
import type { CourseCheck, PublicationIssue } from '@Utils/courseAuthoring'

export const PublishReview = ({
  check,
  pending,
  onClose,
  onPublish,
  onLocate,
}: {
  check: CourseCheck
  pending: boolean
  onClose: () => void
  onPublish: () => void
  onLocate: (issue: PublicationIssue) => void
}) => {
  const errors = check.issues.filter((issue) => issue.severity === 'error')
  return (
    <Modal opened onClose={onClose} closeOnClickOutside={!pending} title="发布检查" size="lg">
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
        {check.issues.map((issue, index) => (
          <Alert
            key={`${issue.code}:${index}`}
            color={issue.severity === 'error' ? 'red' : 'yellow'}
            title={issue.severity === 'error' ? '阻止发布' : '请留意'}
          >
            <Stack gap="xs">
              <Text size="sm">{issue.message}</Text>
              {(issue.categoryId || issue.skillId || issue.contentId) && (
                <Button size="compact-sm" variant="subtle" w="fit-content" onClick={() => onLocate(issue)}>
                  定位问题
                </Button>
              )}
            </Stack>
          </Alert>
        ))}
        {!check.issues.length && (
          <Alert color="green">检查通过，可以发布。容器配置校验不代表镜像已在题目机实际启动验证。</Alert>
        )}
        <Group justify="flex-end">
          <Button variant="default" disabled={pending} onClick={onClose}>
            继续编辑
          </Button>
          <Button disabled={errors.length > 0} loading={pending} onClick={onPublish}>
            确认统一发布
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
