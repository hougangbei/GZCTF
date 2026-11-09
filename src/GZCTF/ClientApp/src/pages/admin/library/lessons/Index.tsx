import { Alert, Badge, Button, Group, Loader, NativeSelect, Stack, Switch, Text, TextInput, Title } from '@mantine/core'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { SourceLabels, ResourceLocations } from '@Components/admin/course-transfer/ResourceLocations'
import { showErrorMsg } from '@Utils/Shared'
import { useAdminLessons } from '@Hooks/useChallengeLibraryAdmin'
import { useCourseResources } from '@Hooks/useCourseTransfers'
import { useAdminSkillCategories } from '@Hooks/useSkillTreeAdmin'
import api, { Role } from '@Api'
import classes from './Index.module.css'

const AdminLessons = () => {
  const { data: lessons, error, isLoading, mutate } = useAdminLessons()
  const { data: categories, error: categoriesError } = useAdminSkillCategories()
  const { t } = useTranslation('lessonWorkspace')
  const navigate = useNavigate()
  const [title, setTitle] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('all')
  const [creating, setCreating] = useState(false)
  const [unassociated, setUnassociated] = useState(false)
  const { data: resources, error: locationError } = useCourseResources('lesson')
  const statusLabel = (value: string) =>
    value === 'Published'
      ? t('library.published', { defaultValue: 'Published' })
      : value === 'Draft'
        ? t('library.draft', { defaultValue: 'Draft' })
        : value
  const categoryLabels = (ids: string[]) =>
    ids.map(
      (id) =>
        categories?.find((category) => category.categoryId === id)?.name ||
        t('library.categoryUnavailable', { defaultValue: 'Category unavailable' })
    )
  const visibleLessons =
    lessons?.filter((lesson) => {
      const state = lesson.publication?.publicationState ?? 'Draft'
      const haystack = [lesson.title, ...categoryLabels(lesson.publication?.categoryIds ?? [])]
        .join(' ')
        .toLocaleLowerCase()
      return (
        (status === 'all' || state === status) &&
        haystack.includes(search.trim().toLocaleLowerCase()) &&
        (!unassociated || resources?.find((r) => r.id === lesson.id)?.unassociated === true)
      )
    }) ?? []
  const create = async () => {
    if (!title.trim() || creating) return
    setCreating(true)
    try {
      const response = await api.adminLessons.adminLessonsCreate({
        locale: 'en',
        localizations: [{ locale: 'en', title: title.trim(), body: '' }],
      })
      if (!response.data.id)
        throw new Error(t('library.createFailed', { defaultValue: 'Unable to open the created lesson' }))
      void mutate()
      navigate(`/admin/library/lessons/${response.data.id}`)
    } catch (error) {
      showErrorMsg(error, t)
    } finally {
      setCreating(false)
    }
  }
  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>
        <Stack className={classes.workspace} gap="xl">
          <div>
            <Title order={1}>{t('library.title', { defaultValue: 'Lesson library' })}</Title>
            <Text c="dimmed" mt="xs">
              {t('library.description', {
                defaultValue: 'Manage lesson content, publication status and category placement.',
              })}
            </Text>
          </div>
          <form
            className={classes.create}
            onSubmit={(event) => {
              event.preventDefault()
              void create()
            }}
          >
            <TextInput
              className={classes.grow}
              label={t('library.lessonTitle', { defaultValue: 'Lesson title' })}
              value={title}
              onChange={(event) => setTitle(event.currentTarget.value)}
              required
              maxLength={200}
            />
            <Button type="submit" loading={creating} disabled={!title.trim()}>
              {t('library.create', { defaultValue: 'Create lesson' })}
            </Button>
          </form>
          <div className={classes.filters}>
            <Switch
              label="只看未关联到课程"
              checked={unassociated}
              disabled={!resources}
              onChange={(e) => setUnassociated(e.currentTarget.checked)}
            />
            <TextInput
              className={classes.grow}
              label={t('library.search', { defaultValue: 'Search lessons' })}
              placeholder={t('library.searchPlaceholder', { defaultValue: 'Search by title or category' })}
              value={search}
              onChange={(event) => setSearch(event.currentTarget.value)}
            />
            <NativeSelect
              label={t('library.status', { defaultValue: 'Publication status' })}
              value={status}
              onChange={(event) => setStatus(event.currentTarget.value)}
              data={[
                { value: 'all', label: t('library.all', { defaultValue: 'All statuses' }) },
                { value: 'Draft', label: statusLabel('Draft') },
                { value: 'Published', label: statusLabel('Published') },
              ]}
            />
          </div>
          {categoriesError && (
            <Alert>
              {t('library.categoryLoadFailed', {
                defaultValue: 'Category names could not be loaded. Lesson editing is still available.',
              })}
            </Alert>
          )}
          {isLoading ? (
            <Group role="status">
              <Loader size="sm" />
              <Text>{t('library.loading', { defaultValue: 'Loading lessons…' })}</Text>
            </Group>
          ) : error ? (
            <Alert title={t('library.loadFailed', { defaultValue: 'Unable to load lessons' })}>
              <Button variant="light" onClick={() => void mutate()}>
                {t('library.retry', { defaultValue: 'Retry' })}
              </Button>
            </Alert>
          ) : !visibleLessons.length ? (
            <div className={classes.empty}>
              <Title order={2} size="h3">
                {lessons?.length
                  ? t('library.noMatches', { defaultValue: 'No matching lessons' })
                  : t('library.empty', { defaultValue: 'No lessons yet' })}
              </Title>
              <Text c="dimmed" mt="xs">
                {lessons?.length
                  ? t('library.adjustFilters', { defaultValue: 'Try a different search or publication status.' })
                  : t('library.createFirst', { defaultValue: 'Create your first lesson to start writing.' })}
              </Text>
            </div>
          ) : (
            <ul className={classes.list} aria-label={t('library.title', { defaultValue: 'Lesson library' })}>
              {visibleLessons.map((lesson) => (
                <li key={lesson.id} className={classes.row}>
                  <div className={classes.details}>
                    <SourceLabels resource={resources?.find((r) => r.id === lesson.id)} />
                    <ResourceLocations
                      resource={resources?.find((r) => r.id === lesson.id)}
                      unavailable={!!locationError}
                    />
                    <Text fw={600} className={classes.lessonTitle}>
                      {lesson.title || t('library.untitled', { defaultValue: 'Untitled lesson' })}
                    </Text>
                    <Group gap="xs" mt="xs">
                      <Badge variant="light">{statusLabel(lesson.publication?.publicationState ?? 'Draft')}</Badge>
                      <Text size="sm" c="dimmed" className={classes.categories}>
                        {categoryLabels(lesson.publication?.categoryIds ?? []).join(' · ') ||
                          t('library.unassigned', { defaultValue: 'Not assigned to a category' })}
                      </Text>
                    </Group>
                  </div>
                  <Button
                    component={Link}
                    to={`/admin/library/lessons/${lesson.id}`}
                    variant="subtle"
                    aria-label={t('library.editNamed', {
                      defaultValue: 'Edit {{title}}',
                      title: lesson.title || t('library.untitled', { defaultValue: 'Untitled lesson' }),
                    })}
                  >
                    {t('library.edit', { defaultValue: 'Edit' })}
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </Stack>
      </WithNavBar>
    </WithRole>
  )
}

export default AdminLessons
