import { Button, Group, Text } from '@mantine/core'
import { mdiContentCopy, mdiOpenInNew, mdiTimerOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  copyInstanceAddress,
  formatRemaining,
  instanceConnection,
  instanceSecondsRemaining,
} from '@Utils/challengeInstance'
import type { ChallengeInstanceResponse } from '@Api'
import classes from './ChallengeWorkspace.module.css'

type Props = {
  instance: ChallengeInstanceResponse
  category: string
  onExpire: () => void
}

export const InstanceConnection = ({ instance, category, onExpire }: Props) => {
  const { t } = useTranslation('learning')
  const [now, setNow] = useState(Date.now)
  const [copyState, setCopyState] = useState<'idle' | 'copied' | 'failed'>('idle')
  const remaining = instanceSecondsRemaining(instance.expiresAtUtc, now)
  const { address, href } = instanceConnection(instance.publicIp, instance.publicPort, category)
  useEffect(() => {
    setNow(Date.now())
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [instance.expiresAtUtc])
  useEffect(() => {
    if (remaining === 0) onExpire()
  }, [remaining === 0, instance.expiresAtUtc, onExpire])
  useEffect(() => {
    setCopyState('idle')
  }, [address])
  useEffect(() => {
    if (copyState !== 'copied') return
    const timer = window.setTimeout(() => setCopyState('idle'), 2500)
    return () => window.clearTimeout(timer)
  }, [copyState])

  return (
    <div className={classes.connection}>
      <div className={classes.timer} data-urgent={remaining !== null && remaining <= 300}>
        <Icon path={mdiTimerOutline} size={0.85} aria-hidden />
        <span>{t('timeRemaining')}</span>
        <strong role="timer" aria-label={t('timeRemaining')}>
          {remaining === null ? t('timeUnknown') : formatRemaining(remaining)}
        </strong>
      </div>
      {remaining === 0 && (
        <Text size="sm" c="orange" role="status">
          {t('instanceExpired')}
        </Text>
      )}
      <Text component="code" className={classes.instanceAddress}>
        {address || t('instanceRunning')}
      </Text>
      {address && (
        <Group gap="xs">
          <Button
            variant="default"
            size="xs"
            leftSection={<Icon path={mdiContentCopy} size={0.65} aria-hidden />}
            onClick={async () => {
              try {
                await copyInstanceAddress(address)
                setCopyState('copied')
              } catch {
                setCopyState('failed')
              }
            }}
          >
            {t(copyState === 'copied' ? 'addressCopied' : 'copyAddress')}
          </Button>
          {href && remaining !== 0 && (
            <Button
              component="a"
              href={href}
              target="_blank"
              rel="noopener noreferrer"
              variant="default"
              size="xs"
              rightSection={<Icon path={mdiOpenInNew} size={0.65} aria-hidden />}
            >
              {t('openChallenge')}
            </Button>
          )}
        </Group>
      )}
      <Text size="xs" c={copyState === 'failed' ? 'red' : 'dimmed'} role="status">
        {copyState === 'failed'
          ? t('copyFailed')
          : copyState === 'copied'
            ? t('addressCopied')
            : !href && address
              ? t('tcpConnectionHint')
              : null}
      </Text>
    </div>
  )
}
