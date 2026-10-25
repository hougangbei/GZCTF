import { Anchor, Box, Image } from '@mantine/core'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import type { AboutDocument, AboutItem } from './types'
import { safeAboutImageUrl, sanitizeAboutHtml } from './security'
import classes from './About.module.css'

export const AboutRenderer: FC<{ document: AboutDocument; subtitle?: string }> = ({ document, subtitle }) =>
  <article className={classes.page}>
    <header className={classes.pageHeader}>
      <h1>{document.title}</h1>
      {subtitle && <p>{subtitle}</p>}
    </header>
    {document.sections.map(section => <section key={section.id} className={classes.storySection}
      data-single={section.items.length === 1 || undefined}>
      <div className={classes.sectionHeader}><h2>{section.title}</h2></div>
      <div className={classes.grid}>
        {section.items.map(item => <AboutBlock key={item.id} item={item} />)}
      </div>
    </section>)}
  </article>

const AboutBlock: FC<{ item: AboutItem }> = ({ item }) => {
  const { t } = useTranslation()
  return <Box className={`${classes.block} ${item.type === 'timeline' ? classes.timelineBlock : ''}`}
    style={{ '--block-width': item.width } as React.CSSProperties}>
    {item.type === 'text' && <>
      {item.title && <h3>{item.title}</h3>}
      <div className={classes.richText} dangerouslySetInnerHTML={{ __html: sanitizeAboutHtml(item.html ?? '') }} />
    </>}
    {item.type === 'image' && <figure>
      {item.href && /^https?:\/\//i.test(item.href)
        ? <Anchor href={item.href} aria-label={item.alt || item.caption || t('common.content.external_link')}
          target="_blank" rel="noopener noreferrer">
          <Image src={safeAboutImageUrl(item.url)} alt={item.alt ?? ''} />
        </Anchor>
        : <Image src={safeAboutImageUrl(item.url)} alt={item.alt ?? ''} />}
      {item.caption && <figcaption>{item.caption}</figcaption>}
    </figure>}
    {item.type === 'timeline' && <>
      {item.title && <h3>{item.title}</h3>}
      <ol className={classes.timeline}>{(item.events ?? []).map((event, index) =>
        <li key={`${event.time}-${index}`}>
          <span className={classes.time}>{event.time}</span>
          <div className={classes.eventContent}>
            <h4>{event.title}</h4>
            {event.image && <Image src={safeAboutImageUrl(event.image)} alt="" maw={480} />}
            <div className={classes.richText}
              dangerouslySetInnerHTML={{ __html: sanitizeAboutHtml(event.description) }} />
          </div>
        </li>)}</ol>
    </>}
  </Box>
}
