import { expect, test } from '@playwright/test'

for (const mobile of [false, true]) {
  test(`blog link opens a new tab on ${mobile ? 'mobile' : 'desktop'}`, async ({ page, context }) => {
    await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
    if (mobile) await page.setViewportSize({ width: 390, height: 844 })
    await page.route('**/api/config', route => route.fulfill({
      json: { title: 'NYNUSEC', laboratoryBlogUrl: 'https://blog.example.com/ctf', portMapping: 'Default' },
    }))
    await context.route('https://blog.example.com/**', route => route.fulfill({ body: '<h1>Laboratory blog</h1>', contentType: 'text/html' }))
    await page.goto('/')
    if (mobile) await page.getByRole('button', { name: '打开导航' }).click()
    const link = page.getByRole('link', { name: '实验室博客', exact: true })
    await expect(link).toBeVisible()
    await expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    const opened = context.waitForEvent('page')
    await link.click()
    const blog = await opened
    await expect(blog).toHaveURL('https://blog.example.com/ctf')
    expect(await blog.evaluate(() => window.opener === null)).toBe(true)
  })
}

for (const url of ['', 'javascript:alert(1)', 'ftp://blog.example.com']) {
  test(`blog entry is hidden for ${url || 'empty configuration'}`, async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
    await page.route('**/api/config', route => route.fulfill({
      json: { title: 'NYNUSEC', laboratoryBlogUrl: url, portMapping: 'Default' },
    }))
    await page.goto('/')
    await expect(page.getByRole('link', { name: '实验室博客', exact: true })).toHaveCount(0)
  })
}

test('admin can save and clear the URL; invalid protocols cannot be saved', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  let laboratoryBlogUrl = ''
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/config', route => route.fulfill({ json: { title: 'NYNUSEC', laboratoryBlogUrl, portMapping: 'Default' } }))
  await page.route('**/api/account/profile', route => route.fulfill({ json: { userName: 'admin', role: 'Admin' } }))
  await page.route('**/api/admin/config', async route => {
    if (route.request().method() === 'PUT') {
      laboratoryBlogUrl = route.request().postDataJSON().globalConfig.laboratoryBlogUrl
      await route.fulfill({ status: 204 })
    } else {
      await route.fulfill({ json: { globalConfig: { title: 'NYNUSEC', laboratoryBlogUrl }, accountPolicy: {}, containerPolicy: {} } })
    }
  })
  await page.route('**/api/admin/qq-bot/settings', route => route.fulfill({ json: { enabled: false, baseUrl: '', groupId: '', messageTemplate: '{member}', accessTokenConfigured: false } }))
  await page.goto('/admin/settings')
  const input = page.getByRole('textbox', { name: '实验室博客链接', exact: true })
  const save = page.getByRole('button', { name: '保存更改' })
  await input.fill('javascript:alert(1)')
  await expect(save).toBeDisabled()
  await input.fill(' https://blog.example.com/ ')
  await save.click()
  await expect.poll(() => laboratoryBlogUrl).toBe('https://blog.example.com/')
  await expect(page.getByRole('link', { name: '实验室博客', exact: true })).toHaveAttribute('href', laboratoryBlogUrl)
  await page.reload()
  await expect(input).toHaveValue(laboratoryBlogUrl)
  await input.fill('')
  await save.click()
  await expect.poll(() => laboratoryBlogUrl).toBe('')
  await expect(page.getByRole('link', { name: '实验室博客', exact: true })).toHaveCount(0)
})
