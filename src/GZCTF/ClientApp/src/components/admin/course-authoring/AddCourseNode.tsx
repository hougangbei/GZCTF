import { Alert, Badge, Button, Group, Modal, Radio, Select, Stack, Text, TextInput } from '@mantine/core'
import { useState, useRef } from 'react'
import useSWR from 'swr'
import type { CourseContent, CourseSkill } from '@Utils/courseAuthoring'
import { CourseRequestError, courseRequest } from '@Hooks/useCourseAuthoring'
import { useUser } from '@Hooks/useUser'
import { fetcher } from '@Api'

export type AddKind = 'category' | 'skill' | 'lesson' | 'challenge'
export type AddedResource = {
  id: string
  name: string
  summary: string
  iconKey: string
  skills?: CourseSkill[]
  contents?: CourseContent[]
  isEnabled?: boolean
  created: boolean
}
const labels: Record<AddKind, string> = { category: '类别', skill: '技能', lesson: '课节', challenge: '题目' }
type CatalogResource = {
  categoryId?: string
  skillId?: string
  id?: string
  name?: string
  title?: string
  summary?: string
  iconKey?: string
  skills?: CourseSkill[]
  contents?: CourseContent[]
  isEnabled?: boolean
  publicationState?: string
  categories?: unknown[]
  trees?: unknown[]
}
export const AddCourseNode = ({
  kind,
  courseId,
  parentKey,
  existingIds,
  onAdd,
  onClose,
  initialMode = 'new',
}: {
  initialMode?: 'new' | 'library'
  kind: AddKind
  courseId: string
  parentKey: string
  existingIds: string[]
  onAdd: (resource: AddedResource) => Promise<void>
  onClose: () => void
}) => {
  const { user } = useUser()
  const operationKey = `course-create:${user?.userId}:${courseId}:${parentKey}:${kind}`
  type PendingCreation = { id: string; name: string; challengeType: string; resource?: AddedResource }
  const [restored] = useState<PendingCreation | undefined>(() => {
    try {
      const raw = sessionStorage.getItem(operationKey)
      if (!raw) return
      const value = JSON.parse(raw)
      return typeof value.id === 'string' && typeof value.name === 'string' ? value : undefined
    } catch {
      return undefined
    }
  })
  const [mode, setMode] = useState<string>(restored ? 'new' : initialMode),
    [name, setName] = useState(restored?.name ?? ''),
    [selected, setSelected] = useState<string | null>(null),
    [message, setMessage] = useState(''),
    [pending, setPending] = useState(false),
    [created, setCreated] = useState<AddedResource | undefined>(restored?.resource)
  const [challengeType, setChallengeType] = useState(restored?.challengeType ?? 'StaticAttachment')
  const creationOperation = useRef<PendingCreation | undefined>(restored)
  const [operationActive, setOperationActive] = useState(Boolean(restored))
  const path =
    kind === 'category'
      ? '/api/admin/skill-categories'
      : kind === 'skill'
        ? '/api/admin/skills'
        : `/api/admin/${kind === 'lesson' ? 'lessons' : 'challenges'}?locale=zh`
  const { data, error, isLoading, mutate } = useSWR<CatalogResource[]>(mode === 'library' ? path : null, fetcher, {
    keepPreviousData: false,
  })
  const resources = (data ?? [])
    .map((item) => ({ ...item, key: item.categoryId ?? item.skillId ?? item.id ?? '' }))
    .filter((item) => item.key && !existingIds.includes(item.key))
  const submit = async () => {
    if (pending) return
    setPending(true)
    setMessage('')
    try {
      let resource = created
      if (!resource && mode === 'new') {
        if (!name.trim()) throw new Error('请填写名称')
        if (name.trim().length > 128) throw new Error('名称不能超过 128 个字符')
        if (!creationOperation.current)
          creationOperation.current = { id: crypto.randomUUID(), name: name.trim(), challengeType }
        setOperationActive(true)
        const operation = creationOperation.current
        // Persist before submitting. If this device cannot retain retries, do not issue a create.
        sessionStorage.setItem(operationKey, JSON.stringify(operation))
        resource = await courseRequest<AddedResource>(
          `/api/admin/skill-trees/${courseId}/workspace/nodes`,
          {
            operationId: operation.id,
            kind,
            name: operation.name,
            ...(kind === 'challenge' ? { challengeType: operation.challengeType } : {}),
          },
          'POST'
        )
        if (!resource.id) throw new Error('创建结果缺少标识，请重试查询本次创建结果。')
        creationOperation.current = { ...operation, resource }
        sessionStorage.setItem(operationKey, JSON.stringify(creationOperation.current))
        setCreated(resource)
      }
      if (!resource) {
        const item = resources.find((item) => item.key === selected)
        if (!item) throw new Error('请选择要添加的资源')
        resource = {
          id: item.key,
          name: item.name ?? item.title ?? '未命名',
          summary: item.summary ?? '',
          iconKey: item.iconKey ?? 'flag',
          skills: item.skills ?? [],
          contents: item.contents ?? [],
          isEnabled: item.isEnabled,
          created: false,
        }
      }
      await onAdd(resource)
      try {
        sessionStorage.removeItem(operationKey)
      } catch {}
    } catch (error) {
      if (error instanceof CourseRequestError && error.status === 400 && !creationOperation.current?.resource) {
        creationOperation.current = undefined
        setOperationActive(false)
        try {
          sessionStorage.removeItem(operationKey)
        } catch {}
      }
      setMessage((error as Error).message)
    } finally {
      setPending(false)
    }
  }
  return (
    <Modal
      opened
      onClose={() => !pending && onClose()}
      title={`添加${labels[kind]}`}
      size="lg"
      closeOnClickOutside={!pending}
    >
      <Stack>
        <Radio.Group label="添加方式" value={mode} onChange={setMode}>
          <Group>
            <Radio value="new" label={`新建${labels[kind]}`} disabled={pending || !!created || operationActive} />
            <Radio value="library" label="从库添加" disabled={pending || !!created || operationActive} />
          </Group>
        </Radio.Group>
        {mode === 'new' ? (
          <>
            <TextInput
              label="名称"
              required
              maxLength={128}
              value={name}
              disabled={pending || !!created || operationActive}
              onChange={(event) => setName(event.currentTarget.value)}
              autoFocus
            />
            {kind === 'challenge' && (
              <Select
                label="题目类型"
                value={challengeType}
                disabled={pending || !!created || operationActive}
                onChange={(value) => value && setChallengeType(value)}
                data={[
                  { value: 'StaticAttachment', label: '静态附件题' },
                  { value: 'DynamicAttachment', label: '动态附件题' },
                  { value: 'StaticContainer', label: '静态容器题' },
                  { value: 'DynamicContainer', label: '动态容器题' },
                ]}
              />
            )}
            <Text size="sm" c="dimmed">
              创建后自动放入当前目录。课节和题目将在完整编辑器中继续设置。
            </Text>
          </>
        ) : (
          <>
            {error && (
              <Alert color="red">
                资源库加载失败。
                <Button variant="subtle" onClick={() => void mutate()}>
                  重试
                </Button>
              </Alert>
            )}
            <Select
              label="选择已有资源"
              searchable
              clearable
              value={selected}
              onChange={setSelected}
              disabled={pending || isLoading}
              data={resources.map((item) => ({
                value: item.key,
                label: `${item.name ?? item.title ?? '未命名'}${item.isEnabled === false ? '（已停用）' : ''}`,
              }))}
              nothingFoundMessage={isLoading ? '正在加载…' : '没有可添加的资源'}
            />
            {resources
              .filter((item) => item.key === selected)
              .map((item) => (
                <Stack key={item.key} gap={4}>
                  <Text size="sm">{item.summary || '暂无简介'}</Text>
                  <Group>
                    <Badge variant="light">
                      {item.publicationState ?? (item.isEnabled === false ? '已停用' : '资源库内容')}
                    </Badge>
                    <Text size="sm" c="dimmed">
                      {item.trees?.length ?? item.categories?.length ?? 0} 处引用 · 完成记录共用
                    </Text>
                  </Group>
                </Stack>
              ))}
          </>
        )}
        {!created && operationActive && (
          <Alert color="yellow">本次创建结果尚未确认。重试会查询同一创建操作，不会再创建一份。</Alert>
        )}
        {created && <Alert color="yellow">资源已创建，关联尚未保存。重试会使用同一份资源，不会重复创建。</Alert>}
        {message && <Alert color="red">{message}</Alert>}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose} disabled={pending}>
            取消
          </Button>
          <Button
            onClick={() => void submit()}
            loading={pending}
            disabled={mode === 'library' && !selected && !created}
          >
            {created ? '重试保存关联' : mode === 'new' ? '创建并添加' : '添加到当前目录'}
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
