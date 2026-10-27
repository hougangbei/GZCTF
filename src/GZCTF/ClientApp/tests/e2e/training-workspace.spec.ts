import { expect, test, type Page } from '@playwright/test'

const treeId = '01990000-0000-7000-8000-000000000001'
const categoryId = '01990000-0000-7000-8000-000000000002'
const challengeId = '01990000-0000-7000-8000-000000000003'
const challengePath = `/skill-trees/${treeId}/${categoryId}/challenge/${challengeId}`

async function setup(page: Page, options: { category?: string; expired?: boolean; dark?: boolean } = {}) {
  await page.addInitScript((dark) => {
    localStorage.setItem('language', JSON.stringify('zh-CN'))
    localStorage.setItem('mantine-color-scheme-value', dark ? 'dark' : 'light')
  }, options.dark ?? true)
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'learner', userName: 'Learner', role: 'User' } })
  )
  const tree = {
    skillTreeId: treeId,
    name: 'Web 安全基础',
    summary: '从原理到实践，逐步掌握安全技能。',
    iconKey: 'shield',
    categoryCount: 1,
    challengeCount: 1,
    categories: [
      {
        categoryId,
        name: 'Web 漏洞入门',
        summary: '通过真实题目理解常见漏洞',
        iconKey: 'web',
        contents: [
          {
            contentId: challengeId,
            kind: 'challenge',
            title: 'HTTP 请求与身份验证',
            summary: '分析请求，找到应用认证逻辑中的薄弱环节。',
            difficulty: 'Normal',
            expectedMinutes: 30,
          },
        ],
      },
    ],
  }
  await page.route('**/api/skill-trees', (route) => route.fulfill({ json: [tree] }))
  await page.route(`**/api/skill-trees/${treeId}`, (route) => route.fulfill({ json: tree }))
  await page.route(`**/api/challenges/${challengeId}?locale=*`, (route) =>
    route.fulfill({
      json: {
        id: challengeId,
        title: 'HTTP 请求与身份验证',
        summary: '',
        body: '这是一套用于练习身份验证的应用。分析请求并找到 Flag。\n\n**目标：** 理解服务端如何验证用户身份。',
        type: 'DynamicContainer',
        ctfCategory: options.category ?? 'Web',
        hasContainer: true,
        hasWriteup: false,
        hintLocaleCount: 0,
      },
    })
  )
  const now = Date.now()
  let expiry = now + (options.expired ? -1000 : 3600000)
  let reads = 0
  let stopped = false
  await page.route(`**/api/challenges/${challengeId}/instances`, (route) => {
    if (route.request().method() === 'DELETE') {
      stopped = true
      return route.fulfill({ status: 204 })
    }
    reads++
    return route.fulfill(
      stopped || (options.expired && reads > 1)
        ? { status: 404, json: {} }
        : {
            json: {
              id: 'instance-one',
              status: 1,
              expiresAtUtc: expiry,
              publicIp: '192.168.1.160',
              publicPort: 32769,
            },
          }
    )
  })
  await page.route(`**/api/challenges/${challengeId}/instances/extend`, (route) => {
    expiry += 3600000
    return route.fulfill({
      json: { id: 'instance-one', status: 1, expiresAtUtc: expiry, publicIp: '192.168.1.160', publicPort: 32769 },
    })
  })
  return { now, reads: () => reads }
}

test('running instance counts down, extends and survives modal reopen; copy and Web entry work', async ({
  page,
  context,
}) => {
  const state = await setup(page)
  await page.clock.setFixedTime(state.now)
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await page.goto(challengePath)
  const timer = page.getByRole('timer')
  await expect(timer).toHaveText('01:00:00')
  await page.clock.setFixedTime(state.now + 61000)
  await expect(timer).toHaveText('00:58:59')
  await expect(page.getByRole('link', { name: '打开题目' })).toHaveAttribute('href', 'http://192.168.1.160:32769/')
  await page.getByRole('button', { name: '复制地址', exact: true }).click()
  await expect(page.getByRole('button', { name: '已复制', exact: true })).toBeVisible()
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('192.168.1.160:32769')
  await page.getByRole('button', { name: '延长', exact: true }).click()
  await expect(timer).toContainText('01:58:59')
  await page.keyboard.press('Escape')
  await page.getByRole('link', { name: /HTTP 请求与身份验证/ }).click()
  await expect(timer).toHaveText('01:58:59')
  await page.getByRole('button', { name: '停止', exact: true }).click()
  await expect(page.getByRole('button', { name: '启动', exact: true })).toBeVisible()
  await expect(timer).toHaveCount(0)
})

test('expired instance reconciles with backend and TCP instance has no browser entry', async ({ page }) => {
  const state = await setup(page, { expired: true, category: 'Pwn' })
  await page.goto(challengePath)
  await expect(page.getByRole('button', { name: '启动', exact: true })).toBeVisible()
  expect(state.reads()).toBeGreaterThan(1)
  await expect(page.getByRole('link', { name: '打开题目' })).toHaveCount(0)
})

test('plain HTTP fallback copies TCP address and exposes manual copy if denied', async ({ page }) => {
  await setup(page, { category: 'Pwn' })
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'clipboard', { value: undefined })
    document.execCommand = (command) => command === 'copy'
  })
  await page.goto(challengePath)
  await expect(page.getByRole('link', { name: '打开题目' })).toHaveCount(0)
  await page.getByRole('button', { name: '复制地址', exact: true }).click()
  await expect(page.getByRole('button', { name: '已复制', exact: true })).toBeVisible()
  await page.evaluate(() => {
    document.execCommand = () => false
  })
  await page.getByRole('button', { name: '已复制', exact: true }).click()
  await expect(page.getByText('复制失败，请选中上方地址手动复制。')).toBeVisible()
})

