import { expect, test, type Page } from '@playwright/test'

const tree = '10000000-0000-0000-0000-000000000001',
  category = '20000000-0000-0000-0000-000000000001',
  skill = '30000000-0000-0000-0000-000000000001'
const ids = ['40000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000002']
async function setup(page: Page) {
  if ((page.viewportSize()?.width ?? 0) > 1000) await page.setViewportSize({ width: 1440, height: 1000 })
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  const state = {
    version: 1,
    checks: 0,
    published: 0,
    saveFails: false,
    actor: 'admin',
    bodies: ['', ''],
    extraLocales: [] as { locale: string; title: string; body: string }[],
    issues: true,
  }
  await page.route('**/api/**', (r) => r.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (r) =>
    r.fulfill({ json: { userId: state.actor, userName: 'Admin', role: 'Admin' } })
  )
  const doc = () => ({
    name: '待修课程',
    summary: '',
    iconKey: 'web',
    categories: [
      {
        categoryId: category,
        name: '类别',
        summary: '',
        iconKey: 'web',
        skills: [
          {
            skillId: skill,
            name: '技能',
            summary: '',
            iconKey: 'web',
            isEnabled: true,
            contents: ids.map((id, i) => ({
              kind: 'challenge',
              contentId: id,
              title: '待修题' + (i + 1),
              summary: '',
              state: 'Draft',
            })),
          },
        ],
      },
    ],
  })
  await page.route(`**/api/admin/skill-trees/${tree}/workspace`, (r) =>
    r.fulfill({
      json: { skillTreeId: tree, version: String(state.version), hasPublishedVersion: true, document: doc() },
    })
  )
  await page.route(`**/api/admin/skill-trees/${tree}/workspace/check`, (r) => {
    state.checks++
    return r.fulfill({
      json: {
        version: String(state.version),
        categoryCount: 1,
        skillCount: 1,
        challengeCount: 2,
        lessonCount: 0,
        issues: state.issues
          ? ids
              .filter((_, i) => !state.bodies[i])
              .map((contentId) => ({
                severity: 'error',
                code: 'challenge_invalid',
                message: 'missing body',
                contentId,
              }))
          : [],
      },
    })
  })
  await page.route(`**/api/admin/skill-trees/${tree}/workspace/publish`, (r) => {
    state.published++
    return r.fulfill({ json: {} })
  })
  for (const [i, id] of ids.entries()) {
    const edit = () => ({
      challenge: {
        id,
        title: '待修题' + (i + 1),
        type: 'StaticAttachment',
        ctfCategory: 'Web',
        difficulty: 'Normal',
        isEnabled: true,
        publicationState: 'Draft',
      },
      localizations: [
        { locale: 'en', title: '待修题' + (i + 1), summary: '', body: state.bodies[i] },
        ...state.extraLocales,
      ],
      runtimeConfigurationJson: '{}',
      flags: [{ kind: 'Static', value: 'flag{synthetic}' }],
      hints: [],
      writeups: [],
      publication: { rowVersion: state.version, categoryIds: [] },
    })
    await page.route(`**/api/admin/challenges/${id}/edit**`, (r) => r.fulfill({ json: edit() }))
    await page.route(new RegExp(`/api/admin/challenges/${id}\\?`), (r) => {
      if (state.saveFails) return r.fulfill({ status: 409, json: { message: 'Conflict' } })
      const texts = r.request().postDataJSON().localizations
      state.bodies[i] = texts.find((t: any) => t.locale === 'en').body
      state.extraLocales = texts.filter((t: any) => t.locale !== 'en')
      state.version++
      return r.fulfill({ json: edit() })
    })
  }
  await page.goto(`/admin/skill-trees/${tree}`)
  await page.getByRole('button', { name: '检查并发布', exact: true }).click()
  await page.getByRole('button', { name: '逐题修复', exact: true }).click()
  await expect(page.getByRole('heading', { name: '逐题修复', exact: true })).toBeVisible()
  return state
}
const queue = (page: Page) => page.getByRole('navigation', { name: '发布问题清单' }).first()
test('keeps two problems across full editors, saves and refresh; only one final recheck', async ({ page }) => {
  const state = await setup(page)
  await queue(page)
    .getByRole('link', { name: /待修题1/ })
    .click()
  await expect(page.getByRole('heading', { name: '待修题1', exact: true })).toBeVisible()
  await page.screenshot({ path: '/tmp/course-review-desktop.png' })
  expect(await page.getByRole('navigation', { name: '管理导航' }).count()).toBe(0)
  await page.getByRole('textbox', { name: '题目说明', exact: true }).fill('第一题正文')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect(queue(page).getByText('已修改 · 待复查')).toHaveCount(1)
  await queue(page)
    .getByRole('link', { name: /待修题2/ })
    .click()
  await expect(page.getByRole('heading', { name: '待修题2', exact: true })).toBeVisible()
  await page.getByRole('textbox', { name: '题目说明', exact: true }).fill('第二题正文')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await page.reload()
  await expect(queue(page).getByRole('link', { name: /待修题/ })).toHaveCount(2)
  await expect(queue(page).getByText('已修改 · 待复查')).toHaveCount(2)
  expect(state.checks).toBe(1)
  await page.getByRole('link', { name: '检查摘要 / 统一复查', exact: true }).first().click()
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: '统一复查全部问题', exact: true }).click()
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeEnabled()
  expect(state.checks).toBe(2)
  await page.getByRole('button', { name: '确认统一发布', exact: true }).click()
  await expect.poll(() => state.published).toBe(1)
})
test('canceling unsaved switching keeps current form and issue list', async ({ page }) => {
  await setup(page)
  await queue(page)
    .getByRole('link', { name: /待修题1/ })
    .click()
  await page.getByRole('textbox', { name: '题目说明', exact: true }).fill('还没保存')
  page.once('dialog', (d) => d.dismiss())
  await queue(page)
    .getByRole('link', { name: /待修题2/ })
    .click()
  await expect(page).toHaveURL(new RegExp(ids[0]))
  await expect(page.getByRole('textbox', { name: '题目说明', exact: true })).toHaveValue('还没保存')
  await expect(queue(page).getByText('已修改 · 待复查')).toHaveCount(0)
})
test('failed save does not mark fixed and server version change blocks old approval', async ({ page }) => {
  const state = await setup(page)
  await queue(page)
    .getByRole('link', { name: /待修题1/ })
    .click()
  await page.getByRole('textbox', { name: '题目说明', exact: true }).fill('保存失败正文')
  state.saveFails = true
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect(page.getByRole('button', { name: '读取最新版本并比较', exact: true })).toBeVisible()
  await expect(queue(page).getByText('已修改 · 待复查')).toHaveCount(0)
})
for (const scheme of ['light', 'dark'] as const) {
  test(`mobile ${scheme} uses a drawer, retains issues and avoids overflow`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: scheme })
    await page.setViewportSize({ width: 390, height: 844 })
    await setup(page)
    await page.getByRole('button', { name: '打开问题清单', exact: true }).click()
    const drawer = page.getByRole('dialog', { name: '问题清单' })
    await drawer.getByRole('link', { name: /待修题1/ }).click()
    await expect(page.getByRole('heading', { name: '待修题1', exact: true })).toBeVisible()
    await expect(drawer).not.toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
    await page.screenshot({ path: `/tmp/course-review-mobile-${scheme}.png`, fullPage: true })
  })
}

