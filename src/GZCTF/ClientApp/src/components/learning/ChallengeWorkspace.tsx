import { Alert, Badge, Button, Group, Stack, Text, TextInput, Title } from '@mantine/core'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { useSWRConfig } from 'swr'
import { Markdown } from '@Components/MarkdownRenderer'
import { useLanguage } from '@Utils/I18n'
import { useChallenge, useChallengeInstance, useLearningMutations } from '@Hooks/useChallengeLibraryAdmin'
import { useUser } from '@Hooks/useUser'
import { ChallengeInstanceStatus } from '@Api'
import classes from './ChallengeWorkspace.module.css'
import { CommunityWriteups } from './CommunityWriteups'

export type ChallengeWorkspaceProps = {
  challengeId: string
  backHref?: string
  previousHref?: string
  nextHref?: string
  modal?: boolean
}

export const ChallengeWorkspace = ({
  challengeId,
  backHref,
  previousHref,
  nextHref,
  modal = false,
}: ChallengeWorkspaceProps) => {
  const { locale } = useLanguage()
  const { user } = useUser()
  const { t } = useTranslation('learning')
  const { t: tChallenge } = useTranslation('challenge')
  const { t: tSkillTrees } = useTranslation('skillTrees')
  const { data: challenge, error } = useChallenge(challengeId, locale, true)
  const { data: instance, mutate: mutateInstance } = useChallengeInstance(challengeId, !!user)
  const { mutate } = useSWRConfig()
  const { startInstance, extendInstance, stopInstance, submitChallenge, nextHint, revealWriteup } =
    useLearningMutations()
  const [flag, setFlag] = useState('')
  const [hints, setHints] = useState<string[]>([])
  const [writeup, setWriteup] = useState<string>()
  const [message, setMessage] = useState<string>()
  const [messageType, setMessageType] = useState<'success' | 'error'>('error')
  const [solveMode, setSolveMode] = useState<string>()
  const instanceActionInFlight = useRef(false)
  const [instanceActionPending, setInstanceActionPending] = useState(false)

  if (!challenge && !error) return <Text>{t('loading')}</Text>
  if (error) return <Text c="red">{t('loadFailed')}</Text>

  const run = async (action: () => Promise<unknown>) => {
    try {
      setMessage(undefined)
      await action()
    } catch (error) {
      setMessageType('error')
      setMessage((error as { response?: { status?: number } })?.response?.status === 409
        ? tSkillTrees('instances.limitReached') : t('actionFailed'))
    }
  }

  const runInstanceAction = async (action: () => Promise<unknown>) => {
    if (instanceActionInFlight.current) return
    instanceActionInFlight.current = true
    setInstanceActionPending(true)
    try {
      await run(action)
    } finally {
      instanceActionInFlight.current = false
      setInstanceActionPending(false)
    }
  }

  const submit = async () => {
    if (!flag.trim()) return
    try {
      const result = await submitChallenge(challengeId, flag)
      setMessageType(result.accepted ? 'success' : 'error')
      setMessage(
        result.accepted
          ? t('accepted')
          : result.rejectionCode === 'challenge.submission_limit_exhausted'
            ? t('submissionLimitExhausted')
            : t('rejected')
      )
      if (result.accepted) {
        setSolveMode(result.solveMode === undefined || result.solveMode === null ? undefined : String(result.solveMode))
        await mutateInstance().catch(() => undefined)
        await mutate((key) => {
          const path = Array.isArray(key) ? key[0] : key
          return typeof path === 'string' && path.startsWith('/api/my-learning')
        })
      }
      setFlag('')
    } catch {
      setMessageType('error')
      setMessage(t('actionFailed'))
    }
  }

  const revealHint = async () => {
    await run(async () => {
      const hint = await nextHint(challengeId, locale)
      if (hint) setHints((current) => [...current, hint.content])
    })
  }

  const revealOfficialWriteup = async () => {
    await run(async () => {
      const result = await revealWriteup(challengeId, locale)
      if (result) setWriteup(result.content)
    })
  }

  const typeKey = challenge!.type.replace(/([a-z])([A-Z])/g, '$1_$2').toLowerCase()
  const hasResource = !!user && (challenge!.hasAttachment || challenge!.hasContainer)

  return (
    <div className={classes.workspace}>
      {!modal && (backHref || previousHref || nextHref) && (
        <Group gap="xs" className={classes.navigation}>
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
      <section className={classes.intro} aria-label={challenge!.title}>
        {!modal && (
          <Title order={2} className={classes.pageTitle}>
            {challenge!.title}
          </Title>
        )}
        <Group gap="xs" className={classes.meta}>
          {challenge!.ctfCategory && (
            <Badge variant="light">
              {challenge!.ctfCategory} · {tChallenge(`category.${challenge!.ctfCategory.toLowerCase()}`)}
            </Badge>
          )}
          <Badge variant="outline" color="gray">
            {tChallenge(`type.${typeKey}.label`)}
          </Badge>
        </Group>
        {challenge!.summary && <Text className={classes.summary}>{challenge!.summary}</Text>}
        <div className={classes.description}>
          {challenge!.body?.trim() ? (
            <Markdown source={challenge!.body} />
          ) : (
            <Text c="dimmed">{tSkillTrees('content.noDescription')}</Text>
          )}
        </div>
      </section>

      <div className={`${classes.activity} ${!hasResource ? classes.singleActivity : ''}`}>
        {hasResource && (
          <section className={classes.activitySection}>
            <Title order={3} className={classes.sectionTitle}>
              {t('instance')}
            </Title>
            {instance?.status === ChallengeInstanceStatus.Running ? (
              <Stack gap="sm" align="flex-start">
                <Text className={classes.instanceAddress}>
                  {instance.publicIp
                    ? `${instance.publicIp}${instance.publicPort ? `:${instance.publicPort}` : ''}`
                    : t('instanceRunning')}
                </Text>
                <Group gap="xs">
                  <Button
                    variant="light"
                    disabled={instanceActionPending}
                    onClick={() =>
                      runInstanceAction(async () => {
                        await extendInstance(challengeId)
                        await mutateInstance()
                      })
                    }
                  >
                    {t('extend')}
                  </Button>
                  <Button
                    color="red"
                    variant="subtle"
                    loading={instanceActionPending}
                    onClick={() =>
                      runInstanceAction(async () => {
                        await stopInstance(challengeId)
                      })
                    }
                  >
                    {t('stop')}
                  </Button>
                </Group>
              </Stack>
            ) : challenge!.hasContainer ? (
              <Button
                variant="light"
                loading={instanceActionPending}
                onClick={() =>
                  runInstanceAction(async () => {
                    await startInstance(challengeId)
                    await mutateInstance()
                  })
                }
              >
                {t('start')}
              </Button>
            ) : (
              <Button component="a" variant="light" href={`/api/challenges/${challengeId}/attachment`}>
                {t('downloadAttachment')}
              </Button>
            )}
          </section>
        )}

        {user ? (
          <section className={classes.activitySection}>
            <Title order={3} className={classes.sectionTitle}>
              {t('submit')}
            </Title>
            <form
              onSubmit={(event) => {
                event.preventDefault()
                void submit()
              }}
              className={classes.submitForm}
            >
              <TextInput
                value={flag}
                onChange={(event) => setFlag(event.currentTarget.value)}
                placeholder={t('flagPlaceholder')}
                aria-label={t('flagPlaceholder')}
                classNames={{ input: classes.flagInput }}
              />
              <Button type="submit" disabled={!flag.trim()}>
                {t('submit')}
              </Button>
            </form>
            {message && (
              <Alert color={messageType === 'success' ? 'teal' : 'red'} className={classes.feedback}>
                {message}
              </Alert>
            )}
            {solveMode && (
              <Text c="teal" size="sm">
                {t('solveMode')}: {solveMode}
              </Text>
            )}
          </section>
        ) : (
          <Button component={Link} to="/account/login" variant="light">
            {tSkillTrees('content.signInToSubmit')}
          </Button>
        )}
      </div>

      {!!user && (challenge!.hintLocaleCount > 0 || challenge!.hasWriteup) && (
        <section className={classes.help}>
          <Stack gap="sm">
            <Title order={3} className={classes.sectionTitle}>
              {t('help')}
            </Title>
            <Group>
              {challenge!.hintLocaleCount > 0 && (
                <Button variant="light" onClick={revealHint}>
                  {t('nextHint')}
                </Button>
              )}
              {challenge!.hasWriteup && (
                <Button variant="light" onClick={revealOfficialWriteup}>
                  {t('showWriteup')}
                </Button>
              )}
            </Group>
            {hints.map((hint, index) => (
              <Alert key={`${index}-${hint}`}>{hint}</Alert>
            ))}
            {writeup && <Markdown source={writeup} />}
          </Stack>
        </section>
      )}
      <CommunityWriteups challengeId={challengeId} />
    </div>
  )
}
