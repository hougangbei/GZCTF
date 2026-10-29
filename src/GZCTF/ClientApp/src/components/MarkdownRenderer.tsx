import { Skeleton, Text, TextProps, Typography } from '@mantine/core'
import DOMPurify from 'dompurify'
import 'katex/dist/katex.min.css'
import { Marked } from 'marked'
import { forwardRef, useMemo, Suspense, FC } from 'react'
import { KatexExtension } from '@Utils/marked/KatexExtension'
import { ShikiExtension } from '@Utils/marked/ShikiExtension'
import classes from '@Styles/Typography.module.css'

export interface MarkdownProps extends React.ComponentPropsWithoutRef<'div'> {
  source: string
  lessonHeadings?: boolean
}

interface InlineMarkdownProps extends TextProps {
  source: string
}

export const InlineMarkdown = forwardRef<HTMLParagraphElement, InlineMarkdownProps>((props, ref) => {
  const { source, ...others } = props

  const inlineMarked = useMemo(() => {
    const instance = new Marked()
    instance.use(KatexExtension()).setOptions({ silent: true })
    return instance
  }, [])

  return (
    <Text
      ref={ref}
      {...others}
      className={classes.inline}
      dangerouslySetInnerHTML={{
        __html: inlineMarked.parseInline(source) ?? '',
      }}
    />
  )
})

export const ContentPlaceholder: FC = () => (
  <>
    <Skeleton height={14} mt={8} radius="xl" />
    <Skeleton height={14} mt={8} radius="xl" />
    <Skeleton height={14} mt={8} width="60%" radius="xl" />
    <Skeleton height={14} mt={8 + 14} radius="xl" />
    <Skeleton height={14} mt={8} radius="xl" />
    <Skeleton height={14} mt={8} width="30%" radius="xl" />
    <Skeleton height={14} mt={8 + 14} radius="xl" />
    <Skeleton height={14} mt={8} width="80%" radius="xl" />
  </>
)

const MarkdownRenderer: FC<Pick<MarkdownProps, 'source' | 'lessonHeadings'>> = (props) => {
  const { source, lessonHeadings } = props

  const html = useMemo(() => {
    let heading = 0
    const instance = new Marked()
    instance.use(KatexExtension()).use(ShikiExtension()).setOptions({ silent: true })
    if (lessonHeadings)
      instance.use({
        renderer: {
          heading({ tokens, depth }) {
            return `<h${depth} id="lesson-heading-${heading++}">${this.parser.parseInline(tokens)}</h${depth}>`
          },
        },
      })
    return DOMPurify.sanitize(instance.parse(source) as string)
  }, [source, lessonHeadings])

  return <div className={classes.root} dangerouslySetInnerHTML={{ __html: html ?? '' }} />
}

export const Markdown = forwardRef<HTMLDivElement, MarkdownProps>((props, ref) => {
  const { source, lessonHeadings, ...others } = props

  return (
    <Typography ref={ref} {...others}>
      <Suspense fallback={<ContentPlaceholder />}>
        <MarkdownRenderer source={source} lessonHeadings={lessonHeadings} />
      </Suspense>
    </Typography>
  )
})
