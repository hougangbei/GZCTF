import { expect, test } from '@playwright/test'

for (const [viewer, canGrant] of [['Admin', true], ['delegated-admin', false]] as const) {
  test(`${viewer} ${canGrant ? 'can' : 'cannot'} select Admin for an ordinary user`, async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('en-US')))
    await page.route('**/api/**', route => route.fulfill({ json: [] }))
    await page.route('**/api/account/profile', route => route.fulfill({ json: {
      userId: '20000000-0000-0000-0000-000000000001', userName: viewer, role: 'Admin',
    } }))
    await page.route(/\/api\/admin\/users\?/, route => route.fulfill({ json: { data: [{
      id: '20000000-0000-0000-0000-000000000002', userName: 'Learner', email: 'learner@example.test', role: 'User', emailConfirmed: true,
    }], total: 1, length: 1 } }))
    await page.route('**/api/admin/registration-reviews**', route => route.fulfill({ json: { items: [], total: 0, page: 1, pageSize: 20 } }))
    await page.goto('/admin/users')
    await page.getByRole('row').filter({ hasText: 'Learner' }).getByRole('button').first().click()
    const adminOption = page.getByRole('dialog').getByRole('radio', { name: 'Admin', exact: true })
    if (canGrant) await expect(adminOption).toBeEnabled()
    else await expect(adminOption).toBeDisabled()
    await expect(page.getByRole('dialog').getByRole('radio', { name: 'Monitor', exact: true })).toBeEnabled()
  })
}
