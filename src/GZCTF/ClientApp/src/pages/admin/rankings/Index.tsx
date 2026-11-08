import { Alert, Badge, Button, Card, Drawer, Group, Loader, Modal, NumberInput, Select, Stack, Table, Tabs, Text, Textarea, TextInput, Title } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { useRef, useState } from 'react'
import { Role } from '@Api'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { rankingOperationId, useRankingActions, useRankingDetail, useRankingOperations, useRankingUsers, type RankingDetail, type RankingOperation } from '@Hooks/useRankingAdmin'
import { LevelsEditor } from '@Components/admin/rankings/LevelsEditor'
import classes from './Rankings.module.css'

type Action = { kind: 'delta' | 'target' | 'visibility' | 'grant' | 'revoke' | 'reverse'; challengeId?: string; title?: string; operation?: RankingOperation }
const date = (value: string | number) => new Date(value).toLocaleString()
const kindLabel: Record<string, string> = { delta: '加减分', target: '设置总分', visibility: '上榜可见性', grant: '补记通关', revoke: '撤销通关', reverse: '撤销调整', mark_solve: '补记通关', revoke_solve: '撤销通关', restore_solve: '重新完成', levels: '等级门槛变更' }
const OperationList = ({ operations, reverse, manageUser, disabled = false }: { operations: RankingOperation[]; reverse?: (operation: RankingOperation) => void; manageUser?: (id: string) => void; disabled?: boolean }) => <Stack gap={0}>
  {!operations.length && <Text c="dimmed">暂无调整记录。</Text>}
  {operations.map(operation => <div key={operation.id} className={classes.operation}>
    <Group justify="space-between"><Text fw={600}>{kindLabel[operation.kind] ?? operation.kind} · {operation.delta > 0 ? '+' : ''}{operation.delta.toLocaleString()} 分</Text><Badge variant="light">{operation.beforeScore.toLocaleString()} → {operation.afterScore.toLocaleString()}</Badge></Group>
    {manageUser && operation.userId && operation.userId !== '00000000-0000-0000-0000-000000000000' && <Button size="compact-sm" variant="subtle" onClick={() => manageUser(operation.userId)}>学员：{operation.targetUserName || operation.userId}</Button>}
    <Text size="sm">{operation.reason}</Text><Text size="xs" c="dimmed">{operation.actorName || '管理员'} · {date(operation.createdAtUtc)}</Text>
    {operation.reversed && <Badge color="gray">已撤销</Badge>}
    {reverse && !operation.reversed && !operation.reversesId && (operation.canReverse ?? ['delta', 'target'].includes(operation.kind)) && <Button mt="xs" size="compact-sm" variant="subtle" color="red" disabled={disabled} onClick={() => reverse(operation)}>撤销这次调整</Button>}
  </div>)}
</Stack>

