import { Alert, Anchor, Skeleton, Stack, Text } from '@mantine/core'
import { Link } from 'react-router'
import useSWR from 'swr'
import type { CourseSelection } from '@Utils/courseAuthoring'
import { fetcher } from '@Api'

export const SharedReferences = ({ id, node }: { id: string; node: CourseSelection }) => {
  const resourceId = node.kind === 'category' ? node.categoryId : node.kind === 'skill' ? node.skillId : node.contentId
  const { data, error } = useSWR<{ skillTreeId: string; name: string; isPublished: boolean }[]>(
    resourceId
      ? `/api/admin/skill-trees/${id}/workspace/references?kind=${node.kind}&resourceId=${encodeURIComponent(resourceId)}`
      : null,
    fetcher,
    { keepPreviousData: false }
  )
  if (!resourceId) return null
  if (error) return <Alert color="yellow">无法核对共享引用，请在发布检查中确认影响范围。</Alert>
  if (!data) return <Skeleton h={50} />
  const others = data.filter((tree) => tree.skillTreeId !== id)
  return (
    <Alert variant="light">
      <Stack gap={4}>
        <Text size="sm">
          {others.length ? `另有 ${others.length} 门已发布课程引用此资源。` : '暂无其他已发布课程引用此资源。'}{' '}
          当前修改只在本课程草稿中，其他课程继续使用原版本。
        </Text>
        {others.map((tree) => (
          <Anchor component={Link} key={tree.skillTreeId} to={`/admin/skill-trees/${tree.skillTreeId}`} size="sm">
            {tree.name}
          </Anchor>
        ))}
      </Stack>
    </Alert>
  )
}
