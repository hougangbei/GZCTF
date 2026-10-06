import { Anchor, Badge, Group, Paper, Stack, Text, Title } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { skillTreeIcons, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import type { MyLearningResponse, MySkillTreeRecordResponse } from '@Api'

type MySkillTreeRecordProps = {
  record: MyLearningResponse
}

export const MySkillTreeRecord = ({ record }: MySkillTreeRecordProps) => {
  const { t } = useTranslation('skillTrees')

  const percent = (item: MySkillTreeRecordResponse) => {
    const total = (item.challengeCount ?? 0) + (item.lessonCount ?? 0)
    if (total === 0) return '0%'
    const completed = (item.completedChallengeCount ?? 0) + (item.completedLessonCount ?? 0)
    return `${Math.round((completed * 100) / total)}%`
  }

  return (
    <Stack gap="lg">
      <Title order={2}>{t('record.title')}</Title>
      {!record.skillTrees?.length ? (
        <Text c="dimmed">{t('record.empty')}</Text>
      ) : (
        <Stack gap="md">
          {record.skillTrees?.map((item) => (
            <Paper key={item.skillTreeId} withBorder p="md">
              <Stack gap="xs">
                <Group gap="sm">
                  <Text size="xl" aria-hidden>
                    {skillTreeIcons[(item.iconKey as SkillTreeIconKey) ?? 'flag']}
                  </Text>
                  {item.isDeleted
                    ? <Text fw={600}>{item.name}</Text>
                    : <Anchor component={Link} to={`/skill-trees/${item.skillTreeId}`} fw={600}>{item.name}</Anchor>}
                  {item.isCurrent && <Badge color="teal">{t('record.current')}</Badge>}
                  {item.isDeleted && <Badge color="gray">{t('record.historical')}</Badge>}
                </Group>
                <Text size="sm">{t('record.progress', { percent: percent(item) })}</Text>
                <Text size="sm">{t('record.categories', { completed: item.completedCategoryCount ?? 0, total: item.categoryCount ?? 0 })}</Text>
                <Text size="sm">{t('record.challenges', { completed: item.completedChallengeCount ?? 0, total: item.challengeCount ?? 0 })}</Text>
                <Text size="sm">{t('record.lessons', { completed: item.completedLessonCount ?? 0, total: item.lessonCount ?? 0 })}</Text>
              </Stack>
            </Paper>
          ))}
        </Stack>
      )}
      {record.recentActivity && record.recentActivity.length > 0 && (
        <Stack gap="xs">
          <Title order={3}>{t('record.recentActivity')}</Title>
          {record.recentActivity.map((activity) => (
            <Text key={`${activity.kind}-${activity.contentId}-${activity.completedAtUtc}`} size="sm">
              {activity.title ?? activity.contentId} · {activity.kind}
              {activity.solveMode ? ` · ${activity.solveMode}` : ''}
            </Text>
          ))}
        </Stack>
      )}
    </Stack>
  )
}
