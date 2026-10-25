import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'
import { getCollectionStatus, PageState } from './PageState'

const render = (props: Parameters<typeof PageState>[0]) => renderToStaticMarkup(createElement(PageState, props))

describe('PageState', () => {
  it('announces loading and hides stale content', () => {
    const html = render({ status: 'loading', children: 'stale content', loadingMessage: 'Loading posts' })
    expect(html).toContain('role="status"')
    expect(html).toContain('Loading posts')
    expect(html).not.toContain('stale content')
  })

  it('shows an empty result without a retry action', () => {
    const html = render({ status: 'empty', children: 'stale content', emptyMessage: 'No posts' })
    expect(html).toContain('No posts')
    expect(html).not.toContain('<button')
    expect(html).not.toContain('stale content')
  })

  it('announces an error and exposes a native retry button', () => {
    const html = render({ status: 'error', children: 'stale content', errorMessage: 'Request failed', onRetry: () => {} })
    expect(html).toContain('role="alert"')
    expect(html).toContain('Request failed')
    expect(html).toContain('<button type="button"')
    expect(html).not.toContain('stale content')
  })

  it('renders success content without status announcements', () => {
    const html = render({ status: 'success', children: createElement('article', null, 'Latest post') })
    expect(html).toContain('<article>Latest post</article>')
    expect(html).not.toContain('role="status"')
    expect(html).not.toContain('role="alert"')
  })
})

describe('getCollectionStatus', () => {
  it('keeps existing data visible during refresh or a later fetch error', () => {
    expect(getCollectionStatus(['post'], new Error('offline'))).toBe('success')
  })

  it('distinguishes no data, empty data, and an initial error', () => {
    expect(getCollectionStatus(undefined, undefined)).toBe('loading')
    expect(getCollectionStatus([], undefined)).toBe('empty')
    expect(getCollectionStatus(undefined, new Error('offline'))).toBe('error')
  })
})
