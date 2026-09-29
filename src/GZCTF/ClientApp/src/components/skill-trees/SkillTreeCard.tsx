import { Group, Paper, Stack, Text, Title } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { skillTreeIcons, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import type { SkillTreeSummaryResponse } from '@Api'
import classes from './SkillTreeOutline.module.css'

type SkillTreeCardProps = {
  tree: SkillTreeSummaryResponse
}

export const SkillTreeCard = ({ tree }: SkillTreeCardProps) => {
  const { t } = useTranslation('skillTrees')
  const icon = skillTreeIcons[(tree.iconKey as SkillTreeIconKey) ?? 'flag']

  return (
    <Paper component={Link} to={`/skill-trees/${tree.skillTreeId}`} withBorder p="md" h="100%"
      className={classes.card}>
      <Stack gap="xs">
        <Group gap="sm" wrap="nowrap">
          <Text size="xl" aria-hidden>
            {icon}
          </Text>
          <Title order={3}>{tree.name}</Title>
        </Group>
        {tree.summary ? (
          <Text c="dimmed" lineClamp={2}>
            {tree.summary}
          </Text>
        ) : null}
        <Group gap="md" mt="xs">
          <Text size="sm">{t('list.categoryCount', { count: tree.categoryCount ?? 0 })}</Text>
          <Text size="sm">{t('list.challengeCount', { count: tree.challengeCount ?? 0 })}</Text>
          <Text size="sm">{t('list.lessonCount', { count: tree.lessonCount ?? 0 })}</Text>
        </Group>
      </Stack>
    </Paper>
  )
}