test('an external workspace version change invalidates a clean review', async ({ page }) => {
  const state = await setup(page)
  state.issues = false
  await page.getByRole('button', { name: '统一复查全部问题', exact: true }).click()
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeEnabled()
  state.version++
  await page.reload()
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeDisabled()
  await expect(page.getByText(/草稿已修改或检查结果已过期/)).toBeVisible()
  expect(state.published).toBe(0)
})
test('another account cannot inherit the first account worklist', async ({ page }) => {
  const state = await setup(page)
  state.actor = 'another-admin'
  await page.reload()
  await expect(page.getByText('尚未检查', { exact: true })).toBeVisible()
  await expect(queue(page).getByRole('link', { name: /待修题/ })).toHaveCount(0)
})
test('lesson save returns to the same worklist and marks pending recheck', async ({ page }) => {
  const state = await setup(page)
  await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((k) => k.startsWith('course-review:'))!
    const review = JSON.parse(sessionStorage.getItem(key)!)
    review.document.categories[0].skills[0].contents[0].kind = 'lesson'
    review.check.issues[0].code = 'lesson_invalid'
    sessionStorage.setItem(key, JSON.stringify(review))
  })
  await page.route(`**/api/admin/lessons/${ids[0]}?*`, (r) => {
    if (r.request().method() === 'PUT') state.version++
    return r.fulfill({
      json: {
        id: ids[0],
        locale: 'zh',
        title: '待修题1',
        body: '课节正文',
        localizations: [{ locale: 'zh', title: '待修题1', body: '课节正文' }],
        publication: { rowVersion: state.version, publicationState: 'Draft', categoryIds: [] },
      },
    })
  })
  await page.goto(`/admin/library/lessons/${ids[0]}?course=${tree}&category=${category}&skill=${skill}&review=1`)
  await page.getByRole('textbox', { name: '课节标题', exact: true }).fill('修改课节标题')
  await page.getByRole('button', { name: '保存并返回问题清单', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/admin/skill-trees/${tree}\\?review=1$`))
  await expect(queue(page).getByText('已修改 · 待复查')).toHaveCount(1)
  expect(state.checks).toBe(1)
})

test('quota failure uses the new in-memory worklist instead of a readable old snapshot', async ({ page }) => {
  const state = await setup(page)
  await page.evaluate(() => {
    const set = Storage.prototype.setItem,
      remove = Storage.prototype.removeItem
    Storage.prototype.setItem = function (k, v) {
      if (k.startsWith('course-review:')) throw new DOMException('Full', 'QuotaExceededError')
      set.call(this, k, v)
    }
    Storage.prototype.removeItem = function (k) {
      if (k.startsWith('course-review:')) throw new DOMException('Blocked', 'SecurityError')
      remove.call(this, k)
    }
  })
  await queue(page)
    .getByRole('link', { name: /待修题1/ })
    .click()
  await expect(page.getByRole('heading', { name: '待修题1', exact: true })).toBeVisible()
  await page.getByRole('textbox', { name: '题目说明', exact: true }).fill('存储满时仍然可继续修复')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect(queue(page).getByText('已修改 · 待复查')).toHaveCount(1)
  await expect(page.getByRole('alert').filter({ hasText: '浏览器暂时无法保存清单' })).toBeVisible()
  await page.getByRole('link', { name: '检查摘要 / 统一复查', exact: true }).first().click()
  state.issues = false
  await page.getByRole('button', { name: '统一复查全部问题', exact: true }).click()
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: '确认统一发布', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/admin/skill-trees/${tree}$`))
  await expect(page.getByRole('link', { name: /^问题清单/ })).toHaveCount(0)
})

