import { Stack } from '@mantine/core'
import { FC, PropsWithChildren } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useLocation } from 'react-router'
import { LogoBox } from '@Components/LogoBox'
import { WithNavBar } from '@Components/WithNavbar'
import { getPlatformName, useConfig } from '@Hooks/useConfig'
import classes from './AccountView.module.css'

interface AccountViewProps extends PropsWithChildren {
  onSubmit?: (event: React.SubmitEvent<HTMLFormElement>) => Promise<void>
}

const steps = [
  { number: '01', key: 'skillTitle', href: '/skill-trees' },
  { number: '02', key: 'learningTitle', href: '/learn' },
  { number: '03', key: 'leaderboardTitle', href: '/leaderboard' },
  { number: '04', key: 'postsTitle', href: '/posts' },
] as const

export const AccountView: FC<AccountViewProps> = ({ onSubmit, children }) => {
  const { t } = useTranslation(['translation', 'home'])
  const { config } = useConfig()
  const platformName = getPlatformName(config)
  const { pathname } = useLocation()
  const page = pathname.split('/').at(-1) ?? 'login'
  const isLogin = page === 'login'

  return (
    <WithNavBar minWidth={0}>
      <div className={classes.page}>
        <section className={classes.intro} aria-labelledby="account-intro-title">
          <span className={classes.badge}><span aria-hidden="true" />{platformName}</span>
          <h1 id="account-intro-title">{isLogin ? t('home:authWelcome') : t('home:authNew')}<br /><em>{t('home:authQuestion')}</em></h1>
          <p>{t('home:authDescription', { platform: platformName })}</p>
          <div className={classes.steps}>
            {steps.map(step => <Link key={step.number} to={step.href} className={classes.step}>
              <span>{step.number}</span><strong>{t(`home:${step.key}`)}</strong>
            </Link>)}
          </div>
          <div className={classes.terminal} aria-hidden="true"><div><i /><i /><i /><span>{platformName} — {t('home:terminalTitle')}</span></div><p><b>›</b> {t('home:line2')}</p></div>
        </section>
        <section className={classes.panel} aria-labelledby="account-form-title">
          <div className={classes.panelHeading}>
            <LogoBox size={32} />
            <h2 id="account-form-title">{t(`account.title.${page}`, { defaultValue: t('account.title.login') })}</h2>
            <p>{t('home:authFormHint', { platform: platformName })}</p>
          </div>
          {page === 'pending' ? (
            <div className={classes.form}><Stack gap="sm" align="stretch">{children}</Stack></div>
          ) : (
            <form className={classes.form} onSubmit={onSubmit}>
              <Stack gap="sm" align="stretch">{children}</Stack>
            </form>
          )}
        </section>
      </div>
    </WithNavBar>
  )
}
