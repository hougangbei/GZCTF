import { describe, expect, it } from 'vitest'
import type { CourseCheck, CourseDocument } from './courseAuthoring'
import {
  buildCourseReview,
  groupReviewIssues,
  markReviewChanged,
  parseCourseReview,
  reviewIssueHref,
  reviewKey,
} from './courseReview'

const document: CourseDocument = {
  name: 'Course',
  summary: '',
  iconKey: 'web',
  categories: [
    {
      categoryId: 'c',
      name: 'C',
      summary: '',
      iconKey: 'web',
      skills: [
        {
          skillId: 's',
          name: 'S',
          summary: '',
          iconKey: 'web',
          isEnabled: true,
          contents: [{ kind: 'challenge', contentId: 'q', title: 'Question', summary: '', state: 'Draft' }],
        },
      ],
    },
  ],
}
const check: CourseCheck = {
  version: 'v1',
  categoryCount: 1,
  skillCount: 1,
  lessonCount: 0,
  challengeCount: 1,
  issues: [
    { severity: 'error', code: 'challenge_invalid', message: 'invalid', contentId: 'q' },
    { severity: 'warning', code: 'disabled_challenge', message: 'disabled', contentId: 'q' },
    { severity: 'warning', code: 'missing_cover', message: 'cover' },
  ],
}
describe('course review workbench', () => {
  it('groups issues by identity and keeps course-level warnings accessible', () => {
    const review = buildCourseReview('tree', document, check)
    const groups = groupReviewIssues(review)
    expect(groups).toHaveLength(2)
    expect(groups[0]).toMatchObject({ title: 'Question', issues: [{ severity: 'error' }, { severity: 'warning' }] })
    expect(reviewIssueHref('tree', groups[0])).toBe(
      '/admin/library/challenges/q?course=tree&category=c&skill=s&review=1'
    )
  })
  it('saving marks changed without removing a problem or pretending it is solved', () => {
    const review = buildCourseReview('tree', document, check)
    const changed = markReviewChanged(review, { kind: 'challenge', contentId: 'q' })
    expect(changed.stale).toBe(true)
    expect(changed.check.issues).toEqual(check.issues)
    expect(changed.changedKeys).toEqual(['challenge:q'])
    expect(markReviewChanged(changed, { kind: 'challenge', contentId: 'q' }).changedKeys).toEqual(['challenge:q'])
    expect(buildCourseReview('tree', document, { ...check, version: 'v2', issues: [] }).stale).toBe(false)
  })
  it('uses account and course isolated keys and rejects invalid or wrong-course snapshots', () => {
    expect(reviewKey('a', 'tree')).not.toBe(reviewKey('b', 'tree'))
    expect(reviewKey('a', 'tree')).not.toBe(reviewKey('a', 'other'))
    expect(parseCourseReview('{bad', 'tree')).toBeUndefined()
    expect(parseCourseReview('{}', 'tree')).toBeUndefined()
    expect(parseCourseReview(JSON.stringify(buildCourseReview('other', document, check)), 'tree')).toBeUndefined()
    expect(parseCourseReview(JSON.stringify(buildCourseReview('tree', document, check)), 'tree')?.check.version).toBe(
      'v1'
    )
  })
})

it('routes missing identities to the course reference instead of an unavailable editor', () => {
  const review = buildCourseReview('tree', document, {
    ...check,
    issues: [{ severity: 'error', code: 'challenge_identity_unavailable', message: 'gone', contentId: 'q' }],
  })
  expect(reviewIssueHref('tree', groupReviewIssues(review)[0])).toBe('/admin/skill-trees/tree?category=c&skill=s')
})

it('rejects malformed nested report data before the recovery page can render it', () => {
  for (const patch of [
    { changes: {} },
    { changes: { added: [null], removed: [] } },
    { impacts: {} },
    { impacts: [null] },
    { runtimeChecks: [null] },
    { runtimeChecks: [{ contentId: 'q', title: {}, score: 100 }] },
    { challengeCount: {} },
  ]) {
    const snapshot = buildCourseReview('tree', document, { ...check, ...patch } as CourseCheck)
    expect(parseCourseReview(JSON.stringify(snapshot), 'tree')).toBeUndefined()
  }
})
