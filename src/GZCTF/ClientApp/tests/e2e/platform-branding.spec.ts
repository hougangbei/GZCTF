import { expect, test } from '@playwright/test'

test('configured platform name is used across public pages and browser titles', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/config', route => route.fulfill({
    json: { title: 'NYNUSEC', portMapping: 'Default' },
  }))

  await page.goto('/')
  await expect(page.getByRole('link', { name: 'NYNUSEC', exact: true })).toBeVisible()
  await expect(page).toHaveTitle('NYNUSEC')
  await expect(page.locator('main')).toContainText('NYNUSEC — 练习路径')
  await expect(page.locator('main')).not.toContainText('GZCTF')

  await page.goto('/account/login')
  await expect(page.getByRole('link', { name: 'NYNUSEC', exact: true })).toBeVisible()
  await expect(page).toHaveTitle(/ - NYNUSEC$/)
  await expect(page.locator('main')).toContainText('使用现有 NYNUSEC 账户继续。')
  await expect(page.locator('main')).not.toContainText('GZCTF')
})

test('a long configured name stays readable without horizontal overflow on a phone', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 })
  await page.route('**/api/config', route => route.fulfill({
    json: { title: 'NYNUSEC Security Training Platform', portMapping: 'Default' },
  }))

  await page.goto('/')
  const brand = page.getByRole('link', { name: 'NYNUSEC Security Training Platform', exact: true })
  await expect(brand).toBeVisible()
  const brandBox = await brand.boundingBox()
  const themeBox = await page.getByRole('button', { name: 'Customize Color' }).boundingBox()
  expect(brandBox && themeBox && brandBox.x + brandBox.width <= themeBox.x - 4).toBe(true)
  expect(themeBox && themeBox.x + themeBox.width <= 320).toBe(true)
  const navigationBox = await page.getByRole('button', { name: 'Open navigation' }).boundingBox()
  expect(navigationBox && navigationBox.x + navigationBox.width <= 320).toBe(true)
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})

test('saving the platform name updates the header and subsequent pages', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  let platformName = 'Old Name'
  await page.route('**/api/config', route => route.fulfill({
    json: { title: platformName, portMapping: 'Default' },
  }))
  await page.route('**/api/account/profile', route => route.fulfill({
    json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'admin', role: 'Admin' },
  }))
  await page.route('**/api/admin/config', async route => {
    if (route.request().method() === 'PUT') {
      platformName = JSON.parse(route.request().postData() ?? '{}').globalConfig.title
      await route.fulfill({ status: 204 })
      return
    }
    await route.fulfill({ json: {
      globalConfig: { title: platformName, slogan: '', description: '', customTheme: '' },
      accountPolicy: {}, containerPolicy: {},
    } })
  })
  await page.route('**/api/admin/qq-bot/settings', route => route.fulfill({
    json: { enabled: false, baseUrl: '', groupId: '', messageTemplate: '{member}', accessTokenConfigured: false },
  }))

  await page.goto('/admin/settings')
  await page.getByRole('textbox', { name: '平台名称' }).fill('NYNUSEC')
  await page.getByRole('button', { name: '保存更改' }).click()
  await expect(page.getByRole('link', { name: 'NYNUSEC', exact: true })).toBeVisible()
  await page.goto('/account/login')
  await expect(page.getByRole('link', { name: 'NYNUSEC', exact: true })).toBeVisible()
  await expect(page).toHaveTitle(/ - NYNUSEC$/)
})
