import { expect, test, type Page } from '@playwright/test'

const treeId = '01990000-0000-7000-8000-000000000001'
const categoryId = '01990000-0000-7000-8000-000000000002'
const challengeId = '01990000-0000-7000-8000-000000000003'
const challengePath = `/skill-trees/${treeId}/${categoryId}/challenge/${challengeId}`

async function setup(page: Page, dark = true) {
  await page.addInitScript((dark) => {
    localStorage.setItem('language', JSON.stringify('zh-CN'))
    localStorage.setItem('mantine-color-scheme-value', dark ? 'dark' : 'light')
    localStorage.setItem('custom-theme', '#7656c9')
  }, dark)
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'learner', userName: 'Learner', role: 'User' } })
  )
  const content = {
    contentId: challengeId,
    kind: 'challenge',
    title: 'HTTP 请求与身份验证',
    summary: '分析请求，找到认证逻辑中的薄弱环节。',
    difficulty: 'Normal',
    expectedMinutes: 30,
  }
  const tree = {
    skillTreeId: treeId,
    name: 'Web 安全基础',
    summary: '',
    iconKey: 'web',
    categories: [
      {
        categoryId,
        name: '身份验证',
        summary: '',
        iconKey: 'web',
        skills: [
          { skillId: 'default-skill', name: '入门技能', contents: [content], challengeCount: 1, lessonCount: 0 },
        ],
        contents: [content],
      },
    ],
  }
  await page.route('**/api/skill-trees', (route) => route.fulfill({ json: [tree] }))
  await page.route(`**/api/skill-trees/${treeId}`, (route) => route.fulfill({ json: tree }))
  await page.route('**/api/my-learning/skill-trees/*', (route) =>
    route.fulfill({
      json: {
        totalCount: 1,
        completedCount: 0,
        completedLessonIds: [],
        solvedChallengeIds: [],
        categories: [],
        skills: [],
      },
    })
  )
  await page.route(`**/api/challenges/${challengeId}?locale=*`, (route) =>
    route.fulfill({
      json: {
        id: challengeId,
        title: content.title,
        summary: '',
        body: '这是一套用于练习身份验证的应用。分析请求并找到 Flag。\n\n**目标：** 理解服务端如何验证用户身份。',
        type: 'DynamicContainer',
        ctfCategory: 'Web',
        hasContainer: true,
        hasAttachment: true,
        hasWriteup: true,
        hintLocaleCount: 2,
      },
    })
  )
  let deletes = 0
  await page.route(`**/api/challenges/${challengeId}/instances`, (route) => {
    if (route.request().method() === 'DELETE') {
      deletes++
      return route.fulfill({ status: 204 })
    }
    return route.fulfill({
      json: {
        id: 'running-instance',
        status: 1,
        expiresAtUtc: Date.now() + 3600000,
        publicIp: '192.168.1.160',
        publicPort: 32769,
      },
    })
  })
  await page.route(`**/api/challenges/${challengeId}/hints/next?*`, (route) =>
    route.fulfill({ json: { hintId: 'hint', sortOrder: 1, locale: 'zh-CN', content: '检查登录请求中的身份字段。' } })
  )
  await page.route(`**/api/challenges/${challengeId}/writeup?*`, (route) =>
    route.fulfill({ json: { locale: 'zh-CN', content: '## 官方解题思路\n\n检查服务端身份验证。' } })
  )
  await page.route(`**/api/challenges/${challengeId}/submissions`, (route) =>
    route.fulfill({ json: { accepted: false, rejectionCode: 'challenge.test_instance' } })
  )
  return { deletes: () => deletes }
}

