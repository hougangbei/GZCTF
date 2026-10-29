import { Button, Group, SegmentedControl, Text } from '@mantine/core'
import { useDeferredValue, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Markdown } from '@Components/MarkdownRenderer'
import { insertMarkdown } from '@Utils/lessonMarkdown'
import api from '@Api'
import classes from './LessonEditor.module.css'

type Props = {
  title: string
  value: string
  onChange: (value: string) => void
  disabled: boolean
  onBusyChange?: (busy: boolean) => void
}
export const LessonMarkdownEditor = ({ title, value, onChange, disabled, onBusyChange }: Props) => {
  const { t } = useTranslation('lessonWorkspace')
  const previewValue = useDeferredValue(value)
  const [mode, setMode] = useState('split')
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState('')
  const textarea = useRef<HTMLTextAreaElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const latestValue = useRef(value)
  const latestChange = useRef(onChange)
  const mounted = useRef(true)
  latestValue.current = value
  latestChange.current = onChange
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const insert = (before: string, after = '', placeholder = '') => {
    if (disabled || uploading) return
    const element = textarea.current
    const result = insertMarkdown(
      latestValue.current,
      element?.selectionStart ?? latestValue.current.length,
      element?.selectionEnd ?? latestValue.current.length,
      before,
      after,
      placeholder
    )
    onChange(result.value)
    requestAnimationFrame(() => {
      element?.focus()
      element?.setSelectionRange(result.start, result.end)
    })
  }
  const upload = async (files: File[]) => {
    if (!files.length || uploading || disabled) return
    if (
      files.some(
        (file) =>
          !['image/png', 'image/jpeg', 'image/gif', 'image/webp'].includes(file.type) || file.size > 10 * 1024 * 1024
      )
    ) {
      setError(t('imageLimit'))
      return
    }
    setUploading(true)
    onBusyChange?.(true)
    setError('')
    // Keep the insertion point stable while the request is in flight.
    const position = textarea.current?.selectionStart ?? latestValue.current.length
    try {
      const response = await api.assets.assetsUpload({ files })
      if (!mounted.current) return
      if (!response.data.length || response.data.some((file) => !file.hash)) throw new Error('Missing upload result')
      const markdown = response.data
        .map(
          (file) =>
            `![${file.name.replace(/[\[\]\\\r\n]/g, '')}](/assets/${encodeURIComponent(file.hash!)}/${encodeURIComponent(file.name)})`
        )
        .join('\n')
      latestChange.current(
        latestValue.current.slice(0, position) + '\n' + markdown + '\n' + latestValue.current.slice(position)
      )
    } catch {
      if (mounted.current) setError(t('imageFailed'))
    } finally {
      if (mounted.current) {
        setUploading(false)
        onBusyChange?.(false)
      }
    }
  }

  return (
    <section className={classes.composer}>
      <Group justify="space-between" className={classes.modebar}>
        <Text fw={600} size="sm">
          {t('content')}
        </Text>
        <SegmentedControl
          size="xs"
          value={mode}
          onChange={setMode}
          data={[
            { value: 'edit', label: t('editMode') },
            { value: 'split', label: t('splitMode') },
            { value: 'preview', label: t('previewMode') },
          ]}
        />
      </Group>
      {mode !== 'preview' && (
        <Group gap={5} className={classes.toolbar} aria-label={t('formatting')}>
          {[
            ['heading', '\n## ', '', t('headingPlaceholder')],
            ['bold', '**', '**', t('textPlaceholder')],
            ['italic', '*', '*', t('textPlaceholder')],
            ['list', '\n- ', '', t('textPlaceholder')],
            ['quote', '\n> ', '', t('textPlaceholder')],
            ['link', '[', '](https://example.com)', t('textPlaceholder')],
            ['code', '\n```text\n', '\n```\n', t('codePlaceholder')],
            ['table', '\n| ', ' |\n| --- | --- |\n|  |  |\n', t('tablePlaceholder')],
          ].map(([name, before, after, placeholder]) => (
            <Button
              key={name}
              size="compact-xs"
              variant="subtle"
              disabled={disabled || uploading}
              onClick={() => insert(before, after, placeholder)}
            >
              {t(name)}
            </Button>
          ))}
          <Button
            size="compact-xs"
            variant="light"
            disabled={disabled}
            loading={uploading}
            onClick={() => fileInput.current?.click()}
          >
            {t('insertImage')}
          </Button>
          <input
            ref={fileInput}
            hidden
            type="file"
            multiple
            accept="image/png,image/jpeg,image/gif,image/webp"
            aria-label={t('insertImage')}
            onChange={(event) => {
              void upload(Array.from(event.target.files ?? []))
              event.target.value = ''
            }}
          />
        </Group>
      )}
      {error && (
        <Text c="red" role="alert" p="sm">
          {error}
        </Text>
      )}
      <div className={classes.panes} data-mode={mode}>
        {mode !== 'preview' && (
          <textarea
            ref={textarea}
            className={classes.source}
            aria-label={t('markdownBody')}
            value={value}
            spellCheck={false}
            disabled={disabled || uploading}
            onChange={(event) => onChange(event.target.value)}
            onPaste={(event) => {
              const files = Array.from(event.clipboardData.files)
              if (files.length) {
                event.preventDefault()
                void upload(files)
              }
            }}
            onDragOver={(event) => {
              if (event.dataTransfer.types.includes('Files')) event.preventDefault()
            }}
            onDrop={(event) => {
              if (event.dataTransfer.files.length) {
                event.preventDefault()
                void upload(Array.from(event.dataTransfer.files))
              }
            }}
          />
        )}
        {mode !== 'edit' && (
          <section className={classes.preview} aria-label={t('studentPreview')}>
            <p className={classes.previewLabel}>{t('studentPreview')}</p>
            <h1>{title || t('untitled')}</h1>
            <Markdown source={previewValue} />
          </section>
        )}
      </div>
      <Text size="xs" c="dimmed" p="sm">
        {t('imageHint')}
      </Text>
    </section>
  )
}
