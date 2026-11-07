import { Alert, Button, FileButton, Group, Image, Stack, Text } from '@mantine/core'
import { useState } from 'react'
import { useHierarchyMutations } from '@Hooks/useSkillHierarchy'
import { useAdminSkillTrees } from '@Hooks/useSkillTreeAdmin'

export const SkillTreeCoverEditor = ({ id }: { id: string }) => {
  const { data: trees } = useAdminSkillTrees()
  const tree = trees?.find((item) => item.skillTreeId === id) as { rowVersion?: number; coverUrl?: string } | undefined
  const { request } = useHierarchyMutations()
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const upload = async (file: File | null) => {
    if (!file || !tree) return
    if (file.size > 5 * 1024 * 1024) {
      setMessage('封面不能超过 5 MB')
      return
    }
    setPending(true)
    setMessage('')
    try {
      const data = new FormData()
      data.set('file', file)
      data.set('rowVersion', String(tree.rowVersion))
      await request(`/api/admin/skill-trees/${id}/cover`, data, 'POST')
    } catch (error) {
      setMessage((error as Error).message)
    } finally {
      setPending(false)
    }
  }
  const reset = async () => {
    setPending(true)
    setMessage('')
    try {
      await request(`/api/admin/skill-trees/${id}/cover`, { rowVersion: tree?.rowVersion }, 'DELETE')
    } catch (error) {
      setMessage((error as Error).message)
    } finally {
      setPending(false)
    }
  }
  return (
    <Stack gap="sm">
      <Text fw={600}>路线封面</Text>
      <Image
        src={tree?.coverUrl || '/images/skill-route.svg'}
        alt="路线封面预览"
        w={320}
        maw="100%"
        style={{ aspectRatio: '16/9', objectFit: 'cover', background: 'var(--hbc-primary-soft)', borderRadius: 12 }}
      />
      <Text size="sm" c="dimmed">
        推荐 16:9，支持 JPEG、PNG、WebP，最大 5 MB。保存只更新草稿，请在课程编排中统一发布。
      </Text>
      {message && <Alert color="red">{message}</Alert>}
      <Group>
        <FileButton onChange={upload} accept="image/jpeg,image/png,image/webp">
          {(props) => (
            <Button {...props} loading={pending} disabled={!tree}>
              上传封面
            </Button>
          )}
        </FileButton>
        <Button variant="default" disabled={!tree?.coverUrl || pending} onClick={reset}>
          恢复默认封面
        </Button>
      </Group>
    </Stack>
  )
}
