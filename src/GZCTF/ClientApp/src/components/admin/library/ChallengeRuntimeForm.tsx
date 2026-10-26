import { Button, FileButton, Group, NumberInput, Select, SimpleGrid, Stack, Text, TextInput } from '@mantine/core'
import { mdiPackageVariantClosed, mdiPaperclip } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { isAttachmentType, isContainerType, type ChallengeDraft } from '@Utils/challengeEditor'
import { ChallengeType } from '@Api'

type Props = {
  value: ChallengeDraft
  onChange: (next: ChallengeDraft) => void
  onUpload: (file: File | null) => Promise<void>
  uploading: boolean
  disabled?: boolean
}

export const ChallengeRuntimeForm = ({ value, onChange, onUpload, uploading, disabled }: Props) => {
  const { t } = useTranslation('learning')
  const setContainer = (patch: Partial<ChallengeDraft['container']>) =>
    onChange({ ...value, container: { ...value.container, ...patch } })

  return (
    <Stack gap="md">
      <Group gap="xs">
        <Icon path={mdiPackageVariantClosed} size={0.85} aria-hidden="true" />
        <Text fw={600}>{t('editorRuntime')}</Text>
      </Group>
      {isContainerType(value.type) && (
        <>
          <TextInput
            label={t('editorContainerImage')}
            value={value.container.ContainerImage}
            onChange={(event) => setContainer({ ContainerImage: event.currentTarget.value })}
            disabled={disabled}
            required
          />
          <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }}>
            <NumberInput
              label={t('editorServicePort')}
              min={1}
              max={65535}
              value={value.container.ExposedPort}
              onChange={(port) => setContainer({ ExposedPort: Number(port) || 1 })}
              disabled={disabled}
            />
            <NumberInput
              label={t('editorCpu')}
              min={1}
              value={value.container.Cpu}
              onChange={(cpu) => setContainer({ Cpu: Number(cpu) || 1 })}
              disabled={disabled}
            />
            <NumberInput
              label={t('editorMemory')}
              min={1}
              value={value.container.MemoryMb}
              onChange={(memory) => setContainer({ MemoryMb: Number(memory) || 1 })}
              disabled={disabled}
            />
            <NumberInput
              label={t('editorStorage')}
              min={0}
              value={value.container.StorageMb}
              onChange={(storage) => setContainer({ StorageMb: Number(storage) || 0 })}
              disabled={disabled}
            />
          </SimpleGrid>
          <Select
            label={t('editorNetworkMode')}
            data={['Open', 'Isolated', 'Custom']}
            value={value.container.NetworkMode}
            onChange={(mode) => mode && setContainer({ NetworkMode: mode })}
            disabled={disabled}
          />
        </>
      )}
      {isAttachmentType(value.type) && (
        <Stack gap="sm">
          <Group justify="space-between">
            <Text size="sm" fw={500}>
              {t('editorAttachments')}
            </Text>
            <FileButton onChange={onUpload} disabled={disabled || uploading}>
              {(props) => (
                <Button
                  {...props}
                  leftSection={<Icon path={mdiPaperclip} size={0.75} aria-hidden="true" />}
                  variant="light"
                  loading={uploading}
                >
                  {t('editorUploadAttachment')}
                </Button>
              )}
            </FileButton>
          </Group>
          {value.attachments.length === 0 && (
            <Text size="sm" c="dimmed">
              {t('editorNoAttachments')}
            </Text>
          )}
          {value.attachments.map((attachment, index) => (
            <Group key={`${attachment.Sha256}-${index}`} align="flex-end" wrap="wrap">
              <Text style={{ flex: 1 }} size="sm">
                {attachment.FileName}
              </Text>
              {value.type === ChallengeType.DynamicAttachment && (
                <TextInput
                  label={t('editorAttachmentFlag')}
                  value={attachment.Flag}
                  onChange={(event) =>
                    onChange({
                      ...value,
                      attachments: value.attachments.map((item, itemIndex) =>
                        itemIndex === index ? { ...item, Flag: event.currentTarget.value } : item
                      ),
                    })
                  }
                  disabled={disabled}
                />
              )}
              <Button
                color="red"
                variant="subtle"
                disabled={disabled}
                onClick={() =>
                  onChange({ ...value, attachments: value.attachments.filter((_, itemIndex) => itemIndex !== index) })
                }
              >
                {t('editorRemove')}
              </Button>
            </Group>
          ))}
        </Stack>
      )}
    </Stack>
  )
}
