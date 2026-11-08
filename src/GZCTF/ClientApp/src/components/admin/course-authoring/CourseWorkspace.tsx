import {
  Alert,
  Badge,
  Button,
  Divider,
  Drawer,
  Group,
  Modal,
  Menu,
  Skeleton,
  Stack,
  Switch,
  Text,
  Textarea,
  TextInput,
  Title,
} from '@mantine/core'
import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { TypedDeleteModal } from '@Components/admin/shared/TypedDeleteModal'
import { PresetIconPicker } from '@Components/admin/skill-trees/PresetIconPicker'
import {
  contentEditorHref,
  moveCourseNode,
  patchCourseSkill,
  reuseCourseSkill,
  removeCourseNode,
  type CourseCategory,
  type CourseCheck,
  type CourseDocument,
  type CourseSelection,
  type CourseSkill,
  type CourseWorkspace as Workspace,
  type PublicationIssue,
} from '@Utils/courseAuthoring'
import type { SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import { courseRequest, useCourseWorkspace } from '@Hooks/useCourseAuthoring'
import { useHistoryLeaveGuard } from '@Hooks/useHistoryLeaveGuard'
import type { SkillTreeDeleteImpactResponse } from '@Api'
import { AddCourseNode, type AddedResource, type AddKind } from './AddCourseNode'
import { CourseCover } from './CourseCover'
import { CourseOutline } from './CourseOutline'
import { CoursePreview } from './CoursePreview'
import classes from './CourseWorkspace.module.css'
import { PublishReview } from './PublishReview'
import { SharedReferences } from './SharedReferences'

export const CourseWorkspace = ({ id }: { id: string }) => {
  const { data, error, mutate } = useCourseWorkspace(id)
  const [params, setParams] = useSearchParams(),
    navigate = useNavigate()
  const [current, setCurrent] = useState<Workspace>(),
    [dirty, setDirty] = useState(false),
    [pending, setPending] = useState(false),
    [message, setMessage] = useState(''),
    [success, setSuccess] = useState('')
  const [selection, setSelection] = useState<CourseSelection>({
    kind: params.get('skill') ? 'skill' : params.get('category') ? 'category' : 'tree',
    categoryId: params.get('category') ?? undefined,
    skillId: params.get('skill') ?? undefined,
  })
  const [deleteImpact, setDeleteImpact] = useState<SkillTreeDeleteImpactResponse>()
  const [addMode, setAddMode] = useState<'new' | 'library'>('new')
  const [adding, setAdding] = useState<AddKind>(),
    [check, setCheck] = useState<CourseCheck>(),
    [preview, setPreview] = useState(false),
    [drawer, setDrawer] = useState(false),
    [removing, setRemoving] = useState(false)
  useHistoryLeaveGuard(dirty, '课程有未保存修改，确定离开吗？')
  const lock = useRef(false),
    initialized = useRef(false)
  useEffect(() => {
    if (data && (!initialized.current || (!dirty && data.version !== current?.version))) {
      initialized.current = true
      setCurrent(data)
    }
  }, [data, dirty, current?.version])
  useEffect(() => {
    const unload = (event: BeforeUnloadEvent) => {
      if (dirty) {
        event.preventDefault()
        event.returnValue = ''
      }
    }
    const leave = (event: MouseEvent) => {
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
      const url = new URL(anchor.href, location.href)
      if (url.pathname === location.pathname && url.search === location.search) return
      if (!window.confirm('课程有未保存修改。离开将丢失这些修改，确定离开吗？')) {
        event.preventDefault()
        event.stopPropagation()
      }
    }
    window.addEventListener('beforeunload', unload)
    window.document.addEventListener('click', leave, true)
    return () => {
      window.removeEventListener('beforeunload', unload)
      window.document.removeEventListener('click', leave, true)
    }
  }, [dirty])
  if (!current)
    return (
      <Stack>
        {error ? (
          <Alert color="red">
            课程工作区加载失败。
            <Button variant="subtle" onClick={() => void mutate()}>
              重新加载
            </Button>
          </Alert>
        ) : (
          <>
            <Skeleton h={54} />
            <Skeleton h={420} />
          </>
        )}
      </Stack>
    )
  const document = current.document
  const category = document.categories.find((item) => item.categoryId === selection.categoryId)
  const skill = category?.skills.find((item) => item.skillId === selection.skillId)
  const content = skill?.contents.find((item) => item.kind === selection.kind && item.contentId === selection.contentId)
  const selected =
    selection.kind === 'tree'
      ? document
      : selection.kind === 'category'
        ? category
        : selection.kind === 'skill'
          ? skill
          : content
  const title =
    selection.kind === 'tree'
      ? document.name
      : selection.kind === 'category'
        ? category?.name
        : selection.kind === 'skill'
          ? skill?.name
          : content?.title
  const changed = (next: CourseDocument) => {
    setCurrent({ ...current, document: next })
    setDirty(true)
    setCheck(undefined)
    setMessage('')
    setSuccess('')
  }
  const select = (node: CourseSelection) => {
    setSelection(node)
    setDrawer(false)
    setParams(
      { ...(node.categoryId ? { category: node.categoryId } : {}), ...(node.skillId ? { skill: node.skillId } : {}) },
      { replace: true }
    )
  }
  const accept = (next: Workspace) => {
    setCurrent(next)
    setDirty(false)
    setCheck(undefined)
    void mutate(next, { revalidate: false })
  }
  const persist = async (next = document) => {
    const saved = await courseRequest<Workspace>(`/api/admin/skill-trees/${id}/workspace`, {
      version: current.version,
      document: next,
    })
    accept(saved)
    return saved
  }
  const execute = async (work: () => Promise<void>) => {
    if (lock.current) return
    lock.current = true
    setPending(true)
    setMessage('')
    setSuccess('')
    try {
      await work()
    } catch (error) {
      setMessage((error as Error).message)
    } finally {
      lock.current = false
      setPending(false)
    }
  }
  const save = () =>
    execute(async () => {
      await persist()
      setSuccess('草稿已保存，学员可见内容尚未改变。')
    })
  const ensureSaved = async () => (dirty ? persist() : current)
  const edit = (node: CourseSelection) =>
    execute(async () => {
      await ensureSaved()
      navigate(contentEditorHref(id, node))
    })
  const updateMetadata = (patch: { name?: string; summary?: string; iconKey?: string; isEnabled?: boolean }) => {
    if (selection.kind === 'tree') changed({ ...document, ...patch })
    else if (selection.kind === 'category')
      changed({
        ...document,
        categories: document.categories.map((item) =>
          item.categoryId === category?.categoryId ? { ...item, ...patch } : item
        ),
      })
    else if (skill) changed(patchCourseSkill(document, skill.skillId, (item) => ({ ...item, ...patch })))
  }
  const add = async (resource: AddedResource) => {
    const normalizeSkill = (item: CourseSkill): CourseSkill =>
      reuseCourseSkill(document, {
        ...item,
        isEnabled: item.isEnabled !== false,
        contents: (item.contents ?? []).map((c) => ({ ...c, summary: c.summary ?? '', state: c.state ?? 'Draft' })),
      })
    let next = document
    let target: CourseSelection
    if (adding === 'category') {
      const node: CourseCategory = {
        categoryId: resource.id,
        name: resource.name,
        summary: resource.summary,
        iconKey: resource.iconKey,
        skills: (resource.skills ?? []).map(normalizeSkill),
      }
      next = {
        ...document,
        categories: document.categories.some((item) => item.categoryId === resource.id)
          ? document.categories
          : [...document.categories, node],
      }
      target = { kind: 'category', categoryId: resource.id }
    } else if (adding === 'skill' && category) {
      const node = normalizeSkill({
        skillId: resource.id,
        name: resource.name,
        summary: resource.summary,
        iconKey: resource.iconKey,
        isEnabled: resource.isEnabled !== false,
        contents: resource.contents ?? [],
      })
      next = {
        ...document,
        categories: document.categories.map((item) =>
          item.categoryId === category.categoryId
            ? {
                ...item,
                skills: item.skills.some((s) => s.skillId === resource.id) ? item.skills : [...item.skills, node],
              }
            : item
        ),
      }
      target = { kind: 'skill', categoryId: category.categoryId, skillId: resource.id }
    } else if ((adding === 'lesson' || adding === 'challenge') && skill && category) {
      const kind = adding
      next = patchCourseSkill(document, skill.skillId, (item) => ({
        ...item,
        contents: item.contents.some((c) => c.contentId === resource.id && c.kind === kind)
          ? item.contents
          : [
              ...item.contents,
              {
                contentId: resource.id,
                kind,
                title: resource.name,
                summary: resource.summary,
                state: resource.created ? 'Draft' : 'Published',
              },
            ],
      }))
      target = { kind, categoryId: category.categoryId, skillId: skill.skillId, contentId: resource.id }
    } else throw new Error('当前父级已不存在，请重新选择目录节点。')
    changed(next)
    await persist(next)
    setAdding(undefined)
    select(target)
    if ((target.kind === 'lesson' || target.kind === 'challenge') && resource.created)
      navigate(contentEditorHref(id, target))
  }
  const inspect = () =>
    execute(async () => {
      const saved = await ensureSaved()
      setCheck(
        await courseRequest<CourseCheck>(
          `/api/admin/skill-trees/${id}/workspace/check`,
          { version: saved.version },
          'POST'
        )
      )
    })
  const publish = () =>
    execute(async () => {
      if (!check || check.version !== current.version || dirty) throw new Error('草稿已变化，请重新检查。')
      const result = await courseRequest<CourseCheck | undefined>(
        `/api/admin/skill-trees/${id}/workspace/publish`,
        { version: current.version },
        'POST'
      )
      const fresh = await mutate()
      if (fresh) accept(fresh)
      setSuccess(`课程已统一发布。版本：${result?.publishedVersion ?? current.version}`)
    })
  const locate = (issue: PublicationIssue) => {
    let node: CourseSelection = { kind: 'tree' }
    for (const c of document.categories)
      for (const s of c.skills) {
        const item = s.contents.find((content) => content.contentId === issue.contentId)
        if (item) node = { kind: item.kind, categoryId: c.categoryId, skillId: s.skillId, contentId: item.contentId }
        else if (!issue.contentId && s.skillId === issue.skillId)
          node = { kind: 'skill', categoryId: c.categoryId, skillId: s.skillId }
      }
    if (!issue.skillId && !issue.contentId && issue.categoryId)
      node = { kind: 'category', categoryId: issue.categoryId }
    select(node)
    setCheck(undefined)
  }
  const rememberOutline = (element: HTMLElement | null) => {
    if (!element) return
    const key = `course-outline:${id}`
    try {
      element.scrollTop = Number(sessionStorage.getItem(key) ?? 0)
    } catch {}
  }
  const outline = (
    <CourseOutline
      document={document}
      selection={selection}
      disabled={pending}
      dirty={dirty}
      onSelect={select}
      onMove={(node, delta) => changed(moveCourseNode(document, node, delta))}
    />
  )
  const existing =
    adding === 'category'
      ? document.categories.map((c) => c.categoryId)
      : adding === 'skill'
        ? (category?.skills ?? []).map((s) => s.skillId)
        : (skill?.contents ?? []).filter((c) => c.kind === adding).map((c) => c.contentId)
  const beginAdd = (kind: AddKind, mode: 'new' | 'library' = 'new') => {
    setAddMode(mode)
    setAdding(kind)
  }
  const childButtons =
    selection.kind === 'tree' ? (
      <>
        <Button disabled={pending} onClick={() => beginAdd('category')}>
          新建类别
        </Button>
        <Button variant="light" disabled={pending} onClick={() => beginAdd('category', 'library')}>
          从库添加类别
        </Button>
      </>
    ) : selection.kind === 'category' ? (
      <>
        <Button disabled={pending} onClick={() => beginAdd('skill')}>
          新建技能
        </Button>
        <Button variant="light" disabled={pending} onClick={() => beginAdd('skill', 'library')}>
          从库添加技能
        </Button>
      </>
    ) : selection.kind === 'skill' ? (
      <>
        <Button disabled={pending} onClick={() => beginAdd('lesson')}>
          新建课节
        </Button>
        <Button variant="light" disabled={pending} onClick={() => beginAdd('challenge')}>
          新建题目
        </Button>
        <Menu>
          <Menu.Target>
            <Button variant="default" disabled={pending}>
              从库添加内容
            </Button>
          </Menu.Target>
          <Menu.Dropdown>
            <Menu.Item onClick={() => beginAdd('lesson', 'library')}>从课节库添加</Menu.Item>
            <Menu.Item onClick={() => beginAdd('challenge', 'library')}>从题目库添加</Menu.Item>
          </Menu.Dropdown>
        </Menu>
      </>
    ) : null

  return (
    <Stack className={classes.workspace} gap="lg">
      <div className={classes.header}>
        <Stack gap={4}>
          <Text size="sm" c="dimmed">
            课程编排
          </Text>
          <Title order={1}>{document.name}</Title>
          <Group>
            <Badge variant="light">{current.hasPublishedVersion ? '已发布 · 正在编辑草稿' : '尚未发布'}</Badge>
            <Text role="status" size="sm">
              {pending ? '正在保存或检查…' : dirty ? '有未保存修改' : '草稿已保存'}
            </Text>
          </Group>
        </Stack>
        <Group>
          <Button
            color="red"
            variant="subtle"
            disabled={pending}
            onClick={() =>
              void execute(async () => {
                setDeleteImpact(
                  await courseRequest<SkillTreeDeleteImpactResponse>(
                    `/api/admin/skill-trees/${id}/delete-impact`,
                    undefined,
                    'GET'
                  )
                )
              })
            }
          >
            删除技能树
          </Button>
          <Button component={Link} to="/admin/skill-trees" variant="subtle">
            全部技能树
          </Button>
          <Button variant="default" onClick={() => void save()} loading={pending}>
            保存草稿
          </Button>
          <Button
            variant="light"
            disabled={pending}
            onClick={() =>
              void execute(async () => {
                await ensureSaved()
                setPreview(true)
              })
            }
          >
            学员预览
          </Button>
          <Button disabled={pending} onClick={() => void inspect()}>
            检查并发布
          </Button>
        </Group>
      </div>
      {message && (
        <Alert color="red">
          <Stack gap="xs">
            <Text>{message}</Text>
            <Button
              variant="subtle"
              w="fit-content"
              disabled={pending}
              onClick={() => {
                if (dirty && !window.confirm('重新加载会放弃当前本地修改，确定继续吗？')) return
                void execute(async () => {
                  const fresh = await mutate()
                  if (fresh) accept(fresh)
                })
              }}
            >
              重新加载草稿
            </Button>
          </Stack>
        </Alert>
      )}
      {success && (
        <Alert color="green">
          {success}
          {current.hasPublishedVersion && (
            <Button
              component={Link}
              to={`/skill-trees/${id}`}
              variant="subtle"
              target="_blank"
              rel="noopener noreferrer"
            >
              查看学员入口
            </Button>
          )}
        </Alert>
      )}
      <Alert>所有目录和内容修改都先保存在草稿中。统一发布后学员才会看到；从库添加会复用原内容与完成记录。</Alert>
      <Button className={classes.mobileToggle} variant="light" onClick={() => setDrawer(true)}>
        打开课程目录
      </Button>
      <div className={classes.layout}>
        <aside
          className={classes.sidebar}
          ref={rememberOutline}
          onScroll={(event) => {
            try {
              sessionStorage.setItem(`course-outline:${id}`, String(event.currentTarget.scrollTop))
            } catch {}
          }}
        >
          {outline}
        </aside>
        <section className={classes.panel} aria-label="节点编辑">
          <Stack gap="lg">
            {!selected ? (
              <>
                <Alert color="yellow">当前目录节点已不存在，请选择其他节点。</Alert>
                <Button onClick={() => select({ kind: 'tree' })}>回到课程信息</Button>
              </>
            ) : (
              <>
                <Group justify="space-between">
                  <Title order={2}>{title}</Title>
                  {selection.kind !== 'tree' && (
                    <Button color="red" variant="subtle" disabled={pending} onClick={() => setRemoving(true)}>
                      从当前目录移出
                    </Button>
                  )}
                </Group>
                <Text size="sm" c="dimmed">
                  {[document.name, category?.name, skill?.name]
                    .filter((value, index, array) => value && array.indexOf(value) === index)
                    .join(' / ')}
                </Text>
                <SharedReferences id={id} node={selection} />
                {selection.kind === 'lesson' || selection.kind === 'challenge' ? (
                  <>
                    <Text>{content?.summary || '打开完整编辑器，设置内容与附件。'}</Text>
                    <Badge w="fit-content" variant="light">
                      {content?.state}
                    </Badge>
                    <Button w="fit-content" onClick={() => void edit(selection)} disabled={pending}>
                      编辑{selection.kind === 'lesson' ? '课节' : '题目'}
                    </Button>
                    <Text size="sm" c="dimmed">
                      当前引用保留原内容身份，通关与积分不会因重复添加而多计一份。
                    </Text>
                  </>
                ) : (
                  <>
                    <TextInput
                      label="名称"
                      required
                      value={'name' in selected ? selected.name : ''}
                      disabled={pending}
                      onChange={(event) => updateMetadata({ name: event.currentTarget.value })}
                    />
                    <Textarea
                      label="简介"
                      minRows={3}
                      maxLength={1024}
                      value={selected.summary}
                      disabled={pending}
                      onChange={(event) => updateMetadata({ summary: event.currentTarget.value })}
                    />
                    <PresetIconPicker
                      value={('iconKey' in selected ? selected.iconKey : 'flag') as SkillTreeIconKey}
                      disabled={pending}
                      onChange={(iconKey) => updateMetadata({ iconKey })}
                    />
                    {selection.kind === 'skill' && skill && (
                      <Switch
                        label="启用技能"
                        checked={skill.isEnabled}
                        disabled={pending}
                        onChange={(event) => updateMetadata({ isEnabled: event.currentTarget.checked })}
                      />
                    )}
                    {selection.kind === 'tree' && (
                      <CourseCover
                        workspace={current}
                        disabled={pending}
                        beforeUpload={ensureSaved}
                        onSaved={accept}
                        onBusyChange={(busy) => {
                          lock.current = busy
                          setPending(busy)
                        }}
                      />
                    )}
                    <Divider
                      label={
                        selection.kind === 'tree'
                          ? '类别编排'
                          : selection.kind === 'category'
                            ? '技能编排'
                            : '课节与题目'
                      }
                    />
                    <Group>
                      {childButtons}
                      <Text size="sm" c="dimmed">
                        新建会自动关联；从库添加会复用同一份内容。
                      </Text>
                    </Group>
                    {(selection.kind === 'tree'
                      ? document.categories
                      : selection.kind === 'category'
                        ? category?.skills
                        : skill?.contents
                    )?.map((item, index) => {
                      const node: CourseSelection =
                        'categoryId' in item
                          ? { kind: 'category', categoryId: item.categoryId }
                          : 'skillId' in item
                            ? { kind: 'skill', categoryId: category!.categoryId, skillId: item.skillId }
                            : {
                                kind: item.kind,
                                categoryId: category!.categoryId,
                                skillId: skill!.skillId,
                                contentId: item.contentId,
                              }
                      const name = 'name' in item ? item.name : item.title
                      return (
                        <div
                          key={`${node.kind}:${'contentId' in item ? item.contentId : name}`}
                          className={classes.resource}
                        >
                          <Stack gap={3}>
                            <Text fw={600}>{name}</Text>
                            <Text size="sm" c="dimmed">
                              {node.kind === 'category'
                                ? '类别'
                                : node.kind === 'skill'
                                  ? '技能'
                                  : node.kind === 'lesson'
                                    ? '课节'
                                    : '题目'}{' '}
                              · 第 {index + 1} 项
                            </Text>
                          </Stack>
                          <Button
                            variant="light"
                            disabled={pending}
                            onClick={() =>
                              node.kind === 'lesson' || node.kind === 'challenge' ? void edit(node) : select(node)
                            }
                          >
                            编辑
                          </Button>
                        </div>
                      )
                    })}
                    {!(
                      selection.kind === 'tree'
                        ? document.categories
                        : selection.kind === 'category'
                          ? category?.skills
                          : skill?.contents
                    )?.length && <div className={classes.empty}>这里还没有内容。从上方新建，或从资源库添加。</div>}
                  </>
                )}
              </>
            )}
          </Stack>
        </section>
      </div>
      {deleteImpact && (
        <TypedDeleteModal
          opened
          entityName={deleteImpact.name ?? document.name}
          title="删除技能树"
          requiresTypedConfirmation={Boolean(deleteImpact.requiresTypedConfirmation)}
          loading={pending}
          impactLines={[
            `关联类别：${deleteImpact.categoryCount ?? 0}`,
            `关联题目：${deleteImpact.challengeCount ?? 0}`,
            `关联课节：${deleteImpact.lessonCount ?? 0}`,
            `学习记录：${deleteImpact.enrollmentCount ?? 0}`,
          ]}
          onClose={() => !pending && setDeleteImpact(undefined)}
          onConfirm={(confirmationName) =>
            execute(async () => {
              await courseRequest(
                `/api/admin/skill-trees/${id}`,
                { confirmationName, rowVersion: deleteImpact.rowVersion },
                'DELETE'
              )
              setDirty(false)
              navigate('/admin/skill-trees')
            })
          }
        />
      )}
      <Drawer opened={drawer} onClose={() => setDrawer(false)} title="课程目录" size="sm">
        {outline}
      </Drawer>
      {adding && (
        <AddCourseNode
          key={`${adding}:${selection.categoryId}:${selection.skillId}`}
          kind={adding}
          courseId={id}
          parentKey={`${selection.categoryId ?? 'tree'}:${selection.skillId ?? 'root'}`}
          initialMode={addMode}
          existingIds={existing}
          onAdd={add}
          onClose={() => setAdding(undefined)}
        />
      )}
      {check && (
        <PublishReview
          check={check}
          document={current.document}
          pending={pending}
          onClose={() => !pending && setCheck(undefined)}
          onPublish={() => void publish()}
          onLocate={locate}
        />
      )}
      {preview && (
        <CoursePreview
          id={id}
          version={current.version}
          onClose={() => setPreview(false)}
          onCheck={() => {
            setPreview(false)
            void inspect()
          }}
        />
      )}
      <Modal opened={removing} onClose={() => setRemoving(false)} title="从当前目录移出">
        <Stack>
          <Text>
            只解除当前目录中的引用，不删除资源，也不删除学员的完成记录。若是共享技能的内容，将同步调整本草稿中该技能的所有位置。
          </Text>
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setRemoving(false)}>
              取消
            </Button>
            <Button
              color="red"
              onClick={() => {
                changed(removeCourseNode(document, selection))
                select(category ? { kind: 'category', categoryId: category.categoryId } : { kind: 'tree' })
                setRemoving(false)
              }}
            >
              确认移出
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  )
}