for (const dark of [true, false]) {
  test(`vertical workspace keeps operations compact and help folded in ${dark ? 'dark' : 'light'} theme`, async ({
    page,
  }, testInfo) => {
    await setup(page, dark)
    await page.setViewportSize({ width: 1280, height: 960 })
    await page.goto(challengePath)
    const dialog = page.getByRole('dialog')
    const instance = dialog.getByRole('heading', { name: '挑战实例', exact: true }).locator('..')
    const submission = dialog.getByRole('heading', { name: '提交 Flag', exact: true }).locator('..')
    await expect(page.getByRole('timer')).toBeVisible()
    const instanceBox = (await instance.boundingBox())!
    const submitBox = (await submission.boundingBox())!
    expect(submitBox.y).toBeGreaterThanOrEqual(instanceBox.y + instanceBox.height)
    expect(instanceBox.height).toBeLessThan(210)
    const flag = dialog.getByRole('textbox', { name: '输入 Flag', exact: true })
    const submit = dialog.getByRole('button', { name: '提交 Flag', exact: true })
    expect(Math.abs((await flag.boundingBox())!.y - (await submit.boundingBox())!.y)).toBeLessThan(2)
    expect((await flag.boundingBox())!.width).toBeGreaterThan(450)
    for (const name of ['帮助', '官方题解', '社区 WP'])
      await expect(dialog.getByRole('button', { name, exact: true })).toHaveAttribute('aria-expanded', 'false')
    await expect(dialog.getByRole('tab', { name: '提交 WP', exact: true })).toBeHidden()
    await expect(dialog.getByRole('alert')).toHaveCount(0)
    const colors = await flag.evaluate((el) => ({
      foreground: getComputedStyle(el).color,
      background: getComputedStyle(el).backgroundColor,
      scheme: document.documentElement.dataset.mantineColorScheme,
    }))
    expect(colors.scheme).toBe(dark ? 'dark' : 'light')
    expect(colors.foreground).not.toBe(colors.background)
    await flag.fill('flag{theme-check}')
    expect(await page.getByRole('timer').evaluate((el) => getComputedStyle(el).color)).toBe(
      await submit.evaluate((el) => getComputedStyle(el).backgroundColor)
    )
    await flag.clear()
    await dialog.screenshot({ path: testInfo.outputPath(`workspace-${dark ? 'dark' : 'light'}-desktop.png`) })

    await page.setViewportSize({ width: 320, height: 720 })
    expect(await dialog.evaluate((el) => el.scrollWidth - el.clientWidth)).toBeLessThanOrEqual(1)
    expect((await submit.boundingBox())!.y).toBeGreaterThan((await flag.boundingBox())!.y)
    expect((await submit.boundingBox())!.width).toBeGreaterThan(220)
    await dialog.screenshot({ path: testInfo.outputPath(`workspace-${dark ? 'dark' : 'light'}-mobile.png`) })
  })
}

test('folded help supports keyboard, hints, official and community writeups without stopping learner instances', async ({
  page,
}) => {
  const state = await setup(page)
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.goto(challengePath)
  const help = page.getByRole('button', { name: '帮助', exact: true })
  await help.focus()
  await page.keyboard.press('Enter')
  await expect(help).toHaveAttribute('aria-expanded', 'true')
  await page.getByRole('button', { name: '查看下一条提示', exact: true }).click()
  await expect(page.getByText('检查登录请求中的身份字段。')).toBeVisible()
  await help.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByText('检查登录请求中的身份字段。')).toBeHidden()
  await page.keyboard.press('Enter')
  await expect(page.getByText('检查登录请求中的身份字段。')).toBeVisible()
  const official = page.getByRole('button', { name: '官方题解', exact: true })
  await official.focus()
  await page.keyboard.press('Space')
  await page.getByRole('button', { name: '查看官方题解', exact: true }).click()
  await expect(page.getByRole('heading', { name: '官方解题思路', exact: true })).toBeVisible()
  const community = page.getByRole('button', { name: '社区 WP', exact: true })
  await community.click()
  await page.getByRole('tab', { name: '提交 WP', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'WP 标题', exact: true })).toBeVisible()
  await page.getByRole('textbox', { name: '输入 Flag', exact: true }).fill('flag{test}')
  await page.getByRole('button', { name: '提交 Flag', exact: true }).click()
  await expect(page.getByText('管理员测试实例不会记录通关或积分。请停止测试实例后再使用正式题目实例。')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  expect(state.deletes()).toBe(0)
})
