import { useEffect } from 'react'
import { useConfig } from '@Hooks/useConfig'

export const PlatformFavicon = () => {
  const { config } = useConfig()
  const logoUrl = config.logoUrl

  useEffect(() => {
    const icon = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (icon) {
      // Logo URLs contain a content hash. A stable version bypasses the old
      // browser favicon cache only when the uploaded image actually changes.
      icon.href = `/favicon.webp?v=${encodeURIComponent(logoUrl || 'default')}`
    }
  }, [logoUrl])

  return null
}
