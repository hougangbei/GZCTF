import { Alert, Button, Checkbox, Group, Modal, Select, Stack, Text } from '@mantine/core'
import { useRef, useState } from 'react'
import { Link } from 'react-router'
import { useSWRConfig } from 'swr'
import { createEditorSessionId } from '@Utils/adminTestSession'
import type { CourseSelection, CourseWorkspace } from '@Utils/courseAuthoring'
import { transferCommand, type TransferResult, type TransferTarget } from '@Utils/courseTransfer'
import { courseRequest, useCourseWorkspace } from '@Hooks/useCourseAuthoring'
import { useAdminSkillTrees } from '@Hooks/useSkillTreeAdmin'

type Props = {
  source: CourseWorkspace
  node: CourseSelection
  onClose: () => void
  onLocal: (target: TransferTarget, mode: 'move' | 'reference') => void
  onTransferred: (result: TransferResult, mode: 'move' | 'reference') => void
}
// One destination picker for mouse, keyboard and mobile; the existing batch page stays separate.
export const ContentDestinationModal = ({ source, node, onClose, onLocal, onTransferred }: Props) => {
  const { data: trees } = useAdminSkillTrees()
  const [treeId, setTreeId] = useState(source.skillTreeId)
  const [destination, setDestination] = useState<string | null>(null)
  const [keep, setKeep] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState('')
  const [conflict, setConflict] = useState(false)
  const lock = useRef(false)
  const retry = useRef<{ fingerprint: string; id: string } | undefined>(undefined)
  const remote = useCourseWorkspace(treeId === source.skillTreeId ? '' : treeId)
  const target = treeId === source.skillTreeId ? source : remote.data?.skillTreeId === treeId ? remote.data : undefined
  const { mutate } = useSWRConfig()
  const sourceCategory = source.document.categories.find((c) => c.categoryId === node.categoryId)
  const sourceSkill = sourceCategory?.skills.find((s) => s.skillId === node.skillId)
  const content = sourceSkill?.contents.find((c) => c.kind === node.kind && c.contentId === node.contentId)
  const choices =
    target?.document.categories.flatMap((c) =>
      c.skills
        .filter((s) => treeId !== source.skillTreeId || s.skillId !== node.skillId)
        .map((s) => ({ value: `${c.categoryId}:${s.skillId}`, label: `${c.name} / ${s.name}` }))
    ) ?? []
  const targetPath = destination?.split(':')
  const targetSkill = target?.document.categories
    .find((c) => c.categoryId === targetPath?.[0])
    ?.skills.find((s) => s.skillId === targetPath?.[1])
  const sourceRefs = source.document.categories.filter((c) => c.skills.some((s) => s.skillId === node.skillId)).length
  const targetRefs =
    target?.document.categories.filter((c) => c.skills.some((s) => s.skillId === targetSkill?.skillId)).length ?? 0
  const duplicate = targetSkill?.contents.some((c) => c.kind === node.kind && c.contentId === node.contentId)
  const apply = async () => {
    if (
      lock.current ||
      !target ||
      !targetSkill ||
      !content ||
      !node.categoryId ||
      !node.skillId ||
      !targetPath ||
      conflict
    )
      return
    const to = { categoryId: targetPath[0], skillId: targetPath[1] },
      mode = keep ? 'reference' : 'move'
    if (treeId === source.skillTreeId) {
      onLocal(to, mode)
      return
    }
    lock.current = true
    setPending(true)
    setError('')
    try {
      const command = transferCommand(
        source,
        target,
        { categoryId: node.categoryId, skillId: node.skillId },
        to,
        [content],
        mode,
        ''
      )
      const fingerprint = JSON.stringify(command)
      if (retry.current?.fingerprint !== fingerprint) retry.current = { fingerprint, id: createEditorSessionId() }
      command.operationId = retry.current.id
      const result = await courseRequest<TransferResult>('/api/admin/course-transfers', command, 'POST')
      // Update scoped caches without fetching an older response over the accepted snapshot.
      void mutate((key) => {
        const path = Array.isArray(key) ? key[0] : key
        return typeof path === 'string' && path.startsWith('/api/admin/course-resources')
      })
      await remote.mutate(result.target, false)
      onTransferred(result, mode)
    } catch (e) {
      setError((e as Error).message)
      if ((e as { status?: number }).status === 409) setConflict(true)
    } finally {
      lock.current = false
      setPending(false)
    }
  }
  return (
    <Modal
      opened
      onClose={() => {
        if (!pending) onClose()
      }}
      title="移动或引用内容"
      centered
      size="md"
      closeOnClickOutside={!pending}
      closeOnEscape={!pending}
      withCloseButton={!pending}
    >
      <Stack>
        <Text fw={600}>{content?.title ?? '内容已不在原位置'}</Text>
        <Text size="sm" c="dimmed">
          从 {source.document.name} / {sourceCategory?.name} / {sourceSkill?.name}
        </Text>
        <Select
          label="目标技能树"
          searchable
          disabled={pending}
          value={treeId}
          data={[
            { value: source.skillTreeId, label: source.document.name },
            ...(trees ?? [])
              .filter((t) => t.skillTreeId && t.skillTreeId !== source.skillTreeId)
              .map((t) => ({ value: t.skillTreeId!, label: t.name ?? t.skillTreeId! })),
          ]}
          onChange={(v) => {
            if (v) {
              setTreeId(v)
              setDestination(null)
              setError('')
              setConflict(false)
            }
          }}
        />
        <Select
          label="目标技能"
          placeholder={target ? '选择类别 / 技能' : '正在加载…'}
          searchable
          data={choices}
          value={destination}
          disabled={pending || !target || conflict}
          onChange={(v) => {
            setDestination(v)
            setError('')
          }}
          nothingFoundMessage="没有可用技能"
        />
        <Checkbox
          label="保留原位置，只添加引用"
          checked={keep}
          disabled={pending}
          onChange={(e) => setKeep(e.currentTarget.checked)}
        />
        {target && !choices.length && (
          <Text size="sm" c="dimmed">
            这棵树没有其他可用技能，请先在课程编排中添加技能。
          </Text>
        )}
        {(sourceRefs > 1 || targetRefs > 1) && (
          <Alert color="yellow">
            技能有多处引用，整理会同步影响该草稿中引用它的所有类别。来源 {sourceRefs} 处，目标 {targetRefs} 处。
          </Alert>
        )}
        {duplicate && (
          <Text size="sm" c="dimmed">
            目标已引用这项内容，不会重复添加。{keep ? '原位置保留。' : '移动只移除原位置的引用。'}
          </Text>
        )}
        <Text size="sm" c="dimmed">
          {treeId === source.skillTreeId
            ? '仅修改当前草稿，发布后生效。'
            : '确认后保存两棵树的草稿；请先发布目标课程，再发布来源课程。'}
          题目身份、通关和积分保留。
        </Text>
        {remote.error && treeId !== source.skillTreeId && (
          <Alert color="red">
            目标草稿加载失败。
            <Button variant="subtle" onClick={() => void remote.mutate()}>
              重新加载目标
            </Button>
          </Alert>
        )}
        {error && (
          <Alert color="red">
            {error}
            {conflict && <Text size="sm">请关闭此窗口，重新加载草稿后再操作。</Text>}
          </Alert>
        )}
        <Group justify="space-between">
          <Button
            component={Link}
            to={`/admin/content-transfer?source=${source.skillTreeId}`}
            variant="subtle"
            disabled={pending}
          >
            批量整理
          </Button>
          <Group>
            <Button variant="default" disabled={pending} onClick={onClose}>
              取消
            </Button>
            <Button loading={pending} disabled={!targetSkill || !content || conflict} onClick={() => void apply()}>
              {keep ? '添加引用' : '移动到此技能'}
            </Button>
          </Group>
        </Group>
      </Stack>
    </Modal>
  )
}
