import { Alert, Badge, Button, Card, Group, Loader, Stack, Table, Text, Title } from '@mantine/core'
import { useState } from 'react'
import { useImportHistory } from '@Hooks/usePlatformImports'

export const ImportHistory = ({ admin = false }: { admin?: boolean }) => {
  const [skip, setSkip] = useState(0)
  const { data, error, mutate } = useImportHistory(admin, skip)
  if (error)
    return (
      <Alert color="red">
        历史记录加载失败。
        <Button variant="subtle" onClick={() => void mutate()}>
          重试
        </Button>
      </Alert>
    )
  if (!data) return <Loader />
  return (
    <Stack>
      <Title order={1}>{admin ? '迁入历史数据档案' : '我的比赛历史'}</Title>
      <Text c="dimmed">
        这里保留原平台的记录，独立于当前训练进度和排行榜。队伍记录以队伍标识展示，不视为个人独立通关。历史分数未导出的会标为“未提供”，不会用当前题目分值代替。
      </Text>
      <Group>
        {data.batches.map((batch) => (
          <Badge key={batch.id} variant="light">
            {batch.title} · {batch.sourceInstance}
          </Badge>
        ))}
      </Group>
      <Card withBorder>
        <Stack>
          <Title order={2}>账号与队伍</Title>
          {data.members?.length ? (
            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>原账号</Table.Th>
                  <Table.Th>队伍</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {data.members.map((member) => (
                  <Table.Tr key={member.id}>
                    <Table.Td>{member.sourceUserName}</Table.Td>
                    <Table.Td>{member.sourceTeamName ?? '个人参赛'}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          ) : (
            <Text c="dimmed">暂无可查看的历史账号。</Text>
          )}
        </Stack>
      </Card>
      <Card withBorder>
        <Stack>
          <Title order={2}>提交与解题记录</Title>
          {!data.submissionCount && <Text c="dimmed">暂无可查看的历史提交。</Text>}
          <div style={{ overflowX: 'auto' }}>
            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>题目</Table.Th>
                  <Table.Th>原账号 / 队伍</Table.Th>
                  <Table.Th>结果</Table.Th>
                  <Table.Th>原始分数</Table.Th>
                  <Table.Th>时间</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {data.submissions.map((row) => (
                  <Table.Tr key={row.id}>
                    <Table.Td>{row.challengeTitle}</Table.Td>
                    <Table.Td>
                      {row.attribution === 'team'
                        ? `队伍：${row.sourceTeamName ?? '未命名'}`
                        : (row.sourceUserName ?? '个人')}
                    </Table.Td>
                    <Table.Td>{row.result}</Table.Td>
                    <Table.Td>{row.score ?? '未提供'}</Table.Td>
                    <Table.Td>{row.submittedAtUtc ? new Date(row.submittedAtUtc).toLocaleString() : '未提供'}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </div>
        </Stack>
      </Card>
      <Card withBorder>
        <Title order={2}>原比赛奖励</Title>
        {data.awards.length ? (
          data.awards.map((row) => (
            <Text key={row.id}>
              {row.name} · {row.value} 分 · {row.sourceTeamName ?? row.sourceUserName}
            </Text>
          ))
        ) : (
          <Text c="dimmed">暂无奖励记录。</Text>
        )}
      </Card>
      <Group justify="space-between">
        <Button variant="default" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - 50))}>
          上一页
        </Button>
        <Text size="sm">
          提交 {data.submissionCount} 条 · 奖励 {data.awardCount} 条
        </Text>
        <Button
          variant="default"
          disabled={skip + 50 >= Math.max(data.submissionCount, data.awardCount, data.memberCount ?? 0)}
          onClick={() => setSkip(skip + 50)}
        >
          下一页
        </Button>
      </Group>
    </Stack>
  )
}
