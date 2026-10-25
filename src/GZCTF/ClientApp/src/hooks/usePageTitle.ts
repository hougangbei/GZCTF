import { useDocumentTitle } from '@mantine/hooks'
import { getPlatformName, useConfig } from '@Hooks/useConfig'

export const usePageTitle = (title?: string) => {
  const { config } = useConfig()
  const platform = getPlatformName(config)

  useDocumentTitle(typeof title === 'string' && title.trim().length > 0 ? `${title} - ${platform}` : platform)
}
