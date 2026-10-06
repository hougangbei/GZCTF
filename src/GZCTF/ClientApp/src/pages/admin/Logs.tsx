import { Button, Group, Tabs } from '@mantine/core'
import { mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import * as signalR from '@microsoft/signalr'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminPage } from '@Components/admin/AdminPage'
import { AuditLogPanel } from '@Components/admin/logs/AuditLogPanel'
import { FlagAttemptPanel } from '@Components/admin/logs/FlagAttemptPanel'
import { SystemLogPanel } from '@Components/admin/logs/SystemLogPanel'

type AdminLogTab = 'audit' | 'flags' | 'system'

const Logs: FC = () => {
  const { t } = useTranslation()
  const [tab, setTab] = useState<AdminLogTab>('audit')
  const [unseen, setUnseen] = useState(0)
  const [refreshKey, setRefreshKey] = useState(0)

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub/admin')
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 60 * 1000 * 24
    connection.on('ReceivedLog', () => setUnseen((count) => count + 1))
    void connection.start().catch(() => undefined)
    return () => { void connection.stop().catch(() => undefined) }
  }, [])

  const refresh = () => {
    setUnseen(0)
    setRefreshKey((value) => value + 1)
  }

  return (
    <AdminPage
      minWidth={390}
      isLoading={false}
      head={
        <Group justify="space-between" w="100%" wrap="wrap">
          <Tabs value={tab} onChange={(value) => value && setTab(value as AdminLogTab)}>
            <Tabs.List>
              <Tabs.Tab value="audit">{t('admin.logsConsole.tabs.audit')}</Tabs.Tab>
              <Tabs.Tab value="flags">{t('admin.logsConsole.tabs.flags')}</Tabs.Tab>
              <Tabs.Tab value="system">{t('admin.logsConsole.tabs.system')}</Tabs.Tab>
            </Tabs.List>
          </Tabs>
          {unseen > 0 && <Button size="sm" variant="light" leftSection={<Icon path={mdiRefresh} size={0.8} />}
            onClick={refresh}>
            {t('admin.logsConsole.newRecords', { count: unseen })}
          </Button>}
        </Group>
      }
    >
      <Tabs value={tab} onChange={(value) => value && setTab(value as AdminLogTab)} keepMounted>
        <Tabs.Panel value="audit" pt="md"><AuditLogPanel refreshKey={refreshKey} /></Tabs.Panel>
        <Tabs.Panel value="flags" pt="md"><FlagAttemptPanel refreshKey={refreshKey} /></Tabs.Panel>
        <Tabs.Panel value="system" pt="md"><SystemLogPanel refreshKey={refreshKey} /></Tabs.Panel>
      </Tabs>
    </AdminPage>
  )
}

export default Logs
