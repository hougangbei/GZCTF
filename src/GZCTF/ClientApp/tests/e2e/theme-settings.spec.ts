import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

const mockPlatformColor = async (page: Page, color: string) => {
  await page.route('**/api/config', route => route.fulfill({
    json: { title: 'NYNUSEC', customTheme: color, portMapping: 'Default' },
  }))
}

test('managed accent colors the homepage and Mantine controls consistently', async ({ page }) => {
  await mockPlatformColor(page, '#E05822')
  await page.goto('/account/login')
  const loginColor = await page.getByRole('button', { name: 'Login', exact: true })
    .evaluate(element => getComputedStyle(element).backgroundColor)
  await page.goto('/')
  const homeColor = await page.getByRole('link', { name: 'Start learning' })
    .evaluate(element => getComputedStyle(element).backgroundColor)

  expect(homeColor).toBe(loginColor)
  expect(homeColor).not.toBe('rgb(99, 102, 241)')
})

test('managed accent keeps page text and buttons readable in both modes', async ({ page }) => {
  await mockPlatformColor(page, '#E05822')
  for (const mode of ['light', 'dark']) {
    await page.addInitScript(value => localStorage.setItem('mantine-color-scheme-value', value), mode)
    await page.goto('/')
    await page.getByRole('link', { name: 'Start learning' }).waitFor()
    await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', mode)
    const ratios = await page.evaluate(() => {
      const channels = (value: string) => (value.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number)
      const luminance = (value: string) => channels(value).map(channel => {
        const normalized = channel / 255
        return normalized <= 0.04045 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4
      }).reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0)
      const ratio = (a: string, b: string) => {
        const [higher, lower] = [luminance(a), luminance(b)].sort((first, second) => second - first)
        return (higher + 0.05) / (lower + 0.05)
      }
      const button = getComputedStyle(document.querySelector('main a[href="/skill-trees"]')!)
      const hero = getComputedStyle(document.querySelector('h1 em')!)
      const body = getComputedStyle(document.body)
      return {
        button: ratio(button.color, button.backgroundColor),
        hero: ratio(hero.color, body.backgroundColor),
      }
    })
    expect(ratios.button).toBeGreaterThanOrEqual(4.5)
    expect(ratios.hero).toBeGreaterThanOrEqual(4.5)
  }
})

test('system, light and dark choices persist and follow the operating system', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' })
  await page.goto('/')
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'dark')

  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'light mode', exact: true }).click()
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'light')
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'light')

  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'System' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'dark')
})

test('color scheme is set before the client application loads', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' })
  await page.route('**/*', route =>
    route.request().resourceType() === 'script' ? route.abort() : route.continue())
  await page.goto('/')
  await expect(page.locator('#root')).toBeEmpty()
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'dark')
})

test('custom accent is saved only on Apply and rejects low contrast colors', async ({ page }) => {
  await page.goto('/')
  const primary = page.getByRole('link', { name: 'Start learning' })
  const original = await primary.evaluate(element => getComputedStyle(element).backgroundColor)
  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'Customize Color' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('radio', { name: 'Customized' }).check()
  await dialog.getByRole('textbox', { name: 'Custom Base Color' }).fill('#FFFFFF')
  await expect(dialog.getByRole('button', { name: 'Apply' })).toBeDisabled()
  await dialog.getByRole('textbox', { name: 'Custom Base Color' }).fill('#E05822')
  await expect(dialog.getByRole('button', { name: 'Apply' })).toBeEnabled()
  expect(await primary.evaluate(element => getComputedStyle(element).backgroundColor)).toBe(original)
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  expect(await primary.evaluate(element => getComputedStyle(element).backgroundColor)).toBe(original)

  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'Customize Color' }).click()
  await dialog.getByRole('radio', { name: 'Customized' }).check()
  await dialog.getByRole('textbox', { name: 'Custom Base Color' }).fill('#E05822')
  await dialog.getByRole('button', { name: 'Apply' }).click()
  await expect.poll(() => page.evaluate(() => localStorage.getItem('custom-theme'))).toBe('#E05822')
  await expect.poll(() => primary.evaluate(element => {
    const [red, , blue] = (getComputedStyle(element).backgroundColor.match(/\d+/g) ?? []).map(Number)
    return red > blue
  })).toBe(true)
  await page.reload()
  await expect.poll(() => page.evaluate(() => localStorage.getItem('custom-theme'))).toBe('#E05822')
  await expect.poll(() => primary.evaluate(element => {
    const [red, , blue] = (getComputedStyle(element).backgroundColor.match(/\d+/g) ?? []).map(Number)
    return red > blue
  })).toBe(true)
})

