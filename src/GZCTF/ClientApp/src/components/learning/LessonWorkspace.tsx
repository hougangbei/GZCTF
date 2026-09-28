import { Button, Card, Group, Loader, Stack, Text, Title } from '@mantine/core'
import { Link } from 'react-router'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Markdown } from '@Components/MarkdownRenderer'
import { useLesson, useLearningMutations } from '@Hooks/useChallengeLibraryAdmin'
import { useLanguage } from '@Utils/I18n'
import { useUser } from '@Hooks/useUser'

export type LessonWorkspaceProps = {
  lessonId: string
  backHref?: string
  previousHref?: string
  nextHref?: string
  modal?: boolean
}

export const LessonWorkspace = ({ lessonId, backHref, previousHref, nextHref, modal = false }: LessonWorkspaceProps) => {
  const { locale } = useLanguage()
  const { user } = useUser()
  const { t } = useTranslation('learning')
  const { t: tSkillTrees } = useTranslation('skillTrees')
  const { data: lesson, error } = useLesson(lessonId, locale, true)
  const { completeLesson } = useLearningMutations()
  const [pending, setPending] = useState(false)
  const [message, setMessage] = useState<string>()

  const complete = async () => {
    setPending(true)
    setMessage(undefined)
    try { await completeLesson(lessonId) } catch { setMessage(t('actionFailed')) } finally { setPending(false) }
  }

  if (!lesson && !error) return <Loader />
  if (error) return <Text c="red">{t('loadFailed')}</Text>

  return (
    <Card withBorder={!modal} padding={modal ? 0 : 'xl'}>
      <Stack>
        {!modal && (backHref || previousHref || nextHref) && (
          <Group gap="xs" mb="md">
            {backHref && (
              <Button component={Link} to={backHref} variant="subtle">
                ← Back
              </Button>
            )}
            {previousHref && (
              <Button component={Link} to={previousHref} variant="subtle">
                ← Previous
              </Button>
            )}
            {nextHref && (
              <Button component={Link} to={nextHref} variant="subtle">
                Next →
              </Button>
            )}
          </Group>
        )}
        {!modal && <Title order={1}>{lesson!.title}</Title>}
        <Markdown source={lesson!.body} />
        <Group justify="flex-end">
          {user
            ? <Button loading={pending} onClick={complete}>{t('markComplete')}</Button>
            : <Button component={Link} to="/account/login" variant="light">{tSkillTrees('content.signInToComplete')}</Button>}
        </Group>
        {message && <Text c="red">{message}</Text>}
      </Stack>
    </Card>
  )
}
