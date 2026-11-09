import { Alert, Button, Group, Modal, Paper, Radio, Select, Stack, Text, Title } from '@mantine/core'
import { useEffect, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useSWRConfig } from 'swr'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import classes from '@Components/admin/course-transfer/CourseTransfer.module.css'
import { TransferColumn } from '@Components/admin/course-transfer/TransferColumn'
import { createEditorSessionId } from '@Utils/adminTestSession'
import type { CourseContent, CourseWorkspace } from '@Utils/courseAuthoring'
import {
  contentKey,
  transferCommand,
  uniqueContents,
  type TransferCommand,
  type TransferResult,
  type TransferTarget,
} from '@Utils/courseTransfer'
import { courseRequest, useCourseWorkspace } from '@Hooks/useCourseAuthoring'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useAdminSkillTrees } from '@Hooks/useSkillTreeAdmin'
import { Role } from '@Api'

const same = (a: TransferTarget | undefined, b: TransferTarget) =>
  a?.categoryId === b.categoryId && a.skillId === b.skillId
const path = (w: CourseWorkspace, p: TransferTarget) => {
  const c = w.document.categories.find((c) => c.categoryId === p.categoryId),
    s = c?.skills.find((s) => s.skillId === p.skillId)
  return [w.document.name, c?.name, s?.name].filter(Boolean).join(' / ')
}
const skill = (w: CourseWorkspace, p?: TransferTarget) =>
  w.document.categories.find((c) => c.categoryId === p?.categoryId)?.skills.find((s) => s.skillId === p?.skillId)