test('preview switches between personal and administrator palettes', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('custom-theme', '#315EAC'))
  await mockPlatformColor(page, '#E05822')
  await page.goto('/')
  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'Customize Color' }).click()
  const dialog = page.getByRole('dialog')
  const swatch = dialog.locator('.mantine-ColorSwatch-colorOverlay').first()
  await expect(swatch).toBeVisible()
  const personal = await swatch.evaluate(element => getComputedStyle(element).backgroundColor)
  await dialog.getByRole('radio', { name: 'Admin Provided' }).check()
  await expect.poll(() => swatch.evaluate(element => getComputedStyle(element).backgroundColor))
    .not.toBe(personal)
})

test('personal accent overrides the managed color until Managed is applied', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('custom-theme', '#315EAC'))
  await mockPlatformColor(page, '#E05822')
  await page.goto('/')
  const primary = page.getByRole('link', { name: 'Start learning' })
  const blueAccent = () => primary.evaluate(element => {
    const [red, , blue] = (getComputedStyle(element).backgroundColor.match(/\d+/g) ?? []).map(Number)
    return blue > red
  })
  await expect.poll(blueAccent).toBe(true)
  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'Customize Color' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('radio', { name: 'Admin Provided' }).check()
  await dialog.getByRole('button', { name: 'Apply' }).click()
  await expect.poll(() => primary.evaluate(element => {
    const [red, , blue] = (getComputedStyle(element).backgroundColor.match(/\d+/g) ?? []).map(Number)
    return red > blue
  })).toBe(true)
  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'Customize Color' }).click()
  await dialog.getByRole('radio', { name: 'Default' }).check()
  await dialog.getByRole('button', { name: 'Apply' }).click()
  await expect.poll(blueAccent).toBe(true)
  expect(await page.evaluate(() => localStorage.getItem('custom-theme'))).toBe('brand')
})

test('administrator cannot save an unreadable platform accent', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/account/profile', route => route.fulfill({
    json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'admin', role: 'Admin' },
  }))
  await page.route('**/api/admin/config', route => route.fulfill({ json: {
    globalConfig: { title: 'NYNUSEC', slogan: '', customTheme: '' },
    accountPolicy: {}, containerPolicy: {},
  } }))
  await page.route('**/api/admin/qq-bot/settings', route => route.fulfill({
    json: { enabled: false, baseUrl: '', groupId: '', messageTemplate: '{member}', accessTokenConfigured: false },
  }))

  await page.goto('/admin/settings')
  const accent = page.getByRole('textbox', { name: '平台颜色' })
  await accent.fill('#FFFFFF')
  await expect(page.getByText('该颜色对比度不足')).toBeVisible()
  await expect(page.getByRole('button', { name: '保存更改' })).toBeDisabled()
  await accent.fill('#E05822')
  await expect(page.getByRole('button', { name: '保存更改' })).toBeEnabled()
})

test('theme settings fit a narrow screen and close with Escape', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 })
  await page.goto('/')
  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'Customize Color' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('button', { name: 'Apply' })).toBeVisible()
  const box = await dialog.boundingBox()
  expect(box && box.x >= 0 && box.x + box.width <= 320).toBe(true)
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
})
