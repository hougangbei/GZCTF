import { expect, test } from '@playwright/test'

test('desktop uses a horizontal header instead of a side rail', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/')
  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(navigation).toBeVisible()
  expect(await navigation.evaluate(element => element.getBoundingClientRect().height)).toBeLessThan(90)
  expect(await page.getByRole('main').evaluate(element => element.getBoundingClientRect().left)).toBe(0)
  await expect(navigation.getByRole('link', { name: 'Skill trees' })).toBeVisible()
})

test('home has a hero with working GZCTF destinations', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1 })).toContainText('Security is learned by doing')
  await expect(page.getByRole('link', { name: 'Start learning' })).toHaveAttribute('href', '/skill-trees')
  await expect(page.getByRole('link', { name: 'Read posts' })).toHaveAttribute('href', '/posts')
  await expect(page.getByRole('heading', { name: 'Where to begin' })).toBeVisible()
})

test('login offers context beside the existing form on desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/account/login')
  const introduction = page.getByRole('heading', { name: /Welcome back/i })
  const username = page.getByRole('textbox', { name: /Username or Email/i })
  await expect(introduction).toBeVisible()
  await expect(username).toBeVisible()
  const introRight = await introduction.evaluate(element => element.getBoundingClientRect().right)
  const formLeft = await username.evaluate(element => element.getBoundingClientRect().left)
  expect(introRight).toBeLessThan(formLeft)
})

test('dark theme keeps the primary call to action readable', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: /dark/i }).first().click()
  const contrast = await page.getByRole('link', { name: 'Start learning' }).evaluate(element => {
    const rgb = (value: string) => (value.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number)
    const luminance = (color: number[]) => color.map(channel => {
      const value = channel / 255
      return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
    }).reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0)
    const style = getComputedStyle(element)
    const levels = [luminance(rgb(style.color)), luminance(rgb(style.backgroundColor))].sort((a, b) => b - a)
    return (levels[0] + 0.05) / (levels[1] + 0.05)
  })
  expect(contrast).toBeGreaterThanOrEqual(4.5)
})

test('email confirmation uses the shared account layout', async ({ page }) => {
  await page.goto('/account/pending')
  await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Start here.' })).toBeVisible()
  await expect(page.getByText('ctf@example.com')).toBeVisible()
  await expect(page.locator('form')).toHaveCount(0)
})

test('dark login placeholders remain readable', async ({ page }) => {
  await page.goto('/account/login')
  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: /dark/i }).first().click()
  const contrast = await page.getByRole('textbox', { name: /Username or Email/i }).evaluate(element => {
    const parse = (color: string) => (color.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number)
    const luminance = (color: number[]) => color.map(channel => {
      const value = channel / 255
      return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
    }).reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0)
    const levels = [luminance(parse(getComputedStyle(element, '::placeholder').color)), luminance(parse(getComputedStyle(element).backgroundColor))].sort((a, b) => b - a)
    return (levels[0] + 0.05) / (levels[1] + 0.05)
  })
  expect(contrast).toBeGreaterThanOrEqual(4.5)
})

test('administrator navigation fits without covering account controls', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 })
  await page.route('**/api/account/profile', route => route.fulfill({
    json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'admin', role: 'Admin' },
  }))
  await page.goto('/')
  await expect(page.getByRole('button', { name: 'Open navigation' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(1280)
})

test('administrator log and about pages remain available on narrow phones', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 })
  await page.route('**/api/account/profile', route => route.fulfill({
    json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'admin', role: 'Admin' },
  }))
  for (const route of ['/admin/logs', '/admin/about']) {
    await page.goto(route)
    await expect(page.getByText('Not wide enough')).toHaveCount(0)
    await expect(page.getByRole('main')).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320)
  }
})

test('administrator settings use readable single-column fields on a narrow phone', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 })
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/account/profile', route => route.fulfill({
    json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'admin', role: 'Admin' },
  }))
  await page.route('**/api/admin/**', route => route.fulfill({ json: [] }))
  await page.route('**/api/admin/qq-bot/settings', route => route.fulfill({
    json: { enabled: false, baseUrl: '', groupId: '', messageTemplate: '{member}', accessTokenConfigured: false },
  }))
  await page.goto('/admin/settings')
  const name = page.getByRole('textbox', { name: '平台名称' })
  await expect(name).toBeVisible()
  expect((await name.boundingBox())?.width).toBeGreaterThan(200)
  await expect(page.getByRole('button', { name: '保存更改' })).toHaveCSS('position', 'static')
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})

test('default port mapping does not probe an optional local reflector', async ({ page }) => {
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  await page.route('**/api/config', route => route.fulfill({
    json: { title: 'GZ', portMapping: 'Default' },
  }))
  await page.goto('/')
  await page.getByRole('heading', { level: 1 }).waitFor()
  await page.waitForTimeout(500)
  expect(errors.filter(error => /wsrx|daemon/i.test(error))).toEqual([])
})
