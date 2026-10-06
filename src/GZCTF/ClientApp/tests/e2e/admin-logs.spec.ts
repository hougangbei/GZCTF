import { expect, test, type Page, type Route } from '@playwright/test'

const mockAdmin = async (page: Page) => {
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'audit-admin', role: 'Admin' } }),
  )
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
}

const auditRecord = (index: number) => ({
  id: `01990000-0000-7000-8000-${String(index).padStart(12, '0')}`,
  occurredAtUtc: new Date(Date.now() - index * 1000).toISOString(),
  actorId: '30000000-0000-0000-0000-000000000001', actorName: `管理员 ${index}`,
  actorKind: 'admin', category: 'users', action: 'users.update', targetType: 'user',
  targetId: `user-${index}`, targetName: `目标用户 ${index}`, succeeded: true, httpStatus: 200,
  errorCode: null, errorReason: null, requestId: `request-${index}`, affectedCount: null,
})

const fulfillPage = async (route: Route, items: unknown[], total: number, page: number) =>
  route.fulfill({ json: { items, total, page, pageSize: 20 } })

test.describe('admin log console', () => {
  test('shows paged compact operations, resets filtered pages, and fetches Flag plaintext only on expand', async ({ page }) => {
    await mockAdmin(page)
    const auditRecords = Array.from({ length: 21 }, (_, index) => auditRecord(index + 1))
    const auditQueries: string[] = []
    await page.route('**/api/admin/audit-events*', async (route) => {
      const url = new URL(route.request().url())
      auditQueries.push(`${url.searchParams.get('page')}:${url.searchParams.get('category') ?? ''}`)
      const currentPage = Number(url.searchParams.get('page') ?? 1)
      const category = url.searchParams.get('category')
      const filtered = category ? auditRecords.slice(0, 3) : auditRecords
      const current = currentPage === 1 ? filtered.slice(0, 20) : filtered.slice(20)
      await fulfillPage(route, current, filtered.length, currentPage)
    })

    let detailRequests = 0
    const submittedFlag = 'flag{only-on-expand}'
    await page.route('**/api/admin/flag-attempts*', async (route) =>
      fulfillPage(route, [{
        id: '01990000-0000-7000-8000-000000000040', occurredAtUtc: new Date().toISOString(),
        userId: '01990000-0000-7000-8000-000000000041', userName: 'learner',
        challengeId: '01990000-0000-7000-8000-000000000042', challengeName: '入门题',
        submissionId: null, outcome: 'incorrect', rejectionCode: 'challenge.flag_incorrect',
      }], 1, 1),
    )
    await page.route('**/api/admin/flag-attempts/01990000-0000-7000-8000-000000000040', async (route) => {
      detailRequests++
      await route.fulfill({ json: {
        id: '01990000-0000-7000-8000-000000000040', occurredAtUtc: new Date().toISOString(),
        userId: '01990000-0000-7000-8000-000000000041', userName: 'learner',
        challengeId: '01990000-0000-7000-8000-000000000042', challengeName: '入门题',
        submissionId: null, outcome: 'incorrect', rejectionCode: 'challenge.flag_incorrect',
        originalAvailable: true, submittedFlag,
      } })
    })
    await page.route('**/api/admin/logs*', (route) => fulfillPage(route, [], 0, 1))

    await page.goto('/admin/logs')
    await expect(page.getByRole('tab').filter({ hasText: /操作日志|Flag 提交|系统状态/ }))
      .toHaveText(['操作日志', 'Flag 提交', '系统状态'])
    await expect(page.getByText('管理员 1', { exact: true })).toBeVisible()
    await expect(page.getByText('管理员 21', { exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: '2', exact: true }).click()
    await expect(page.getByText('管理员 21', { exact: true })).toBeVisible()
    await expect.poll(() => auditQueries.at(-1)).toBe('2:')

    await page.getByRole('combobox', { name: '类别' }).click()
    await page.getByRole('option', { name: '用户与权限' }).click()
    await expect.poll(() => auditQueries.at(-1)).toBe('1:users')
    await expect(page.getByText('管理员 21', { exact: true })).toHaveCount(0)

    await page.getByRole('tab', { name: 'Flag 提交' }).click()
    await expect(page.getByText('入门题')).toBeVisible()
    await expect(page.getByText(submittedFlag)).toHaveCount(0)
    expect(detailRequests).toBe(0)
    await page.getByRole('button', { name: /learner/ }).click()
    await expect(page.getByText(submittedFlag)).toBeVisible()
    expect(detailRequests).toBe(1)
  })

  test('keeps log summaries and disclosure controls usable on a narrow screen', async ({ page }) => {
    await mockAdmin(page)
    await page.setViewportSize({ width: 390, height: 844 })
    await page.route('**/api/admin/audit-events*', async (route) =>
      fulfillPage(route, [auditRecord(1)], 1, 1),
    )
    await page.route('**/api/admin/flag-attempts*', (route) => fulfillPage(route, [], 0, 1))
    await page.route('**/api/admin/logs*', (route) => fulfillPage(route, [], 0, 1))

    await page.goto('/admin/logs')
    const operationTab = page.getByRole('tab', { name: '操作日志' })
    await expect(operationTab).toBeVisible()
    const summary = page.getByRole('button', { name: /管理员 1/ }).first()
    await expect(summary).toBeVisible()
    const bounds = await summary.boundingBox()
    expect(bounds).not.toBeNull()
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(390)
    await summary.click()
    await expect(page.getByText('目标用户 1', { exact: true })).toBeVisible()
  })
})
