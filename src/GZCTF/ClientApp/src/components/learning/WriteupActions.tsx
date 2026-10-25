import { Button, Group, Menu } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import { fetcher } from '@Api'
import type { CommunityWriteup } from './CommunityWriteups'

export type WriteupRequest = { tab: 'browse' | 'submit'; id: number }

export const WriteupActions = ({
  challengeId,
  hasOfficial,
  onOfficial,
  onRequest,
}: {
  challengeId: string
  hasOfficial: boolean
  onOfficial: () => void
  onRequest: (tab: WriteupRequest['tab']) => void
}) => {
  const { t } = useTranslation('skillTrees')
  const path = `/api/challenges/${challengeId}/community-writeups`
  const { data, error, isLoading } = useSWR<CommunityWriteup[]>(path, fetcher)
  const items = data ?? []
  const count = items.length + (hasOfficial ? 1 : 0)
  const pdfUrl = (id: string) => `${path}/${id}/pdf`

  return (
    <Group role="group" gap="xs" mt="sm" aria-label={t('writeups.quickActions')}>
      {count === 1 && !hasOfficial ? (
        <Button component="a" variant="light" href={pdfUrl(items[0].id)} target="_blank" rel="noopener noreferrer">
          {t('writeups.view')}
        </Button>
      ) : count === 1 && hasOfficial ? (
        <Button variant="light" onClick={onOfficial}>
          {t('writeups.view')}
        </Button>
      ) : count > 1 ? (
        <Menu withinPortal position="bottom-start">
          <Menu.Target>
            <Button variant="light">{t('writeups.view')}</Button>
          </Menu.Target>
          <Menu.Dropdown>
            {hasOfficial && <Menu.Item onClick={onOfficial}>{t('writeups.official')}</Menu.Item>}
            {items.map((item) => (
              <Menu.Item key={item.id} component="a" href={pdfUrl(item.id)} target="_blank" rel="noopener noreferrer">
                {item.title} · {item.authorName}
              </Menu.Item>
            ))}
          </Menu.Dropdown>
        </Menu>
      ) : error ? (
        <Button variant="light" onClick={() => onRequest('browse')}>
          {t('writeups.view')}
        </Button>
      ) : (
        <Button variant="light" disabled loading={isLoading}>
          {t(isLoading ? 'writeups.view' : 'writeups.unavailable')}
        </Button>
      )}
      <Button variant="subtle" onClick={() => onRequest('submit')}>
        {t('writeups.submit')}
      </Button>
    </Group>
  )
}
