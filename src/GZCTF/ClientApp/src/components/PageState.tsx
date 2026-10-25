import type { ReactNode } from 'react'
import classes from './PageState.module.css'

export interface PageStateProps {
  status: 'loading' | 'empty' | 'error' | 'success'
  children?: ReactNode
  loadingMessage?: string
  emptyMessage?: string
  errorMessage?: string
  onRetry?: () => void
  retryLabel?: string
}

export function getCollectionStatus<T>(data: readonly T[] | undefined, error: unknown): PageStateProps['status'] {
  if (data !== undefined) return data.length === 0 ? 'empty' : 'success'
  return error ? 'error' : 'loading'
}

export function PageState({
  status,
  children,
  loadingMessage = 'Loading…',
  emptyMessage = 'No data available',
  errorMessage = 'Unable to load this page',
  onRetry,
  retryLabel = 'Try again',
}: PageStateProps) {
  if (status === 'success') return <>{children}</>

  return (
    <section className={classes.state} role={status === 'error' ? 'alert' : 'status'} aria-busy={status === 'loading'}>
      {status === 'loading' && <span className={classes.spinner} aria-hidden="true" />}
      {status === 'empty' && <span className={classes.icon} aria-hidden="true">∅</span>}
      {status === 'error' && <span className={classes.icon} aria-hidden="true">!</span>}
      <p className={classes.message}>
        {status === 'loading' ? loadingMessage : status === 'empty' ? emptyMessage : errorMessage}
      </p>
      {status === 'error' && onRetry && (
        <button type="button" className={classes.retry} onClick={onRetry}>
          {retryLabel}
        </button>
      )}
    </section>
  )
}
