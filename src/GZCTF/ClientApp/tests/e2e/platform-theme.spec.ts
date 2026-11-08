import { expect, test, type Page } from '@playwright/test'

const rgb = (value: string) =>
  (value.match(/[\d.]+/g) ?? [])
    .slice(0, 3)
    .map(Number)
    .map((channel) => (value.startsWith('color(srgb') ? channel * 255 : channel))
const contrast = (first: string, second: string) => {
  const luminance = (value: string) =>
    rgb(value)
      .map((channel) => channel / 255)
      .map((channel) => (channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4))
      .reduce((sum, channel, i) => sum + channel * [0.2126, 0.7152, 0.0722][i], 0)
  const sorted = [luminance(first), luminance(second)].sort((a, b) => b - a)
  return (sorted[0] + 0.05) / (sorted[1] + 0.05)
}
async function setup(page: Page, scheme: 'light' | 'dark', custom = 'brand') {
  await page.addInitScript(
    ({ scheme, custom }) => {
      localStorage.setItem('language', JSON.stringify('zh-CN'))
      localStorage.setItem('mantine-color-scheme-value', scheme)
      localStorage.setItem('custom-theme', custom)
    },
    { scheme, custom }
  )
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'theme-admin', userName: 'Admin', role: 'Admin' } })
  )
  await page.route('**/api/config', (route) => route.fulfill({ json: { title: 'NYNUCTF', customTheme: '#18804b' } }))
  await page.route('**/api/admin/skills', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/my-learning/skill-trees/theme-tree', (route) =>
    route.fulfill({
      json: {
        totalCount: 14,
        completedCount: 0,
        completedLessonIds: [],
        solvedChallengeIds: [],
        categories: [],
        skills: [],
      },
    })
  )
  await page.route('**/api/skill-trees', (route) =>
    route.fulfill({
      json: [
        {
          skillTreeId: 'theme-tree',
          name: 'Web 安全学习路线',
          summary: '从基础概念到实践练习，循序渐进完成课程。',
          iconKey: 'web',
          categoryCount: 2,
          skillCount: 4,
          challengeCount: 8,
          lessonCount: 6,
        },
      ],
    })
  )
}
async function measured(page: Page, selector: string) {
  return page
    .locator(selector)
    .first()
    .evaluate((element) => {
      const style = getComputedStyle(element)
      const placeholder = getComputedStyle(element, '::placeholder')
      let current: Element | null = element
      let background = 'rgb(255, 255, 255)'
      while (current) {
        const color = getComputedStyle(current).backgroundColor
        if (color !== 'rgba(0, 0, 0, 0)' && color !== 'transparent') {
          background = color
          break
        }
        current = current.parentElement
      }
      return { background, ink: style.color, border: style.borderTopColor, placeholder: placeholder.color }
    })
}
for (const scheme of ['light', 'dark'] as const) {
  for (const custom of ['brand', '#315EAC', '']) {
    test(`${scheme} ${custom || 'managed'} neutrals and inputs remain readable across public and admin`, async ({
      page,
    }, testInfo) => {
      await setup(page, scheme, custom)
      await page.goto('/admin/imports')
      const input = page.getByRole('textbox', { name: '来源标识', exact: true })
      await expect(input).toBeVisible()
      const field = await measured(page, 'input[placeholder="gzctf-s7"]')
      expect(contrast(field.ink, field.background)).toBeGreaterThanOrEqual(4.5)
      expect(contrast(field.placeholder, field.background)).toBeGreaterThanOrEqual(4.5)
      expect(contrast(field.border, field.background)).toBeGreaterThanOrEqual(3)
      const disabled = await measured(page, 'button:disabled')
      expect(contrast(disabled.ink, disabled.background)).toBeGreaterThanOrEqual(4.5)
      const body = await measured(page, 'body')
      expect(Math.max(...rgb(body.background)) - Math.min(...rgb(body.background))).toBeLessThanOrEqual(8)
      await input.fill('campus-gzctf')
      await expect.poll(() => input.evaluate(element => getComputedStyle(element).outlineOffset)).toBe('-1px')
      await page.setViewportSize({ width: 1280, height: 900 })
      if (custom === 'brand')
        await page.screenshot({ path: testInfo.outputPath(`admin-${scheme}.png`), fullPage: true })
      await page.goto('/skill-trees')
      await expect(page.getByRole('link', { name: /Web 安全学习路线/ })).toBeVisible()
      const publicBody = await measured(page, 'body')
      expect(publicBody.background).toBe(body.background)
      expect(contrast(publicBody.ink, publicBody.background)).toBeGreaterThanOrEqual(7)
      if (custom === 'brand')
        await page.screenshot({ path: testInfo.outputPath(`learning-${scheme}.png`), fullPage: true })
      await page.setViewportSize({ width: 360, height: 800 })
      expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
    })
  }
}

