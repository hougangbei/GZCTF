import {
  Alert,
  Badge,
  Button,
  Card,
  Checkbox,
  Divider,
  Group,
  Input,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useState } from 'react'
import { Link } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { useImportBatches } from '@Hooks/useChallengeLibraryAdmin'
import {
  usePlatformImportActions,
  usePlatformImportBatches,
  type ImportOptions,
  type ImportPreview,
  type ImportBatch,
} from '@Hooks/usePlatformImports'
import { useAdminSkills } from '@Hooks/useSkillHierarchy'
import api, { Role } from '@Api'

const AdminImports = () => {
  const { data: batches } = usePlatformImportBatches()
  const { data: legacy, mutate: refreshLegacy } = useImportBatches()
  const { data: skills } = useAdminSkills()
  const { upload } = usePlatformImportActions()
  const [file, setFile] = useState<File | null>(null)
  const [options, setOptions] = useState<ImportOptions>({
    sourceInstance: '',
    includeAccounts: true,
    includeHistory: true,
    mergeScores: false,
  })
  const [preview, setPreview] = useState<ImportPreview>()
  const [result, setResult] = useState<ImportBatch>()
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const update = (next: ImportOptions) => {
    setOptions(next)
    setPreview(undefined)
    setResult(undefined)
    setMessage('')
  }
  const process = async (execute = false) => {
    if (!file) return
    setPending(true)
    setMessage('')
    try {
      if (execute && preview) {
        setResult(await upload<ImportBatch>(file, options, preview.planFingerprint))
        setPreview(undefined)
      } else setPreview(await upload<ImportPreview>(file, options))
    } catch (error) {
      setMessage((error as Error).message)
      if (execute) setPreview(undefined)
    } finally {
      setPending(false)
    }
  }
  const legacyUpload = async () => {
    if (!file) return
    setPending(true)
    setMessage('')
    try {
      await api.imports.importsUpload({ package: file })
      await refreshLegacy()
      setMessage('内部格式导入完成')
    } catch {
      setMessage('内部格式导入失败，请检查文件。')
    } finally {
      setPending(false)
    }
  }
  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>
        <Stack>
          <Group justify="space-between">
            <Title>数据导入</Title>
            <Button component={Link} to="/admin/imports/history" variant="light">
              历史数据档案
            </Button>
          </Group>
          <Text c="dimmed">
            导入 GZCTF 比赛包或 CTFd 完整备份。题目进入训练库，账号与历史成绩保留，原平台不受影响。
          </Text>
          <Card withBorder>
            <Stack>
              <Input.Wrapper label="导出 ZIP" description="完整备份 ZIP 最大 256 MiB，解压后总量不超过 256 MiB。">
                <input
                  aria-label="导出 ZIP"
                  type="file"
                  accept=".zip,application/zip"
                  disabled={pending}
                  onChange={(event) => {
                    setFile(event.currentTarget.files?.[0] ?? null)
                    setPreview(undefined)
                    setResult(undefined)
                    setMessage('')
                  }}
                />
              </Input.Wrapper>
              <TextInput
                label="来源标识"
                description="为原平台起一个固定标识，例如 campus-ctfd。以后从同一个平台导入时沿用，避免重复。"
                value={options.sourceInstance}
                disabled={pending}
                onChange={(event) => update({ ...options, sourceInstance: event.currentTarget.value })}
              />
              <Select
                label="导入位置"
                description="默认按比赛创建路线草稿，也可把题目关联到已有技能。"
                searchable
                clearable
                placeholder="新建路线、类别和技能"
                disabled={pending}
                value={options.targetSkillId ?? null}
                data={(skills ?? []).map((skill) => ({ value: skill.skillId, label: skill.name }))}
                onChange={(value) => {
                  const skill = skills?.find((item) => item.skillId === value)
                  update({ ...options, targetSkillId: value ?? undefined, targetSkillRowVersion: skill?.rowVersion })
                }}
              />
              {!options.targetSkillId && (
                <TextInput
                  label="路线名称（可选）"
                  placeholder="使用原比赛名称"
                  value={options.treeName ?? ''}
                  disabled={pending}
                  onChange={(event) => update({ ...options, treeName: event.currentTarget.value || undefined })}
                />
              )}
              <Checkbox
                label="导入账号（保留受支持的旧密码，首次成功登录自动升级）"
                checked={options.includeAccounts}
                disabled={pending}
                onChange={(event) => update({ ...options, includeAccounts: event.currentTarget.checked, mergeScores: event.currentTarget.checked ? options.mergeScores : false })}
              />
              <Checkbox
                label="保留原比赛历史成绩、队伍和提交记录"
                description={options.mergeScores ? "同时保存来源历史，并将个人旧成绩作为训练排行榜起点。" : "历史记录独立存放；未勾选合并成绩时不计入训练排行榜。队伍成绩不分配给个人。"}
                checked={options.includeHistory}
                disabled={pending}
                onChange={(event) => update({ ...options, includeHistory: event.currentTarget.checked, mergeScores: event.currentTarget.checked ? options.mergeScores : false })}
              />
              <Checkbox
                label="合并 CTFd 旧成绩到训练排行榜"
                description="仅支持 CTFd 个人模式的一次性迁移。保留旧总分和扣分，后续新题继续累计；同时导入账号和历史。"
                checked={options.mergeScores ?? false}
                disabled={pending}
                onChange={(event) => update({
                  ...options,
                  mergeScores: event.currentTarget.checked,
                  ...(event.currentTarget.checked ? { includeAccounts: true, includeHistory: true } : {}),
                })}
              />
              {options.mergeScores && (
                <Alert>旧题通关同步保留，重做不重复加分。旧总分固定为导出时的快照，不受题目停用或后来改分影响。每个来源只迁入一次成绩。</Alert>
              )}
              <Button
                w="fit-content"
                loading={pending}
                disabled={!file || !options.sourceInstance.trim()}
                onClick={() => process()}
              >
                检查导出包
              </Button>
            </Stack>
          </Card>
          {message && (
            <Alert role="alert" color="red">
              {message}
            </Alert>
          )}
          {preview && (
            <Card withBorder>
              <Stack>
                <Group>
                  <Title order={2}>{preview.title}</Title>
                  <Badge>{preview.format}</Badge>
                </Group>
                <Group>
                  <Text>{preview.challengeCount} 道题目</Text>
                  <Text>{preview.fileCount} 个文件</Text>
                  <Text>{preview.userCount} 个账号</Text>
                  <Text>{preview.teamCount} 支队伍</Text>
                  <Text>{preview.submissionCount} 条提交</Text>
                </Group>
                <Alert>导入后为草稿，请在题目库核对配置，再发布到技能树。导入不会启动容器或下载镜像。</Alert>
                {preview.issues.map((issue, index) => (
                  <Alert
                    key={`${issue.code}-${index}`}
                    color={issue.severity === 'error' ? 'red' : 'yellow'}
                    title={`${issue.severity === 'error' ? '需处理' : '提示'}${issue.sourceId ? ` · 来源编号 ${issue.sourceId}` : ''}`}
                  >
                    <Text>{issue.message}</Text>
                    {[
                      'duplicate_user_name',
                      'duplicate_user_email',
                      'invalid_user_name',
                      'invalid_user_email',
                      'skipped_source_user',
                    ].includes(issue.code) && (
                      <Checkbox
                        mt="xs"
                        label={`不创建来源账号 ${issue.sourceId}，只保留历史记录`}
                        checked={options.skipUserIds?.includes(issue.sourceId) ?? false}
                        disabled={pending}
                        onChange={(event) => {
                          const ids = options.skipUserIds ?? []
                          setOptions({
                            ...options,
                            skipUserIds: event.currentTarget.checked
                              ? [...new Set([...ids, issue.sourceId])]
                              : ids.filter((id) => id !== issue.sourceId),
                          })
                          setPreview({ ...preview, canImport: false })
                        }}
                      />
                    )}

                    {['invalid_user_name', 'duplicate_user_name'].includes(issue.code) && (
                      <TextInput
                        label={`账号 ${issue.sourceId} 改名`}
                        description="填写后重新检查导出包"
                        disabled={pending}
                        value={options.userNameOverrides?.[issue.sourceId] ?? ''}
                        onChange={(event) => {
                          setOptions({
                            ...options,
                            userNameOverrides: {
                              ...options.userNameOverrides,
                              [issue.sourceId]: event.currentTarget.value,
                            },
                          })
                          setPreview({ ...preview, canImport: false })
                        }}
                      />
                    )}
                  </Alert>
                ))}
                <Button w="fit-content" disabled={!preview.canImport} loading={pending} onClick={() => process(true)}>
                  确认导入
                </Button>
              </Stack>
            </Card>
          )}
          {result && (
            <Alert color="green" title="导入完成">
              <Stack gap="xs">
                <Text>
                  已处理 {result.challengeCount} 道题目，新增 {result.userCount} 个账号。
                </Text>
                {result.treeId && <Link to={`/admin/skill-trees/${result.treeId}`}>查看导入的路线草稿</Link>}
                <Link to="/admin/imports/history">查看历史数据档案</Link>
              </Stack>
            </Alert>
          )}
          <Divider label="导入批次" />
          <div style={{ overflowX: 'auto' }}>
            <Table>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>名称 / 来源</Table.Th>
                  <Table.Th>格式</Table.Th>
                  <Table.Th>题目</Table.Th>
                  <Table.Th>账号</Table.Th>
                  <Table.Th>历史提交</Table.Th>
                  <Table.Th>路线</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {batches?.map((batch) => (
                  <Table.Tr key={batch.id}>
                    <Table.Td>
                      {batch.title}
                      <Text size="xs" c="dimmed">
                        {batch.sourceInstance}
                      </Text>
                    </Table.Td>
                    <Table.Td>{batch.format}</Table.Td>
                    <Table.Td>{batch.challengeCount}</Table.Td>
                    <Table.Td>{batch.userCount}</Table.Td>
                    <Table.Td>{batch.submissionCount}</Table.Td>
                    <Table.Td>
                      {batch.treeId && <Link to={`/admin/skill-trees/${batch.treeId}`}>编辑路线</Link>}
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </div>
          <details>
            <summary>旧内部格式导入与批次</summary>
            <Stack mt="sm">
              <Text size="sm">仅用于此前的内部迁移包；GZCTF/CTFd 导出请使用上方预检。</Text>
              <Button variant="default" disabled={!file || pending} onClick={legacyUpload}>
                导入所选内部迁移包
              </Button>
              {legacy?.map((batch) => (
                <Text key={batch.id} size="sm">
                  {batch.id} · {batch.state} · {batch.challengeCount} 道题目
                </Text>
              ))}
            </Stack>
          </details>
        </Stack>
      </WithNavBar>
    </WithRole>
  )
}
export default AdminImports
