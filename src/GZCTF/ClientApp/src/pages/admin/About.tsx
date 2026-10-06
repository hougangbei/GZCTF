import { Button, FileInput, Group, Modal, Select, Stack, Text, TextInput, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { Puck, RichTextMenu, type Config, type Data } from '@puckeditor/core'
import { Mark } from '@tiptap/core'
import { Table, TableCell, TableHeader, TableRow } from '@tiptap/extension-table'
import '@puckeditor/core/puck.css'
import { useEffect, useMemo, useState } from 'react'
import { AdminPage } from '@Components/admin/AdminPage'
import { usePageTitle } from '@Hooks/usePageTitle'
import { AboutRenderer } from '../../features/about/AboutRenderer'
import type { AboutDocument, AboutItem, AboutSection } from '../../features/about/types'
import { safeAboutImageUrl, sanitizeAboutHtml } from '../../features/about/security'
import classes from '../../features/about/About.module.css'

type AboutState = { document: string; revision: number; lockOwnerId?: string; lockOwnerName?: string; canEdit: boolean }
type ApiResult = Response
const WIDTHS = [3, 4, 6, 8, 12] as const
const COLOR_PRESETS = ['cyan', 'blue', 'green', 'orange', 'red', 'purple'] as const
const PresetColor = Mark.create({
  name: 'aboutTextColor',
  addAttributes: () => ({ preset: { default: null, parseHTML: (element) => COLOR_PRESETS.find((color) => element.classList.contains(`about-color-${color}`)) ?? null, renderHTML: () => ({}) } }),
  parseHTML: () => COLOR_PRESETS.map((color) => ({ tag: `span.about-color-${color}`, getAttrs: () => ({ preset: color }) })),
  renderHTML: ({ HTMLAttributes }) => ['span', { class: `about-color-${HTMLAttributes.preset}` }, 0],
})
const widthField = { type: 'select' as const, label: '网格宽度', options: WIDTHS.map((value) => ({ label: `${value}/12 列`, value: `${value}` })) }
const puckConfig: Config = {
  root: { fields: { title: { type: 'text', label: '标题' } } },
  components: {
    TextBlock: {
      fields: {
        title: { type: 'text', label: '标题' },
        html: {
          type: 'richtext',
          label: '正文',
          initialHeight: 260,
          tiptap: { extensions: [Table.configure({ resizable: false }), TableRow, TableHeader, TableCell, PresetColor] },
          renderMenu: ({ children, editor }) => (
            <>
              {children}
              <RichTextMenu.Group>
                <label>
                  文字颜色
                  <select aria-label="预设文字颜色" defaultValue="" onChange={(event) => {
                    const color = event.currentTarget.value
                    if (color) editor?.chain().focus().setMark('aboutTextColor', { preset: color }).run()
                    else editor?.chain().focus().unsetMark('aboutTextColor').run()
                  }}>
                    <option value="">默认</option>
                    {COLOR_PRESETS.map((color) => <option key={color} value={color}>{color}</option>)}
                  </select>
                </label>
                <RichTextMenu.Control icon={<span>表格</span>} title="插入 2×2 表格" onClick={() => { editor?.chain().focus().insertTable({ rows: 2, cols: 2, withHeaderRow: true }).run() }} />
              </RichTextMenu.Group>
            </>
          ),
        },
        width: widthField,
      },
      defaultProps: { title: '新图文', html: '<p>在这里编辑实验室介绍。</p>', width: '6' },
      render: ({ title, html }) => <article><h3>{title}</h3><div dangerouslySetInnerHTML={{ __html: sanitizeAboutHtml(typeof html === 'string' ? html : '') }} /></article>,
    },
    ImageBlock: { fields: { url: { type: 'text', label: '图片 HTTPS 或本站地址' }, href: { type: 'text', label: '点击跳转链接（HTTP/HTTPS）' }, alt: { type: 'text', label: '替代文字' }, caption: { type: 'text', label: '图片说明' }, width: widthField }, defaultProps: { url: '', href: '', alt: '', caption: '', width: '6' }, render: ({ url, href, alt, caption }) => <figure><a href={/^https?:\/\//i.test(href) ? href : undefined} rel="noopener noreferrer"><img src={safeAboutImageUrl(url)} alt={alt} style={{ maxWidth: '100%' }} /></a><figcaption>{caption}</figcaption></figure> },
    TimelineBlock: { fields: { title: { type: 'text', label: '标题' }, events: { type: 'array', label: '时间轴事件', arrayFields: { time: { type: 'text', label: '时间标签' }, title: { type: 'text', label: '标题' }, description: { type: 'textarea', label: '说明 HTML' }, image: { type: 'text', label: '图片 HTTPS 或本站地址' } }, defaultItemProps: { time: '2026', title: '新事件', description: '<p>事件说明</p>', image: '' } }, width: widthField }, defaultProps: { title: '发展时间轴', events: [] as { time: string; title: string; description: string; image?: string }[], width: '12' }, render: ({ title, events }) => <article><h3>{title}</h3><ol>{events?.map((event: { time: string; title: string; description: string }, index: number) => <li key={index}><strong>{event.time} · {event.title}</strong><div dangerouslySetInnerHTML={{ __html: sanitizeAboutHtml(event.description ?? '') }} /></li>)}</ol></article> },
  },
}

function sectionToData(section: AboutSection): Data {
  return { root: { props: { title: section.title } }, content: section.items.map((item) => ({
    type: item.type === 'text' ? 'TextBlock' : item.type === 'image' ? 'ImageBlock' : 'TimelineBlock',
    props: { ...item, width: `${item.width}` },
    id: item.id,
  })), zones: {} }
}
function dataToSection(data: Data, original: AboutSection): AboutSection {
  const items = data.content.flatMap((entry): AboutItem[] => {
    const props = entry.props as Record<string, unknown>
    const type = entry.type === 'TextBlock' ? 'text' : entry.type === 'ImageBlock' ? 'image' : entry.type === 'TimelineBlock' ? 'timeline' : undefined
    if (!type) return []
    const id = (entry as unknown as { id?: string }).id ?? crypto.randomUUID()
    const width = WIDTHS.includes(Number(props.width) as (typeof WIDTHS)[number]) ? Number(props.width) as AboutItem['width'] : 6
    if (type === 'text') return [{ id, type, width, title: String(props.title ?? ''), html: String(props.html ?? '') }]
    if (type === 'image') return [{ id, type, width, url: String(props.url ?? ''), href: String(props.href ?? ''), alt: String(props.alt ?? ''), caption: String(props.caption ?? '') }]
    return [{ id, type, width, title: String(props.title ?? ''), events: Array.isArray(props.events) ? props.events as AboutItem['events'] : [] }]
  })
  return { ...original, title: data.root.props?.title || original.title, items }
}

const AboutAdmin = () => {
  const [state, setState] = useState<AboutState>()
  const [document, setDocument] = useState<AboutDocument>()
  const [sectionId, setSectionId] = useState<string>()
  const [editing, setEditing] = useState(false)
  const [preview, setPreview] = useState(false)
  const [previewMobile, setPreviewMobile] = useState(false)
  const [sourceMode, setSourceMode] = useState(false)
  const [history, setHistory] = useState<{ id: string; versionNumber: number; publisherName: string; publishedAtUtc: string }[]>([])
  const [versionPreview, setVersionPreview] = useState<AboutDocument>()
  const [upload, setUpload] = useState<File | null>(null)
  usePageTitle('关于页管理')
  const activeSection = document?.sections.find((section) => section.id === sectionId)
  const editorData = useMemo(() => activeSection ? sectionToData(activeSection) : { root: { props: { title: '' } }, content: [], zones: {} } as Data, [activeSection])
  const isDirty = !!document && !!state && JSON.stringify(document) !== JSON.stringify(JSON.parse(state.document))

  const refresh = async () => {
    const response = await fetch('/api/admin/about', { credentials: 'include' })
    if (!response.ok) throw new Error('读取管理数据失败')
    const next: AboutState = await response.json()
    setState(next)
    if (next.canEdit) setEditing(true)
    if (!isDirty) setDocument(JSON.parse(next.document))
    setSectionId((id) => id ?? JSON.parse(next.document).sections[0]?.id)
    const versions = await fetch('/api/admin/about/versions?take=50', { credentials: 'include' })
    if (versions.ok) setHistory((await versions.json()).items)
  }
  useEffect(() => { void refresh().catch((error) => notifications.show({ color: 'red', message: error.message })) }, [])
  const api = async (url: string, method = 'POST', body?: unknown): Promise<ApiResult> => fetch(url, { method, credentials: 'include', headers: body ? { 'Content-Type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined })
  const acquire = async () => { const r = await api('/api/admin/about/lock'); if (!r.ok) { notifications.show({ color: 'red', message: '编辑锁被占用，当前为只读状态' }); return } setEditing(true); await refresh() }
  const release = async () => { if (isDirty) { notifications.show({ color: 'yellow', message: '有未保存的编辑，请先保存草稿后再退出。' }); return } const r = await api('/api/admin/about/lock', 'DELETE'); if (r.ok) { setEditing(false); await refresh() } }
  const update = (next: AboutDocument) => setDocument(next)
  const save = async () => {
    if (!document || !state) return
    const response = await api('/api/admin/about/draft', 'PUT', { document: JSON.stringify(document), expectedRevision: state.revision })
    if (!response.ok) { notifications.show({ color: 'red', message: '保存失败；请刷新以检查修订冲突，当前编辑保留在本地。' }); return }
    const saved = await response.json(); setState({ ...state, revision: saved.revision, document: saved.document }); setDocument(JSON.parse(saved.document))
    notifications.show({ color: saved.normalized ? 'yellow' : 'teal', message: saved.normalized ? '草稿已保存，危险或不支持的 HTML 已清理。' : '草稿已保存。' })
  }
  const publish = async () => { if (!state) return; if (isDirty) { notifications.show({ color: 'yellow', message: '有未保存的编辑，请先保存草稿再发布。' }); return } const response = await api('/api/admin/about/publish', 'POST', { expectedRevision: state.revision }); if (!response.ok) { notifications.show({ color: 'red', message: '发布失败，当前公开版本保持不变。' }); return } await refresh(); notifications.show({ color: 'teal', message: '已发布。' }) }
  const restore = async (id: string) => { if (!state) return; if (isDirty) { notifications.show({ color: 'yellow', message: '请先保存草稿，再恢复历史版本。' }); return } const response = await api(`/api/admin/about/versions/${id}/restore`, 'POST', { expectedRevision: state.revision }); if (!response.ok) { notifications.show({ color: 'red', message: '恢复失败，请检查修订冲突。' }); return } await refresh() }
  const viewVersion = async (id: string) => { const response = await fetch(`/api/admin/about/versions/${id}`, { credentials: 'include' }); if (!response.ok) { notifications.show({ color: 'red', message: '版本内容无法读取。' }); return } const version = await response.json(); setVersionPreview(JSON.parse(version.documentJson)) }
  const addSection = () => { const section = { id: crypto.randomUUID(), title: '新板块', items: [] }; update({ ...document!, sections: [...document!.sections, section] }); setSectionId(section.id) }
  const uploadImage = async () => { if (!upload) return; const body = new FormData(); body.append('file', upload); const r = await fetch('/api/admin/about/images', { method: 'POST', credentials: 'include', body }); if (!r.ok) { notifications.show({ color: 'red', message: '图片上传失败或格式无效。' }); return } const result = await r.json(); navigator.clipboard.writeText(result.url).catch(() => undefined); notifications.show({ message: `图片已上传，地址已复制：${result.url}` }); setUpload(null) }

  return <AdminPage minWidth={390} head={<Group w="100%" justify="space-between"><Text fw={700}>实验室关于页 · 修订 {state?.revision ?? '—'}{isDirty ? ' · 有未保存更改' : ''}</Text><Group><Button variant="default" onClick={() => setPreview(!preview)}>{preview ? '返回编辑器' : '预览'}</Button>{editing && <><Button variant="light" onClick={() => void save()}>保存草稿</Button><Button onClick={() => void publish()} disabled={isDirty}>发布</Button><Button color="gray" variant="subtle" onClick={() => void release()} disabled={isDirty}>退出编辑</Button></>}</Group></Group>}>
    <Stack w="100%" maw={1440}>
      {!editing && <Group><Text c="dimmed">{state?.lockOwnerName ? `由 ${state.lockOwnerName} 持有编辑锁；当前只读。` : '当前为只读状态。'}</Text><Button onClick={() => void acquire()} disabled={!!state?.lockOwnerId}>获取编辑锁</Button></Group>}
      {document && <><TextInput label="页面标题" value={document.title} disabled={!editing} onChange={(event) => update({ ...document, title: event.currentTarget.value })} />
        <Group align="end"><Select label="板块" value={sectionId ?? null} data={document.sections.map((section) => ({ value: section.id, label: section.title }))} onChange={(value) => setSectionId(value ?? undefined)} w={260} /><Button disabled={!editing} onClick={addSection}>新增板块</Button>
          {activeSection && <><TextInput label="板块名称" value={activeSection.title} disabled={!editing} onChange={(event) => update({ ...document, sections: document.sections.map((s) => s.id === activeSection.id ? { ...s, title: event.currentTarget.value } : s) })} />
          <Button variant="default" disabled={!editing || document.sections.findIndex((s) => s.id === activeSection.id) === 0} onClick={() => { const index = document.sections.findIndex((s) => s.id === activeSection.id); const sections = [...document.sections]; [sections[index - 1], sections[index]] = [sections[index], sections[index - 1]]; update({ ...document, sections }) }}>上移</Button>
          <Button variant="default" disabled={!editing || document.sections.findIndex((s) => s.id === activeSection.id) === document.sections.length - 1} onClick={() => { const index = document.sections.findIndex((s) => s.id === activeSection.id); const sections = [...document.sections]; [sections[index + 1], sections[index]] = [sections[index], sections[index + 1]]; update({ ...document, sections }) }}>下移</Button>
          <Button variant="default" disabled={!editing} onClick={() => { const duplicate = { ...activeSection, id: crypto.randomUUID(), title: `${activeSection.title} 副本`, items: activeSection.items.map((item) => ({ ...item, id: crypto.randomUUID() })) }; const index = document.sections.indexOf(activeSection); const sections = [...document.sections]; sections.splice(index + 1, 0, duplicate); update({ ...document, sections }); setSectionId(duplicate.id) }}>复制板块</Button>
          <Button color="red" variant="subtle" disabled={!editing} onClick={() => { const sections = document.sections.filter((s) => s.id !== activeSection.id); update({ ...document, sections }); setSectionId(sections[0]?.id) }}>删除板块</Button></>}</Group>
        {preview && <Group><Button variant={previewMobile ? 'default' : 'filled'} onClick={() => setPreviewMobile(false)}>桌面屏宽</Button><Button variant={previewMobile ? 'filled' : 'default'} onClick={() => setPreviewMobile(true)}>手机屏宽</Button></Group>}
        {!preview && activeSection && editing && <Group><Button variant={sourceMode ? 'filled' : 'default'} onClick={() => setSourceMode(!sourceMode)}>HTML 源码视图</Button><FileInput label="上传图片（最大 10 MiB）" value={upload} onChange={setUpload} accept="image/*" /><Button onClick={() => void uploadImage()} disabled={!upload}>上传并复制地址</Button></Group>}
        {preview ? <div style={{ maxWidth: previewMobile ? 390 : 1280, width: '100%', marginInline: 'auto', border: previewMobile ? '1px solid var(--mantine-color-dark-4)' : undefined, padding: previewMobile ? 12 : undefined }}><AboutRenderer document={document} /></div> : activeSection && editing ? sourceMode ? <Stack>{activeSection.items.filter((item) => item.type === 'text').map((item) => <Textarea key={item.id} label={`${item.title || '图文正文'} HTML 源码（保存时经白名单清理）`} minRows={8} autosize value={item.html ?? ''} onChange={(event) => update({ ...document, sections: document.sections.map((s) => s.id === activeSection.id ? { ...s, items: s.items.map((current) => current.id === item.id ? { ...current, html: event.currentTarget.value } : current) } : s) })} />)}</Stack> : <div className={classes.editor}><Puck config={puckConfig} data={editorData} onChange={(data) => update({ ...document, sections: document.sections.map((s) => s.id === activeSection.id ? dataToSection(data, s) : s) })} /></div> : <AboutRenderer document={{ ...document, sections: document.sections.filter((s) => s.id === sectionId) }} />}</>}
      <Stack mt="xl"><Text fw={600}>已发布版本</Text>{history.map((version) => <Group key={version.id} justify="space-between" p="xs" className={classes.section}><Text>v{version.versionNumber} · {version.publisherName} · {new Date(version.publishedAtUtc).toLocaleString()}</Text><Group><Button size="xs" variant="default" onClick={() => void viewVersion(version.id)}>查看</Button><Button size="xs" variant="light" disabled={!editing} onClick={() => void restore(version.id)}>恢复为草稿</Button></Group></Group>)}</Stack>
    </Stack>
    <Modal opened={!!versionPreview} onClose={() => setVersionPreview(undefined)} title={versionPreview?.title} size="90%"><Stack mah="75vh" style={{ overflow: 'auto' }}>{versionPreview && <AboutRenderer document={versionPreview} />}</Stack></Modal>
  </AdminPage>
}
export default AboutAdmin