const RankingAdmin = () => {
  const [search, setSearch] = useState(''); const [query] = useDebouncedValue(search, 250)
  const [skip, setSkip] = useState(0); const [operationSkip, setOperationSkip] = useState(0)
  const [visibility, setVisibility] = useState('all')
  const { data: users, error: usersError, mutate: reloadUsers } = useRankingUsers(query, skip, visibility)
  const { data: operations, error: operationsError, mutate: reloadOperations } = useRankingOperations(operationSkip)
  const [selected, setSelected] = useState<string>(); const { data: detail, error: detailError, mutate: reloadDetail } = useRankingDetail(selected)
  const { request, refresh, exportCsv } = useRankingActions()
  const [userOperationSkip, setUserOperationSkip] = useState(0)
  const { data: userOperations, error: userOperationsError, mutate: reloadUserOperations } = useRankingOperations(userOperationSkip, selected)
  const [snapshot, setSnapshot] = useState<RankingDetail>(); const [action, setAction] = useState<Action>()
  const [value, setValue] = useState<number | string>(0); const [reason, setReason] = useState(''); const [message, setMessage] = useState(''); const [pending, setPending] = useState(false)
  const [challengeSearch, setChallengeSearch] = useState('')
  const retry = useRef<{ payload: string; id: string } | undefined>(undefined)
  const open = (next: Action) => {
    if (!detail || detail.user.userId !== selected || (next.kind === 'reverse' && next.operation?.userId !== selected)) return
    setSnapshot(detail); setAction(next); setValue(next.kind === 'target' ? detail.user.score : 0); setReason(''); setMessage(''); retry.current = undefined
  }
  const submit = async () => {
    if (!action || !snapshot || !reason.trim()) return
    setPending(true); setMessage('')
    const user = snapshot.user
    const body = { version: user.version, reason: reason.trim(), ...(action.kind === 'delta' || action.kind === 'target' ? { kind: action.kind, value: Number(value) } : {}), ...(action.kind === 'visibility' ? { hidden: !user.hidden } : {}) }
    let path = `/${user.userId}/adjustments`; let method = 'POST'
    if (action.kind === 'visibility') { path = `/${user.userId}/visibility`; method = 'PUT' }
    if (action.kind === 'grant' || action.kind === 'revoke') { path = `/${user.userId}/challenges/${action.challengeId}/solve`; method = action.kind === 'revoke' ? 'DELETE' : 'POST' }
    if (action.kind === 'reverse') path = `/adjustments/${action.operation!.id}/reverse`
    const payload = JSON.stringify({ path, method, body })
    if (retry.current?.payload !== payload) retry.current = { payload, id: rankingOperationId() }
    try {
      await request(path, method, { ...body, operationId: retry.current.id })
      setAction(undefined); setMessage('操作成功'); retry.current = undefined
      await refresh(); await reloadDetail()
    } catch (error) { setMessage((error as Error).message) }
    finally { setPending(false) }
  }
  const visibleUsers = users?.items ?? []
  const challenges = detail?.challenges.filter(challenge => challenge.title.toLowerCase().includes(challengeSearch.toLowerCase())) ?? []
  return <WithRole requiredRole={Role.Admin}><WithNavBar minWidth={0}><Stack className={classes.page}>
    <Group justify="space-between"><div><Title order={1}>排行榜管理</Title><Text c="dimmed">调整成绩、纠正通关并管理等级；所有变更保留记录。</Text></div><Button variant="default" disabled={pending} onClick={() => void exportCsv().catch(error => setMessage(error.message))}>导出排行榜 CSV</Button></Group>
    {message && !action && <Alert role="status">{message}</Alert>}
    <Tabs defaultValue="users"><Tabs.List><Tabs.Tab value="users">学员成绩</Tabs.Tab><Tabs.Tab value="operations">调整记录</Tabs.Tab><Tabs.Tab value="levels">等级设置</Tabs.Tab></Tabs.List>
      <Tabs.Panel value="users" pt="md"><Stack>
        <div className={classes.controls}><TextInput label="搜索学员" placeholder="输入用户名" value={search} onChange={event => { setSearch(event.currentTarget.value); setSkip(0) }} /><Select label="账号状态筛选" value={visibility} onChange={value => { setVisibility(value ?? 'all'); setSkip(0) }} data={[{ value: 'all', label: '全部账号' }, { value: 'hidden', label: '隐藏账号' }, { value: 'banned', label: '封禁账号' }]} /></div>
        {usersError ? <Alert color="red">列表加载失败。<Button variant="subtle" onClick={() => void reloadUsers()}>重试</Button></Alert> : !users ? <Loader /> : <>
          <Card withBorder p={0}><div className={classes.table}><Table striped highlightOnHover><Table.Thead><Table.Tr><Table.Th>用户名</Table.Th><Table.Th>等级</Table.Th><Table.Th>累计积分</Table.Th><Table.Th>解题数</Table.Th><Table.Th>状态</Table.Th><Table.Th>操作</Table.Th></Table.Tr></Table.Thead><Table.Tbody>
            {visibleUsers.map(user => <Table.Tr key={user.userId}><Table.Td><Text fw={600}>{user.userName}</Text><Text size="xs" c="dimmed" className={classes.signature} title={user.bio}>{user.bio || '暂未填写'}</Text></Table.Td><Table.Td>Lv.{user.level}</Table.Td><Table.Td>{user.score.toLocaleString()}</Table.Td><Table.Td>{user.solvedCount}</Table.Td><Table.Td>{user.role === 'Banned' ? <Badge color="red">封禁</Badge> : <Badge variant="light" color={user.hidden ? 'gray' : undefined}>{user.hidden ? '隐藏' : '上榜'}</Badge>}</Table.Td><Table.Td><Button size="compact-sm" variant="subtle" onClick={() => { setSelected(user.userId); setUserOperationSkip(0); setChallengeSearch(''); setMessage('') }}>管理 {user.userName}</Button></Table.Td></Table.Tr>)}
          </Table.Tbody></Table>{!visibleUsers.length && <Text p="md" c="dimmed">暂无符合条件的账号。</Text>}</div></Card>
          <Group justify="space-between"><Button variant="default" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - 20))}>上一页</Button><Text size="sm">共 {users.total} 个账号 · 第 {Math.floor(skip / 20) + 1} 页</Text><Button variant="default" disabled={skip + 20 >= users.total} onClick={() => setSkip(skip + 20)}>下一页</Button></Group>
        </>}
      </Stack></Tabs.Panel>
      <Tabs.Panel value="operations" pt="md"><Stack>{operationsError ? <Alert color="red">调整记录加载失败。<Button variant="subtle" onClick={() => void reloadOperations()}>重试</Button></Alert> : !operations ? <Loader /> : <><OperationList operations={operations.items} manageUser={id => { setSelected(id); setUserOperationSkip(0); setChallengeSearch(''); setMessage('') }} /><Group justify="space-between"><Button variant="default" disabled={operationSkip === 0} onClick={() => setOperationSkip(Math.max(0, operationSkip - 20))}>上一页记录</Button><Text>共 {operations.total} 条</Text><Button variant="default" disabled={operationSkip + 20 >= operations.total} onClick={() => setOperationSkip(operationSkip + 20)}>下一页记录</Button></Group></>}</Stack></Tabs.Panel>
      <Tabs.Panel value="levels" pt="md"><LevelsEditor /></Tabs.Panel>
    </Tabs>
    <Drawer opened={!!selected} onClose={() => { if (!pending) { setSelected(undefined); setAction(undefined) } }} title="学员成绩详情" size="xl">
      {detailError ? <Alert color="red">详情加载失败。<Button variant="subtle" onClick={() => void reloadDetail()}>重试</Button></Alert> : !detail || detail.user.userId !== selected ? <Loader /> : <Stack>
        <div className={classes.detailHeader}><Group justify="space-between"><Title order={2}>{detail.user.userName}</Title><Badge>Lv.{detail.user.level}</Badge></Group><Text size="xl" fw={700}>{detail.user.score.toLocaleString()} 分</Text><Text c="dimmed">{detail.user.solvedCount} 道通关 · {detail.user.hidden ? '当前隐藏上榜' : '当前允许上榜'}</Text><Text size="sm">旧基线 {detail.user.importedScore.toLocaleString()} · 新解题 {detail.user.earnedScore.toLocaleString()} · 调整 {detail.user.adjustmentScore.toLocaleString()}</Text></div>
        <Group><Button disabled={pending} onClick={() => open({ kind: 'delta' })}>加分 / 扣分</Button><Button variant="default" disabled={pending} onClick={() => open({ kind: 'target' })}>设置目标总分</Button><Button variant="default" disabled={pending} onClick={() => open({ kind: 'visibility' })}>{detail.user.hidden ? '恢复上榜' : '隐藏上榜'}</Button><Button variant="subtle" disabled={pending} onClick={() => void reloadDetail()}>刷新成绩</Button></Group>
        <Tabs defaultValue="challenges"><Tabs.List><Tabs.Tab value="challenges">通关管理</Tabs.Tab><Tabs.Tab value="history">该学员调整记录</Tabs.Tab></Tabs.List>
          <Tabs.Panel value="challenges" pt="md"><Stack><TextInput label="搜索题目" value={challengeSearch} onChange={event => setChallengeSearch(event.currentTarget.value)} /><Text size="sm" c="dimmed">撤销通关同步扣回该题分数，重新完成后恢复；重复操作不会重复计分。</Text>{challenges.map(challenge => <Card withBorder key={challenge.challengeId} p="sm"><Group justify="space-between" wrap="nowrap"><div><Text fw={600}>{challenge.title}</Text><Text size="xs" c="dimmed">{challenge.imported ? '迁入题目' : '训练题目'} · {challenge.frozenScoreAvailable === false ? '冻结分值待核实' : `${challenge.score} 分`} · {challenge.solved ? '已通关' : '未通关'}{challenge.solvedAtUtc ? ` · ${date(challenge.solvedAtUtc)}` : ''}</Text></div><Button size="compact-sm" variant="light" color={challenge.solved ? 'red' : undefined} disabled={pending || challenge.frozenScoreAvailable === false} onClick={() => open({ kind: challenge.solved ? 'revoke' : 'grant', challengeId: challenge.challengeId, title: challenge.title })}>{challenge.solved ? '撤销通关' : '补记通关'}</Button></Group></Card>)}{!challenges.length && <Text c="dimmed">暂无匹配题目。</Text>}</Stack></Tabs.Panel>
          <Tabs.Panel value="history" pt="md"><Stack>
            {userOperationsError ? <Alert color="red">调整记录加载失败。<Button variant="subtle" onClick={() => void reloadUserOperations()}>重试</Button></Alert> : !userOperations ? <Loader /> : <>
              <OperationList operations={userOperations.items} reverse={operation => open({ kind: 'reverse', operation })} disabled={pending} />
              <Group justify="space-between"><Button variant="default" disabled={pending || userOperationSkip === 0} onClick={() => setUserOperationSkip(Math.max(0, userOperationSkip - 20))}>上一页学员记录</Button><Text size="sm">共 {userOperations.total} 条</Text><Button variant="default" disabled={pending || userOperationSkip + 20 >= userOperations.total} onClick={() => setUserOperationSkip(userOperationSkip + 20)}>下一页学员记录</Button></Group>
            </>}
          </Stack></Tabs.Panel>
        </Tabs>
      </Stack>}
    </Drawer>
    <Modal opened={!!action} onClose={() => { if (!pending) setAction(undefined) }} title={action ? kindLabel[action.kind] : ''} centered>
      {action && snapshot && <Stack><Text>学员：{snapshot.user.userName} · 当前总分 {snapshot.user.score.toLocaleString()}</Text>
        {action.title && <Text>题目：{action.title}</Text>}
        {(action.kind === 'delta' || action.kind === 'target') && <><NumberInput label={action.kind === 'delta' ? '积分差额（扣分填负数）' : '目标总分'} value={value} allowDecimal={false} disabled={pending} onChange={setValue} /><Text size="sm">调整后：{(action.kind === 'target' ? Number(value) : snapshot.user.score + Number(value)).toLocaleString()} 分</Text></>}
        {action.kind === 'visibility' && <Alert>{snapshot.user.hidden ? '恢复公开上榜；封禁账号仍不会显示。' : '仅隐藏公开排行，不影响登录和学习。'}</Alert>}
        {action.kind === 'revoke' && <Alert color="yellow">将撤销通关并同步扣分，之后可以重新完成。原始历史和操作记录保留。</Alert>}
        {action.kind === 'reverse' && <Alert color="yellow">将冲销这次调整：{action.operation?.delta} 分。原记录保留，同一调整只能撤销一次。</Alert>}
        <Textarea label="调整原因" value={reason} required minRows={3} maxLength={1000} disabled={pending} onChange={event => setReason(event.currentTarget.value)} />
        {message && <Alert color="red" role="alert">{message}<Button variant="subtle" disabled={pending} onClick={async () => { const latest = await reloadDetail(); if (latest) { setSnapshot(latest); retry.current = undefined; setMessage('成绩已刷新，请重新核对操作后确认。') } }}>刷新后核对</Button></Alert>}
        <Button disabled={!reason.trim() || pending || ((action.kind === 'delta' || action.kind === 'target') && (value === '' || !Number.isSafeInteger(Number(value))))} loading={pending} onClick={() => void submit()}>确认{kindLabel[action.kind]}</Button>
      </Stack>}
    </Modal>
  </Stack></WithNavBar></WithRole>
}
export default RankingAdmin
