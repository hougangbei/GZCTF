import { Alert, Button, FileButton, Group, Image, Stack, Text } from '@mantine/core'
import { useState } from 'react'
import type { CourseWorkspace } from '@Utils/courseAuthoring'
import { courseRequest } from '@Hooks/useCourseAuthoring'

export const CourseCover = ({
  workspace,
  disabled,
  beforeUpload,
  onSaved,
  onBusyChange,
}: {
  workspace: CourseWorkspace
  disabled: boolean
  beforeUpload: () => Promise<CourseWorkspace>
  onSaved: (next: CourseWorkspace) => void
  onBusyChange: (busy: boolean) => void
}) => {
  const [pending, setPending] = useState(false),
    [message, setMessage] = useState('')
  const update = async (file: File | null, remove = false) => {
    if (!file && !remove) return
    if (file && (file.size > 5 * 1024 * 1024 || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type))) {
      setMessage('请选择不超过 5 MB 的 JPEG、PNG 或 WebP 图片')
      return
    }
    setPending(true)
    onBusyChange(true)
    setMessage('')
    try {
      const saved = await beforeUpload()
      const form = new FormData()
      form.set('version', saved.version)
      if (file) form.set('file', file)
      const next = await courseRequest<CourseWorkspace>(
        `/api/admin/skill-trees/${workspace.skillTreeId}/workspace/cover`,
        remove ? { version: saved.version } : form,
        remove ? 'DELETE' : 'POST'
      )
      onSaved(next)
    } catch (error) {
      setMessage((error as Error).message)
    } finally {
      setPending(false)
      onBusyChange(false)
    }
  }
  return (
    <Stack gap="xs">
      <Text fw={600}>课程封面</Text>
      <Image
        src={workspace.document.coverUrl || '/images/skill-route.svg'}
        alt="草稿封面"
        w={300}
        maw="100%"
        style={{ aspectRatio: '16/9', objectFit: 'cover', borderRadius: 'var(--mantine-radius-md)' }}
      />
      <Text size="sm" c="dimmed">
        仅更新草稿，统一发布后学员才会看到。推荐 16:9，最大 5 MB。
      </Text>
      {message && <Alert color="red">{message}</Alert>}
      <Group>
        <FileButton onChange={(file) => void update(file)} accept="image/jpeg,image/png,image/webp">
          {(props) => (
            <Button {...props} variant="light" disabled={disabled || pending} loading={pending}>
              上传封面
            </Button>
          )}
        </FileButton>
        <Button
          variant="default"
          disabled={disabled || pending || !workspace.document.coverUrl}
          onClick={() => void update(null, true)}
        >
          恢复默认封面
        </Button>
      </Group>
    </Stack>
  )
}
