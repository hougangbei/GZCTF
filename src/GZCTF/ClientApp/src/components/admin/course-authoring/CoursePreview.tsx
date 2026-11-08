import { Alert, Badge, Button, Group, Modal, Skeleton, Stack, Text, Title } from '@mantine/core'
import { useState } from 'react'
import useSWR from 'swr'
import { Markdown } from '@Components/MarkdownRenderer'
import type { HierarchyTree } from '@Utils/skillHierarchy'
import { fetcher } from '@Api'

type Item = { kind: string; contentId?: string | null; title?: string | null }
export const CoursePreview = ({
  id,
  version,
  onClose,
  onCheck,
}: {
  id: string
  version: string
  onClose: () => void
  onCheck: () => void
}) => {
  const { data, error, mutate } = useSWR<HierarchyTree>(
    `/api/admin/skill-trees/${id}/workspace/preview?version=${encodeURIComponent(version)}`,
    fetcher,
    { keepPreviousData: false, revalidateOnMount: true, dedupingInterval: 0 }
  )
  const [selection, setSelection] = useState<Item>()
  const contents =
    data?.categories?.flatMap((category) => category.skills?.flatMap((skill) => skill.contents) ?? []) ?? []
  const unique = [...new Map(contents.map((item) => [`${item.kind}:${item.contentId}`, item])).values()]
  return (
    <Modal opened onClose={onClose} title="管理员草稿预览 · 尚未发布" size="xl">
      <Stack>
        <Alert>这是当前草稿按发布规则生成的学员视角，不会改变线上课程。发布前仍需通过配置与附件检查。</Alert>
        {error ? (
          <Alert color="red">
            预览加载失败或当前内容还不满足发布条件。
            <Button variant="subtle" onClick={() => void mutate()}>
              重试
            </Button>
          </Alert>
        ) : !data ? (
          <Skeleton h={250} />
        ) : (
          <>
            <Title order={2}>{data.name}</Title>
            <Text>{data.summary}</Text>
            <Group>
              <Badge variant="light">{unique.filter((item) => item.kind === 'lesson').length} 篇课节</Badge>
              <Badge variant="light">{unique.filter((item) => item.kind === 'challenge').length} 道题目</Badge>
            </Group>
            {data.categories?.map((category) => (
              <Stack key={category.categoryId} gap="xs">
                <Title order={3}>{category.name}</Title>
                {category.skills?.map((skill) => (
                  <Stack key={skill.skillId} pl="md" gap={4}>
                    <Text fw={600}>{skill.name}</Text>
                    {skill.contents.map((item) => (
                      <Button
                        key={`${item.kind}:${item.contentId}`}
                        variant="subtle"
                        w="fit-content"
                        onClick={() => setSelection(item as Item)}
                      >
                        {item.kind === 'lesson' ? '课节' : '题目'} · {item.title}
                      </Button>
                    ))}
                  </Stack>
                ))}
              </Stack>
            ))}
            {!unique.length && <Text c="dimmed">当前没有学员可学习的内容。</Text>}
            {selection && <PreviewBody key={`${selection.kind}:${selection.contentId}`} id={id} item={selection} />}
          </>
        )}
        <Button onClick={onCheck}>查看发布检查</Button>
      </Stack>
    </Modal>
  )
}
const PreviewBody = ({ id, item }: { id: string; item: Item }) => {
  const path =
    item.kind === 'lesson'
      ? `/api/admin/lessons/${item.contentId}?locale=zh&course=${id}`
      : `/api/admin/challenges/${item.contentId}/edit?locale=zh&course=${id}`
  const { data, error } = useSWR<{ body?: string; localizations?: { locale?: string; body?: string }[] }>(
    path,
    fetcher,
    { keepPreviousData: false, revalidateOnMount: true, dedupingInterval: 0 }
  )
  const localization =
    data?.localizations?.find((l) => l.locale === 'zh') ??
    data?.localizations?.find((l) => l.locale === 'en') ??
    data?.localizations?.[0]
  return (
    <section aria-label="草稿内容预览">
      <Title order={3}>{item.title}</Title>
      {error ? (
        <Alert color="red">内容预览加载失败，请打开完整编辑器核对。</Alert>
      ) : !data ? (
        <Skeleton h={150} />
      ) : (
        <Markdown source={localization?.body ?? data.body ?? ''} />
      )}
    </section>
  )
}
