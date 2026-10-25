import { Button, Text, ColorInput, Group, Modal, ModalProps, Radio, Stack, InputBase, useMantineTheme } from '@mantine/core'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ColorPreview } from '@Components/ColorPreview'
import { ColorProvider, CustomColor, useCustomColor } from '@Utils/ThemeOverride'
import { resolveThemePalette } from '@Utils/ThemePalette'
import { useConfig } from '@Hooks/useConfig'
import misc from '@Styles/Misc.module.css'

const colorRegex = /^#[0-9A-F]{6}$/i

export const CustomColorModal: FC<ModalProps> = (props) => {
  const { customColor, setCustomColor } = useCustomColor()
  const [color, setColor] = useState<CustomColor>(customColor)
  const theme = useMantineTheme()
  const { config } = useConfig()

  const { t } = useTranslation()

  const customPalette = color.provider === ColorProvider.Custom ? resolveThemePalette(color.color) : null
  const managedPalette = resolveThemePalette(config.customTheme)
  const valid = color.provider !== ColorProvider.Custom || customPalette !== null
  const colors = color.provider === ColorProvider.Custom
    ? customPalette?.colors ?? theme.colors.brand
    : color.provider === ColorProvider.Managed ? managedPalette?.colors ?? theme.colors.brand : theme.colors.brand

  useEffect(() => {
    setColor(customColor)
  }, [customColor])

  const cancel = () => {
    setColor(customColor)
    props.onClose()
  }

  const apply = () => {
    if (!valid) return
    setCustomColor(color)
    props.onClose()
  }

  return (
    <Modal title={t('common.content.color.title')} {...props} onClose={cancel}>
      <Stack>
        <Radio.Group
          label={t('common.content.color.provider.label')}
          description={t('common.content.color.provider.description')}
          value={color.provider}
          onChange={(e) => {
            setColor({ ...color, provider: e as ColorProvider })
          }}
        >
          <Group justify="space-around" mt="xs">
            {Object.keys(ColorProvider).map((p) => (
              <Radio
                key={p}
                value={p}
                label={
                  <Text size="sm" fw="bold">
                    {t(`common.content.color.provider.${p.toLowerCase()}`)}
                  </Text>
                }
              />
            ))}
          </Group>
        </Radio.Group>
        <InputBase
          label={t('common.content.color.palette.label')}
          description={t('common.content.color.palette.description')}
          variant="unstyled"
          disabled={color.provider !== ColorProvider.Custom}
          component={ColorPreview}
          h="100%"
          colors={colors}
          displayColorsInfo={false}
          classNames={{ input: misc.flex }}
        />
        <ColorInput
          size="sm"
          label={t('common.content.color.custom.label')}
          description={t('common.content.color.custom.description')}
          placeholder={t('common.content.color.custom.placeholder')}
          disabled={color.provider !== ColorProvider.Custom}
          error={color.provider === ColorProvider.Custom && !valid
            ? t(colorRegex.test(color.color) ? 'common.content.color.custom.contrast_error' : 'common.error.param_invalid')
            : undefined}
          value={color.provider === ColorProvider.Custom ? color.color : ''}
          onChange={(value) => setColor({ ...color, color: value })}
        />
        <Group justify="flex-end">
          <Button variant="default" onClick={cancel}>{t('common.modal.cancel')}</Button>
          <Button onClick={apply} disabled={!valid}>{t('common.content.color.apply')}</Button>
        </Group>
      </Stack>
    </Modal>
  )
}
