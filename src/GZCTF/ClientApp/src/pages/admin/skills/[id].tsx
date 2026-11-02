import { Alert, Button, Divider, Group, MultiSelect, Stack, Switch, Text, TextInput, Title } from '@mantine/core'
import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { TypedDeleteModal } from '@Components/admin/shared/TypedDeleteModal'
import { SkillTreeForm, type SkillTreeFormValues } from '@Components/admin/skill-trees/SkillTreeForm'
import type { AdminSkill } from '@Utils/skillHierarchy'
import { moveItem, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import { useAdminLessons, useAdminChallenges } from '@Hooks/useChallengeLibraryAdmin'
import { useAdminSkill, useHierarchyMutations } from '@Hooks/useSkillHierarchy'
import { useAdminSkillCategories, useAdminSkillTrees } from '@Hooks/useSkillTreeAdmin'
import { Role, type SkillTreeContentSummaryResponse } from '@Api'

const SkillEdit = () => {
  const { id } = useParams()
  const { data: skill, error, mutate } = useAdminSkill(id)
  const { data: categories } = useAdminSkillCategories()
  const { data: trees } = useAdminSkillTrees()
  const { data: lessons } = useAdminLessons()
  const { data: challenges } = useAdminChallenges()
  const { request } = useHierarchyMutations()
  const navigate = useNavigate()
  const initialized = useRef<string | undefined>(undefined)
  const dragIndex = useRef<number | null>(null)
  const [values, setValues] = useState<SkillTreeFormValues>({ name: '', summary: '', iconKey: 'flag' })
  const [enabled, setEnabled] = useState(true)
  const [version, setVersion] = useState(0)
  const [contents, setContents] = useState<SkillTreeContentSummaryResponse[]>([])
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const [impact, setImpact] = useState<{
    rowVersion: number
    categoryCount?: number
    requiresTypedConfirmation?: boolean
  }>()
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [confirmation, setConfirmation] = useState('')
  useEffect(() => {
    if (skill && initialized.current !== skill.skillId) {
      initialized.current = skill.skillId
      setValues({ name: skill.name, summary: skill.summary, iconKey: skill.iconKey as SkillTreeIconKey })
      setEnabled(skill.isEnabled)
      setVersion(skill.rowVersion)
      setContents(skill.contents ?? [])
    }
  }, [skill])
  const run = async (work: () => Promise<void>) => {
    setPending(true)
    setMessage('')
    try {
      await work()
      setMessage('保存成功')
    } catch (error) {
      setMessage((error as Error).message)
    } finally {
      setPending(false)
    }
  }
  const options = [
    ...(lessons ?? []).map((item) => ({
      value: `lesson:${item.id}`,
      label: `课节 · ${item.title}`,
      item: { contentId: item.id, title: item.title, kind: 'lesson' },
    })),
    ...(challenges ?? []).map((item) => ({
      value: `challenge:${item.id}`,
      label: `题目 · ${item.title}`,
      item: { contentId: item.id, title: item.title, kind: 'challenge' },
    })),
  ]
  const selected = contents.map((item) => `${item.kind}:${item.contentId}`)
  const save = () =>
    run(async () => {
      const fresh = await request<AdminSkill>(`/api/admin/skills/${id}`, {
        ...values,
        isEnabled: enabled,
        rowVersion: version,
      })
      setVersion(fresh.rowVersion)
      await mutate()
    })
  const saveContents = () =>
    run(async () => {
      const fresh = await request<AdminSkill>(`/api/admin/skills/${id}/contents`, {
        rowVersion: version,
        contents: contents.map((item, sortOrder) => ({ kind: item.kind, contentId: item.contentId, sortOrder })),
      })
      setVersion(fresh.rowVersion)
      await mutate()
    })
  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>
        <Stack>
          <Group justify="space-between">
            <Title>{skill?.name ?? '编辑技能'}</Title>
            <Button component={Link} to="/admin/skills" variant="subtle">
              返回技能库
            </Button>
          </Group>
          {(error || message) && (
            <Alert color={error || message !== '保存成功' ? 'red' : 'green'}>{error ? '技能加载失败' : message}</Alert>
          )}
          <Button
            variant="subtle"
            w="fit-content"
            onClick={() => {
              initialized.current = undefined
              void mutate()
            }}
          >
            重新加载，放弃本地修改
          </Button>
          {skill && (
            <>
              <Alert>
                此技能关联 {skill.categories.length} 个类别；更改将同步到所有引用位置。
                {skill.categories.map((item) => item.name).join('、')}
              </Alert>
              <Stack gap={4}>
                {skill.categories.flatMap((category) =>
                  ((categories ?? []).find((item) => item.categoryId === category.categoryId)?.trees ?? []).map(
                    (tree) => (
                      <Text size="sm" key={`${category.categoryId}-${tree.skillTreeId}`}>
                        <Link to={`/admin/skill-trees/${tree.skillTreeId}`}>
                          {(trees ?? []).find((item) => item.skillTreeId === tree.skillTreeId)?.name ?? '路线'}
                        </Link>{' '}
                        → <Link to={`/admin/skill-categories/${category.categoryId}`}>{category.name}</Link> →{' '}
                        {skill.name}
                      </Text>
                    )
                  )
                )}
              </Stack>
              <Switch
                label="启用技能"
                checked={enabled}
                onChange={(event) => setEnabled(event.currentTarget.checked)}
              />
              <SkillTreeForm
                values={values}
                onChange={setValues}
                onSubmit={save}
                submitting={pending}
                submitLabel="保存技能信息"
              />
              <Divider label="题目与课节编排" />
              <MultiSelect
                searchable
                label="添加内容（复用原内容与完成记录）"
                data={[
                  ...options.map(({ value, label }) => ({ value, label })),
                  ...contents
                    .filter((item) => !options.some((option) => option.value === `${item.kind}:${item.contentId}`))
                    .map((item) => ({ value: `${item.kind}:${item.contentId}`, label: item.title ?? '未命名内容' })),
                ]}
                value={selected}
                onChange={(keys) =>
                  setContents((current) => [
                    ...current.filter((item) => keys.includes(`${item.kind}:${item.contentId}`)),
                    ...keys
                      .filter((key) => !selected.includes(key))
                      .flatMap((key) => options.find((item) => item.value === key)?.item ?? []),
                  ])
                }
              />
              <Text c="dimmed" size="sm">
                拖动排序，也可以使用上移、下移按钮。取消关联不会删除内容。
              </Text>
              {contents.map((item, index) => (
                <Group
                  key={`${item.kind}:${item.contentId}`}
                  justify="space-between"
                  p="sm"
                  draggable
                  onDragStart={() => {
                    dragIndex.current = index
                  }}
                  onDragOver={(event) => event.preventDefault()}
                  onDrop={(event) => {
                    event.preventDefault()
                    if (dragIndex.current !== null) setContents(moveItem(contents, dragIndex.current, index))
                    dragIndex.current = null
                  }}
                  style={{ border: '1px solid var(--hbc-border)', borderRadius: 8 }}
                >
                  <Text>
                    {item.kind === 'lesson' ? '课节' : '题目'} · {item.title}
                  </Text>
                  <Group gap="xs">
                    <Button
                      size="xs"
                      variant="subtle"
                      disabled={index === 0}
                      aria-label={`上移 ${item.title}`}
                      onClick={() => setContents(moveItem(contents, index, index - 1))}
                    >
                      ↑
                    </Button>
                    <Button
                      size="xs"
                      variant="subtle"
                      disabled={index === contents.length - 1}
                      aria-label={`下移 ${item.title}`}
                      onClick={() => setContents(moveItem(contents, index, index + 1))}
                    >
                      ↓
                    </Button>
                    <Button
                      size="xs"
                      variant="subtle"
                      onClick={() => setContents(contents.filter((_, i) => i !== index))}
                    >
                      取消关联
                    </Button>
                  </Group>
                </Group>
              ))}
              <Button loading={pending} onClick={saveContents} w="fit-content">
                保存内容顺序
              </Button>
              <Divider label="删除技能" />
              <Text size="sm">删除只移除技能及其引用，保留题目、课节和学习记录。</Text>
              <TextInput
                label="输入技能名称确认删除"
                value={confirmation}
                onChange={(event) => setConfirmation(event.currentTarget.value)}
              />
              <Button
                color="red"
                variant="light"
                loading={pending}
                disabled={confirmation !== skill.name}
                onClick={() =>
                  run(async () => {
                    const response = await fetch(`/api/admin/skills/${id}/delete-impact`, { credentials: 'include' })
                    if (!response.ok) throw new Error('无法获取删除影响')
                    setImpact(await response.json())
                    setDeleteOpen(true)
                  })
                }
              >
                查看删除影响
              </Button>
            </>
          )}
          <TypedDeleteModal
            opened={deleteOpen}
            entityName={skill?.name ?? ''}
            title="删除技能"
            impactLines={[
              `将影响 ${impact?.categoryCount ?? skill?.categories.length ?? 0} 个类别，内容和学习记录保留。`,
            ]}
            requiresTypedConfirmation
            loading={pending}
            onClose={() => setDeleteOpen(false)}
            onConfirm={(name) =>
              run(async () => {
                await request(
                  `/api/admin/skills/${id}`,
                  { rowVersion: impact?.rowVersion ?? version, confirmationName: name },
                  'DELETE'
                )
                navigate('/admin/skills')
              })
            }
          />
        </Stack>
      </WithNavBar>
    </WithRole>
  )
}
export default SkillEdit
