import { Badge, Button, Group, Stack, Text } from '@mantine/core'
import { useRef } from 'react'
import type { CourseDocument, CourseSelection } from '@Utils/courseAuthoring'
import classes from './CourseWorkspace.module.css'

type Props = {
  document: CourseDocument
  selection: CourseSelection
  disabled: boolean
  dirty?: boolean
  onSelect: (node: CourseSelection) => void
  onMove: (node: CourseSelection, delta: number) => void
}
export const CourseOutline = ({ document, selection, disabled, dirty, onSelect, onMove }: Props) => {
  const dragging = useRef<{ node: CourseSelection; index: number } | undefined>(undefined)
  const row = (name: string, node: CourseSelection, index: number, count: number, state?: string) => {
    const active =
      selection.kind === node.kind &&
      selection.categoryId === node.categoryId &&
      selection.skillId === node.skillId &&
      selection.contentId === node.contentId
    return (
      <div
        className={classes.outlineRow}
        draggable={!disabled && node.kind !== 'tree'}
        onDragStart={(event) => {
          dragging.current = { node, index }
          event.dataTransfer.effectAllowed = 'move'
          event.dataTransfer.setData('text/plain', name)
        }}
        onDragOver={(event) => {
          if (!disabled) event.preventDefault()
        }}
        onDragEnd={() => {
          dragging.current = undefined
        }}
        onDrop={(event) => {
          event.preventDefault()
          const source = dragging.current
          dragging.current = undefined
          if (
            disabled ||
            !source ||
            (source.node.categoryId !== node.categoryId && node.kind !== 'category') ||
            (source.node.skillId !== node.skillId && node.kind !== 'skill' && node.kind !== 'category')
          )
            return
          const group = (kind: string) => (kind === 'lesson' || kind === 'challenge' ? 'content' : kind)
          if (group(source.node.kind) !== group(node.kind)) return
          onMove(source.node, index - source.index)
        }}
      >
        <button
          type="button"
          className={classes.node}
          aria-current={active ? 'page' : undefined}
          onClick={() => onSelect(node)}
        >
          <span className={classes.nodeKind}>
            {node.kind === 'tree'
              ? '路线'
              : node.kind === 'category'
                ? '类别'
                : node.kind === 'skill'
                  ? '技能'
                  : node.kind === 'lesson'
                    ? '课节'
                    : '题目'}
          </span>
          <span>{name}</span>
          {state && (
            <Badge size="xs" variant="light" color={state === '停用' ? 'gray' : undefined}>
              {state}
            </Badge>
          )}
        </button>
        {node.kind !== 'tree' && (
          <Group gap={0} wrap="nowrap">
            <Button
              size="compact-xs"
              variant="subtle"
              aria-label={`上移 ${name}`}
              disabled={disabled || index === 0}
              onClick={() => onMove(node, -1)}
            >
              ↑
            </Button>
            <Button
              size="compact-xs"
              variant="subtle"
              aria-label={`下移 ${name}`}
              disabled={disabled || index === count - 1}
              onClick={() => onMove(node, 1)}
            >
              ↓
            </Button>
          </Group>
        )}
      </div>
    )
  }
  return (
    <nav aria-label="课程编排目录" className={classes.outline}>
      {row(document.name, { kind: 'tree' }, 0, 1, dirty ? '未保存' : '课程草稿')}
      {document.categories.map((category, ci) => (
        <div key={category.categoryId} className={classes.branch}>
          {row(category.name, { kind: 'category', categoryId: category.categoryId }, ci, document.categories.length)}
          {category.skills.map((skill, si) => (
            <div key={skill.skillId} className={classes.branch}>
              {row(
                skill.name,
                { kind: 'skill', categoryId: category.categoryId, skillId: skill.skillId },
                si,
                category.skills.length,
                skill.isEnabled ? '启用' : '停用'
              )}
              {skill.contents.map((content, i) => (
                <div key={`${content.kind}:${content.contentId}`} className={classes.branch}>
                  {row(
                    content.title,
                    {
                      kind: content.kind,
                      categoryId: category.categoryId,
                      skillId: skill.skillId,
                      contentId: content.contentId,
                    },
                    i,
                    skill.contents.length,
                    content.state === 'Published' ? '已发布内容' : '草稿内容'
                  )}
                </div>
              ))}
            </div>
          ))}
        </div>
      ))}
      {!document.categories.length && (
        <Stack p="sm" gap={4}>
          <Text size="sm" c="dimmed">
            先添加一个类别，再逐步加入技能和内容。
          </Text>
        </Stack>
      )}
    </nav>
  )
}
