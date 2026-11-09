import {
  Alert,
  Badge,
  Button,
  Checkbox,
  Group,
  Modal,
  Paper,
  Progress,
  Radio,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import { createEditorSessionId } from '@Utils/adminTestSession'
import {
  applicablePackage,
  checksumValue,
  offlineBlocked,
  offlineBusy,
  validPackageName,
  type OfflinePackage,
  type OfflineSettings,
  type OfflineStatus,
} from '@Utils/offlineUpdates'
import api, { ContentType, fetcher } from '@Api'
import classes from './OfflinePackageUpdates.module.css'

const endpoint = '/api/admin/updates/packages'
async function request<T>(path: string, body?: unknown, method = 'POST'): Promise<T> {
  const response = await api.request<T>({ path, method, type: ContentType.Json, body, format: 'json' })
  return response.data
}
function message(error: unknown, fallback: string) {
  const e = error as { response?: { data?: { message?: string } }; error?: { message?: string }; message?: string }
  return e.response?.data?.message ?? e.error?.message ?? (error instanceof Error ? error.message : fallback)
}

export const OfflinePackageUpdates = () => {
  const { t } = useTranslation('admin')
  const tr = (key: string) => t(`offlineUpdates.${key}`)
  const {
    data: status,
    error: statusError,
    mutate: refreshStatus,
  } = useSWR<OfflineStatus>('/api/admin/updates', fetcher, { refreshInterval: 2000 })
  const {
    data: settings,
    error: settingsError,
    mutate: refreshSettings,
  } = useSWR<OfflineSettings>(`${endpoint}/settings`, fetcher)
  const {
    data: packages,
    error: packagesError,
    mutate: refreshPackages,
  } = useSWR<{ directory: string; items: OfflinePackage[] }>(endpoint, fetcher)
  const [directory, setDirectory] = useState('')
  const initialized = useRef(false)
  useEffect(() => {
    if (settings && !initialized.current) {
      setDirectory(settings.directory)
      initialized.current = true
    }
  }, [settings])
  const [selected, setSelected] = useState<string>()
  const [uploaded, setUploaded] = useState<OfflinePackage>()
  const [operation, setOperation] = useState<string>()
  const [error, setError] = useState<string>()
  const [progress, setProgress] = useState(0)
  const [confirm, setConfirm] = useState(false)
  const [maintenance, setMaintenance] = useState(false)
  const [now, setNow] = useState(Date.now())
  const [invalidReports, setInvalidReports] = useState<string[]>([])
  const upload = useRef<XMLHttpRequest | undefined>(undefined)
  const fileInput = useRef<HTMLInputElement>(null)
  const checksumInput = useRef<HTMLInputElement>(null)
  const retry = useRef<{ validationId: string; operationId: string } | undefined>(undefined)
  const pendingConfirmation = useRef<{ validationId: string; packageId: string } | undefined>(undefined)
  const operationInFlight = useRef(false)
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])
  useEffect(() => () => upload.current?.abort(), [])
  const active = !!operation || offlineBusy(status?.phase)
  const blocked = offlineBlocked(status?.phase)
  const items = packages?.items ?? []
  const item = items.find((p) => p.id === selected) ?? (uploaded?.id === selected ? uploaded : undefined)
  const report = status?.validation && status.validation.packageId === selected ? status.validation : undefined
  const directoryDirty = !!settings && directory !== settings.directory
  const disconnected = !!statusError || !!settingsError || !!packagesError
  const canApply =
    !active &&
    !blocked &&
    !directoryDirty &&
    !disconnected &&
    !!item &&
    applicablePackage(status, selected, now) &&
    !invalidReports.includes(report!.id)
  const canConfirm =
    canApply &&
    report?.id === pendingConfirmation.current?.validationId &&
    selected === pendingConfirmation.current?.packageId
  const eligibility = useRef({ canApply, selected, report })
  eligibility.current = { canApply, selected, report }
  const invalidate = () => {
    if (status?.validation?.id) setInvalidReports((ids) => [...ids, status.validation!.id])
  }
  const run = async (name: string, action: () => Promise<void>) => {
    if (operationInFlight.current) return
    operationInFlight.current = true
    setOperation(name)
    setError(undefined)
    try {
      await action()
    } catch (e) {
      setError(message(e, tr('operationFailed')))
    } finally {
      operationInFlight.current = false
      setOperation(undefined)
    }
  }
  const saveDirectory = () =>
    run('directory', async () => {
      invalidate()
      const next = await request<OfflineSettings>(`${endpoint}/settings`, { directory }, 'PUT')
      setDirectory(next.directory)
      setSelected(undefined)
      await refreshSettings(next, false)
      await refreshPackages()
      await refreshStatus()
    })
  const scan = () =>
    run('scan', async () => {
      invalidate()
      await refreshPackages()
      await refreshStatus()
    })

  const attachChecksum = async (file: File, packageId: string) => {
    invalidate()
    if (file.size > 4096) throw new Error(tr('invalidChecksum'))
    const sha256 = checksumValue(await file.text())
    if (!sha256) throw new Error(tr('invalidChecksum'))
    await request(`${endpoint}/${packageId}/checksum`, { sha256 })
    await refreshPackages()
    await refreshStatus()
  }
  const receiveFile = (file: File): Promise<OfflinePackage> =>
    new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()
      upload.current = xhr
      xhr.open('POST', `${endpoint}/upload`)
      xhr.withCredentials = true
      xhr.timeout = 15 * 60 * 1000
      xhr.setRequestHeader('Content-Type', 'application/octet-stream')
      xhr.setRequestHeader('X-File-Name', encodeURIComponent(file.name))
      xhr.upload.onprogress = (event) => {
        if (event.lengthComputable) setProgress(Math.round((event.loaded / event.total) * 100))
      }
      xhr.onload = () => {
        upload.current = undefined
        let data: OfflinePackage & { message?: string }
        try {
          data = JSON.parse(xhr.responseText)
        } catch {
          reject(new Error(tr('operationFailed')))
          return
        }
        if (xhr.status >= 200 && xhr.status < 300) resolve(data)
        else reject(new Error(data.message ?? tr('operationFailed')))
      }
      xhr.onerror = xhr.ontimeout = () => {
        upload.current = undefined
        reject(new Error(tr('uploadFailed')))
      }
      xhr.onabort = () => {
        upload.current = undefined
        reject(new Error(tr('uploadCancelled')))
      }
      xhr.send(file)
    })
  const acceptFiles = (files: File[]) =>
    run('upload', async () => {
      const file = files.find((f) => validPackageName(f.name))
      if (!file) throw new Error(tr('invalidPackage'))
      if (!settings || !file.size || file.size > Math.min(settings.maxUploadBytes, 1073741824))
        throw new Error(tr('tooLarge'))
      invalidate()
      setProgress(0)
      const result = await receiveFile(file)
      setUploaded(result)
      setSelected(result.id)
      const checksum = files.find((f) => f !== file && f.name.endsWith('.sha256'))
      if (checksum) await attachChecksum(checksum, result.id)
      await refreshPackages()
      await refreshStatus()
    })
  const check = () =>
    run('validate', async () => {
      if (!selected) return
      invalidate()
      await request(`${endpoint}/${selected}/validate`, {})
      await refreshStatus()
    })
  const apply = async () => {
    const latest = eligibility.current
    if (
      !latest.canApply ||
      !latest.report ||
      !latest.selected ||
      !maintenance ||
      latest.report.id !== pendingConfirmation.current?.validationId ||
      latest.selected !== pendingConfirmation.current?.packageId ||
      Date.parse(latest.report.expiresAtUtc) <= Date.now()
    )
      return
    const validationId = latest.report.id,
      packageId = latest.selected
    if (retry.current?.validationId !== validationId)
      retry.current = { validationId, operationId: createEditorSessionId() }
    const operationId = retry.current.operationId
    setConfirm(false)
    await run('apply', async () => {
      await request(`${endpoint}/${packageId}/apply`, { validationId, operationId, maintenanceConfirmed: true })
      await refreshStatus()
    })
  }

  return (
    <Stack className={classes.workspace} gap="lg">
      <Group justify="space-between">
        <div>
          <Title order={2}>{tr('title')}</Title>
          <Text c="dimmed" size="sm">
            {tr('description')}
          </Text>
        </div>
        <Badge variant="light">linux/amd64</Badge>
      </Group>
      {disconnected && (
        <Alert color="yellow" role="status">
          {tr('reconnecting')}
        </Alert>
      )}
      {error && <Alert color="red">{error}</Alert>}
      {blocked && <Alert color="red">{tr('recoveryRequired')}</Alert>}
      <Paper className={classes.panel} withBorder p="md" radius="md">
        <Stack gap="xs">
          <Group justify="space-between">
            <Text fw={600}>{tr('status')}</Text>
            <Badge variant="light">
              {t(`offlineUpdates.phases.${status?.phase ?? 'idle'}`, { defaultValue: status?.phase })}
            </Badge>
          </Group>
          {status?.currentSha && (
            <Text size="sm" className={classes.hash}>
              {tr('current')} {status.currentSha}
            </Text>
          )}
          {status?.currentImage && (
            <Text size="sm" className={classes.hash}>
              {tr('currentImage')} {status.currentImage}
            </Text>
          )}
          {status?.message && (
            <Text size="sm" role="status">
              {status.message}
            </Text>
          )}
          {status?.backupPath && (
            <Text size="sm" className={classes.hash}>
              {tr('backup')} {status.backupPath}
            </Text>
          )}
          <Text size="sm" c="dimmed">
            {tr('serviceIndependent')}
          </Text>
        </Stack>
      </Paper>
      <div className={classes.sources}>
        <Paper className={classes.panel} withBorder p="lg" radius="md">
          <Stack>
            <Title order={3} size="h4">
              {tr('folder')}
            </Title>
            <TextInput
              label={tr('directory')}
              description={tr('directoryHelp')}
              value={directory}
              disabled={active || blocked || !settings}
              onChange={(e) => {
                setDirectory(e.currentTarget.value)
                invalidate()
              }}
            />
            {!!settings?.allowedRoots?.length && (
              <Text size="xs" c="dimmed">
                {tr('allowedRoots')} {settings.allowedRoots.join('、')}
              </Text>
            )}
            <Group>
              <Button
                variant="default"
                loading={operation === 'directory'}
                disabled={active || blocked || !directoryDirty || !directory.trim()}
                onClick={() => void saveDirectory()}
              >
                {tr('saveDirectory')}
              </Button>
              <Button
                variant="light"
                loading={operation === 'scan'}
                disabled={active || blocked || directoryDirty || !settings}
                onClick={() => void scan()}
              >
                {tr('scan')}
              </Button>
            </Group>
          </Stack>
        </Paper>
        <Paper className={classes.panel} withBorder p="lg" radius="md">
          <Stack>
            <Title order={3} size="h4">
              {tr('upload')}
            </Title>
            <div
              className={classes.drop}
              onDragOver={(e) => e.preventDefault()}
              onDrop={(e) => {
                e.preventDefault()
                if (!active && !blocked && settings) void acceptFiles(Array.from(e.dataTransfer.files))
              }}
            >
              <Stack gap="sm">
                <Text size="sm">{tr('dropHelp')}</Text>
                <Button
                  variant="default"
                  disabled={active || blocked || !settings}
                  onClick={() => fileInput.current?.click()}
                >
                  {tr('choosePackage')}
                </Button>
              </Stack>
            </div>
            <input
              hidden
              ref={fileInput}
              type="file"
              multiple
              accept=".tar.gz,.tgz,.tar,.zip,.sha256"
              aria-label={tr('choosePackage')}
              disabled={active || blocked || !settings}
              onChange={(e) => {
                const files = Array.from(e.currentTarget.files ?? [])
                e.currentTarget.value = ''
                if (files.length) void acceptFiles(files)
              }}
            />
            <Text size="xs" c="dimmed">
              {t('offlineUpdates.uploadLimit', {
                size: ((settings?.maxUploadBytes ?? 1073741824) / 1024 ** 2).toFixed(0),
              })}
            </Text>
            {operation === 'upload' && (
              <>
                <Progress aria-label={tr('progress')} value={progress} />
                <Group justify="space-between">
                  <Text size="sm">{progress}%</Text>
                  <Button variant="subtle" onClick={() => upload.current?.abort()}>
                    {tr('cancelUpload')}
                  </Button>
                </Group>
              </>
            )}
          </Stack>
        </Paper>
      </div>
      <Paper className={classes.panel} withBorder p="lg" radius="md">
        <Stack>
          <Title order={3} size="h4">
            {tr('packages')}
          </Title>
          {!items.length && !uploaded && <Text c="dimmed">{tr('empty')}</Text>}
          {[...items, ...(uploaded && !items.some((p) => p.id === uploaded.id) ? [uploaded] : [])].map((pkg) => (
            <div className={classes.package} key={pkg.id}>
              <Radio
                aria-label={pkg.name}
                checked={selected === pkg.id}
                disabled={active || blocked || directoryDirty}
                onChange={() => {
                  setSelected(pkg.id)
                  setError(undefined)
                }}
              />
              <Stack className={classes.details} gap={4}>
                <Text fw={600}>{pkg.name}</Text>
                <Text size="xs" c="dimmed">
                  {(pkg.size / 1024 ** 2).toFixed(1)} MiB ·{' '}
                  {tr(pkg.source === 'upload' ? 'fromUpload' : 'fromDirectory')} ·{' '}
                  {new Date(pkg.modifiedAtUtc).toLocaleString()}
                </Text>
                <Text size="xs" c={pkg.checksumProvided ? 'dimmed' : undefined}>
                  {tr(pkg.checksumProvided ? 'checksumProvided' : 'noChecksum')}
                </Text>
                {pkg.sha256 && <Text className={classes.hash}>{pkg.sha256}</Text>}
              </Stack>
            </div>
          ))}
          <Group>
            <Button
              variant="default"
              disabled={!selected || active || blocked || directoryDirty}
              onClick={() => checksumInput.current?.click()}
            >
              {tr('chooseChecksum')}
            </Button>
            <Text size="xs" c="dimmed">
              {tr('checksumHelp')}
            </Text>
          </Group>
          <input
            hidden
            ref={checksumInput}
            aria-label={tr('chooseChecksum')}
            type="file"
            accept=".sha256,.txt"
            disabled={!selected || active || blocked}
            onChange={(e) => {
              const file = e.currentTarget.files?.[0]
              e.currentTarget.value = ''
              if (file && selected) void run('checksum', () => attachChecksum(file, selected))
            }}
          />
          <Group justify="flex-end">
            <Button
              variant="default"
              loading={operation === 'validate' || status?.phase === 'validating'}
              disabled={!item || active || blocked || directoryDirty || disconnected}
              onClick={() => void check()}
            >
              {tr('check')}
            </Button>
            <Button
              disabled={!canApply}
              loading={operation === 'apply'}
              onClick={() => {
                pendingConfirmation.current = { validationId: report!.id, packageId: selected! }
                setMaintenance(false)
                setConfirm(true)
              }}
            >
              {tr('apply')}
            </Button>
          </Group>
        </Stack>
      </Paper>
      {report && (
        <Paper className={classes.panel} withBorder p="lg" radius="md">
          <Stack gap="sm">
            <Title order={3} size="h4">
              {tr('report')}
            </Title>
            <Text fw={600}>{report.version}</Text>
            <Text className={classes.hash}>{report.artifactHash}</Text>
            {report.checks.map((check) => (
              <div key={check.key}>
                <Group>
                  <Badge
                    variant="light"
                    color={check.status === 'failed' ? 'red' : check.status === 'passed' ? 'green' : undefined}
                  >
                    {tr(check.status)}
                  </Badge>
                  <Text fw={500} size="sm">
                    {check.label}
                  </Text>
                </Group>
                <Text size="sm" mt={4}>
                  {check.message}
                </Text>
              </div>
            ))}
            {Date.parse(report.expiresAtUtc) <= now && <Alert color="yellow">{tr('expired')}</Alert>}
          </Stack>
        </Paper>
      )}
      <Modal opened={confirm} onClose={() => setConfirm(false)} title={tr('confirmTitle')} centered>
        <Stack>
          <Text size="sm">{tr('confirmHelp')}</Text>
          <Text fw={600}>{item?.name}</Text>
          <Text size="sm">
            {status?.currentSha ?? tr('unknownVersion')} → {report?.version}
          </Text>
          {status?.currentImage && (
            <Text size="sm" className={classes.hash}>
              {tr('currentImage')} {status.currentImage}
            </Text>
          )}
          {report?.targetImage && (
            <Text size="sm" className={classes.hash}>
              {tr('targetImage')} {report.targetImage}
            </Text>
          )}
          {status?.backupDirectory && (
            <Text size="sm" className={classes.hash}>
              {tr('backupDirectory')} {status.backupDirectory}
            </Text>
          )}
          <Checkbox
            label={tr('maintenance')}
            checked={maintenance}
            onChange={(e) => setMaintenance(e.currentTarget.checked)}
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setConfirm(false)}>
              {tr('cancel')}
            </Button>
            <Button disabled={!maintenance || !canConfirm} onClick={() => void apply()}>
              {tr('confirm')}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
