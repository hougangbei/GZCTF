import { Alert, Badge, Button, Group, Modal, MultiSelect, Select, Stack, Text, TextInput } from '@mantine/core'
import { useEffect, useRef, useState } from 'react'
import { PresetIconPicker } from '@Components/admin/skill-trees/PresetIconPicker'
import type { SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import { useAdminSkills, useHierarchyMutations } from '@Hooks/useSkillHierarchy'
import { useAdminSkillCategories, useAdminSkillTrees } from '@Hooks/useSkillTreeAdmin'

export type ContentPublishSelection = {
  categoryIds: string[]
  inlineCategories: Array<{ skillTreeId: string; name: string; summary: string; iconKey: SkillTreeIconKey }>
}
type Props = {
  opened: boolean
  kind: 'challenge' | 'lesson'
  contentId: string
  rowVersion: number
  initialCategoryIds: string[]
  onClose: () => void
  onPublished: () => void | Promise<void>
}
export const ContentPublishModal = ({ opened, kind, contentId, rowVersion, onClose, onPublished }: Props) => {
  const { data: trees } = useAdminSkillTrees()
  const { data: categories } = useAdminSkillCategories()
  const { data: skills, error: skillsError } = useAdminSkills()
  const { request } = useHierarchyMutations()
  const [treeId, setTreeId] = useState<string | null>(null)
  const [categoryId, setCategoryId] = useState<string | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [inline, setInline] = useState({ name: '', summary: '', iconKey: 'flag' as SkillTreeIconKey })
  const [message, setMessage] = useState('')
  const [pending, setPending] = useState(false)
  const initialized = useRef(false)
  useEffect(() => {
    if (!opened) {
      initialized.current = false
      return
    }
    if (skills && !initialized.current) {
      initialized.current = true
      setTreeId(null)
      setCategoryId(null)
      setSelected(
        skills
          .filter((skill) =>
            skill.contents?.some((content) => content.kind === kind && content.contentId === contentId)
          )
          .map((skill) => skill.skillId)
      )
      setInline({ name: '', summary: '', iconKey: 'flag' })
      setMessage('')
    }
  }, [opened, skills, contentId, kind])
  const availableCategories = (categories ?? []).filter((category) =>
    category.trees?.some((ref) => ref.skillTreeId === treeId)
  )
  const availableSkills = (skills ?? []).filter(
    (skill) => skill.categories?.some((category) => category.categoryId === categoryId) && skill.isEnabled
  )
  const publish = async () => {
    if (!selected.length && !(categoryId && inline.name.trim())) {
      setMessage('请选择技能或在类别下新建技能')
      return
    }
    setPending(true)
    setMessage('')
    try {
      await request(
        `/api/admin/${kind === 'challenge' ? 'challenges' : 'lessons'}/${contentId}/publish`,
        {
          rowVersion,
          categoryIds: [],
          inlineCategories: [],
          skillIds: selected,
          inlineSkills: categoryId && inline.name.trim() ? [{ ...inline, name: inline.name.trim(), categoryId }] : [],
        },
        'POST'
      )
      await onPublished()
      onClose()
    } catch (error) {
      setMessage((error as Error).message)
    } finally {
      setPending(false)
    }
  }
  return (
    <Modal opened={opened} onClose={onClose} title={kind === 'challenge' ? '发布题目' : '发布课节'} size="lg">
      <Stack>
        <Text size="sm" c="dimmed">
          选择路线 → 类别 → 技能。内容和完成记录可在多个技能间共用。
        </Text>
        {(message || skillsError) && (
          <Alert color="red">{skillsError ? '技能列表加载失败，请重新打开后重试' : message}</Alert>
        )}
        <Select
          label="选择路线"
          data={(trees ?? []).map((tree) => ({ value: tree.skillTreeId!, label: tree.name! }))}
          value={treeId}
          onChange={(value) => {
            setTreeId(value)
            setCategoryId(null)
          }}
        />
        <Select
          label="选择类别"
          disabled={!treeId}
          data={availableCategories.map((category) => ({ value: category.categoryId!, label: category.name! }))}
          value={categoryId}
          onChange={setCategoryId}
        />
        <MultiSelect
          searchable
          label="关联技能"
          disabled={!skills}
          data={(skills ?? [])
            .filter(
              (skill) =>
                selected.includes(skill.skillId) || availableSkills.some((item) => item.skillId === skill.skillId)
            )
            .map((skill) => ({ value: skill.skillId, label: skill.name }))}
          value={selected}
          onChange={setSelected}
          description="已关联的其他位置也会保留；取消选择会解除该关联。"
        />
        {categoryId && (
          <Stack gap="xs">
            <Text fw={600}>或在当前类别中新建技能</Text>
            <TextInput
              label="新技能名称"
              value={inline.name}
              onChange={(event) => setInline({ ...inline, name: event.currentTarget.value })}
            />
            <TextInput
              label="新技能简介"
              value={inline.summary}
              onChange={(event) => setInline({ ...inline, summary: event.currentTarget.value })}
            />
            <PresetIconPicker value={inline.iconKey} onChange={(iconKey) => setInline({ ...inline, iconKey })} />
          </Stack>
        )}
        <Stack gap={4}>
          {(skills ?? [])
            .filter((skill) => selected.includes(skill.skillId))
            .flatMap((skill) =>
              skill.categories.flatMap((category) => {
                const parents = (categories ?? []).find((item) => item.categoryId === category.categoryId)?.trees ?? []
                return parents.map((parent) => (
                  <Text size="sm" key={`${skill.skillId}-${category.categoryId}-${parent.skillTreeId}`}>
                    {(trees ?? []).find((tree) => tree.skillTreeId === parent.skillTreeId)?.name ?? '路线'} →{' '}
                    {category.name} → {skill.name}
                  </Text>
                ))
              })
            )}
        </Stack>
        <Group>
          {(skills ?? [])
            .filter((skill) => selected.includes(skill.skillId))
            .map((skill) => (
              <Badge key={skill.skillId}>
                {skill.name} · {skill.categories.length} 个类别
              </Badge>
            ))}
        </Group>
        {selected.some((id) => (skills ?? []).find((skill) => skill.skillId === id)?.categories.length! > 1) && (
          <Alert>包含共享技能，发布将同步影响所有引用位置。</Alert>
        )}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose} disabled={pending}>
            取消
          </Button>
          <Button loading={pending} disabled={!skills || Boolean(skillsError)} onClick={publish}>
            发布
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
