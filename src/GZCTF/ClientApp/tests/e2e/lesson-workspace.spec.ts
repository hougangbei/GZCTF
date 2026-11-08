import { expect, test, type Page } from '@playwright/test'

const path = '/skill-trees/tree/category/lesson/one'
async function setup(page: Page, admin = false, language = 'zh-CN') {
  await page.addInitScript((language) => localStorage.setItem('language', JSON.stringify(language)), language)
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/config', (route) => route.fulfill({ json: { title: 'NYNUCTF', customTheme: '#E05822' } }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'reader', userName: 'Reader', role: admin ? 'Admin' : 'User' } })
  )
  let completed: string[] = []
  await page.route('**/api/my-learning*', (route) => route.fulfill({ json: { completedLessonIds: completed } }))
  await page.route('**/api/my-learning/skill-trees/tree', route => route.fulfill({json:{completedLessonIds:completed,solvedChallengeIds:[],totalCount:2,completedCount:completed.length,categories:[],skills:[]}}))
  await page.route('**/api/skill-trees/tree', (route) =>
    route.fulfill({
      json: {
        skillTreeId: 'tree',
        name: 'Web 安全入门',
        categories: [
          {
            categoryId: 'category',
            name: 'HTTP 基础',
            skills: [{skillId:'default-skill',name:'HTTP 技能',summary:'',iconKey:'web',sortOrder:0,lessonCount:2,challengeCount:0,contents:[{kind:'lesson',contentId:'one',title:'HTTP 请求与响应'},{kind:'lesson',contentId:'two',title:'Cookie 与 Session'}]}],
            contents: [
              { kind: 'lesson', contentId: 'one', title: 'HTTP 请求与响应' },
              { kind: 'lesson', contentId: 'two', title: 'Cookie 与 Session' },
            ],
          },
        ],
      },
    })
  )
  await page.route('**/api/learning-lessons/*?locale=*', (route) =>
    route.fulfill({
      json: {
        lessonId: route.request().url().includes('/two?') ? 'two' : 'one',
        title: route.request().url().includes('/two?') ? 'Cookie 与 Session' : 'HTTP 请求与响应',
        body: '## 请求\n\n正文 **重点**\n\n```text\n## 代码里的标题\n```\n\n## 请求\n\n<img src=x onerror="window.lessonXss=true">',
      },
    })
  )
  await page.route('**/api/learning-lessons/one/complete', (route) => {
    completed = ['one']
    return route.fulfill({ status: 204 })
  })
}

test('lesson is a full page with course navigation, unique heading links and completion', async ({ page }) => {
  await setup(page)
  await page.goto(path)
  await expect(page.getByRole('heading', { name: 'HTTP 请求与响应', exact: true })).toBeVisible()
  await expect(page.getByRole('dialog')).toHaveCount(0)
  const toc = page.getByRole('navigation', { name: '本节目录' })
  await expect(toc.getByRole('link', { name: '请求', exact: true })).toHaveCount(2)
  await expect(toc).not.toContainText('代码里的标题')
  expect(
    await toc.getByRole('link').evaluateAll((links) => new Set(links.map((link) => link.getAttribute('href'))).size)
  ).toBe(2)
  await page.getByRole('button', { name: '标记完成', exact: true }).click()
  await expect(page.getByRole('button', { name: '已完成', exact: true })).toBeVisible()
  expect(await page.evaluate(() => (window as unknown as { lessonXss?: boolean }).lessonXss)).toBeUndefined()
  await page.getByRole('link', { name: /下一节/ }).click()
  await expect(page.getByRole('heading', { name: 'Cookie 与 Session', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '标记完成', exact: true })).toBeVisible()
})

