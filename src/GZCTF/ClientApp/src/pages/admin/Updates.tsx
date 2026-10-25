import { Alert, Anchor, Badge, Button, Group, Paper, Select, Stack, Text, TextInput, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import { AdminPage } from '@Components/admin/AdminPage'
import api, { ContentType, fetcher } from '@Api'

type UpdateSource = { kind: 'github' | 'package' | 'directory'; location: string; ref: string; sha256: string }
type Validation = {
  id: string
  sourceDigest: string
  version?: string
  artifactHash?: string
  expiresAtUtc?: string
  canApply: boolean
  checks: { key: string; label: string; status: 'passed' | 'failed' | 'running'; message: string }[]
}
type UpdateStatus = {
  configured: boolean
  mode?: 'native' | 'compose'
  phase: string
  message?: string | null
  currentSha?: string | null
  latestSha?: string | null
  latestUrl?: string | null
  publishedAtUtc?: string | null
  updateAvailable?: boolean
  checkError?: string | null
  validation?: Validation
  targetSha?: string | null
  backupPath?: string | null
  changedAtUtc?: string | null
}
const activePhases = ['validating', 'accepted', 'pulling', 'backingUp', 'starting']
const emptySource: UpdateSource = { kind: 'github', location: '', ref: '', sha256: '' }

const Updates = () => {
  const { t } = useTranslation('admin')
  const { data, error, mutate, isLoading } = useSWR<UpdateStatus>('/api/admin/updates', fetcher, {
    refreshInterval: 5000,
  })
  const {
    data: source,
    error: sourceError,
    mutate: mutateSource,
  } = useSWR<UpdateSource>('/api/admin/updates/source', fetcher)
  const [form, setForm] = useState<UpdateSource>(emptySource)
  const initialized = useRef(false)
  const [operation, setOperation] = useState<'saving' | 'validating' | 'applying' | 'refreshing'>()
  const [operationError, setOperationError] = useState<string>()
  const [saved, setSaved] = useState(false)
  const [blockedReports, setBlockedReports] = useState<string[]>([])
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    if (source && !initialized.current) {
      setForm(source)
      initialized.current = true
    }
  }, [source])
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])
  const dirty = !source || (['kind', 'location', 'ref', 'sha256'] as const).some((key) => form[key] !== source[key])
  const active = !!operation || (!!data && activePhases.includes(data.phase))
  const legacy = !!data?.configured && !data.mode
  const report = data?.validation && !blockedReports.includes(data.validation.id) ? data.validation : undefined
  const expiresAt = report?.expiresAtUtc ? Date.parse(report.expiresAtUtc) : NaN
  const hasExpiry = Number.isFinite(expiresAt)
  const expired = hasExpiry && expiresAt <= now
  const canValidatedApply =
    !legacy &&
    !!data?.configured &&
    !error &&
    !sourceError &&
    !dirty &&
    !active &&
    data.phase === 'ready' &&
    !!report?.canApply &&
    !!report.id &&
    hasExpiry &&
    !expired
  const canLegacyApply =
    legacy &&
    !error &&
    !active &&
    !!data?.currentSha &&
    !!data.latestSha &&
    !!data.updateAvailable &&
    !['interrupted', 'recoveryRequired'].includes(data.phase) &&
    !(data.phase === 'failed' && !!data.backupPath)
  const canApply = legacy ? canLegacyApply : canValidatedApply
  // Confirmation can remain open while a report expires, polling fails, or the source changes.
  const eligibility = useRef({ canApply, report, legacy, latestSha: data?.latestSha })
  eligibility.current = { canApply, report, legacy, latestSha: data?.latestSha }
  const updateForm = (next: Partial<UpdateSource>) => {
    setForm((value) => ({ ...value, ...next }))
    setSaved(false)
  }
  const invalidateReport = () => {
    if (data?.validation?.id) setBlockedReports((ids) => [...ids, data.validation!.id])
    void mutate((current) => (current ? { ...current, validation: undefined } : current), false)
  }
  const save = async () => {
    setOperation('saving')
    setOperationError(undefined)
    setSaved(false)
    invalidateReport()
    try {
      const response = await api.request<UpdateSource>({
        path: '/api/admin/updates/source',
        method: 'PUT',
        type: ContentType.Json,
        body: form,
        format: 'json',
      })
      setForm(response.data)
      await mutateSource(response.data, false)
      setSaved(true)
      await mutate()
    } catch {
      setOperationError(t('updates.saveFailed'))
    } finally {
      setOperation(undefined)
    }
  }
  const refreshLegacy = async () => {
    setOperation('refreshing')
    setOperationError(undefined)
    try {
      const response = await api.request<UpdateStatus>({
        path: '/api/admin/updates',
        method: 'GET',
        query: { refresh: true },
        format: 'json',
      })
      await mutate(response.data, false)
    } catch {
      setOperationError(t('updates.loadFailed'))
    } finally {
      setOperation(undefined)
    }
  }
  const applyLegacy = async (targetSha: string) => {
    const latest = eligibility.current
    if (!latest.legacy || !latest.canApply || latest.latestSha !== targetSha) return
    setOperation('applying')
    setOperationError(undefined)
    try {
      await api.request({ path: '/api/admin/updates', method: 'POST', type: ContentType.Json, body: { targetSha } })
      await mutate()
    } catch {
      setOperationError(t('updates.applyFailed'))
    } finally {
      setOperation(undefined)
    }
  }
  const validate = async () => {
    setOperation('validating')
    setOperationError(undefined)
    invalidateReport()
    try {
      await api.request({ path: '/api/admin/updates/validate', method: 'POST' })
      await mutate()
    } catch {
      setOperationError(t('updates.validateFailed'))
    } finally {
      setOperation(undefined)
    }
  }
  const apply = async (validationId: string) => {
    const latest = eligibility.current
    if (
      !latest.canApply ||
      latest.report?.id !== validationId ||
      !latest.report.expiresAtUtc ||
      Date.parse(latest.report.expiresAtUtc) <= Date.now()
    )
      return
    setOperation('applying')
    setOperationError(undefined)
    invalidateReport()
    try {
      await api.request({ path: '/api/admin/updates', method: 'POST', type: ContentType.Json, body: { validationId } })
      await mutate()
    } catch {
      setOperationError(t('updates.applyFailed'))
    } finally {
      setOperation(undefined)
    }
  }
  const confirmUpdate = () => {
    if (!canApply) return
    if (legacy && data?.latestSha) {
      const targetSha = data.latestSha
      modals.openConfirmModal({
        title: t('updates.confirmTitle'),
        children: <Text size="sm">{t('updates.legacyConfirmBody', { sha: targetSha.slice(0, 12) })}</Text>,
        labels: { confirm: t('updates.apply'), cancel: t('updates.cancel') },
        confirmProps: { color: 'orange' },
        onConfirm: () => {
          void applyLegacy(targetSha)
        },
      })
      return
    }
    if (!report) return
    const validationId = report.id
    modals.openConfirmModal({
      title: t('updates.confirmTitle'),
      children: <Text size="sm">{t('updates.confirmBody', { version: report.version ?? t('updates.unknown') })}</Text>,
      labels: { confirm: t('updates.apply'), cancel: t('updates.cancel') },
      confirmProps: { color: 'orange' },
      onConfirm: () => {
        void apply(validationId)
      },
    })
  }

  return (
    <AdminPage isLoading={isLoading}>
      <Stack w="100%" maw={900} gap="md" style={{ minWidth: 0 }}>
        <Title order={2}>{t('updates.title')}</Title>
        <Text c="dimmed">{t('updates.description')}</Text>
        {(error || sourceError) && <Alert color="red">{t('updates.loadFailed')}</Alert>}
        {operationError && <Alert color="red">{operationError}</Alert>}
        {data && !data.configured && <Alert color="yellow">{t('updates.notConfigured')}</Alert>}
        <Paper withBorder p="lg" radius="md">
          <Stack gap="sm">
            <Text fw={600}>{t('updates.sourceTitle')}</Text>
            {legacy && <Alert color="yellow">{t('updates.legacyAgent')}</Alert>}
            <Select
              label={t('updates.sourceKind')}
              value={form.kind}
              disabled={active || !source}
              allowDeselect={false}
              data={(['github', 'package', 'directory'] as const).map((value) => ({
                value,
                label: t(`updates.sources.${value}`),
              }))}
              onChange={(kind) =>
                kind && updateForm({ kind: kind as UpdateSource['kind'], location: '', ref: '', sha256: '' })
              }
            />
            <TextInput
              label={t(`updates.locations.${form.kind}`)}
              value={form.location}
              disabled={active || !source}
              description={
                form.kind === 'directory'
                  ? t('updates.directoryHelp')
                  : form.kind === 'github'
                    ? t('updates.githubHelp')
                    : t('updates.packageHelp')
              }
              onChange={(event) => updateForm({ location: event.currentTarget.value })}
            />
            {form.kind === 'github' && (
              <TextInput
                label={t('updates.ref')}
                value={form.ref}
                disabled={active || !source}
                onChange={(event) => updateForm({ ref: event.currentTarget.value })}
              />
            )}
            {form.kind === 'package' && (
              <TextInput
                label={t('updates.sha256')}
                value={form.sha256}
                disabled={active || !source}
                onChange={(event) => updateForm({ sha256: event.currentTarget.value })}
              />
            )}
            {saved && (
              <Text size="sm" c="teal" role="status">
                {t('updates.saved')}
              </Text>
            )}
            {dirty && initialized.current && (
              <Text size="sm" c="dimmed">
                {t('updates.unsaved')}
              </Text>
            )}
            <Group justify="flex-end">
              <Button
                variant="default"
                loading={operation === 'saving'}
                disabled={active || !source || !dirty || !form.location.trim()}
                onClick={() => void save()}
              >
                {t('updates.save')}
              </Button>
              <Button
                loading={operation === 'validating'}
                disabled={legacy || active || dirty || !!error || !!sourceError || !data?.configured}
                onClick={() => void validate()}
              >
                {t('updates.validate')}
              </Button>
            </Group>
          </Stack>
        </Paper>
        {data && (
          <Paper withBorder p="lg" radius="md">
            <Stack gap="sm">
              <Group justify="space-between">
                <Text fw={600}>{t('updates.task')}</Text>
                <Badge>{t(`updates.phases.${data.phase}`, { defaultValue: data.phase })}</Badge>
              </Group>
              <Text size="sm" style={{ overflowWrap: 'anywhere' }}>
                {t('updates.current')}:{' '}
                {(legacy ? data.currentSha?.slice(0, 12) : data.currentSha) ?? t('updates.unknown')}
              </Text>
              {legacy && (
                <>
                  <Group justify="space-between">
                    <Text size="sm">{t('updates.latest')}</Text>
                    <Text size="sm" ff="monospace">
                      {data.latestSha?.slice(0, 12) ?? t('updates.unknown')}
                    </Text>
                  </Group>
                  {data.checkError && <Alert color="yellow">{data.checkError}</Alert>}
                  {data.publishedAtUtc && (
                    <Text size="sm" c="dimmed">
                      {t('updates.publishedAt')}: {new Date(data.publishedAtUtc).toLocaleString()}
                    </Text>
                  )}
                  {data.latestUrl && (
                    <Anchor href={data.latestUrl} target="_blank" rel="noopener noreferrer">
                      {t('updates.workflow')}
                    </Anchor>
                  )}
                  <Group>
                    <Badge color={data.updateAvailable ? 'blue' : 'gray'}>
                      {data.updateAvailable ? t('updates.available') : t('updates.noUpdate')}
                    </Badge>
                    <Button
                      variant="default"
                      disabled={active}
                      loading={operation === 'refreshing'}
                      onClick={() => void refreshLegacy()}
                    >
                      {t('updates.refresh')}
                    </Button>
                  </Group>
                </>
              )}
              {data.message && (
                <Alert
                  color={['failed', 'invalid', 'interrupted', 'recoveryRequired'].includes(data.phase) ? 'red' : 'blue'}
                >
                  {data.message}
                </Alert>
              )}
              {!legacy &&
                (report ? (
                  <>
                    <Text fw={600}>{t('updates.report')}</Text>
                    {report.version && (
                      <Group gap="xs">
                        <Text size="sm">{t('updates.version')}:</Text>
                        <Text size="sm">{report.version}</Text>
                      </Group>
                    )}
                    {report.artifactHash && (
                      <>
                        <Text size="sm">{t('updates.artifactHash')}</Text>
                        <Text size="xs" ff="monospace" style={{ overflowWrap: 'anywhere' }}>
                          {report.artifactHash}
                        </Text>
                      </>
                    )}
                    {report.checks.map((check) => (
                      <Stack gap={4} key={check.key}>
                        <Group gap="xs">
                          <Badge
                            color={check.status === 'passed' ? 'teal' : check.status === 'failed' ? 'red' : 'blue'}
                          >
                            {t(`updates.checks.${check.status}`)}
                          </Badge>
                          <Text size="sm" fw={500}>
                            {check.label}
                          </Text>
                        </Group>
                        <Text size="sm" c="dimmed" style={{ overflowWrap: 'anywhere' }}>
                          {check.message}
                        </Text>
                      </Stack>
                    ))}
                    {hasExpiry ? (
                      <Text size="sm" c={expired ? 'orange' : 'dimmed'}>
                        {expired
                          ? t('updates.expired')
                          : t('updates.expires', { time: new Date(expiresAt).toLocaleString() })}
                      </Text>
                    ) : (
                      (data.phase === 'ready' || report.canApply) && (
                        <Text size="sm" c="dimmed">
                          {t('updates.needsValidation')}
                        </Text>
                      )
                    )}
                  </>
                ) : (
                  <Text c="dimmed" size="sm">
                    {t('updates.needsValidation')}
                  </Text>
                ))}
              {data.targetSha && (
                <Text size="sm" style={{ overflowWrap: 'anywhere' }}>
                  {t('updates.target')}: {data.targetSha}
                </Text>
              )}
              {data.backupPath && (
                <Text size="sm" style={{ overflowWrap: 'anywhere' }}>
                  {t('updates.backup')}: {data.backupPath}
                </Text>
              )}
              {data.changedAtUtc && (
                <Text size="sm" c="dimmed">
                  {t('updates.changedAt')}: {new Date(data.changedAtUtc).toLocaleString()}
                </Text>
              )}
            </Stack>
          </Paper>
        )}
        <Alert color="orange">{t('updates.warning')}</Alert>
        <Group justify="flex-end">
          <Button color="orange" disabled={!canApply} loading={operation === 'applying'} onClick={confirmUpdate}>
            {t('updates.apply')}
          </Button>
        </Group>
      </Stack>
    </AdminPage>
  )
}

export default Updates
