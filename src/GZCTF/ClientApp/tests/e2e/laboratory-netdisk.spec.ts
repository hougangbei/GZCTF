import { expect, test } from '@playwright/test'

for (const mobile of [false, true]) {
  test(`netdisk link opens a new tab on ${mobile ? 'mobile' : 'desktop'}`, async ({ page, context }) => {
    await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
    if (mobile) await page.setViewportSize({ width: 390, height: 844 })
    await page.route('**/api/config', (route) =>
      route.fulfill({
        json: { title: 'NYNUSEC', laboratoryNetdiskUrl: 'https://pan.example.com/ctf', portMapping: 'Default' },
      })
    )
    await context.route('https://pan.example.com/**', (route) =>
      route.fulfill({ body: '<h1>Laboratory netdisk</h1>', contentType: 'text/html' })
    )
    await page.goto('/')
    if (mobile) await page.getByRole('button', { name: '打开导航' }).click()
    const link = page.getByRole('link', { name: '实验室网盘', exact: true })
    await expect(link).toBeVisible()
    await expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    const opened = context.waitForEvent('page')
    await link.click()
    const blog = await opened
    await expect(blog).toHaveURL('https://pan.example.com/ctf')
    expect(await blog.evaluate(() => window.opener === null)).toBe(true)
  })
}

for (const url of ['', 'javascript:alert(1)', 'ftp://pan.example.com']) {
  test(`netdisk entry is hidden for ${url || 'empty configuration'}`, async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
    await page.route('**/api/config', (route) =>
      route.fulfill({
        json: { title: 'NYNUSEC', laboratoryNetdiskUrl: url, portMapping: 'Default' },
      })
    )
    await page.goto('/')
    await expect(page.getByRole('link', { name: '实验室网盘', exact: true })).toHaveCount(0)
  })
}

test('admin can save and clear the URL; invalid protocols cannot be saved', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  let laboratoryNetdiskUrl = ''
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/config', (route) =>
    route.fulfill({ json: { title: 'NYNUSEC', laboratoryNetdiskUrl, portMapping: 'Default' } })
  )
  await page.route('**/api/account/profile', (route) => route.fulfill({ json: { userName: 'admin', role: 'Admin' } }))
  await page.route('**/api/admin/config', async (route) => {
    if (route.request().method() === 'PUT') {
      laboratoryNetdiskUrl = route.request().postDataJSON().globalConfig.laboratoryNetdiskUrl
      await route.fulfill({ status: 204 })
    } else {
      await route.fulfill({
        json: { globalConfig: { title: 'NYNUSEC', laboratoryNetdiskUrl }, accountPolicy: {}, containerPolicy: {} },
      })
    }
  })
  await page.route('**/api/admin/qq-bot/settings', (route) =>
    route.fulfill({
      json: { enabled: false, baseUrl: '', groupId: '', messageTemplate: '{member}', accessTokenConfigured: false },
    })
  )
  await page.goto('/admin/settings')
  const input = page.getByRole('textbox', { name: '实验室网盘链接', exact: true })
  const save = page.getByRole('button', { name: '保存更改' })
  await input.fill('javascript:alert(1)')
  await expect(save).toBeDisabled()
  await input.fill(' https://pan.example.com/ ')
  await save.click()
  await expect.poll(() => laboratoryNetdiskUrl).toBe('https://pan.example.com/')
  await expect(page.getByRole('link', { name: '实验室网盘', exact: true })).toHaveAttribute(
    'href',
    laboratoryNetdiskUrl
  )
  await page.reload()
  await expect(input).toHaveValue(laboratoryNetdiskUrl)
  await input.fill('')
  await save.click()
  await expect.poll(() => laboratoryNetdiskUrl).toBe('')
  await expect(page.getByRole('link', { name: '实验室网盘', exact: true })).toHaveCount(0)
})

test('blog and netdisk remain independent even when they share one URL', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/config', (r) =>
    r.fulfill({
      json: {
        title: 'NYNUSEC',
        laboratoryBlogUrl: 'https://pan.example.com/',
        laboratoryNetdiskUrl: 'https://pan.example.com/',
      },
    })
  )
  await page.goto('/')
  await expect(page.getByRole('link', { name: '实验室博客', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: '实验室网盘', exact: true })).toBeVisible()
})

for (const width of [1120, 1280, 1440]) {
  test(`header with both external links fits ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
    await page.route('**/api/config', (r) =>
      r.fulfill({
        json: {
          title: 'NYNUCTF',
          laboratoryBlogUrl: 'https://blog.example.com/',
          laboratoryNetdiskUrl: 'https://pan.example.com/',
        },
      })
    )
    await page.route('**/api/account/profile', (r) =>
      r.fulfill({ json: { userId: 'admin', userName: '管理员', role: 'Admin' } })
    )
    await page.goto('/')
    const link = page.getByRole('link', { name: '实验室网盘', exact: true })
    await expect(link).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
    const nav = await link.boundingBox(),
      actions = await page.getByRole('button', { name: '语言', exact: true }).boundingBox()
    expect(nav!.x + nav!.width).toBeLessThanOrEqual(actions!.x)
  })
}
