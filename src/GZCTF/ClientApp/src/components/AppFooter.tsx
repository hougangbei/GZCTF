import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Copyright } from '@Components/Copyright'
import { FooterRender } from '@Components/FooterRender'
import { useIsMobile } from '@Utils/ThemeOverride'
import { useConfig } from '@Hooks/useConfig'
import classes from '@Styles/AppFooter.module.css'

export const AppFooter: FC = () => {
  const { config } = useConfig()
  const isMobile = useIsMobile()
  const { t } = useTranslation()

  return (
    <footer className={classes.wrapper}>
      <div className={classes.inner}>
        <nav aria-label={t('common.accessibility.footer_navigation')} className={classes.links}>
          <Link to="/skill-trees">{t('skillTrees:navigation.title')}</Link>
          <Link to="/posts">{t('common.tab.post')}</Link>
          <Link to="/about">{t('common.tab.about')}</Link>
        </nav>
        <Copyright isMobile={isMobile} />
        {config.footerInfo && <FooterRender source={config.footerInfo} />}
      </div>
    </footer>
  )
}
