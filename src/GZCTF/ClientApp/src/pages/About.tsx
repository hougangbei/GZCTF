import { Button, Center, Loader, Stack, Text } from '@mantine/core'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { WithNavBar } from '@Components/WithNavbar'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'
import { AboutRenderer } from '../features/about/AboutRenderer'
import type { AboutDocument } from '../features/about/types'

const About = () => {
  const { t } = useTranslation()
  const { user } = useUser()
  const [data, setData] = useState<{ published: boolean; document?: AboutDocument }>()
  useEffect(() => { fetch('/api/about').then((r) => r.json()).then(setData).catch(() => setData({ published: false })) }, [])
  usePageTitle(data?.document?.title ?? t('common.title.about'))
  return <WithNavBar minWidth={0}><Stack maw={1200} mx="auto" w="100%" px="md" py="md" pos="relative">
    {user?.role === Role.Admin && <Button component="a" href="/admin/about" variant="light" size="xs" ml="auto">编辑</Button>}
    {!data ? <Center mih={240}><Loader /></Center> : data.published && data.document ? <AboutRenderer document={data.document} /> : <Center mih={360}><Stack ta="center" gap="xs"><Text ff="monospace" c="cyan" size="xl">&gt; 内容建设中</Text><Text c="dimmed">实验室介绍正在整理，敬请期待。</Text></Stack></Center>}
  </Stack></WithNavBar>
}
export default About