test('system theme changes update the complete neutral palette without reloading', async ({ page }) => {
  await setup(page, 'light')
  await page.addInitScript(() => localStorage.setItem('mantine-color-scheme-value', 'auto'))
  await page.emulateMedia({ colorScheme: 'light' })
  await page.goto('/admin/imports')
  await expect(page.getByRole('textbox', { name: '来源标识', exact: true })).toBeVisible()
  const before = await measured(page, 'body')
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'dark')
  const after = await measured(page, 'body')
  expect(after.background).not.toBe(before.background)
  const field = await measured(page, 'input[placeholder="gzctf-s7"]')
  expect(contrast(field.placeholder, field.background)).toBeGreaterThanOrEqual(4.5)
})

for (const scheme of ['light', 'dark'] as const) {
  test(`${scheme} accent text remains readable on selected navigation and soft action buttons`, async ({ page }) => {
    await setup(page, scheme, '')
    await page.goto('/admin/imports')
    await expect(page.getByRole('textbox', { name: '来源标识', exact: true })).toBeVisible()
    for (const selector of ['a[aria-current="page"][href="/admin/imports"]', 'a[href="/admin/imports/history"]']) {
      const colors = await measured(page, selector)
      expect(contrast(colors.ink, colors.background)).toBeGreaterThanOrEqual(4.5)
    }
  })
}

for (const scheme of ['light', 'dark'] as const) {
  test(`${scheme} error and unstyled input semantics survive global theme styling`, async ({ page }) => {
    await setup(page, scheme)
    await page.goto('/admin/imports')
    const field = page.getByRole('textbox', { name: '来源标识', exact: true })
    await field.fill('中文来源')
    await expect(field).toHaveAttribute('data-error')
    await expect.poll(async () => {
      const channels = rgb((await measured(page, 'input[placeholder="gzctf-s7"]')).border)
      return channels[0] - channels[1]
    }).toBeGreaterThan(0)
    const colors = await measured(page, 'input[placeholder="gzctf-s7"]')
    expect(contrast(colors.ink, colors.background)).toBeGreaterThanOrEqual(4.5)
    const border = rgb(colors.border)
    expect(border[0]).toBeGreaterThan(border[1])
    await page.getByRole('button', { name: '主题设置', exact: true }).click()
    // Theme menu keeps the existing personal color and scheme controls.
    await expect(page.getByRole('menu')).toBeVisible()
    await page.getByRole('menuitem', { name: '主题设置', exact: true }).click()
    await page.getByRole('dialog').getByRole('radio', { name: '自定义', exact: true }).check()
    const palette = page.getByRole('dialog').locator('.mantine-Input-input[data-variant="unstyled"]')
    await expect(palette).toBeVisible()
    expect(await palette.evaluate((el) => getComputedStyle(el).backgroundColor)).toBe('rgba(0, 0, 0, 0)')
    await page.keyboard.press('Escape')
  })
}

for (const [scheme, custom] of [
  ['light', '#0161f1'],
  ['dark', '#012066'],
] as const) {
  test(`${scheme} soft action hover uses the neutral tint and accessible custom foreground`, async ({ page }) => {
    await setup(page, scheme, custom)
    await page.goto('/admin/imports')
    const action = page.getByRole('link', { name: '历史数据档案', exact: true })
    await expect(action).toBeVisible()
    const computed = await action.evaluate((element) => {
      const root = getComputedStyle(document.documentElement)
      return root.getPropertyValue('--mantine-color-custom-light-hover').trim()
    })
    expect(computed).toContain('color-mix')
    await action.hover()
    const colors = await measured(page, 'a[href="/admin/imports/history"]')
    expect(contrast(colors.ink, colors.background)).toBeGreaterThanOrEqual(4.5)
  })
}
