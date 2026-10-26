import { Group, LoadingOverlay, Stack, Title } from '@mantine/core'
import type { GroupProps } from '@mantine/core'
import type { FC, PropsWithChildren, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation } from 'react-router'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import { usePageTitle } from '@Hooks/usePageTitle'

export interface AdminTabProps extends PropsWithChildren {
  head?: ReactNode
  isLoading?: boolean
  headProps?: GroupProps
}
export const WithAdminTab: FC<AdminTabProps> = ({ head, headProps, isLoading, children }) => {
  const { t } = useTranslation()
  const title = t(`admin.tab.${useLocation().pathname.split('/')[2] || 'users'}`)
  usePageTitle(title)
  return (
    <Stack gap="md" pos="relative" w="100%">
      <Title order={1}>{title}</Title>
      {head && (
        <Group wrap="wrap" justify="space-between" w="100%" {...headProps}>
          {head}
        </Group>
      )}
      {children}
      <LoadingOverlay visible={isLoading ?? false} overlayProps={DEFAULT_LOADING_OVERLAY} />
    </Stack>
  )
}
