import { Alert, Button, Group, Modal, Select, Stack, Switch, Text, TextInput, Title } from '@mantine/core'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { SkillTreeForm, type SkillTreeFormValues } from '@Components/admin/skill-trees/SkillTreeForm'
import type { AdminSkill } from '@Utils/skillHierarchy'
import { useAdminSkills, useHierarchyMutations } from '@Hooks/useSkillHierarchy'
import { useAdminSkillCategories } from '@Hooks/useSkillTreeAdmin'
import { Role } from '@Api'

const Skills = () => {
  const { data: skills, error } = useAdminSkills()
  const { data: categories } = useAdminSkillCategories()
  const [categoryId, setCategoryId] = useState<string | null>(null)
  const { request } = useHierarchyMutations()
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [opened, setOpened] = useState(false)
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const [enabledOnly, setEnabledOnly] = useState(false)
  const [values, setValues] = useState<SkillTreeFormValues>({ name: '', summary: '', iconKey: 'flag' })
  const create = async () => {
    if (!values.name.trim()) {
      setMessage('请填写技能名称')
      return
    }
    setPending(true)
    try {
      const skill = await request<AdminSkill>('/api/admin/skills', { ...values, isEnabled: true }, 'POST')
      navigate(`/admin/skills/${skill.skillId}`)
    } catch (error) {
      setMessage((error as Error).message)
    } finally {
      setPending(false)
    }
  }
  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>
        <Stack>
          <Group justify="space-between">
            <Title>技能库</Title>
            <Button onClick={() => setOpened(true)}>新建技能</Button>
          </Group>
          <Text c="dimmed">技能可以关联多个类别。修改共享技能后，所有引用位置都会同步更新。</Text>
          <TextInput label="搜索技能" value={search} onChange={(event) => setSearch(event.currentTarget.value)} />
          <Select
            label="按类别筛选"
            clearable
            searchable
            data={(categories ?? []).map((category) => ({ value: category.categoryId!, label: category.name! }))}
            value={categoryId}
            onChange={setCategoryId}
          />
          <Switch
            label="只看已启用"
            checked={enabledOnly}
            onChange={(event) => setEnabledOnly(event.currentTarget.checked)}
          />
          {error && <Alert color="red">技能库加载失败，请刷新重试。</Alert>}
          {!skills && !error && <Text role="status">正在加载…</Text>}
          {skills
            ?.filter(
              (skill) =>
                (!categoryId || skill.categories.some((category) => category.categoryId === categoryId)) &&
                (!enabledOnly || skill.isEnabled) &&
                `${skill.name} ${skill.summary}`.toLowerCase().includes(search.toLowerCase())
            )
            .map((skill) => (
              <Group
                key={skill.skillId}
                justify="space-between"
                p="md"
                style={{ border: '1px solid var(--hbc-border)', borderRadius: 'var(--mantine-radius-md)' }}
              >
                <Stack gap={2}>
                  <Text fw={600}>{skill.name}</Text>
                  <Text size="sm" c="dimmed">
                    {skill.contents?.length ?? 0} 项内容 · {skill.categories?.length ?? 0} 个类别 ·{' '}
                    {skill.isEnabled ? '已启用' : '已停用'}
                  </Text>
                </Stack>
                <Button component={Link} to={`/admin/skills/${skill.skillId}`} variant="light">
                  编辑技能
                </Button>
              </Group>
            ))}
          {skills?.length === 0 && <Text>还没有技能，先创建一个。</Text>}
          <Modal opened={opened} onClose={() => setOpened(false)} title="新建技能">
            <Stack>
              {message && <Alert color="red">{message}</Alert>}
              <SkillTreeForm
                values={values}
                onChange={setValues}
                onSubmit={create}
                submitting={pending}
                submitLabel="创建技能"
              />
            </Stack>
          </Modal>
        </Stack>
      </WithNavBar>
    </WithRole>
  )
}
export default Skills
