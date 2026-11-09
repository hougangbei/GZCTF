import { Alert, Badge, Button, Group, Modal, Stack, Text } from '@mantine/core'
import { useState } from 'react'
import { Link } from 'react-router'
import type { ResourceRow } from '@Utils/courseTransfer'
import { locationLabel } from '@Utils/courseTransfer'

export const SourceLabels = ({ resource }: { resource?: ResourceRow }) => (
  <Group gap={4}>
    {(resource ? (resource.sourceLabels.length ? resource.sourceLabels : ['自建']) : ['来源加载中']).map((label) => (
      <Badge
        key={label}
        variant="light"
        style={{ maxWidth: '100%', height: 'auto', whiteSpace: 'normal', overflowWrap: 'anywhere' }}
      >
        {label}
      </Badge>
    ))}
  </Group>
)
export const ResourceLocations = ({
  resource,
  unavailable = false,
}: {
  resource?: ResourceRow
  unavailable?: boolean
}) => {
  const [opened, setOpened] = useState(false)
  return (
    <>
      <Button size="compact-xs" variant="subtle" disabled={!resource} onClick={() => setOpened(true)}>
        {unavailable
          ? '位置加载失败'
          : !resource
            ? '位置加载中'
            : resource.unassociated
              ? '未关联到课程'
              : `查看位置（${resource.locations.length}）`}
      </Button>
      <Modal
        opened={opened && !!resource}
        onClose={() => setOpened(false)}
        title={`${resource?.name ?? ''} · 内容位置`}
      >
        <Stack>
          <SourceLabels resource={resource} />
          <Text size="sm">资源库名称：{resource?.name}</Text>
          <Text size="sm">包含 {resource?.contentCount ?? 0} 项不同内容</Text>
          {resource?.unassociated && <Alert>内容仍在资源库中，没有被删除。可在课程编排中从库添加。</Alert>}
          {resource?.locations.map((p, i) => (
            <div key={`${p.treeId}:${p.categoryId}:${p.skillId}:${p.state}:${i}`}>
              <Group>
                <Badge variant="outline">
                  {p.state === 'draft'
                    ? '草稿'
                    : resource.locations.some(
                          (d) =>
                            d.state === 'draft' &&
                            d.treeId === p.treeId &&
                            d.categoryId === p.categoryId &&
                            d.skillId === p.skillId
                        )
                      ? '已发布'
                      : '已发布 · 下次发布后移出'}
                </Badge>
                <Text size="sm" style={{ overflowWrap: 'anywhere' }}>
                  {locationLabel(p)}
                </Text>
              </Group>
              <Button
                mt="xs"
                variant="subtle"
                size="compact-xs"
                component={Link}
                to={`/admin/skill-trees/${p.treeId}?category=${p.categoryId}${p.skillId ? '&skill=' + p.skillId : ''}`}
              >
                打开课程编排
              </Button>
              <Button
                ml="xs"
                mt="xs"
                variant="light"
                size="compact-xs"
                component={Link}
                to={`/admin/content-transfer?source=${p.treeId}`}
              >
                整理此课程内容
              </Button>
            </div>
          ))}
          <Text size="xs" c="dimmed">
            位置区分当前草稿与已发布版本。草稿移出后，学员入口仍保留到重新发布；同名不代表同一资源。
          </Text>
        </Stack>
      </Modal>
    </>
  )
}