test('a damaged cached report can be replaced with a new check instead of crashing', async ({ page }) => {
  await setup(page)
  await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((k) => k.startsWith('course-review:'))!
    const snapshot = JSON.parse(sessionStorage.getItem(key)!)
    snapshot.check.changes = {}
    sessionStorage.setItem(key, JSON.stringify(snapshot))
  })
  await page.reload()
  await expect(page.getByText('尚未检查', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '统一复查全部问题', exact: true }).click()
  await expect(queue(page).getByRole('link', { name: /待修题/ })).toHaveCount(2)
})

test('a long worklist keeps the selected question visible after changing editor routes', async ({ page }) => {
  await setup(page)
  await page.evaluate(() => {
    const key = Object.keys(sessionStorage).find((k) => k.startsWith('course-review:'))!
    const r = JSON.parse(sessionStorage.getItem(key)!),
      items = r.document.categories[0].skills[0].contents
    const extras = Array.from({ length: 20 }, (_, i) => ({
      ...items[0],
      contentId: crypto.randomUUID(),
      title: '其他问题' + i,
    }))
    r.document.categories[0].skills[0].contents = [items[0], ...extras, items[1]]
    r.check.issues = [
      r.check.issues[0],
      ...extras.map((c) => ({
        severity: 'error',
        code: 'challenge_invalid',
        message: 'empty',
        contentId: c.contentId,
      })),
      r.check.issues[1],
    ]
    sessionStorage.setItem(key, JSON.stringify(r))
  })
  await page.reload()
  await queue(page)
    .getByRole('link', { name: /待修题2/ })
    .click()
  await expect(page.getByRole('heading', { name: '待修题2', exact: true })).toBeVisible()
  const active = queue(page).locator('a[aria-current="page"]')
  await expect(active).toContainText('待修题2')
  await expect
    .poll(async () => {
      const b = await active.boundingBox()
      return !!b && b.y >= 56 && b.y + b.height <= 1000
    })
    .toBe(true)
})

