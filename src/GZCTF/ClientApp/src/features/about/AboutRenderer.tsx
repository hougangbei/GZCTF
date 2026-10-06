import { Anchor, Box, Image, Stack, Text, Title } from '@mantine/core'
import { FC } from 'react'
import type { AboutDocument, AboutItem } from './types'
import { safeAboutImageUrl, sanitizeAboutHtml } from './security'
import classes from './About.module.css'

export const AboutRenderer: FC<{ document: AboutDocument }> = ({ document }) => <Stack className={classes.page} gap="xl">
  <Title order={1} className={classes.title}>{document.title}</Title>
  {document.sections.map((section) => <section key={section.id} className={classes.section}>
    <Title order={2}>{section.title}</Title>
    <div className={classes.grid}>{section.items.map((item) => <AboutBlock key={item.id} item={item} />)}</div>
  </section>)}
</Stack>

const AboutBlock: FC<{ item: AboutItem }> = ({ item }) => <Box className={classes.block} style={{ '--block-width': item.width } as React.CSSProperties}>
  {item.type === 'text' && <><Title order={3}>{item.title}</Title><div className={classes.richText} dangerouslySetInnerHTML={{ __html: sanitizeAboutHtml(item.html ?? '') }} /></>}
  {item.type === 'image' && <figure>{item.href && /^https?:\/\//i.test(item.href) ? <Anchor href={item.href} target="_blank" rel="noopener noreferrer"><Image src={safeAboutImageUrl(item.url)} alt={item.alt ?? ''} /></Anchor> : <Image src={safeAboutImageUrl(item.url)} alt={item.alt ?? ''} />}<figcaption>{item.caption}</figcaption></figure>}
  {item.type === 'timeline' && <><Title order={3}>{item.title}</Title><ol className={classes.timeline}>{(item.events ?? []).map((event, i) => <li key={`${event.time}-${i}`}>
    <Text c="cyan" ff="monospace">{event.time}</Text><Title order={4}>{event.title}</Title>
    {event.image && <Image src={safeAboutImageUrl(event.image)} alt="" maw={480} />}
    <div className={classes.richText} dangerouslySetInnerHTML={{ __html: sanitizeAboutHtml(event.description) }} />
  </li>)}</ol></>}
</Box>
