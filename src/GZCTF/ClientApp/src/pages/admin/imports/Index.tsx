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
  const [legacyFile, setLegacyFile] = useState<File | null>(null)
  const [purpose, setPurpose] = useState<'GZCTF-GAME' | 'CTFd'>('GZCTF-GAME')
  const [options, setOptions] = useState<ImportOptions>({
    sourceInstance: '',
    includeAccounts: false,
    includeHistory: false,
    mergeScores: false,
  })
  const [preview, setPreview] = useState<ImportPreview>()
  const [result, setResult] = useState<ImportBatch>()
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState('')
  const validSource = /^[A-Za-z0-9._:/-]{1,128}$/.test(options.sourceInstance)
  const formatMismatch = preview && preview.format !== purpose
  const effectiveOptions: ImportOptions =
    purpose === 'GZCTF-GAME'
      ? {
          ...options,
          includeAccounts: false,
          includeHistory: false,
          mergeScores: false,
          userNameOverrides: undefined,
          skipUserIds: undefined,
        }
      : options
  const update = (next: ImportOptions) => {
    setOptions(next)
    setPreview(undefined)
    setResult(undefined)
    setMessage('')
  }
  const process = async (execute = false) => {
    if (!file || !validSource || (execute && (!preview?.canImport || formatMismatch))) return
    setPending(true)
    setMessage('')
    try {
      if (execute && preview) {
        setResult(await upload<ImportBatch>(file, effectiveOptions, preview.planFingerprint))
        setPreview(undefined)
      } else setPreview(await upload<ImportPreview>(file, effectiveOptions))
    } catch (error) {
      setMessage((error as Error).message)
      if (execute) setPreview(undefined)
    } finally {
      setPending(false)
    }
  }
  const legacyUpload = async () => {
    if (!legacyFile) return
    setPending(true)
    setMessage('')
    try {
      await api.imports.importsUpload({ package: legacyFile })
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
          <Text c="dimmed">GZCTF 用于导入题目与资源；CTFd 用于一次性迁移题目、账号和历史。原平台不受影响。</Text>
          <Card withBorder>
            <Stack>
              <Select
                label="导入用途"
                value={purpose}
                allowDeselect={false}
                disabled={pending}
                data={[
                  { value: 'GZCTF-GAME', label: 'GZCTF：导入题目' },
                  { value: 'CTFd', label: 'CTFd：一次性平台迁移' },
                ]}
                onChange={(value) => {
                  if (value !== 'GZCTF-GAME' && value !== 'CTFd') return
                  setPurpose(value)
                  update({
                    ...options,
                    includeAccounts: value === 'CTFd',
                    includeHistory: value === 'CTFd',
                    mergeScores: false,
                    userNameOverrides: undefined,
                    skipUserIds: undefined,
                  })
                }}
              />
              <Text size="sm" c="dimmed">
                {purpose === 'GZCTF-GAME'
                  ? '只导入题目名称、正文、类型、分值、Flag、提示、附件和容器配置，不导入用户、比赛成绩或提交记录。'
                  : '完整备份迁移到训练平台，保留受支持的旧密码；历史成绩是否计入训练排行榜需单独确认。迁移后不与原 CTFd 实时同步。'}
              </Text>
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
                description="使用来源平台的英文标识，如 gzctf-s7 或 campus-ctfd，以后保持不变。中文比赛名称可填在路线名称中。"
                placeholder={purpose === 'GZCTF-GAME' ? 'gzctf-s7' : 'campus-ctfd'}
                error={
                  options.sourceInstance && !validSource
                    ? '请使用 1–128 个英文字母、数字或 . _ : / -，不要填写中文、空格或括号。'
                    : undefined
                }
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
              {purpose === 'CTFd' && (
                <>
                  <Checkbox
                    label="导入账号（保留受支持的旧密码，首次成功登录自动升级）"
                    checked={options.includeAccounts}
                    disabled={pending}
                    onChange={(event) =>
                      update({
                        ...options,
                        includeAccounts: event.currentTarget.checked,
                        mergeScores: event.currentTarget.checked ? options.mergeScores : false,
                      })
                    }
                  />
                  <Checkbox
                    label="保留原比赛历史成绩、队伍和提交记录"
                    description={
                      options.mergeScores
                        ? '同时保存来源历史，并将个人旧成绩作为训练排行榜起点。'
                        : '历史记录独立存放；未勾选合并成绩时不计入训练排行榜。队伍成绩不分配给个人。'
                    }
                    checked={options.includeHistory}
                    disabled={pending}
                    onChange={(event) =>
                      update({
                        ...options,
                        includeHistory: event.currentTarget.checked,
                        mergeScores: event.currentTarget.checked ? options.mergeScores : false,
                      })
                    }
                  />
                  <Checkbox
                    label="合并 CTFd 旧成绩到训练排行榜"
                    description="仅支持 CTFd 个人模式的一次性迁移。保留旧总分和扣分，后续新题继续累计；同时导入账号和历史。"
                    checked={options.mergeScores ?? false}
                    disabled={pending}
                    onChange={(event) =>
                      update({
                        ...options,
                        mergeScores: event.currentTarget.checked,
                        ...(event.currentTarget.checked ? { includeAccounts: true, includeHistory: true } : {}),
                      })
                    }
                  />
                  {options.mergeScores && (
                    <Alert>
                      旧题通关同步保留，重做不重复加分。旧总分固定为导出时的快照，不受题目停用或后来改分影响。每个来源只迁入一次成绩。
                    </Alert>
                  )}
                </>
              )}
              <Button w="fit-content" loading={pending} disabled={!file || !validSource} onClick={() => process()}>
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
                {formatMismatch && (
                  <Alert color="red" role="alert">
                    导出包与所选用途不匹配，请切换导入用途后重新检查。检测到的格式：{preview.format}。
                  </Alert>
                )}
                <Group>
                  <Text>{preview.challengeCount} 道题目</Text>
                  <Text>{preview.fileCount} 个文件</Text>
                  {purpose === 'CTFd' && (
                    <>
                      <Text>{preview.userCount} 个账号</Text>
                      <Text>{preview.teamCount} 支队伍</Text>
                      <Text>{preview.submissionCount} 条提交</Text>
                    </>
                  )}
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
                <Button
                  w="fit-content"
                  disabled={!preview.canImport || !!formatMismatch}
                  loading={pending}
                  onClick={() => process(true)}
                >
                  确认导入
                </Button>
              </Stack>
            </Card>
          )}
          {result && (
            <Alert color="green" title="导入完成">
              <Stack gap="xs">
                <Text>
                  已处理 {result.challengeCount} 道题目
                  {result.format === 'CTFd' ? `，新增 ${result.userCount} 个账号` : ''}。
                </Text>
                {result.treeId && <Link to={`/admin/skill-trees/${result.treeId}`}>查看导入的路线草稿</Link>}
                {result.format === 'CTFd' && <Link to="/admin/imports/history">查看历史数据档案</Link>}
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
              <Input.Wrapper label="旧内部格式 ZIP">
                <input
                  aria-label="旧内部格式 ZIP"
                  type="file"
                  accept=".zip,application/zip"
                  disabled={pending}
                  onChange={(event) => {
                    setLegacyFile(event.currentTarget.files?.[0] ?? null)
                    setMessage('')
                  }}
                />
              </Input.Wrapper>
              <Button variant="default" disabled={!legacyFile || pending} onClick={legacyUpload}>
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