test('imported hidden empty Chinese version is diagnosed and explicitly repaired without overwriting other translations', async ({
  page,
}) => {
  const state = await setup(page)
  state.extraLocales = [
    { locale: 'zh', title: '中文原题名', body: '' },
    { locale: 'ja', title: 'Existing translation', body: '保留的已有正文' },
  ]
  await page.route(`**/api/admin/skill-trees/${tree}/workspace/check`, (r) => {
    state.checks++
    const empty = state.extraLocales.filter((l) => !l.body.trim()).map((l) => l.locale)
    return r.fulfill({
      json: {
        version: String(state.version),
        categoryCount: 1,
        skillCount: 1,
        challengeCount: 2,
        lessonCount: 0,
        issues: empty.length
          ? [
              {
                severity: 'error',
                code: 'challenge_body_missing',
                message: 'Missing body',
                contentId: ids[0],
                locales: empty,
              },
            ]
          : [],
      },
    })
  })
  await queue(page)
    .getByRole('link', { name: /待修题1/ })
    .click()
  await expect(page.getByRole('heading', { name: '待修题1', exact: true })).toBeVisible()
  const warning = page.getByRole('alert').filter({ hasText: '其他语言版本尚未完整' })
  await expect(warning).toContainText('zh')
  await expect(warning.getByRole('button', { name: '保存并补齐空白语言' })).toBeDisabled()
  await page.getByRole('textbox', { name: '题目说明', exact: true }).fill('用户已经补好的题目说明')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect(queue(page).getByText('已修改 · 待复查')).toHaveCount(1)
  expect(state.extraLocales.find((l) => l.locale === 'zh')?.body).toBe('')
  await page.getByRole('link', { name: '检查摘要 / 统一复查', exact: true }).first().click()
  await page.getByRole('button', { name: '统一复查全部问题', exact: true }).click()
  await expect(page.getByRole('alert').filter({ hasText: '以下语言版本的题目正文为空：zh' })).toBeVisible()
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeDisabled()
  await queue(page)
    .getByRole('link', { name: /待修题1/ })
    .click()
  await expect(page.getByRole('heading', { name: '待修题1', exact: true })).toBeVisible()
  await page.getByRole('button', { name: '保存并补齐空白语言', exact: true }).click()
  await expect(page.getByRole('alert').filter({ hasText: '其他语言版本尚未完整' })).not.toBeVisible()
  expect(state.extraLocales.find((l) => l.locale === 'zh')).toMatchObject({
    title: '中文原题名',
    body: '用户已经补好的题目说明',
  })
  expect(state.extraLocales.find((l) => l.locale === 'ja')?.body).toBe('保留的已有正文')
  await page.getByRole('link', { name: '检查摘要 / 统一复查', exact: true }).first().click()
  await page.getByRole('button', { name: '统一复查全部问题', exact: true }).click()
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeEnabled()
})
