import { Alert, Anchor, Button, Divider, FileInput, Group, Paper, Stack, Text, TextInput, Title } from '@mantine/core'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { ContentType, fetcher } from '@Api'

type CommunityWriteup = {
  id: string
  title: string
  authorName: string
  fileSize: number
  createdAtUtc: string
}

export const CommunityWriteups = ({ challengeId }: { challengeId: string }) => {
  const { t } = useTranslation('skillTrees')
  const [open, setOpen] = useState(false)
  const [title, setTitle] = useState('')
  const [authorName, setAuthorName] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState<string>()
  const [error, setError] = useState(false)
  const path = `/api/challenges/${challengeId}/community-writeups`
  const { data: writeups, error: loadError, mutate } = useSWR<CommunityWriteup[]>(open ? path : null, fetcher)

  const submit = async () => {
    if (!file || !title.trim() || !authorName.trim()) return
    setPending(true)
    setMessage(undefined)
    try {
      const form = new FormData()
      form.append('title', title.trim())
      form.append('authorName', authorName.trim())
      form.append('file', file)
      await api.request({ path, method: 'POST', body: form, type: ContentType.FormData, format: 'json' })
      setTitle('')
      setAuthorName('')
      setFile(null)
      setError(false)
      setMessage(t('writeups.pendingReview'))
      await mutate()
    } catch {
      setError(true)
      setMessage(t('writeups.submitFailed'))
    } finally {
      setPending(false)
    }
  }

  return (
    <Stack gap="sm">
      <Group justify="space-between">
        <Title order={3}>{t('writeups.title')}</Title>
        <Button variant="light" onClick={() => setOpen((value) => !value)}>
          {t('writeups.view')}
        </Button>
      </Group>
      {open && (
        <Stack gap="md">
          {loadError ? <Text c="red">{t('writeups.loadFailed')}</Text> : null}
          {writeups?.length === 0 && <Text c="dimmed">{t('writeups.empty')}</Text>}
          {writeups?.map((writeup) => (
            <Paper key={writeup.id} withBorder p="sm" radius="md">
              <Anchor href={`${path}/${writeup.id}/pdf`} target="_blank" rel="noopener noreferrer">
                {writeup.title}
              </Anchor>
              <Text size="xs" c="dimmed">{t('writeups.by', { author: writeup.authorName })}</Text>
            </Paper>
          ))}
          <Divider />
          <Title order={4}>{t('writeups.submit')}</Title>
          <Text size="sm" c="dimmed">{t('writeups.submitHint')}</Text>
          <TextInput label={t('writeups.wpTitle')} value={title} maxLength={160}
            onChange={(event) => setTitle(event.currentTarget.value)} />
          <TextInput label={t('writeups.author')} value={authorName} maxLength={80}
            onChange={(event) => setAuthorName(event.currentTarget.value)} />
          <FileInput label={t('writeups.pdf')} accept=".pdf,application/pdf" value={file}
            onChange={setFile} clearable />
          <Button loading={pending} disabled={!file || !title.trim() || !authorName.trim()}
            onClick={() => void submit()}>{t('writeups.submit')}</Button>
          {message && <Alert color={error ? 'red' : 'teal'}>{message}</Alert>}
        </Stack>
      )}
    </Stack>
  )
}
