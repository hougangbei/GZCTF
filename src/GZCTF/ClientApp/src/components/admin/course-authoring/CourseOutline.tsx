import { Badge, Button, Group, Menu, Stack, Text } from '@mantine/core'
import { useRef, useState } from 'react'
import type { CourseDocument, CourseSelection } from '@Utils/courseAuthoring'
import type { TransferTarget } from '@Utils/courseTransfer'
import classes from './CourseWorkspace.module.css'

type Props = {
  document: CourseDocument
  selection: CourseSelection
  disabled: boolean
  dirty?: boolean
  onSelect: (node: CourseSelection) => void
  onTransfer: (node: CourseSelection, target?: TransferTarget) => void
  onMove: (node: CourseSelection, delta: number) => void
}
export const CourseOutline = ({ document, selection, disabled, dirty, onSelect, onMove, onTransfer }: Props) => {
  const [dropKey, setDropKey] = useState('')
  const key = (node: CourseSelection) => [node.kind, node.categoryId, node.skillId, node.contentId].join(':')
  const isContent = (node: CourseSelection) => node.kind === 'lesson' || node.kind === 'challenge'
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
        data-drop-target={dropKey === key(node) || undefined}
        data-outline-kind={node.kind}
        data-outline-id={node.contentId ?? node.skillId ?? node.categoryId}
        draggable={!disabled && node.kind !== 'tree'}
        onDragStart={(event) => {
          dragging.current = { node, index }
          event.dataTransfer.effectAllowed = 'move'
          event.dataTransfer.setData('text/plain', name)
        }}
        onDragOver={(event) => {
          const source = dragging.current?.node
          if (disabled || !source) return
          const crossSkill =
            isContent(source) && (node.kind === 'skill' || isContent(node)) && source.skillId !== node.skillId
          const sameGroup = source.kind === node.kind || (isContent(source) && isContent(node))
          const sameParent =
            source.kind === 'category' ||
            (source.categoryId === node.categoryId && (source.kind === 'skill' || source.skillId === node.skillId))
          if (crossSkill || (sameGroup && sameParent)) {
            event.preventDefault()
            event.dataTransfer.dropEffect = 'move'
            setDropKey(key(node))
          }
        }}
        onDragLeave={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDropKey('')
        }}
        onDragEnd={() => {
          setDropKey('')
          dragging.current = undefined
        }}
        onDrop={(event) => {
          event.preventDefault()
          setDropKey('')
          const source = dragging.current
          dragging.current = undefined
          if (
            !disabled &&
            source &&
            isContent(source.node) &&
            (node.kind === 'skill' || isContent(node)) &&
            source.node.skillId !== node.skillId &&
            node.categoryId &&
            node.skillId
          ) {
            onTransfer(source.node, { categoryId: node.categoryId, skillId: node.skillId })
            return
          }
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
            {isContent(node) && (
              <Menu position="bottom-end" withinPortal>
                <Menu.Target>
                  <Button
                    size="compact-xs"
                    variant="subtle"
                    aria-label={`更多操作 ${name}`}
                    disabled={disabled}
                    draggable={false}
                  >
                    ⋯
                  </Button>
                </Menu.Target>
                <Menu.Dropdown>
                  <Menu.Item onClick={() => onTransfer(node)}>移动或引用到…</Menu.Item>
                  <Menu.Item disabled={index === 0} onClick={() => onMove(node, -1)}>
                    上移
                  </Menu.Item>
                  <Menu.Item disabled={index === count - 1} onClick={() => onMove(node, 1)}>
                    下移
                  </Menu.Item>
                </Menu.Dropdown>
              </Menu>
            )}
            {!isContent(node) && (
              <>
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
              </>
            )}
          </Group>
        )}
      </div>
    )
  }
  return (
    <nav aria-label="课程编排目录" className={classes.outline}>
      <Text size="xs" c="dimmed" mb="xs">
        拖到技能可移动；同技能内拖动可排序。
      </Text>
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
