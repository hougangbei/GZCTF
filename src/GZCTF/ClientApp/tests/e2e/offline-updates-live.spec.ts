import { expect, test } from '@playwright/test'
import path from 'node:path'

// This test replaces an application. Never run implicitly or against a shared deployment.
test('isolated host accepts a browser package and completes after the page closes', async ({ browser, baseURL }) => {
  const packagePath = process.env.E2E_OFFLINE_PACKAGE_PATH
  const password = process.env.E2E_ADMIN_PASSWORD
  test.skip(
    process.env.E2E_OFFLINE_UPDATE_QA !== 'isolated' || !packagePath || !password,
    'Requires an explicitly isolated deployment, host updater, candidate package and QA administrator'
  )
  const host = new URL(baseURL!).hostname
  expect(['127.0.0.1', 'localhost', '[::1]']).toContain(host)
  test.setTimeout(300000)
  const context = await browser.newContext({ baseURL })
  await context.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  try {
    const login = await context.request.post('/api/account/login', {
      data: { userName: process.env.E2E_ADMIN_USER ?? 'Admin', password },
    })
    expect(login.ok()).toBeTruthy()
    const page = await context.newPage()
    await page.goto('/admin/updates')
    await expect(page.getByRole('heading', { name: '离线更新包', exact: true })).toBeVisible()
    await page.getByLabel('选择更新包', { exact: true }).setInputFiles(packagePath!)
    await expect(page.locator('input[type=radio]:checked')).toHaveAttribute('aria-label', path.basename(packagePath!), {
      timeout: 90000,
    })
    if (process.env.E2E_OFFLINE_CHECKSUM_PATH)
      await page
        .getByLabel('选择校验文件（可选）', { exact: true })
        .setInputFiles(process.env.E2E_OFFLINE_CHECKSUM_PATH)
    await page.getByRole('button', { name: '检查更新包', exact: true }).click()
    await expect(page.getByRole('button', { name: '备份并更新', exact: true })).toBeEnabled({ timeout: 120000 })
    await page.getByRole('button', { name: '备份并更新', exact: true }).click()
    await page.getByRole('checkbox', { name: '我已暂停平台入口并通知学员维护' }).check()
    await Promise.all([
      page.waitForResponse((response) => response.url().endsWith('/apply') && response.status() === 202),
      page.getByRole('button', { name: '确认更新', exact: true }).click(),
    ])
    await page.close()
    await expect
      .poll(
        async () => {
          try {
            const response = await context.request.get('/api/admin/updates', { timeout: 5000 })
            return response.ok() ? (await response.json()).phase : 'disconnected'
          } catch {
            return 'disconnected'
          }
        },
        { timeout: 180000, intervals: [1000, 2000, 3000] }
      )
      .toBe('succeeded')
    const reopened = await context.newPage()
    await reopened.goto('/admin/updates')
    await expect(reopened.getByRole('heading', { name: '离线更新包', exact: true })).toBeVisible()
  } finally {
    await context.close()
  }
})
