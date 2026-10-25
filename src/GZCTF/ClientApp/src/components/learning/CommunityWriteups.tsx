import {
  Alert,
  Anchor,
  Badge,
  Button,
  FileInput,
  Group,
  Skeleton,
  Stack,
  Tabs,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { mdiFilePdfBox, mdiOpenInNew, mdiUpload } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { ContentType, fetcher } from '@Api'
import classes from './CommunityWriteups.module.css'
import type { WriteupRequest } from './WriteupActions'

export type CommunityWriteup = {
  id: string
  title: string
  authorName: string
  fileSize: number
  createdAtUtc: string
}
const MAX_PDF_BYTES = 20 * 1024 * 1024
const fileSize = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`

type Props = { challengeId: string; request?: WriteupRequest }

export const CommunityWriteups = (props: Props) => <WriteupPanel key={props.challengeId} {...props} />

const WriteupPanel = ({ challengeId, request }: Props) => {
  const { t } = useTranslation('skillTrees')
  const [tab, setTab] = useState<string | null>('browse')
  const section = useRef<HTMLElement>(null)
  useEffect(() => {
    if (!request) return
    setTab(request.tab)
    const frame = requestAnimationFrame(() => {
      section.current?.scrollIntoView({ block: 'nearest' })
      section.current
        ?.querySelector<HTMLElement>(`[role=tab][data-value=${request.tab}]`)
        ?.focus({ preventScroll: true })
    })
    return () => cancelAnimationFrame(frame)
  }, [request])
  const [title, setTitle] = useState('')
  const [authorName, setAuthorName] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [fileError, setFileError] = useState<string>()
  const [pending, setPending] = useState(false)
  const inFlight = useRef(false)
  const [message, setMessage] = useState<string>()
  const [error, setError] = useState(false)
  const path = `/api/challenges/${challengeId}/community-writeups`
  const { data: writeups, error: loadError, isLoading, mutate } = useSWR<CommunityWriteup[]>(path, fetcher)

  const selectFile = (value: File | null) => {
    setFile(value)
    setMessage(undefined)
    setFileError(
      value && !value.name.toLowerCase().endsWith('.pdf')
        ? t('writeups.invalidPdf')
        : value && value.size > MAX_PDF_BYTES
          ? t('writeups.tooLarge')
          : value && value.size < 5
            ? t('writeups.invalidPdf')
            : undefined
    )
  }

  const submit = async () => {
    if (inFlight.current || !file || fileError || !title.trim() || !authorName.trim()) return
    inFlight.current = true
    setPending(true)
    setMessage(undefined)
    try {
      if ((await file.slice(0, 5).text()) !== '%PDF-') {
        setFileError(t('writeups.invalidPdf'))
        return
      }
      await api.request({
        path,
        method: 'POST',
        type: ContentType.FormData,
        format: 'json',
        body: { title: title.trim(), authorName: authorName.trim(), file },
      })
      setTitle('')
      setAuthorName('')
      setFile(null)
      setError(false)
      setMessage(t('writeups.pendingReview'))
      // Pending submissions are private: no public-list refresh is needed here.
    } catch (cause) {
      setError(true)
      setMessage(
        (cause as { response?: { status?: number } })?.response?.status === 429
          ? t('writeups.rateLimited')
          : t('writeups.submitFailed')
      )
    } finally {
      inFlight.current = false
      setPending(false)
    }
  }

  return (
    <section ref={section} className={classes.section} aria-label={t('writeups.title')}>
      <div className={classes.heading}>
        <Title order={3} className={classes.title}>
          {t('writeups.title')}
        </Title>
        <Text size="sm" c="dimmed">
          {t('writeups.description')}
        </Text>
      </div>
      <Tabs value={tab} onChange={setTab} keepMounted={false}>
        <Tabs.List className={classes.tabs}>
          <Tabs.Tab value="browse" data-value="browse">
            {t('writeups.browse')}
          </Tabs.Tab>
          <Tabs.Tab value="submit" data-value="submit" leftSection={<Icon path={mdiUpload} size={0.7} />}>
            {t('writeups.submit')}
          </Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="browse" pt="lg">
          {loadError ? (
            <Alert color="red" role="alert">
              <Group justify="space-between" gap="sm">
                <Text size="sm">{t('writeups.loadFailed')}</Text>
                <Button size="xs" variant="light" color="red" onClick={() => void mutate().catch(() => undefined)}>
                  {t('writeups.retry')}
                </Button>
              </Group>
            </Alert>
          ) : isLoading ? (
            <Stack gap="sm" aria-label={t('writeups.loading')}>
              <Skeleton h={64} />
              <Skeleton h={64} />
            </Stack>
          ) : writeups?.length === 0 ? (
            <div className={classes.empty}>
              <Icon path={mdiFilePdfBox} size={1.4} aria-hidden />
              <Text size="sm" fw={500}>
                {t('writeups.empty')}
              </Text>
              <Text size="sm" c="dimmed">
                {t('writeups.emptyHint')}
              </Text>
              <Button variant="light" onClick={() => setTab('submit')}>
                {t('writeups.submit')}
              </Button>
            </div>
          ) : (
            <ul className={classes.list}>
              {writeups?.map((writeup) => (
                <li key={writeup.id} className={classes.row}>
                  <div className={classes.document}>
                    <Icon path={mdiFilePdfBox} size={1} aria-hidden />
                  </div>
                  <div className={classes.details}>
                    <Anchor
                      className={classes.link}
                      href={`${path}/${writeup.id}/pdf`}
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      {writeup.title}
                    </Anchor>
                    <Text size="xs" c="dimmed" className={classes.metadata}>
                      {t('writeups.by', { author: writeup.authorName })} · PDF · {fileSize(writeup.fileSize)}
                    </Text>
                  </div>
                  <Icon path={mdiOpenInNew} size={0.7} className={classes.external} aria-hidden />
                </li>
              ))}
            </ul>
          )}
        </Tabs.Panel>
        <Tabs.Panel value="submit" pt="lg">
          <form
            onSubmit={(event) => {
              event.preventDefault()
              void submit()
            }}
            className={classes.form}
            aria-busy={pending}
          >
            <Text size="sm" c="dimmed" className={classes.hint}>
              {t('writeups.submitHint')}
            </Text>
            <div className={classes.fields}>
              <TextInput
                label={t('writeups.wpTitle')}
                placeholder={t('writeups.titlePlaceholder')}
                required
                value={title}
                maxLength={160}
                disabled={pending}
                onChange={(event) => setTitle(event.currentTarget.value)}
              />
              <TextInput
                label={t('writeups.author')}
                placeholder={t('writeups.authorPlaceholder')}
                required
                value={authorName}
                maxLength={80}
                disabled={pending}
                onChange={(event) => setAuthorName(event.currentTarget.value)}
              />
            </div>
            <div className={classes.upload}>
              <FileInput
                label={t('writeups.pdf')}
                description={t('writeups.fileHint')}
                placeholder={t('writeups.selectPdf')}
                leftSection={<Icon path={mdiFilePdfBox} size={0.9} />}
                accept=".pdf,application/pdf"
                value={file}
                required
                error={fileError}
                disabled={pending}
                onChange={selectFile}
                clearable
              />
              {file && !fileError && (
                <Text size="xs" c="dimmed" mt={8}>
                  {fileSize(file.size)}
                </Text>
              )}
            </div>
            {message && (
              <Alert color={error ? 'red' : 'teal'} role={error ? 'alert' : 'status'}>
                {message}
              </Alert>
            )}
            <div className={classes.actions}>
              <Badge variant="light" color="gray" size="sm">
                {t('writeups.reviewRequired')}
              </Badge>
              <Button
                type="submit"
                loading={pending}
                disabled={!file || !!fileError || !title.trim() || !authorName.trim()}
                leftSection={<Icon path={mdiUpload} size={0.8} />}
              >
                {t('writeups.submit')}
              </Button>
            </div>
          </form>
        </Tabs.Panel>
      </Tabs>
    </section>
  )
}
