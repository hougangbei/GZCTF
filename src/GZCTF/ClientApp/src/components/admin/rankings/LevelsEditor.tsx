import { Alert, Button, Card, Group, Loader, NumberInput, Stack, Text, Title } from '@mantine/core'
import { useEffect, useRef, useState } from 'react'
import { rankingOperationId, useRankingActions, useRankingLevels } from '@Hooks/useRankingAdmin'

export const LevelsEditor = () => {
  const { data, error, mutate } = useRankingLevels()
  const { request, refresh } = useRankingActions()
  const [draft, setDraft] = useState<number[]>()
  const [version, setVersion] = useState('')
  const [affected, setAffected] = useState<number>()
  const [message, setMessage] = useState('')
  const [pending, setPending] = useState(false)
  const operation = useRef<string | undefined>(undefined)
  useEffect(() => { if (data && !draft) { setDraft(data.thresholds); setVersion(data.version) } }, [data, draft])
  if (error) return <Alert color="red">等级配置加载失败。<Button variant="subtle" onClick={() => void mutate()}>重试</Button></Alert>
  if (!data || !draft) return <Loader />
  const invalid = !draft.length || draft[0] !== 0 || draft.some((value, index) => !Number.isSafeInteger(value) || value < 0 || value > 2147483647 || (index > 0 && value <= draft[index - 1]))
  const change = (values: number[]) => { setDraft(values); setAffected(undefined); setMessage(''); operation.current = undefined }
  const process = async (save = false) => {
    setPending(true); setMessage('')
    try {
      if (save) {
        operation.current ??= rankingOperationId()
        const saved = await request<{ version: string; thresholds: number[] }>('/levels', 'PUT', { operationId: operation.current, version, thresholds: draft })
        setVersion(saved.version); setDraft(saved.thresholds); setAffected(undefined); operation.current = undefined
        setMessage('等级规则已保存'); await refresh()
      } else {
        const preview = await request<{ affectedUserCount: number }>('/levels/preview', 'POST', { version, thresholds: draft })
        setAffected(preview.affectedUserCount)
      }
    } catch (error) { setMessage((error as Error).message) }
    finally { setPending(false) }
  }
  return <Card withBorder><Stack>
    <Title order={2}>等级门槛</Title>
    <Text c="dimmed">等级按累计积分自动计算。扣分后重新定级，负分保持 Lv.1；调整门槛不改变任何人的积分。</Text>
    {data.version !== version && <Alert color="yellow">规则已被其他管理员修改，请先重新加载。</Alert>}
    {draft.map((threshold, index) => <Group key={index} align="end" wrap="nowrap">
      <NumberInput label={`Lv.${index + 1} 最低积分`} value={threshold} min={0} max={2147483647} allowDecimal={false} disabled={pending || index === 0} onChange={value => change(draft.map((item, i) => i === index ? Number(value) : item))} style={{ flex: 1 }} />
      {index === draft.length - 1 && index > 0 && <Button variant="subtle" color="red" disabled={pending} onClick={() => change(draft.slice(0, -1))}>移除末级</Button>}
    </Group>)}
    <Button variant="default" w="fit-content" disabled={pending || draft.length >= 50} onClick={() => change([...draft, draft[draft.length - 1] + 1000])}>添加等级</Button>
    {invalid && <Alert color="red">第一级必须从 0 开始，后续门槛必须是严格递增的非负整数。</Alert>}
    {affected !== undefined && <Alert>应用后将有 {affected} 个账号的等级变化。积分不会改变。</Alert>}
    {message && <Alert role="status">{message}</Alert>}
    <Group>
      <Button disabled={invalid || pending || data.version !== version} onClick={() => void process()}>预览等级变化</Button>
      <Button disabled={affected === undefined || invalid || pending || data.version !== version} loading={pending} onClick={() => void process(true)}>确认应用等级</Button>
      <Button variant="default" disabled={pending} onClick={async () => { const latest = await mutate(); if (latest) { setDraft(latest.thresholds); setVersion(latest.version); setAffected(undefined); setMessage(''); operation.current = undefined } }}>重新加载</Button>
    </Group>
  </Stack></Card>
}
