import { Group } from '@mantine/core'
import { NumberInput, Select, SimpleGrid, Stack, Switch, Text, Textarea, TextInput } from '@mantine/core'
import { mdiPencilOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import type { ChallengeDraft } from '@Utils/challengeEditor'
import { ChallengeCategory, Difficulty } from '@Api'

type Props = {
  value: ChallengeDraft
  onChange: (next: ChallengeDraft) => void
  disabled?: boolean
}

export const ChallengeBasicsForm = ({ value, onChange, disabled }: Props) => {
  const categoryIcons = useChallengeCategoryLabelMap()
  const { t } = useTranslation('learning')
  const { t: tChallenge } = useTranslation('challenge')
  const { t: tSkillTrees } = useTranslation('skillTrees')
  const set = (patch: Partial<ChallengeDraft>) => onChange({ ...value, ...patch })
  const standardDifficulties = [Difficulty.Baby, Difficulty.Easy, Difficulty.Normal, Difficulty.Hard]
  const difficulties = standardDifficulties.includes(value.difficulty)
    ? standardDifficulties
    : [...standardDifficulties, value.difficulty]

  return (
    <Stack gap="md">
      <Group gap="xs">
        <Icon path={mdiPencilOutline} size={0.85} aria-hidden="true" />
        <Text fw={600}>{t('editorBasics')}</Text>
      </Group>
      <TextInput
        label={t('adminTitle')}
        required
        value={value.title}
        onChange={(event) => set({ title: event.currentTarget.value })}
        disabled={disabled}
      />
      <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }}>
        <Select
          label={t('adminCtfCategory')}
          data={Object.values(ChallengeCategory).map((category) => ({
            value: category,
            label: `${category} · ${tChallenge(`category.${category.toLowerCase()}`)}`,
          }))}
          leftSection={<Icon path={categoryIcons.get(value.ctfCategory)!.icon} size={0.8} aria-hidden="true" />}
          renderOption={({ option }) => (
            <Group gap="xs">
              <Icon path={categoryIcons.get(option.value as ChallengeCategory)!.icon} size={0.8} aria-hidden="true" />
              <span>{option.label}</span>
            </Group>
          )}
          value={value.ctfCategory}
          onChange={(category) => category && set({ ctfCategory: category as ChallengeCategory })}
          searchable
          disabled={disabled}
        />
        <Select
          label={t('editorDifficulty')}
          data={difficulties.map((difficulty) => ({
            value: difficulty,
            label: `${tSkillTrees(`difficulty.${difficulty.toLowerCase()}`)}${standardDifficulties.includes(difficulty) ? '' : tSkillTrees('difficulty.legacySuffix')}`,
          }))}
          value={value.difficulty}
          onChange={(difficulty) => difficulty && set({ difficulty: difficulty as Difficulty })}
          disabled={disabled}
        />
        <NumberInput
          label={t('editorScore')}
          description={t('editorScoreHint')}
          min={0}
          max={10000}
          allowDecimal={false}
          value={value.score}
          onChange={(score) => set({ score: Math.max(0, Math.min(10000, Number(score) || 0)) })}
          disabled={disabled}
        />
        <NumberInput
          label={t('editorExpectedMinutes')}
          min={1}
          max={1440}
          value={value.expectedMinutes}
          onChange={(minutes) => set({ expectedMinutes: Number(minutes) || 1 })}
          disabled={disabled}
        />
        <NumberInput
          label={t('editorSubmissionLimit')}
          description={t('editorSubmissionLimitHint')}
          min={0}
          value={value.submissionLimit}
          onChange={(limit) => set({ submissionLimit: Math.max(0, Number(limit) || 0) })}
          disabled={disabled}
        />
      </SimpleGrid>
      <TextInput
        label={t('editorSummary')}
        value={value.summary}
        onChange={(event) => set({ summary: event.currentTarget.value })}
        disabled={disabled}
      />
      <Textarea
        label={t('editorDescription')}
        description={t('editorMarkdownHint')}
        minRows={8}
        autosize
        value={value.body}
        onChange={(event) => set({ body: event.currentTarget.value })}
        disabled={disabled}
      />
      <Switch
        label={t('editorEnabled')}
        description={t('editorEnabledHint')}
        checked={value.isEnabled}
        onChange={(event) => set({ isEnabled: event.currentTarget.checked })}
        disabled={disabled}
      />
    </Stack>
  )
}
