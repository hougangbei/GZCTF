import { Anchor, Badge, Group, Paper, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { skillTreeIcons, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import type { SkillTreeDetailResponse } from '@Api'
import classes from './SkillTreeOutline.module.css'

type SkillTreeOutlineProps = {
  tree: SkillTreeDetailResponse
  selectedCategoryId?: string
}

export const SkillTreeOutline = ({ tree, selectedCategoryId }: SkillTreeOutlineProps) => {
  const { t } = useTranslation('skillTrees')
  const selected = tree.categories?.find((category) => category.categoryId === selectedCategoryId)
  const treeHref = `/skill-trees/${tree.skillTreeId}`
  const icon = skillTreeIcons[(tree.iconKey as SkillTreeIconKey) ?? 'flag']

  if (!tree.categories?.length) {
    return <Text c="dimmed">{t('editor.emptyPreview')}</Text>
  }

  return (
    <Stack gap="xl">
      <Group gap="sm">
        <Text size="xl" aria-hidden>{icon}</Text>
        <div>
          <Title order={2}>{tree.name}</Title>
          {tree.summary && <Text c="dimmed">{tree.summary}</Text>}
        </div>
      </Group>
      {selected ? (
        <Stack gap="md">
          <Anchor component={Link} to={treeHref} size="sm">← {t('content.allCategories')}</Anchor>
          <Title order={3}>{selected.name}</Title>
          {selected.summary && <Text c="dimmed">{selected.summary}</Text>}
          {selected.contents?.length ? (
            <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
              {selected.contents.map((content) => (
                <Paper
                  key={`${content.kind}-${content.contentId}`}
                  component={Link}
                  to={`${treeHref}/${selected.categoryId}/${content.kind}/${content.contentId}`}
                  withBorder p="lg" radius="md" className={classes.card}
                >
                  <Stack gap="xs" h="100%">
                    <Group justify="space-between" wrap="nowrap">
                      <Text size="xl" aria-hidden>{content.kind === 'challenge' ? '🧩' : '📖'}</Text>
                      {content.kind === 'challenge' && content.difficulty && <Badge variant="light">{t(`difficulty.${content.difficulty.toLowerCase()}`)}</Badge>}
                    </Group>
                    <Title order={4}>{content.title}</Title>
                    {content.summary && <Text size="sm" c="dimmed" lineClamp={2}>{content.summary}</Text>}
                    {content.kind === 'challenge' && content.expectedMinutes ? (
                      <Text size="xs" c="dimmed" mt="auto">{t('content.expectedMinutes', { count: content.expectedMinutes })}</Text>
                    ) : null}
                  </Stack>
                </Paper>
              ))}
            </SimpleGrid>
          ) : <Text c="dimmed">{t('editor.emptyPreview')}</Text>}
        </Stack>
      ) : (
        <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
          {tree.categories.map((category) => (
            <Paper
              key={category.categoryId}
              component={Link}
              to={`${treeHref}?category=${category.categoryId}`}
              withBorder p="lg" radius="md" className={classes.card}
            >
              <Stack gap="xs" h="100%">
                <Text size="xl" aria-hidden>{skillTreeIcons[(category.iconKey as SkillTreeIconKey) ?? 'flag']}</Text>
                <Title order={3}>{category.name}</Title>
                {category.summary && <Text size="sm" c="dimmed" lineClamp={2}>{category.summary}</Text>}
                <Text size="sm" c="dimmed" mt="auto">{t('content.itemCount', { count: category.contents?.length ?? 0 })}</Text>
              </Stack>
            </Paper>
          ))}
        </SimpleGrid>
      )}
    </Stack>
  )
}
