import { ActionIcon, AppShell, Box, Burger, Button, Group, Menu, Popover, useComputedColorScheme, useMantineColorScheme } from '@mantine/core'
import { mdiAccountCircleOutline, mdiCached, mdiCheck, mdiLaptop, mdiLogout, mdiPalette, mdiTransitConnectionVariant, mdiTranslate, mdiWeatherNight, mdiWeatherSunny } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { AppNavbar } from '@Components/AppNavbar'
import { LogoBox } from '@Components/LogoBox'
import { WsrxManager } from '@Components/WsrxManager'
import { AppControlProps } from '@Components/WithNavbar'
import { clearLocalCache } from '@Utils/Cache'
import { LanguageMap, SupportedLanguages, useLanguage } from '@Utils/I18n'
import { getPlatformName, useConfig } from '@Hooks/useConfig'
import { useLogOut, useUser } from '@Hooks/useUser'
import { ContainerPortMappingType } from '@Api'
import classes from '@Styles/AppHeader.module.css'

export const AppHeader = ({ openColorModal, navOpened = false, mobile = false, onNavToggle, onNavigate }: AppControlProps) => {
  const { t } = useTranslation()
  const { config } = useConfig()
  const platformName = getPlatformName(config)
  const { user, error } = useUser()
  const loggedIn = Boolean(user && !error)
  const logout = useLogOut()
  const location = useLocation()
  const { colorScheme: selectedColorScheme, setColorScheme } = useMantineColorScheme()
  const colorScheme = useComputedColorScheme('light', { getInitialValueInEffect: false })
  const { setLanguage, supportedLanguages } = useLanguage()
  const themeLabel = selectedColorScheme === 'auto'
    ? t('common.tab.theme.system')
    : t(colorScheme === 'dark' ? 'common.tab.theme.dark' : 'common.tab.theme.light')

  return (
    <AppShell.Header className={classes.header}>
      <div className={classes.inner}>
        <Link to="/" className={classes.brand} onClick={onNavigate} aria-label={platformName}>
          <LogoBox size={25} />
          <span className={classes.brandName}>{platformName}</span>
        </Link>
        {!mobile && <AppNavbar mobile={false} mobileHidden={false} onNavigate={onNavigate ?? (() => {})} />}
        <div className={classes.spacer} />
        <Group gap="xs" wrap="nowrap" className={classes.actions}>
          {config.portMapping === ContainerPortMappingType.PlatformProxy && (
            <Popover position="bottom" width={320}>
              <Popover.Target><ActionIcon variant="subtle" aria-label="WebSocket reflector"><Icon path={mdiTransitConnectionVariant} size={0.9} /></ActionIcon></Popover.Target>
              <Popover.Dropdown><WsrxManager /></Popover.Dropdown>
            </Popover>
          )}
          <Menu position="bottom-end" width={170}>
            <Menu.Target><ActionIcon variant="subtle" className={classes.iconAction} aria-label={t('common.accessibility.language')}><Icon path={mdiTranslate} size={0.9} /></ActionIcon></Menu.Target>
            <Menu.Dropdown>{supportedLanguages.map((lang: SupportedLanguages) => <Menu.Item key={lang} onClick={() => setLanguage(lang)}>{LanguageMap[lang] ?? lang}</Menu.Item>)}</Menu.Dropdown>
          </Menu>
          <Menu position="bottom-end" width={200}>
            <Menu.Target>
              <Button variant="default" size="xs" className={classes.themeButton} leftSection={<Icon path={colorScheme === 'dark' ? mdiWeatherNight : mdiWeatherSunny} size={0.8} />} aria-label={t('common.content.color.title')}>
                {themeLabel}
              </Button>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Item leftSection={<Icon path={selectedColorScheme === 'auto' ? mdiCheck : mdiLaptop} size={0.8} />} onClick={() => setColorScheme('auto')}>{t('common.tab.theme.system')}</Menu.Item>
              <Menu.Item leftSection={<Icon path={selectedColorScheme === 'light' ? mdiCheck : mdiWeatherSunny} size={0.8} />} onClick={() => setColorScheme('light')}>{t('common.tab.theme.light')}</Menu.Item>
              <Menu.Item leftSection={<Icon path={selectedColorScheme === 'dark' ? mdiCheck : mdiWeatherNight} size={0.8} />} onClick={() => setColorScheme('dark')}>{t('common.tab.theme.dark')}</Menu.Item>
              <Menu.Item leftSection={<Icon path={mdiPalette} size={0.8} />} onClick={openColorModal}>{t('common.content.color.title')}</Menu.Item>
              <Menu.Item leftSection={<Icon path={mdiCached} size={0.8} />} onClick={clearLocalCache}>{t('common.tab.account.clean_cache')}</Menu.Item>
            </Menu.Dropdown>
          </Menu>
          {loggedIn ? (
            <Menu position="bottom-end" width={185}>
              <Menu.Target><Button variant="light" size="xs" className={classes.accountButton} leftSection={<Icon path={mdiAccountCircleOutline} size={0.85} />}>{user?.userName}</Button></Menu.Target>
              <Menu.Dropdown>
                <Menu.Item component={Link} to="/account/profile">{t('common.tab.account.profile')}</Menu.Item>
                <Menu.Item component={Link} to="/account/learning">{t('skillTrees:list.title')}</Menu.Item>
                <Menu.Divider />
                <Menu.Item color="red" leftSection={<Icon path={mdiLogout} size={0.8} />} onClick={logout}>{t('common.tab.account.logout')}</Menu.Item>
              </Menu.Dropdown>
            </Menu>
          ) : (
            <Button component={Link} to={`/account/login?from=${encodeURIComponent(location.pathname + location.search)}`} size="xs" className={classes.loginButton} leftSection={<Icon path={mdiAccountCircleOutline} size={0.85} />}>{t('common.tab.account.login')}</Button>
          )}
          {mobile && <Burger id="mobile-nav-toggle" opened={navOpened} onClick={onNavToggle} size="sm" aria-label={t(navOpened ? 'common.accessibility.close_navigation' : 'common.accessibility.open_navigation')} aria-controls="mobile-main-navigation" aria-expanded={navOpened} />}
        </Group>
      </div>
      {mobile && <Box id="mobile-main-navigation"><AppNavbar mobile mobileHidden={!navOpened} onNavigate={onNavigate ?? (() => {})} /></Box>}
    </AppShell.Header>
  )
}
