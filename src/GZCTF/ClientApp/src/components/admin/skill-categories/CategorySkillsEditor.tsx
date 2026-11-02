import { Alert, Button, Group, MultiSelect, Stack, Text } from '@mantine/core'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import useSWR from 'swr'
import type { SkillSummary } from '@Utils/skillHierarchy'
import { moveItem } from '@Utils/skillTreeAdmin'
import { useAdminSkills, useHierarchyMutations } from '@Hooks/useSkillHierarchy'
import { fetcher } from '@Api'

export const CategorySkillsEditor = ({ id, onSaved }: { id: string; onSaved: () => Promise<unknown> }) => {
  const { data: category, mutate } = useSWR<{ categoryId: string; rowVersion: number; skills: SkillSummary[] }>(
    `/api/admin/skill-categories/${id}`,
    fetcher
  )
  const { data: skills } = useAdminSkills()
  const { request } = useHierarchyMutations()
  const initialized = useRef<string | undefined>(undefined)
  const dirty = useRef(false)
  const dragIndex = useRef<number | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [version, setVersion] = useState(0)
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  useEffect(() => {
    if (category && initialized.current !== id) {
      initialized.current = id
      setSelected([...(category.skills ?? [])].sort((a, b) => a.sortOrder - b.sortOrder).map((item) => item.skillId))
      setVersion(category.rowVersion)
      dirty.current = false
    } else if (category && !dirty.current) {
      setSelected([...(category.skills ?? [])].sort((a, b) => a.sortOrder - b.sortOrder).map((item) => item.skillId))
      setVersion(category.rowVersion)
    }
  }, [category, id])
  const changeSelected = (next: string[]) => {
    dirty.current = true
    setSelected(next)
  }
  const all = new Map([...(category?.skills ?? []), ...(skills ?? [])].map((item) => [item.skillId, item]))
  const save = async () => {
    setPending(true)
    setMessage('')
    try {
      await request(`/api/admin/skill-categories/${id}/skills`, {
        rowVersion: version,
        skills: selected.map((skillId, sortOrder) => ({ skillId, sortOrder })),
      })
      dirty.current = false
      const fresh = await mutate()
      setVersion(fresh?.rowVersion ?? version)
      await onSaved()
      setMessage('技能关联已保存')
    } catch (error) {
      setMessage((error as Error).message)
    } finally {
      setPending(false)
    }
  }
  return (
    <Stack>
      <Text size="sm" c="dimmed">
        关联技能后，在技能中编排题目和课节。共享技能会同步更新；取消关联不会删除技能。
      </Text>
      {message && <Alert>{message}</Alert>}
      <Group>
        <Button component={Link} to="/admin/skills" variant="light">
          打开技能库 / 新建技能
        </Button>
        <Button
          variant="subtle"
          onClick={() => {
            initialized.current = undefined
            void mutate()
          }}
        >
          重新加载关联
        </Button>
      </Group>
      <MultiSelect
        label="关联技能"
        searchable
        data={[...all.values()].map((item) => ({ value: item.skillId, label: item.name }))}
        value={selected}
        onChange={(keys) =>
          changeSelected([
            ...selected.filter((key) => keys.includes(key)),
            ...keys.filter((key) => !selected.includes(key)),
          ])
        }
      />
      {selected.map((key, index) => (
        <Group
          key={key}
          p="sm"
          justify="space-between"
          draggable
          onDragStart={() => {
            dragIndex.current = index
          }}
          onDragOver={(event) => event.preventDefault()}
          onDrop={(event) => {
            event.preventDefault()
            if (dragIndex.current !== null) changeSelected(moveItem(selected, dragIndex.current, index))
            dragIndex.current = null
          }}
          style={{ border: '1px solid var(--hbc-border)', borderRadius: 8 }}
        >
          <Link to={`/admin/skills/${key}`}>{all.get(key)?.name}</Link>
          <Group gap="xs">
            <Button
              size="xs"
              variant="subtle"
              disabled={index === 0}
              aria-label={`上移 ${all.get(key)?.name}`}
              onClick={() => changeSelected(moveItem(selected, index, index - 1))}
            >
              ↑
            </Button>
            <Button
              size="xs"
              variant="subtle"
              disabled={index === selected.length - 1}
              aria-label={`下移 ${all.get(key)?.name}`}
              onClick={() => changeSelected(moveItem(selected, index, index + 1))}
            >
              ↓
            </Button>
            <Button size="xs" variant="subtle" onClick={() => changeSelected(selected.filter((item) => item !== key))}>
              取消关联
            </Button>
          </Group>
        </Group>
      ))}
      <Button w="fit-content" loading={pending} onClick={save}>
        保存技能关联与顺序
      </Button>
    </Stack>
  )
}
