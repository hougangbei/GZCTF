import { Group, Pagination, Text } from '@mantine/core'
import { FC } from 'react'

type Props = { page: number; pageSize: number; total: number; onChange: (page: number) => void }

export const LogPager: FC<Props> = ({ page, pageSize, total, onChange }) => {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  return (
    <Group justify="space-between" mt="md" wrap="wrap">
      <Text size="sm" c="dimmed">
        {total}
      </Text>
      <Pagination total={pages} value={page} onChange={onChange} withEdges size="sm" />
    </Group>
  )
}