export const CourseTransfer = () => {
  usePageTitle('整理课程内容')
  const [params, setParams] = useSearchParams()
  const sourceId = params.get('source') ?? '',
    targetId = params.get('target') ?? ''
  const { data: trees } = useAdminSkillTrees()
  const source = useCourseWorkspace(sourceId),
    target = useCourseWorkspace(targetId)
  const { mutate } = useSWRConfig()
  const [from, setFrom] = useState<TransferTarget>(),
    [to, setTo] = useState<TransferTarget>(),
    [checked, setChecked] = useState<Set<string>>(new Set())
  const [mode, setMode] = useState<'move' | 'reference'>('move'),
    [pending, setPending] = useState(false),
    [error, setError] = useState(''),
    [success, setSuccess] = useState<TransferResult>(),
    [conflict, setConflict] = useState(false)
  const [confirmation, setConfirmation] = useState<{
    command: TransferCommand
    from: string
    to: string
    names: string[]
    duplicates: number
    sourceRefs: number
    targetRefs: number
  }>()
  const inflight = useRef(false),
    drag = useRef<{ parent: TransferTarget; items: CourseContent[]; token: string } | undefined>(undefined),
    retry = useRef<{ fingerprint: string; operationId: string } | undefined>(undefined)
  const selected = source.data
    ? uniqueContents(skill(source.data, from)?.contents ?? []).filter((c) => checked.has(contentKey(c)))
    : []
  const unavailable =
    pending ||
    !!source.error ||
    !!target.error ||
    !source.data ||
    !target.data ||
    source.data.skillTreeId !== sourceId ||
    target.data.skillTreeId !== targetId
  const reset = () => {
    setFrom(undefined)
    setTo(undefined)
    setChecked(new Set())
    setConfirmation(undefined)
    setSuccess(undefined)
    setError('')
    setConflict(false)
    drag.current = undefined
    retry.current = undefined
  }
  useEffect(() => {
    reset()
  }, [sourceId, targetId])
  const changeTree = (side: 'source' | 'target', id: string | null) => {
    if (pending) return
    reset()
    setParams((p) => {
      id ? p.set(side, id) : p.delete(side)
      return p
    })
  }
  const selectSource = (p: TransferTarget) => {
    if (!same(from, p)) {
      setFrom(p)
      setChecked(new Set())
    }
  }
  const toggle = (p: TransferTarget, item: CourseContent) => {
    if (!same(from, p)) {
      setFrom(p)
      setChecked(new Set([contentKey(item)]))
    } else
      setChecked((current) => {
        const next = new Set(current)
        next.has(contentKey(item)) ? next.delete(contentKey(item)) : next.add(contentKey(item))
        return next
      })
  }
  const preview = (
    parent: TransferTarget | undefined = from,
    destination: TransferTarget | undefined = to,
    items = selected
  ) => {
    if (unavailable || conflict || !source.data || !target.data || !parent || !destination || !items.length) return
    if (items.length > 100) {
      setError('每次最多整理100项内容，请分批操作。')
      return
    }
    const command = transferCommand(source.data, target.data, parent, destination, items, mode, '')
    const fingerprint = JSON.stringify(command)
    if (retry.current?.fingerprint !== fingerprint)
      retry.current = { fingerprint, operationId: createEditorSessionId() }
    command.operationId = retry.current.operationId
    const destinationSkill = skill(target.data, destination)
    if (!destinationSkill) return
    setConfirmation({
      command,
      from: path(source.data, parent),
      to: path(target.data, destination),
      names: items.map((i) => i.title),
      duplicates: items.filter((i) => destinationSkill.contents.some((t) => contentKey(t) === contentKey(i))).length,
      sourceRefs: source.data.document.categories.reduce(
        (n, c) => n + c.skills.filter((s) => s.skillId === parent.skillId).length,
        0
      ),
      targetRefs: target.data.document.categories.reduce(
        (n, c) => n + c.skills.filter((s) => s.skillId === destination.skillId).length,
        0
      ),
    })
  }
  const apply = async () => {
    if (inflight.current || !confirmation || !source.data || !target.data) return
    const c = confirmation.command
    if (
      c.sourceTreeId !== sourceId ||
      c.targetTreeId !== targetId ||
      c.sourceVersion !== source.data.version ||
      c.targetVersion !== target.data.version
    ) {
      setError('草稿版本已变化，请刷新后重新确认。')
      setConflict(true)
      setConfirmation(undefined)
      return
    }
    inflight.current = true
    setPending(true)
    setError('')
    try {
      const result = await courseRequest<TransferResult>('/api/admin/course-transfers', c, 'POST')
      await source.mutate(result.source, false)
      await target.mutate(result.target, false)
      setSuccess(result)
      setChecked(new Set())
      setConfirmation(undefined)
      retry.current = undefined
      void mutate((key) => {
        const p = Array.isArray(key) ? key[0] : key
        return typeof p === 'string' && p.startsWith('/api/admin/course-resources')
      })
    } catch (e) {
      setError((e as Error).message)
      if ((e as { status?: number }).status === 409) {
        setConflict(true)
        setConfirmation(undefined)
      }
    } finally {
      inflight.current = false
      setPending(false)
    }
  }
  const refresh = async () => {
    if (pending) return
    setConfirmation(undefined)
    setError('')
    try {
      await Promise.all([source.mutate(), target.mutate()])
      setConflict(false)
      retry.current = undefined
    } catch {
      setError('刷新失败，请重试。')
    }
  }
  const dragStart = (p: TransferTarget, item: CourseContent, e: React.DragEvent) => {
    if (unavailable) return
    const items = same(from, p) && checked.has(contentKey(item)) ? selected : [item]
    const token = createEditorSessionId()
    drag.current = { parent: p, items, token }
    setFrom(p)
    setChecked(new Set(items.map(contentKey)))
    e.dataTransfer.effectAllowed = 'copyMove'
    e.dataTransfer.setData('application/x-course-content', token)
  }
  const drop = (p: TransferTarget, e: React.DragEvent) => {
    e.preventDefault()
    const payload = drag.current
    drag.current = undefined
    if (!payload || e.dataTransfer.getData('application/x-course-content') !== payload.token) return
    setTo(p)
    preview(payload.parent, p, payload.items)
  }
  const treeOptions = (trees ?? [])
    .filter((t) => t.skillTreeId)
    .map((t) => ({ value: t.skillTreeId!, label: t.name ?? '未命名' }))
  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>
        <Stack className={classes.workspace} gap="lg">
          <Group justify="space-between">
            <div>
              <Title order={1}>整理课程内容</Title>
              <Text c="dimmed">移动引用，不复制题目；通关和积分共用原记录。操作先保存草稿，发布后才对学员生效。</Text>
            </div>
            <Button component={Link} to="/admin/skill-trees" variant="default">
              返回技能树
            </Button>
          </Group>
          {error && (
            <Alert color="red">
              {error}
              {conflict && (
                <Button variant="subtle" onClick={() => void refresh()}>
                  刷新两边草稿
                </Button>
              )}
            </Alert>
          )}
          {(source.error || target.error) && (
            <Alert color="red">
              课程草稿加载失败。
              <Button variant="subtle" onClick={() => void refresh()}>
                重试
              </Button>
            </Alert>
          )}
          {success && (
            <Alert color="green">
              <Stack gap="xs">
                <Text fw={600}>已保存两边草稿，尚未发布。</Text>
                <Text size="sm">
                  新增 {success.addedCount} 项，移出 {success.removedCount} 项，目标已存在 {success.duplicateCount} 项。
                  {success.noOp ? '相同位置无需移动。' : ''}
                </Text>
                <Text size="sm">
                  跨树移动请先发布目标树，再发布来源树，避免内容暂时不可见。空技能和类别不会自动删除。
                </Text>
                <Group>
                  <Button component={Link} variant="light" to={`/admin/skill-trees/${success.target.skillTreeId}`}>
                    打开目标草稿
                  </Button>
                  <Button component={Link} variant="default" to={`/admin/skill-trees/${success.source.skillTreeId}`}>
                    打开来源草稿
                  </Button>
                </Group>
              </Stack>
            </Alert>
          )}
          <div className={classes.columns}>
            <Stack>
              <Select
                label="来源技能树"
                value={sourceId || null}
                data={treeOptions}
                searchable
                disabled={pending}
                onChange={(id) => changeTree('source', id)}
              />
              {source.data?.skillTreeId === sourceId && (
                <TransferColumn
                  workspace={source.data}
                  side="source"
                  selection={from}
                  checked={checked}
                  disabled={unavailable}
                  onSource={selectSource}
                  onToggle={toggle}
                  onTarget={setTo}
                  onDrag={dragStart}
                  onDrop={drop}
                  onDragEnd={() => {
                    drag.current = undefined
                  }}
                />
              )}
            </Stack>
            <Stack>
              <Select
                label="目标技能树"
                value={targetId || null}
                data={treeOptions}
                searchable
                disabled={pending}
                onChange={(id) => changeTree('target', id)}
              />
              {target.data?.skillTreeId === targetId && (
                <TransferColumn
                  workspace={target.data}
                  side="target"
                  selection={to}
                  checked={checked}
                  disabled={unavailable}
                  onSource={selectSource}
                  onToggle={toggle}
                  onTarget={setTo}
                  onDrag={dragStart}
                  onDrop={drop}
                  onDragEnd={() => {
                    drag.current = undefined
                  }}
                />
              )}
            </Stack>
          </div>
          <Paper className={classes.actions}>
            <Group justify="space-between">
              <Radio.Group
                value={mode}
                onChange={(value) => setMode(value as 'move' | 'reference')}
                aria-label="整理方式"
              >
                <Group>
                  <Radio value="move" label="移动（移出原位置）" disabled={pending} />
                  <Radio value="reference" label="添加引用（保留原位置）" disabled={pending} />
                </Group>
              </Radio.Group>
              <Group>
                <Text size="sm">已选 {selected.length} 项</Text>
                <Button
                  disabled={unavailable || conflict || !from || !to || !selected.length}
                  onClick={() => preview()}
                >
                  {mode === 'move' ? '移动到所选技能' : '添加到所选技能'}
                </Button>
              </Group>
            </Group>
          </Paper>
          <Modal
            opened={!!confirmation}
            onClose={() => !pending && setConfirmation(undefined)}
            title={mode === 'move' ? '确认移动内容' : '确认添加引用'}
            centered
            closeOnClickOutside={!pending}
          >
            <Stack>
              {confirmation && (
                <>
                  <Text size="sm">来源：{confirmation.from}</Text>
                  <Text size="sm">目标：{confirmation.to}</Text>
                  {confirmation.names.map((name, i) => (
                    <Text size="sm" key={i}>
                      {name}
                    </Text>
                  ))}
                  <Alert>
                    仅修改草稿，不自动发布。
                    {confirmation.command.mode === 'move'
                      ? '目标已存在的内容不再新增，仍会移出来源。'
                      : '保留来源，目标去重引用。'}
                    目标已存在 {confirmation.duplicates} 项。
                  </Alert>
                  {(confirmation.sourceRefs > 1 || confirmation.targetRefs > 1) && (
                    <Alert color="yellow">
                      来源技能在本草稿出现 {confirmation.sourceRefs} 处，目标技能出现 {confirmation.targetRefs}{' '}
                      处；同一技能的这些位置会同步调整，其他树保持独立快照。
                    </Alert>
                  )}
                </>
              )}
              <Group justify="flex-end">
                <Button variant="default" disabled={pending} onClick={() => setConfirmation(undefined)}>
                  取消
                </Button>
                <Button loading={pending} disabled={unavailable || conflict} onClick={() => void apply()}>
                  {confirmation?.command.mode === 'reference' ? '确认添加引用' : '确认移动'}
                </Button>
              </Group>
            </Stack>
          </Modal>
        </Stack>
      </WithNavBar>
    </WithRole>
  )
}
export default CourseTransfer
