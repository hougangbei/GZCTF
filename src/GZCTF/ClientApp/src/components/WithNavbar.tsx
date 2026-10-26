import { AppShell, Box, LoadingOverlay, Stack, useComputedColorScheme, useMantineTheme } from '@mantine/core'
import React, { FC, useEffect, useState } from 'react'
import { useLocation } from 'react-router'
import { AdminShell } from '@Components/admin/AdminShell'
import { useTranslation } from 'react-i18next'
import { AppFooter } from '@Components/AppFooter'
import { AppHeader } from '@Components/AppHeader'
import { CustomColorModal } from '@Components/CustomColorModal'
import { IconHeader } from '@Components/IconHeader'
import { Watermark } from '@Components/Watermark'
import { WithWiderScreen } from '@Components/WithWiderScreen'
import { DEFAULT_LOADING_OVERLAY } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'
import classes from './WithNavbar.module.css'

interface WithNavBarProps extends React.PropsWithChildren {
  className?: string
  width?: string
  minWidth?: number
  isLoading?: boolean
  withFooter?: boolean
  withHeader?: boolean
  stickyHeader?: boolean
}

export interface AppControlProps {
  openColorModal: () => void
  mobile?: boolean
  navOpened?: boolean
  onNavToggle?: () => void
  onNavigate?: () => void
}

export const WithNavBar: FC<WithNavBarProps> = ({ children, className, width, isLoading, minWidth, withFooter = false, withHeader, stickyHeader = false }) => {
  const admin = useLocation().pathname.startsWith('/admin')
  const theme = useMantineTheme()
  const colorScheme = useComputedColorScheme('light', { getInitialValueInEffect: false })
  const { user, error: userError } = useUser()
  const mobile = useIsMobile(1100)
  const [colorModalOpened, setColorModalOpened] = useState(false)
  const [navOpened, setNavOpened] = useState(false)
  const { t } = useTranslation()

  useEffect(() => {
    if (!navOpened) return
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setNavOpened(false)
        document.getElementById('mobile-nav-toggle')?.focus()
      }
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [navOpened])

  return (
    <WithWiderScreen minWidth={minWidth ?? 0}>
      <a className={classes.skipLink} href="#main-content">{t('common.accessibility.skip_to_content')}</a>
      <Watermark
        text={user?.userId ?? ''}
        textColor={colorScheme === 'dark' ? theme.colors.gray[3] : theme.colors.gray[7]}
        rotate={-12}
        textSize={14}
        gutter={22}
        opacity={colorScheme === 'dark' ? 0.018 : 0.025}
      >
        <AppShell className={className} p={0} header={{ height: 56 }}>
          <AppHeader openColorModal={() => setColorModalOpened(true)} mobile={mobile} navOpened={navOpened} onNavToggle={() => setNavOpened(open => !open)} onNavigate={() => setNavOpened(false)} />
          {mobile && navOpened && <button type="button" className={classes.scrim} aria-label={t('common.accessibility.close_navigation')} onClick={() => setNavOpened(false)} />}
          <AppShell.Main id="main-content" tabIndex={-1} inert={Boolean(mobile && navOpened)} aria-hidden={mobile && navOpened || undefined}>
            <Stack className={classes.main} data-pb={withFooter || undefined}>
              <LoadingOverlay visible={isLoading ?? false} overlayProps={DEFAULT_LOADING_OVERLAY} />
              {withHeader && <IconHeader px={mobile ? '2%' : '10%'} sticky={stickyHeader} />}
              {admin && user?.role === Role.Admin && !userError ? <AdminShell>{children}</AdminShell> : <Box w={width ?? '100%'} className={classes.content}>{children}</Box>}
              <CustomColorModal opened={colorModalOpened} onClose={() => setColorModalOpened(false)} />
            </Stack>
            {withFooter && <AppFooter />}
          </AppShell.Main>
        </AppShell>
      </Watermark>
    </WithWiderScreen>
  )
}
