import { Group, Stack, Text, TextInput, UnstyledButton } from '@mantine/core'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { skillTreeIconKeys, skillTreeIcons, type SkillTreeIconKey } from '@Utils/skillTreeAdmin'
import classes from './PresetIconPicker.module.css'

type PresetIconPickerProps = {
  value: SkillTreeIconKey
  onChange: (value: SkillTreeIconKey) => void
  disabled?: boolean
  error?: string
}

export const PresetIconPicker = ({ value, onChange, disabled, error }: PresetIconPickerProps) => {
  const { t } = useTranslation('skillTrees')
  const [query, setQuery] = useState('')
  const normalized = query.trim().toLocaleLowerCase()
  const keys = skillTreeIconKeys.filter((key) => `${key} ${t(`icon.${key}`)}`.toLocaleLowerCase().includes(normalized))
  return (
    <Stack gap="xs">
      <Group justify="space-between" gap="xs">
        <Text size="sm" fw={500}>
          {t('icon.label')}
        </Text>
        <Text size="sm" c="dimmed">
          {t('icon.selected')} · {skillTreeIcons[value]} {t(`icon.${value}`)}
        </Text>
      </Group>
      <TextInput
        aria-label={t('icon.search')}
        placeholder={t('icon.search')}
        value={query}
        disabled={disabled}
        onChange={(event) => setQuery(event.currentTarget.value)}
      />
      <div className={classes.grid} role="group" aria-label={t('icon.label')}>
        {keys.map((key) => (
          <UnstyledButton
            key={key}
            type="button"
            className={classes.option}
            aria-label={t(`icon.${key}`)}
            aria-pressed={value === key}
            disabled={disabled}
            onClick={() => onChange(key)}
          >
            <span className={classes.symbol} aria-hidden>
              {skillTreeIcons[key]}
            </span>
            <span>{t(`icon.${key}`)}</span>
          </UnstyledButton>
        ))}
      </div>
      {!keys.length && (
        <Text size="sm" c="dimmed" role="status">
          {t('icon.empty')}
        </Text>
      )}
      {error && (
        <Text size="xs" c="red" role="alert">
          {error}
        </Text>
      )}
    </Stack>
  )
}
