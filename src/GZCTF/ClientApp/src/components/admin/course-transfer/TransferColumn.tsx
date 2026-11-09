import { Badge, Button, Checkbox, Group, Paper, Stack, Text, Title } from '@mantine/core'
import type { CourseContent, CourseWorkspace } from '@Utils/courseAuthoring'
import { contentKey, type TransferTarget } from '@Utils/courseTransfer'
import classes from './CourseTransfer.module.css'

export const TransferColumn = ({
  workspace,
  side,
  selection,
  checked,
  disabled,
  onSource,
  onToggle,
  onTarget,
  onDrag,
  onDrop,
  onDragEnd,
}: {
  workspace: CourseWorkspace
  side: 'source' | 'target'
  selection?: TransferTarget
  checked: Set<string>
  disabled: boolean
  onSource: (parent: TransferTarget) => void
  onToggle: (parent: TransferTarget, item: CourseContent) => void
  onTarget: (parent: TransferTarget) => void
  onDrag: (parent: TransferTarget, item: CourseContent, event: React.DragEvent) => void
  onDrop: (parent: TransferTarget, event: React.DragEvent) => void
  onDragEnd: () => void
}) => (
  <Paper withBorder p="md" radius="md" className={classes.panel}>
    <Stack>
      <Group justify="space-between">
        <Title order={3} size="h4">
          {workspace.document.name}
        </Title>
        <Badge variant="light">草稿</Badge>
      </Group>
      {!workspace.document.categories.length && <Text c="dimmed">目录为空，请先在课程编排中创建类别和技能。</Text>}
      {workspace.document.categories.map((category) => (
        <Stack key={category.categoryId} gap="xs">
          <Text fw={600}>{category.name}</Text>
          {category.skills.length === 0 && (
            <Text size="sm" c="dimmed">
              此类别暂无技能。
            </Text>
          )}
          {category.skills.map((skill) => {
            const parent = { categoryId: category.categoryId, skillId: skill.skillId }
            const active = selection?.categoryId === category.categoryId && selection?.skillId === skill.skillId
            const count = workspace.document.categories.reduce(
              (n, c) => n + c.skills.filter((s) => s.skillId === skill.skillId).length,
              0
            )
            return (
              <div
                key={skill.skillId}
                className={`${classes.skill} ${side === 'target' ? classes.target : ''} ${active ? classes.selected : ''}`}
                data-testid={side === 'target' ? `transfer-target-${skill.skillId}` : undefined}
                onDragOver={(e) => {
                  if (side === 'target' && !disabled) e.preventDefault()
                }}
                onDrop={(e) => {
                  if (side === 'target' && !disabled) onDrop(parent, e)
                }}
              >
                <Stack gap="xs">
                  <Group justify="space-between">
                    <Text fw={500}>{skill.name}</Text>
                    <Button
                      size="compact-xs"
                      variant={active ? 'filled' : 'light'}
                      disabled={disabled}
                      aria-pressed={active}
                      aria-label={`${side === 'target' ? '选择目标' : '选择来源'} ${category.name} / ${skill.name}`}
                      onClick={() => (side === 'target' ? onTarget(parent) : onSource(parent))}
                    >
                      {side === 'target' ? '选择目标' : '选择此技能'}
                    </Button>
                  </Group>
                  {count > 1 && (
                    <Text size="xs" c="dimmed">
                      本草稿共享技能，影响 {count} 处位置
                    </Text>
                  )}
                  {skill.contents.map((item) => (
                    <div
                      key={contentKey(item)}
                      className={classes.content}
                      data-testid={side === 'source' ? `transfer-content-${item.contentId}` : undefined}
                      draggable={side === 'source' && !disabled}
                      onDragStart={(e) => onDrag(parent, item, e)}
                      onDragEnd={onDragEnd}
                    >
                      {side === 'source' ? (
                        <Checkbox
                          disabled={disabled}
                          aria-label={`选择 ${item.title}`}
                          checked={active && checked.has(contentKey(item))}
                          label={item.title}
                          onChange={() => onToggle(parent, item)}
                        />
                      ) : (
                        <Text size="sm">{item.title}</Text>
                      )}
                      <Text size="xs" c="dimmed">
                        {item.kind === 'challenge' ? '题目' : '课节'} ·{' '}
                        {{ Draft: '草稿内容', Published: '已发布内容', Retired: '已退役', Merged: '已合并' }[
                          item.state
                        ] ??
                          item.state ??
                          '草稿内容'}
                      </Text>
                    </div>
                  ))}
                  {!skill.contents.length && (
                    <Text size="xs" c="dimmed">
                      {side === 'target' ? '可将题目/课节放到这里' : '此技能暂无内容'}
                    </Text>
                  )}
                </Stack>
              </div>
            )
          })}
        </Stack>
      ))}
    </Stack>
  </Paper>
)