test('mobile lesson course directory opens on demand without overflow', async ({ page }) => {
  await setup(page)
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto(path)
  await page.getByRole('button', { name: '课程目录', exact: true }).click()
  await expect(page.getByRole('dialog').getByRole('link', { name: /Cookie 与 Session/ })).toBeVisible()
  await page
    .getByRole('dialog')
    .getByRole('link', { name: /Cookie 与 Session/ })
    .click()
  await expect(page.getByRole('heading', { name: 'Cookie 与 Session', exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})

test('editor previews, preserves other locales, saves, and restores a local draft', async ({ page }) => {
  await setup(page, true)
  let body = '## 原始内容'
  let saved: unknown
  await page.route('**/api/admin/lessons/one*', async (route) => {
    if (route.request().method() === 'PUT') {
      saved = route.request().postDataJSON()
      body = (saved as { localizations: { locale: string; body: string }[] }).localizations.find(
        (x) => x.locale === 'en'
      )!.body
    }
    return route.fulfill({
      json: {
        id: 'one',
        localizations: [
          { locale: 'en', title: 'HTTP 课节', body },
          { locale: 'ja', title: '既存', body: '保持' },
        ],
        publication: { publicationState: 'Published', rowVersion: 1, categoryIds: [] },
      },
    })
  })
  await page.goto('/admin/library/lessons/one')
  await page.getByRole('textbox', { name: 'Markdown 正文', exact: true }).fill('## 新的小节\n\n**内容**')
  await expect(
    page.getByRole('region', { name: '学生视角预览' }).getByRole('heading', { name: '新的小节' })
  ).toBeVisible()
  await page.getByRole('button', { name: '保存', exact: true }).click()
  await expect(page.getByText('已保存', { exact: true })).toBeVisible()
  expect((saved as { localizations: unknown[] }).localizations).toContainEqual({
    locale: 'ja',
    title: '既存',
    body: '保持',
  })
  await page.getByRole('textbox', { name: 'Markdown 正文', exact: true }).fill('本机未提交的草稿')
  page.once('dialog', (dialog) => dialog.accept())
  await page.reload()
  await page.getByRole('button', { name: '恢复本机草稿', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'Markdown 正文', exact: true })).toHaveValue('本机未提交的草稿')
  await page.keyboard.press('Shift+Tab')
  await expect(page.getByRole('textbox', { name: 'Markdown 正文', exact: true })).not.toBeFocused()
})

test('lesson reader follows personal accent changes and system/light/dark themes live', async ({ page }) => {
  await setup(page, false, 'en-US')
  await page.goto(path)
  const complete = page.getByRole('button', { name: 'Mark complete', exact: true })
  const active = page
    .getByRole('navigation', { name: 'Course directory' })
    .getByRole('link', { name: 'HTTP 请求与响应' })
  const probe = (variable: string) =>
    page.evaluate((variable) => {
      const e = document.createElement('span')
      e.style.color = `var(${variable})`
      document.body.append(e)
      const value = getComputedStyle(e).color
      e.remove()
      return value
    }, variable)
  const colors: string[] = []
  for (const provider of ['Customized', 'Default', 'Admin Provided']) {
    await page.getByRole('button', { name: 'Customize Color' }).click()
    await page.getByRole('menuitem', { name: 'Customize Color' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('radio', { name: provider, exact: true }).check()
    if (provider === 'Customized') await dialog.getByRole('textbox', { name: 'Custom Base Color' }).fill('#315EAC')
    await dialog.getByRole('button', { name: 'Apply', exact: true }).click()
    const expected = await probe('--mantine-primary-color-filled')
    colors.push(expected)
    await expect.poll(() => complete.evaluate((e) => getComputedStyle(e).backgroundColor)).toBe(expected)
    await expect.poll(() => active.evaluate((e) => getComputedStyle(e).color)).toBe(expected)
    await expect
      .poll(() =>
        page
          .locator('article strong')
          .filter({ hasText: '重点' })
          .evaluate((e) => getComputedStyle(e).color)
      )
      .toBe(expected)
  }
  expect(new Set(colors).size).toBe(3)
  for (const mode of ['light mode', 'dark mode', 'System']) {
    await page.getByRole('button', { name: 'Customize Color' }).click()
    await page.getByRole('menuitem', { name: mode, exact: true }).click()
    await expect
      .poll(() => page.locator('article').evaluate((e) => getComputedStyle(e).backgroundColor))
      .toBe(await probe('--hbc-card'))
  }
  await page.emulateMedia({ colorScheme: 'light' })
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'light')
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.locator('html')).toHaveAttribute('data-mantine-color-scheme', 'dark')
})