for (const dark of [true, false]) {
  test(`compact grids and modal fit desktop and phone (${dark ? 'dark' : 'light'})`, async ({ page }, testInfo) => {
    await setup(page, { dark })
    await page.setViewportSize({ width: 1280, height: 960 })
    await page.goto('/skill-trees')
    const tree = page.getByRole('link', { name: /Web 安全基础/ })
    expect((await tree.boundingBox())!.width).toBeLessThan(450)
    await tree.click()
    const category = page.getByRole('link', { name: /Web 漏洞入门/ })
    expect((await category.boundingBox())!.width).toBeLessThan(450)
    await category.click()
    const challenge = page.getByRole('link', { name: /HTTP 请求与身份验证/ })
    expect((await challenge.boundingBox())!.width).toBeLessThan(450)
    await page.screenshot({ path: testInfo.outputPath('tiles-desktop.png'), fullPage: true })
    await challenge.click()
    await expect(page.getByRole('timer')).toBeVisible()
    await page.getByRole('dialog').screenshot({ path: testInfo.outputPath('modal-desktop.png') })
    await page.setViewportSize({ width: 390, height: 844 })
    const dialog = page.getByRole('dialog')
    expect((await dialog.boundingBox())!.width).toBeLessThanOrEqual(390)
    expect(await dialog.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(1)
    await page.getByRole('button', { name: '复制地址', exact: true }).focus()
    await expect(page.getByRole('button', { name: '复制地址', exact: true })).toBeFocused()
    await dialog.screenshot({ path: testInfo.outputPath('modal-mobile.png') })
    await page.getByRole('tab', { name: '提交 WP', exact: true }).click()
    const wpTitle = page.getByRole('textbox', { name: 'WP 标题', exact: true })
    await wpTitle.scrollIntoViewIfNeeded()
    await expect(wpTitle).toBeInViewport()
    expect(await dialog.evaluate(el => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(1)
    await page.keyboard.press('Escape')
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
  })
}

test('tree and category creation offer searchable icons with a persistent selected preview', async ({
  page,
}, testInfo) => {
  await setup(page)
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } })
  )
  await page.goto('/admin/skill-trees')
  await page.getByRole('button', { name: '创建技能树', exact: true }).first().click()
  let dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('group', { name: '图标', exact: true }).getByRole('button')).toHaveCount(40)
  await dialog.getByRole('textbox', { name: '搜索图标' }).fill('terminal')
  await dialog.getByRole('button', { name: '终端', exact: true }).click()
  await expect(dialog.getByRole('button', { name: '终端', exact: true })).toHaveAttribute('aria-pressed', 'true')
  await dialog.getByRole('textbox', { name: '搜索图标' }).fill('找不到的图标')
  await expect(dialog.getByText('没有匹配的图标')).toBeVisible()
  await expect(dialog.getByText('当前选择 · 💻 终端')).toBeVisible()
  await dialog.getByRole('textbox', { name: '搜索图标' }).fill('')
  await dialog.screenshot({ path: testInfo.outputPath('icon-picker.png') })
  await page.keyboard.press('Escape')
  await page.goto('/admin/skill-trees?tab=categories')
  await page.getByRole('button', { name: '创建类别', exact: true }).first().click()
  dialog = page.getByRole('dialog')
  await dialog.getByRole('textbox', { name: '搜索图标' }).fill('network')
  await dialog.getByRole('button', { name: '网络', exact: true }).click()
  await expect(dialog.getByText('当前选择 · 🌐 网络')).toBeVisible()
})

test('clipboard fallback writes the address when the modern API refuses access', async ({ page, context }) => {
  await setup(page, { category: 'Pwn' })
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await page.goto(challengePath)
  await page.evaluate(() => {
    Object.defineProperty(navigator.clipboard, 'writeText', { value: async () => { throw new DOMException('denied', 'NotAllowedError') } })
  })
  await page.getByRole('button', { name: '复制地址', exact: true }).click()
  await expect(page.getByRole('button', { name: '已复制', exact: true })).toBeVisible()
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('192.168.1.160:32769')
})

test('maximum-length unbroken titles stay inside compact phone cards', async ({ page }) => {
  await setup(page)
  await page.setViewportSize({ width: 390, height: 844 })
  const title = 'W'.repeat(128)
  const summary = 'S'.repeat(1024)
  const tree = { skillTreeId: treeId, name: title, summary, iconKey: 'web',
    categories: [{ categoryId, name: title, summary, iconKey: 'web',
      contents: [{ contentId: challengeId, kind: 'challenge', title, summary, difficulty: 'Normal', expectedMinutes: 1440 }] }] }
  await page.route('**/api/skill-trees', route => route.fulfill({ json: [tree] }))
  await page.route(`**/api/skill-trees/${treeId}`, route => route.fulfill({ json: tree }))
  for (const url of ['/skill-trees', `/skill-trees/${treeId}`, `/skill-trees/${treeId}?category=${categoryId}`]) {
    await page.goto(url)
    const card = page.getByRole('link', { name: new RegExp(title) }).last()
    await expect(card).toBeVisible()
    expect(await card.evaluate(el => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(1)
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
  }
})
