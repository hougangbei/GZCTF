import { Alert, Badge, Button, Group, Select, Text, TextInput, Title } from '@mantine/core'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import type { KeyedMutator } from 'swr'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { ContentPublishModal } from '@Components/admin/library/ContentPublishModal'
import classes from '@Components/admin/library/LessonEditor.module.css'
import { LessonMarkdownEditor } from '@Components/admin/library/LessonMarkdownEditor'
import { mergeLessonLocalization } from '@Utils/LearningAdmin'
import { useAdminSkillCategories } from '@Hooks/useSkillTreeAdmin'
import { useUser } from '@Hooks/useUser'
import api, { Role, type LessonResponse } from '@Api'

type Draft = { title: string; body: string }
type StoredDraft = Draft & { updatedAt: number }
const sameDraft = (first: Draft, second: Draft) => first.title === second.title && first.body === second.body

// This session is remounted for every account, lesson and locale. No pending edit or upload
// can accidentally become the initial state of another localization.
const LessonEditSession = ({
  id,
  userId,
  locale,
  data,
  mutate,
  onLocaleChange,
}: {
  id: string
  userId: string
  locale: string
  data: LessonResponse
  mutate: KeyedMutator<LessonResponse>
  onLocaleChange: (locale: string) => void
}) => {
  const { t } = useTranslation('lessonWorkspace')
  const { t: tSkillTrees } = useTranslation('skillTrees')
  const { data: categories } = useAdminSkillCategories()
  const localization = data.localizations?.find((item) => item.locale?.toLowerCase() === locale.toLowerCase())
  const serverDraft = {
    title: localization?.title ?? (data.locale?.toLowerCase() === locale.toLowerCase() ? data.title : '') ?? '',
    body: localization?.body ?? (data.locale?.toLowerCase() === locale.toLowerCase() ? data.body : '') ?? '',
  }
  const draftKey = `lesson-draft:v1:${userId}:${id}:${locale}`
  const [draft, setDraft] = useState<Draft>(serverDraft)
  const [saved, setSaved] = useState<Draft>(serverDraft)
  const [stored] = useState(() => {
    try {
      const value = JSON.parse(localStorage.getItem(draftKey) ?? 'null') as StoredDraft | null
      return {
        draft:
          value && typeof value.title === 'string' && typeof value.body === 'string' && !sameDraft(value, serverDraft)
            ? value
            : null,
        failed: false,
      }
    } catch {
      return { draft: null, failed: true }
    }
  })
  const [recovery, setRecovery] = useState<StoredDraft | null>(stored.draft)
  const [storageFailed, setStorageFailed] = useState(stored.failed)
  const [saving, setSaving] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [saveFailed, setSaveFailed] = useState(false)
  const [hasSaved, setHasSaved] = useState(false)
  const [publishOpen, setPublishOpen] = useState(false)
  const [publishRowVersion, setPublishRowVersion] = useState<number>()
  const dirty = !sameDraft(draft, saved)
  const saveLock = useRef(false)
  const uploadLock = useRef(false)
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  const storeDraft = (next: Draft) => {
    try {
      localStorage.setItem(draftKey, JSON.stringify({ ...next, updatedAt: Date.now() }))
      setStorageFailed(false)
    } catch {
      setStorageFailed(true)
    }
  }
  const updateDraft = (next: Draft) => {
    setDraft(next)
    setHasSaved(false)
    setSaveFailed(false)
    storeDraft(next)
  }
  const removeDraft = () => {
    try {
      localStorage.removeItem(draftKey)
      setStorageFailed(false)
    } catch {
      setStorageFailed(true)
    }
  }

  const save = async (): Promise<number | undefined> => {
    if (saveLock.current || uploadLock.current || recovery || !draft.title.trim()) return undefined
    saveLock.current = true
    setSaving(true)
    setSaveFailed(false)
    const snapshot = { ...draft, title: draft.title.trim() }
    let storageSnapshot: string | null | undefined
    try {
      storageSnapshot = localStorage.getItem(draftKey)
    } catch {
      setStorageFailed(true)
    }
    try {
      const response = await api.adminLessons.adminLessonsUpdate(id, {
        locale,
        localizations: mergeLessonLocalization(data.localizations ?? [], { locale, ...snapshot }),
      })
      // A newer session or another tab may have replaced this draft while the request ran.
      // Remove only the exact stored revision that this save submitted.
      try {
        if (storageSnapshot && localStorage.getItem(draftKey) === storageSnapshot) {
          const storedSnapshot = JSON.parse(storageSnapshot) as Draft
          if (
            typeof storedSnapshot.title === 'string' &&
            storedSnapshot.title.trim() === snapshot.title &&
            storedSnapshot.body === snapshot.body
          ) {
            localStorage.removeItem(draftKey)
          }
        }
      } catch {
        if (mounted.current) setStorageFailed(true)
      }
      if (mounted.current) {
        setDraft(snapshot)
        setSaved(snapshot)
        setHasSaved(true)
        setPublishRowVersion(response.data.publication?.rowVersion)
      }
      await mutate(response.data, { revalidate: false })
      return response.data.publication?.rowVersion
    } catch {
      if (mounted.current) {
        setSaveFailed(true)
        // Edits were already stored synchronously. Never overwrite a newer local draft here.
      }
      return undefined
    } finally {
      saveLock.current = false
      if (mounted.current) setSaving(false)
    }
  }
  const saveRef = useRef(save)
  saveRef.current = save
  useEffect(() => {
    const keyboard = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
        event.preventDefault()
        void saveRef.current()
      }
    }
    window.addEventListener('keydown', keyboard)
    return () => window.removeEventListener('keydown', keyboard)
  }, [])
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => {
      if (!dirty) return
      event.preventDefault()
      event.returnValue = ''
    }
    const navigate = (event: MouseEvent) => {
      if (
        !dirty ||
        event.defaultPrevented ||
        event.button !== 0 ||
        event.ctrlKey ||
        event.metaKey ||
        event.shiftKey ||
        event.altKey
      )
        return
      const anchor = (event.target as Element).closest('a[href]')
      if (!(anchor instanceof HTMLAnchorElement) || anchor.target === '_blank' || anchor.hasAttribute('download'))
        return
      const target = new URL(anchor.href, location.href)
      if (
        target.origin !== location.origin ||
        (target.pathname === location.pathname && target.search === location.search)
      )
        return
      if (
        !window.confirm(
          t('leaveConfirm', {
            defaultValue: 'You have unsaved changes. Leave this page? Your local draft will remain on this device.',
          })
        )
      ) {
        event.preventDefault()
        event.stopPropagation()
      }
    }
    window.addEventListener('beforeunload', beforeUnload)
    document.addEventListener('click', navigate, true)
    return () => {
      window.removeEventListener('beforeunload', beforeUnload)
      document.removeEventListener('click', navigate, true)
    }
  }, [dirty, t])

  const locales = Array.from(
    new Set([
      'en',
      // Learner requests use language-only locale codes (useLanguage().locale).
      'zh',
      ...(data.localizations ?? []).flatMap((item) => (item.locale ? [item.locale] : [])),
      locale,
    ])
  )
  const published = data.publication?.publicationState === 'Published'
  return (
    <div className={classes.workspace}>
      <header className={classes.header}>
        <div>
          <Title order={1}>{t('editLesson', { defaultValue: 'Edit lesson' })}</Title>
          <Text size="sm" c="dimmed" role="status">
            {saving
              ? t('saving', { defaultValue: 'Saving…' })
              : dirty
                ? t('unsaved', { defaultValue: 'Unsaved changes' })
                : hasSaved
                  ? t('saved', { defaultValue: 'Saved' })
                  : ''}
          </Text>
        </div>
        <Group gap="sm">
          <Badge color={published ? 'green' : 'gray'} variant="light">
            {tSkillTrees(`state.${(data.publication?.publicationState ?? 'Draft').toLowerCase()}`)}
          </Badge>
          <Button
            loading={saving}
            disabled={uploading || !!recovery || !draft.title.trim()}
            onClick={() => void save()}
          >
            {t('save', { defaultValue: 'Save' })}
          </Button>
          <Button
            variant="light"
            disabled={saving || uploading || !!recovery || !draft.title.trim()}
            onClick={async () => {
              const version = await save()
              if (version !== undefined && mounted.current) {
                setPublishRowVersion(version)
                setPublishOpen(true)
              }
            }}
          >
            {t('publish', { defaultValue: 'Publish lesson' })}
          </Button>
        </Group>
      </header>
      {published && (
        <Alert color="orange" mb="md">
          {t('publishedWarning', {
            defaultValue: 'This lesson is published. Saving changes updates the content students can see.',
          })}
        </Alert>
      )}
      {storageFailed && (
        <Alert color="orange" mb="md" role="alert">
          {t('storageFailed', {
            defaultValue:
              'This device cannot store a local draft. Keep this page open and save your changes to the server.',
          })}
        </Alert>
      )}
      {saveFailed && (
        <Alert color="red" mb="md" role="alert">
          {t('saveFailed', { defaultValue: 'Could not save. Your edits are still here.' })}
        </Alert>
      )}
      {recovery && (
        <Alert
          className={classes.recovery}
          title={t('localDraftFound', { defaultValue: 'A local draft is available on this device.' })}
        >
          <Text size="sm" mb="sm">
            {t('localDraftHint', {
              defaultValue: 'Recover it to continue editing, or discard it to use the server copy.',
            })}
          </Text>
          <Group>
            <Button
              size="xs"
              onClick={() => {
                updateDraft(recovery)
                setRecovery(null)
              }}
            >
              {t('recoverDraft', { defaultValue: 'Recover local draft' })}
            </Button>
            <Button
              size="xs"
              variant="default"
              onClick={() => {
                removeDraft()
                setRecovery(null)
              }}
            >
              {t('discardDraft', { defaultValue: 'Discard local draft' })}
            </Button>
          </Group>
        </Alert>
      )}
      <div className={classes.body}>
        <div className={classes.main}>
          <TextInput
            label={t('lessonTitle', { defaultValue: 'Lesson title' })}
            value={draft.title}
            disabled={saving || uploading || !!recovery}
            onChange={(event) => updateDraft({ ...draft, title: event.currentTarget.value })}
          />
          <LessonMarkdownEditor
            key={`${userId}:${id}:${locale}`}
            title={draft.title}
            value={draft.body}
            onChange={(body) => updateDraft({ ...draft, body })}
            disabled={saving || !!recovery}
            onBusyChange={(busy) => {
              uploadLock.current = busy
              setUploading(busy)
            }}
          />
        </div>
        <aside className={classes.settings}>
          <h2>{t('settings', { defaultValue: 'Lesson settings' })}</h2>
          <Select
            label={t('locale', { defaultValue: 'Content language' })}
            value={locale}
            data={locales}
            allowDeselect={false}
            disabled={saving || uploading}
            onChange={(value) => {
              if (
                value &&
                value !== locale &&
                (!dirty ||
                  window.confirm(
                    t('localeConfirm', {
                      defaultValue: 'Switch language? Unsaved changes will remain in a local draft for this language.',
                    })
                  ))
              )
                onLocaleChange(value)
            }}
          />
          <Text size="xs" c="dimmed">
            {t('categories', { defaultValue: 'Skill categories' })}
          </Text>
          <Text size="sm">
            {(data.publication?.categoryIds ?? [])
              .map(
                (id) =>
                  categories?.find((category) => category.categoryId === id)?.name || t('library.categoryUnavailable')
              )
              .join(' · ') || t('library.unassigned')}
          </Text>
          <Link to="/admin/skill-categories">{t('manageCategories')}</Link>
        </aside>
      </div>
      <ContentPublishModal
        opened={publishOpen}
        kind="lesson"
        contentId={id}
        rowVersion={publishRowVersion ?? data.publication?.rowVersion ?? 0}
        initialCategoryIds={data.publication?.categoryIds ?? []}
        onClose={() => setPublishOpen(false)}
        onPublished={async () => {
          await mutate()
        }}
      />
    </div>
  )
}

