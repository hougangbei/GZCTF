import { markCourseReviewEdited } from '@Hooks/useCourseReview'
import { courseReviewHref } from '@Utils/courseReview'
import { useAdminTestSession } from '@Hooks/useAdminTestSession'
import { runtimeErrorKey } from '@Utils/runtimeErrors'
import { useHistoryLeaveGuard } from '@Hooks/useHistoryLeaveGuard'
import { Icon } from '@mdi/react'
import { mdiPlayOutline, mdiStop } from '@mdi/js'
import { Alert, Badge, Button, Card, Code, Group, Modal, Stack, Text, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import useSWR from 'swr'
import { contextualPath, courseContext, workspaceHref } from '@Utils/courseAuthoring'
import { courseRequest, CourseRequestError } from '@Hooks/useCourseAuthoring'
import { useUser } from '@Hooks/useUser'
import api, { ChallengeInstanceStatus, ChallengeType, Role, type ChallengeEditResponse, fetcher } from '@Api'
import { ChallengeBasicsForm } from '@Components/admin/library/ChallengeBasicsForm'
import { ChallengeFlagEditor } from '@Components/admin/library/ChallengeFlagEditor'
import { ChallengeHelpEditor } from '@Components/admin/library/ChallengeHelpEditor'
import { ChallengeRuntimeForm } from '@Components/admin/library/ChallengeRuntimeForm'
import { ContentPublishModal } from '@Components/admin/library/ContentPublishModal'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { Markdown } from '@Components/MarkdownRenderer'
import { useChallengeCategoryLabelMap, showErrorMsg } from '@Utils/Shared'
import { getApiErrorCode } from '@Utils/skillTreeAdmin'
import {
  challengeCommandFromDraft,
  challengeDraftFromResponse,
  isContainerType,
  type ChallengeDraft,
} from '@Utils/challengeEditor'

const ChallengeEditSession = () => {
  const categoryIcons = useChallengeCategoryLabelMap()
  const { id } = useParams()
  const navigate = useNavigate()
  const { t } = useTranslation('learning')
  const { t: tDefault } = useTranslation()
  const { t: tSkillTrees } = useTranslation('skillTrees')
  const {user}=useUser()
  const [params] = useSearchParams()
  const parsedContext = courseContext(params)
  const context = parsedContext === 'invalid' ? undefined : parsedContext
  const backHref = context ? (params.get('review') === '1' ? courseReviewHref(context.courseId) : workspaceHref(context.courseId,{kind:'skill',categoryId:context.categoryId,skillId:context.skillId})) : '/admin/skill-trees?tab=challenges'
  const { data, error, mutate } = useSWR<ChallengeEditResponse>(
    id && user?.userId && parsedContext !== 'invalid' ? [contextualPath(`/api/admin/challenges/${id}/edit?locale=en`, context),user.userId] : null,
    ([path]:[string,string])=>fetcher(path), {keepPreviousData:false}
  )
  const initializedId = useRef<string | undefined>(undefined)
  const saveInFlight = useRef(false)
  const uploadInFlight = useRef(false)
  const testInFlight = useRef(false)
  const [draft, setDraft] = useState<ChallengeDraft>()
  const [savedDraft, setSavedDraft] = useState('')
  const dirty = Boolean(draft && JSON.stringify(draft) !== savedDraft)
  useHistoryLeaveGuard(dirty, '题目有未保存修改，确定离开吗？')
  useEffect(() => {
    const unload = (event: BeforeUnloadEvent) => { if(dirty){event.preventDefault();event.returnValue=''} }
    const leave = (event: MouseEvent) => {
      if(!dirty || event.defaultPrevented || event.button!==0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey)return
      const link=(event.target as Element).closest('a[href]')
      if(!(link instanceof HTMLAnchorElement)||link.target==='_blank'||link.hasAttribute('download'))return
      if(!window.confirm('题目有未保存修改，确定离开吗？')){event.preventDefault();event.stopPropagation()}
    }
    window.addEventListener('beforeunload',unload);document.addEventListener('click',leave,true)
    return()=>{window.removeEventListener('beforeunload',unload);document.removeEventListener('click',leave,true)}
  },[dirty])
  const [saving, setSaving] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [testing, setTesting] = useState(false)
  const [publishOpen, setPublishOpen] = useState(false)
  const [previewOpen, setPreviewOpen] = useState(false)
  const [retireOpen, setRetireOpen] = useState(false)
  const [retiring, setRetiring] = useState(false)
  const [publishVersion, setPublishVersion] = useState<number>()
  const { instance: testInstance, prepareStart: prepareTestStart, stop: stopTestInstance } = useAdminTestSession(id, user?.userId, context?.courseId)
  const [formError, setFormError] = useState<string>()
  const [conflict, setConflict] = useState(false)
  const [serverCompare,setServerCompare]=useState<ChallengeEditResponse>()
  const retired = data?.challenge?.publicationState === 'Retired' || data?.challenge?.publicationState === 'Merged'

  useEffect(() => {
    if (!id || !data || initializedId.current === id) return
    initializedId.current = id
    const initial = challengeDraftFromResponse(data)
    setDraft(initial)
    setSavedDraft(JSON.stringify(initial))
    setPublishVersion(data.publication?.rowVersion)
  }, [data, id])

  if (!id) return null

  const save = async (): Promise<number | undefined> => {
    if (conflict) return undefined
    if (!data || !draft || saveInFlight.current || uploadInFlight.current) return
    if (draft.type === ChallengeType.DynamicContainer && /\{userid\}/i.test(draft.container.FlagTemplate)) {
      setFormError(t('editorFlagTemplateObsolete'))
      return
    }
    if (!draft.title.trim()) {
      setFormError(t('adminTitle'))
      return
    }
    saveInFlight.current = true
    setSaving(true)
    setFormError(undefined)
    try {
      const command = challengeCommandFromDraft(draft, data, publishVersion)
      const response = context
        ? { data: await courseRequest<ChallengeEditResponse>(contextualPath(`/api/admin/challenges/${id}`, context), command) }
        : await api.adminChallenges.adminChallengesUpdate(id, command)
      if(context) markCourseReviewEdited(user?.userId,context.courseId,{kind:'challenge',contentId:id})
      setPublishVersion(response.data.publication?.rowVersion)
      setSavedDraft(JSON.stringify(draft))
      await mutate(response.data, { revalidate: false })
      showNotification({ color: 'green', message: t('editorSaved') })
      return response.data.publication?.rowVersion
    } catch (saveError) {
      if (getApiErrorCode(saveError) === 'learning.challenge_changed' || saveError instanceof CourseRequestError && saveError.status===409) setConflict(true)
      else if (context) setFormError((saveError as Error).message)
      else showErrorMsg(saveError, tDefault)
      return undefined
    } finally {
      saveInFlight.current = false
      setSaving(false)
    }
  }

  const publish = async () => {
    if (context) {
      const version = await save()
      if(version !== undefined) navigate(backHref)
      return
    }
    if (!draft?.body.trim()) {
      setFormError(tSkillTrees('errors.challengeBodyRequired'))
      return
    }
    const version = await save()
    if (version !== undefined) {
      setPublishVersion(version)
      setPublishOpen(true)
    }
  }

  const reload = async () => {
    try {
      const fresh = await mutate()
      if (!fresh) return
      initializedId.current = id
      const initial = challengeDraftFromResponse(fresh)
      setDraft(initial)
      setSavedDraft(JSON.stringify(initial))
      setPublishVersion(fresh.publication?.rowVersion)
      setFormError(undefined)
      setConflict(false)
    } catch (reloadError) {
      showErrorMsg(reloadError, tDefault)
    }
  }

  const upload = async (file: File | null) => {
    if (!file || !draft || saveInFlight.current || uploadInFlight.current || testInFlight.current) return
    uploadInFlight.current = true
    setUploading(true)
    try {
      const form=new FormData();form.append('files',file)
      const response=context
        ? {data:await courseRequest<{hash:string;name:string;storageKey:string}[]>(`/api/admin/skill-trees/${context.courseId}/workspace/assets`,form,'POST')}
        : {data:await courseRequest<{hash:string;name:string;storageKey:string}[]>(`/api/admin/content-assets?kind=challenge&contentId=${encodeURIComponent(id)}`,form,'POST')}
      const hash = response.data[0]?.hash
      if (!hash || hash.length !== 64) throw new Error('Attachment upload did not return a SHA-256 hash.')
      const entry = {
        FileName: file.name,
        Sha256: hash,
        StorageKey: 'storageKey' in response.data[0] ? String(response.data[0].storageKey) : `uploads/${hash.slice(0, 2)}/${hash.slice(2, 4)}/${hash}`,
        Flag: '',
      }
      setDraft((current) => current && ({
        ...current,
        attachments: current.type === ChallengeType.StaticAttachment ? [entry] : [...current.attachments, entry],
      }))
    } catch (uploadError) {
      showErrorMsg(uploadError, tDefault)
    } finally {
      uploadInFlight.current = false
      setUploading(false)
    }
  }

  const toggleTestContainer = async () => {
    if (!draft || !isContainerType(draft.type) || uploadInFlight.current || testInFlight.current) return
    const startOwnedTest = prepareTestStart()
    testInFlight.current = true
    setTesting(true)
    try {
      if (testInstance?.status === ChallengeInstanceStatus.Running) {
        await stopTestInstance()
      } else {
        const version = await save()
        if (version === undefined) return
        await startOwnedTest()
      }
    } catch (testError) {
      const key = runtimeErrorKey(testError)
      if (key) showNotification({ color: 'red', message: tDefault(key) })
      else showErrorMsg(testError, tDefault)
    } finally {
      testInFlight.current = false
      setTesting(false)
    }
  }

  const retire = async () => {
    setRetiring(true)
    try {
      await api.adminChallenges.adminChallengesDelete(id)
      showNotification({ color: 'green', message: t('editorRetired') })
      navigate('/admin/skill-trees?tab=challenges')
    } catch (retireError) {
      showErrorMsg(retireError, tDefault)
    } finally {
      setRetiring(false)
    }
  }

  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>
        <Stack gap="lg" maw={1100} mx="auto">
          <Group justify="space-between" align="flex-start">
            <Stack gap={4}>
              <Title order={1}>{draft?.title ?? t('adminEditChallenge')}</Title>
              <Group gap="xs">
                <Badge variant="light" color={data?.challenge?.publicationState === 'Published' ? 'green' : 'gray'}>
                  {tSkillTrees(`state.${(data?.challenge?.publicationState ?? 'Draft').toLowerCase()}`)}
                </Badge>
                {draft && <Badge variant="outline" color="gray" leftSection={<Icon path={categoryIcons.get(draft.ctfCategory)!.icon} size={0.65} aria-hidden="true" />}>{draft.ctfCategory}</Badge>}
                {draft && <Badge variant="outline">{draft.type}</Badge>}
              </Group>
            </Stack>
            <Button component={Link} to={backHref} variant="subtle">
              {context ? (params.get('review') === '1' ? '返回问题清单' : '返回课程编排') : tSkillTrees('workspace.tabs.challenges')}
            </Button>
          </Group>

          {parsedContext === 'invalid' && <Alert color="red">课程编辑上下文无效，请从课程编排重新打开。</Alert>}
          {context && <Alert>正在编辑课程草稿，保存后请返回课程统一发布。</Alert>}
          {error && <Alert color="red">{t('loadFailed')}</Alert>}
          {conflict && (
            <Alert color="red" title={t('editorChanged')}>
              {context ? <Stack><Text>你的输入已保留。先核对最新服务端内容，再决定采用哪份。</Text><Button onClick={async()=>{try{setServerCompare(await courseRequest<ChallengeEditResponse>(contextualPath(`/api/admin/challenges/${id}/edit?locale=en`,context),undefined,'GET'))}catch(error){setFormError((error as Error).message)}}}>读取最新版本并比较</Button>{serverCompare&&<><Title order={3}>服务端最新题目</Title><Text>{challengeDraftFromResponse(serverCompare).title}</Text><Markdown source={challengeDraftFromResponse(serverCompare).body}/><Text size="sm">以下只显示发生变化的字段。运行参数与 Flag 仅在管理员比较界面显示，不会出现在学员端。</Text>{Object.entries(challengeDraftFromResponse(serverCompare)).filter(([key,value])=>JSON.stringify(value)!==JSON.stringify(draft?.[key as keyof ChallengeDraft])).map(([key,value])=><Stack key={key} gap={4}><Text fw={600}>{key}</Text><Text size="xs">服务端最新值</Text><Code block style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{JSON.stringify(value,null,2)}</Code><Text size="xs">我的本地值</Text><Code block style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere'}}>{JSON.stringify(draft?.[key as keyof ChallengeDraft],null,2)}</Code></Stack>)}<Group><Button variant="default" onClick={()=>{const next=challengeDraftFromResponse(serverCompare);setDraft(next);setSavedDraft(JSON.stringify(next));setPublishVersion(serverCompare.publication?.rowVersion);void mutate(serverCompare,{revalidate:false});setConflict(false);setServerCompare(undefined)}}>采用服务端内容</Button><Button onClick={()=>{setPublishVersion(serverCompare.publication?.rowVersion);void mutate(serverCompare,{revalidate:false});setConflict(false);setServerCompare(undefined)}}>已比较，保留我的输入继续编辑</Button></Group></>}</Stack> : <Button size="compact-sm" onClick={reload}>{t('editorReload')}</Button>}
            </Alert>
          )}
          {!draft && !error ? <Text>{t('loading')}</Text> : draft && (
            <>
              {formError && <Alert color="red">{formError}</Alert>}
              <Card withBorder padding="lg">
                <ChallengeBasicsForm value={draft} onChange={setDraft} disabled={saving || testing || retired} />
              </Card>
              <Card withBorder padding="lg">
                <ChallengeRuntimeForm
                  value={draft} onChange={setDraft} onUpload={upload}
                  uploading={uploading} disabled={saving || testing || retired}
                />
                {isContainerType(draft.type) && (
                  <Stack mt="md" gap="xs" align="flex-start">
                    <Text size="sm" c="dimmed">{t('editorTestSessionHint')}</Text>
                    <Button leftSection={<Icon path={testInstance?.status === ChallengeInstanceStatus.Running ? mdiStop : mdiPlayOutline} size={0.8} aria-hidden="true" />} variant="light" loading={testing} disabled={saving || uploading || retired} onClick={toggleTestContainer}>
                      {testInstance?.status === ChallengeInstanceStatus.Running
                        ? t('editorTestStop') : t('editorTestStart')}
                    </Button>
                    {testInstance?.status === ChallengeInstanceStatus.Running && testInstance.publicIp && (
                      <Text size="sm">
                        {t('editorTestAddress')}: {testInstance.publicIp}
                        {testInstance.publicPort ? `:${testInstance.publicPort}` : ''}
                      </Text>
                    )}
                  </Stack>
                )}
              </Card>
              <Card withBorder padding="lg">
                <ChallengeFlagEditor value={draft} onChange={setDraft} disabled={saving || testing || retired} />
              </Card>
              <Card withBorder padding="lg">
                <ChallengeHelpEditor value={draft} onChange={setDraft} disabled={saving || testing || retired} />
              </Card>
              <Group justify="flex-end">
                {!context && <Button color="red" variant="subtle" disabled={saving || uploading || testing || retired} onClick={() => setRetireOpen(true)}>
                  {t('editorRetireChallenge')}
                </Button>}
                <Button variant="light" onClick={() => setPreviewOpen(true)}>{t('editorPreview')}</Button>
                <Button variant="light" loading={saving} disabled={uploading || testing || retired} onClick={save}>{t('editorSaveDraft')}</Button>
                <Button color="green" loading={saving} disabled={uploading || testing || retired} onClick={publish}>
                  {context ? (params.get('review') === '1' ? '保存并返回问题清单' : '保存并返回课程') : tSkillTrees('publish.challengeTitle')}
                </Button>
              </Group>
              <Modal opened={previewOpen} onClose={() => setPreviewOpen(false)} title={t('editorPreview')} size="lg">
                <Stack>
                  <Title order={3}>{draft.title}</Title>
                  <Text c="dimmed">{draft.summary}</Text>
                  <Markdown source={draft.body} />
                </Stack>
              </Modal>
              <Modal opened={retireOpen} onClose={() => setRetireOpen(false)} title={t('editorRetireChallenge')}>
                <Stack>
                  <Text>{t('editorRetireHint', { title: draft.title })}</Text>
                  <Group justify="flex-end">
                    <Button variant="default" onClick={() => setRetireOpen(false)}>{t('editorCancel')}</Button>
                    <Button color="red" loading={retiring} onClick={retire}>{t('editorRetire')}</Button>
                  </Group>
                </Stack>
              </Modal>
              {!context && <ContentPublishModal
                opened={publishOpen}
                kind="challenge"
                contentId={id}
                rowVersion={publishVersion ?? data?.publication?.rowVersion ?? 0}
                initialCategoryIds={data?.publication?.categoryIds ?? []}
                onClose={() => setPublishOpen(false)}
                onPublished={async () => {
                  await mutate()
                  navigate('/admin/skill-trees?tab=challenges')
                }}
              />}
            </>
          )}
        </Stack>
      </WithNavBar>
    </WithRole>
  )
}

const AdminChallengeEdit = () => {
  const {id}=useParams(); const [params]=useSearchParams(); const context=courseContext(params); const {user}=useUser()
  if(context==='invalid')return <WithRole requiredRole={Role.Admin}><WithNavBar minWidth={0}><Alert color="red">课程编辑上下文无效，请从课程编排重新打开。</Alert></WithNavBar></WithRole>
  return <ChallengeEditSession key={`${user?.userId ?? 'pending'}:${id}:${context?.courseId ?? 'library'}`}/>
}
export default AdminChallengeEdit
