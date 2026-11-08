import { expect, test, type Page } from '@playwright/test'

const treeId = '01990000-0000-7000-8000-000000000001'
const categoryId = '01990000-0000-7000-8000-000000000002'
const challengeId = '01990000-0000-7000-8000-000000000003'
const challengePath = `/skill-trees/${treeId}/${categoryId}/challenge/${challengeId}`

async function setup(page: Page, options: { category?: string; expired?: boolean; dark?: boolean } = {}) {
  await page.addInitScript((dark) => {
    localStorage.setItem('mantine-color-scheme-value', dark ? 'dark' : 'light')
  }, options.dark ?? true)
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/my-learning/skill-trees/*',route=>route.fulfill({json:{totalCount:1,completedCount:0,completedLessonIds:[],solvedChallengeIds:[],categories:[],skills:[]}}))
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
  for (const category of tree.categories) Object.assign(category, {skills:[{skillId:'default-skill',name:'入门技能',summary:'',iconKey:'web',sortOrder:0,challengeCount:category.contents.length,lessonCount:0,contents:category.contents}]})
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

// Read the application theme as a rendered color so aliases resolve exactly as they do in controls.
async function themeColor(page: Page, variable: string) {
  return page.evaluate((variable) => {
    const probe = document.createElement('span')
    probe.style.color = `var(${variable})`
    document.body.append(probe)
    const color = getComputedStyle(probe).color
    probe.remove()
    return color
  }, variable)
}

async function applyAccent(page: Page, provider: 'Customized' | 'Default' | 'Admin Provided') {
  await page.getByRole('button', { name: 'Customize Color' }).click()
  await page.getByRole('menuitem', { name: 'Customize Color' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('radio', { name: provider, exact: true }).check()
  if (provider === 'Customized') await dialog.getByRole('textbox', { name: 'Custom Base Color' }).fill('#315EAC')
  await dialog.getByRole('button', { name: 'Apply', exact: true }).click()
}

test('skill tree accents follow live personal, default and managed choices', async ({ page }) => {
  await setup(page, { dark: false })
  await page.route('**/api/config', (route) =>
    route.fulfill({ json: { title: 'Theme test', customTheme: '#E05822', portMapping: 'Default' } })
  )
  await page.goto('/skill-trees')
  const card = page.getByRole('link', { name: /Web 安全基础/ })
  const accent = card.locator('span').last()
  await expect(card).toBeVisible()
  const modalPage = await page.context().newPage()
  await setup(modalPage, { dark: false })
  await modalPage.route('**/api/config', (route) =>
    route.fulfill({ json: { title: 'Theme test', customTheme: '#E05822', portMapping: 'Default' } })
  )
  await modalPage.goto(challengePath)
  const challengeDialog = modalPage.getByRole('dialog')
  await challengeDialog.getByRole('textbox').first().fill('flag{theme-test}')
  const colors: string[] = []
  for (const provider of ['Customized', 'Default', 'Admin Provided'] as const) {
    await applyAccent(page, provider)
    const expected = await themeColor(page, '--mantine-primary-color-filled')
    colors.push(expected)
    await expect
      .poll(() =>
        challengeDialog
          .getByRole('button', { name: 'Submit flag', exact: true })
          .evaluate((element) => getComputedStyle(element).backgroundColor)
      )
      .toBe(expected)
    await expect
      .poll(() =>
        challengeDialog
          .getByRole('tab')
          .first()
          .evaluate((element) => getComputedStyle(element).borderBottomColor)
      )
      .toBe(expected)
    await expect.poll(() => accent.evaluate((element) => getComputedStyle(element).color)).toBe(expected)
  }
  expect(new Set(colors).size).toBe(3)
  expect(await page.evaluate(() => localStorage.getItem('custom-theme'))).toBe('')
})

test('challenge portal and skill-tree surfaces follow light, dark and system preferences', async ({ page }) => {
  await setup(page, { dark: false })
  await page.addInitScript(() => localStorage.setItem('custom-theme', '#315EAC'))
  await page.goto(challengePath)
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await dialog.getByRole('textbox').first().fill('flag{theme-test}')
  const filled = dialog.getByRole('button', { name: 'Submit flag', exact: true })
  const tab = dialog.getByRole('tab').first()
  for (const mode of ['light', 'dark', 'auto'] as const) {
    await page.evaluate((mode) => {
      localStorage.setItem('mantine-color-scheme-value', mode)
      window.dispatchEvent(
        new StorageEvent('storage', { key: 'mantine-color-scheme-value', newValue: mode, storageArea: localStorage })
      )
    }, mode)
    if (mode === 'auto') await page.emulateMedia({ colorScheme: 'light' })
    await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', mode === 'dark' ? 'dark' : 'light')
    const expectedCard = await themeColor(page, '--hbc-card')
    await expect.poll(() => dialog.evaluate((element) => getComputedStyle(element).backgroundColor)).toBe(expectedCard)
    const expectedAccent = await themeColor(page, '--mantine-primary-color-filled')
    await expect
      .poll(() => filled.evaluate((element) => getComputedStyle(element).backgroundColor))
      .toBe(expectedAccent)
    await expect.poll(() => tab.evaluate((element) => getComputedStyle(element).borderBottomColor)).toBe(expectedAccent)
  }
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'dark')
  await expect
    .poll(() => dialog.evaluate((element) => getComputedStyle(element).backgroundColor))
    .toBe(await themeColor(page, '--hbc-card'))
  await page.keyboard.press('Escape')
  const card = page.getByRole('region', {name:'技能内容'}).getByRole('link', { name: /HTTP 请求与身份验证/ })
  await expect
    .poll(() => card.evaluate((element) => getComputedStyle(element).backgroundColor))
    .toBe(await themeColor(page, '--hbc-card'))
})

test('leaderboard respects the global surface and accent in both color schemes', async ({ page }) => {
  await setup(page, { dark: false })
  await page.addInitScript(() => localStorage.setItem('custom-theme', '#315EAC'))
  await page.route('**/api/learning-leaderboard?*', (route) =>
    route.fulfill({ json: { metric: 'score', entries: [], series: [] } })
  )
  await page.goto('/leaderboard')
  const board = page.locator('[data-competition-board]')
  const action = board.getByRole('link', { name: 'Explore skill trees' })
  await expect(action).toBeVisible()
  for (const mode of ['light', 'dark'] as const) {
    await page.getByRole('button', { name: 'Customize Color' }).click()
    await page.getByRole('menuitem', { name: `${mode} mode`, exact: true }).click()
    await expect
      .poll(() => board.evaluate((element) => getComputedStyle(element).backgroundColor))
      .toBe(await themeColor(page, '--hbc-bg'))
    await expect
      .poll(() => action.evaluate((element) => getComputedStyle(element).backgroundColor))
      .toBe(await themeColor(page, '--mantine-primary-color-filled'))
  }
})