const LessonEditPage = ({ id, userId }: { id: string; userId: string }) => {
  const { t } = useTranslation('lessonWorkspace')
  const [locale, setLocale] = useState('en')
  const { data, error, mutate } = api.adminLessons.useAdminLessonsGet(
    id,
    { locale: 'en' },
    { revalidateOnFocus: false }
  )
  if (error)
    return (
      <Alert color="red" role="alert">
        {t('loadFailed', { defaultValue: 'Could not load this lesson.' })}
        <Button variant="subtle" onClick={() => void mutate()}>
          {t('retry', { defaultValue: 'Try again' })}
        </Button>
      </Alert>
    )
  if (!data) return <Text>{t('loading', { defaultValue: 'Loading…' })}</Text>
  return (
    <LessonEditSession
      key={`${userId}:${id}:${locale}`}
      id={id}
      userId={userId}
      locale={locale}
      data={data}
      mutate={mutate}
      onLocaleChange={setLocale}
    />
  )
}

const AdminLessonEdit = () => {
  const { id } = useParams()
  const { user } = useUser()
  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>
        {id && user?.userId && <LessonEditPage key={`${user.userId}:${id}`} id={id} userId={user.userId} />}
      </WithNavBar>
    </WithRole>
  )
}

export default AdminLessonEdit
