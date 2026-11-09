import { expect, test, type Page } from '@playwright/test'

const tree = '10000000-0000-0000-0000-000000000001',
  other = '10000000-0000-0000-0000-000000000002'
const question = { contentId: 'q', kind: 'challenge', title: '题目Q', summary: '', state: 'Draft' }
const make = (id: string, name: string, prefix: string, contents: any[]) => ({
  skillTreeId: id,
  version: 'v1',
  hasPublishedVersion: true,
  document: {
    name,
    summary: '',
    iconKey: 'web',
    categories: [
      {
        categoryId: prefix + 'c',
        name: '类别',
        summary: '',
        iconKey: 'web',
        skills: [
          { skillId: prefix + 's', name: '原技能', summary: '', iconKey: 'web', isEnabled: true, contents },
          {
            skillId: prefix + 't',
            name: '目标技能',
            summary: '',
            iconKey: 'web',
            isEnabled: true,
            contents: [] as any[],
          },
        ],
      },
    ],
  },
})
async function setup(page: Page) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  const state = {
    source: make(tree, '当前课程', '', [question]),
    target: make(other, '其他课程', 'x', []),
    commands: [] as any[],
    saves: 0,
    publishes: 0,
    fail: 0,
  }
  await page.route('**/api/**', (r) => r.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (r) =>
    r.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } })
  )
  await page.route('**/api/admin/skill-trees', (r) =>
    r.fulfill({
      json: [
        { skillTreeId: tree, name: '当前课程' },
        { skillTreeId: other, name: '其他课程' },
      ],
    })
  )
  await page.route(`**/api/admin/skill-trees/${tree}/workspace`, (r) => {
    if (r.request().method() === 'PUT') {
      state.saves++
      state.source = {
        ...state.source,
        version: 'v' + (state.saves + 1),
        document: r.request().postDataJSON().document,
      }
    }
    return r.fulfill({ json: state.source })
  })
  await page.route(`**/api/admin/skill-trees/${other}/workspace`, (r) => r.fulfill({ json: state.target }))
  await page.route('**/workspace/publish', (r) => {
    state.publishes++
    return r.fulfill({ json: {} })
  })
  await page.route('**/api/admin/course-transfers', (r) => {
    const cmd = r.request().postDataJSON()
    state.commands.push(cmd)
    if (state.fail) return r.fulfill({ status: state.fail, json: { message: '暂时失败' } })
    state.target.document.categories[0].skills[1].contents = [question]
    if (cmd.mode === 'move') state.source.document.categories[0].skills[0].contents = []
    state.source.version = 'accepted'
    state.target.version = 'accepted'
    return r.fulfill({
      json: {
        source: state.source,
        target: state.target,
        addedCount: 1,
        removedCount: cmd.mode === 'move' ? 1 : 0,
        duplicateCount: 0,
        noOp: false,
      },
    })
  })
  await page.goto(`/admin/skill-trees/${tree}`)
  await expect(page.getByRole('heading', { name: '当前课程', exact: true }).first()).toBeVisible()
  return state
}
const outline = (page: Page) => page.getByRole('navigation', { name: '课程编排目录' }).first()
async function picker(page: Page) {
  await outline(page).getByRole('button', { name: '更多操作 题目Q', exact: true }).click()
  await page.getByRole('menuitem', { name: '移动或引用到…' }).click()
  return page.getByRole('dialog', { name: '移动或引用内容' })
}
test('dragging in the directory moves across skills in local draft only', async ({ page }) => {
  const state = await setup(page)
  const nav = outline(page)
  await nav.locator('[data-outline-id="q"]').dragTo(nav.locator('[data-outline-id="t"]'))
  await expect(page.getByRole('alert').filter({ hasText: '已移动，保存草稿并发布后生效。' })).toBeVisible()
  expect(state.saves).toBe(0)
  expect(state.commands).toHaveLength(0)
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect.poll(() => state.saves).toBe(1)
  expect(state.source.document.categories[0].skills[0].contents).toEqual([])
  expect(state.source.document.categories[0].skills[1].contents).toMatchObject([{ contentId: 'q' }])
  expect(state.publishes).toBe(0)
})
test('menu offers same-tree reference without duplicating original or publishing', async ({ page }) => {
  const state = await setup(page),
    modal = await picker(page)
  await modal.getByLabel('目标技能', { exact: true }).click()
  await page.getByRole('option', { name: '类别 / 目标技能', exact: true }).click()
  await modal.getByLabel('保留原位置，只添加引用').check()
  await modal.getByRole('button', { name: '添加引用', exact: true }).click()
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect.poll(() => state.saves).toBe(1)
  expect(state.source.document.categories[0].skills.map((s) => s.contents.length)).toEqual([1, 1])
  expect(state.publishes).toBe(0)
})
test('cross-tree retry keeps operation ID and explicit reference mode', async ({ page }) => {
  const state = await setup(page),
    modal = await picker(page)
  await modal.getByLabel('目标技能树', { exact: true }).click()
  await page.getByRole('option', { name: '其他课程', exact: true }).click()
  await modal.getByLabel('目标技能', { exact: true }).click()
  await page.getByRole('option', { name: '类别 / 目标技能', exact: true }).click()
  await modal.getByLabel('保留原位置，只添加引用').check()
  state.fail = 503
  await modal.getByRole('button', { name: '添加引用', exact: true }).click()
  await expect(modal.getByText('暂时失败', { exact: true })).toBeVisible()
  state.fail = 0
  await modal.getByRole('button', { name: '添加引用', exact: true }).click()
  await expect(modal).not.toBeVisible()
  expect(state.commands).toHaveLength(2)
  expect(state.commands[0].operationId).toMatch(/^[0-9a-f-]{36}$/i)
  expect(state.commands[0].operationId).toBe(state.commands[1].operationId)
  expect(state.commands[1]).toMatchObject({
    mode: 'reference',
    sourceTreeId: tree,
    targetTreeId: other,
    targetSkillId: 'xt',
  })
  expect(state.source.document.categories[0].skills[0].contents).toHaveLength(1)
  expect(state.publishes).toBe(0)
})
test('conflict prevents blind retry and keeps original directory', async ({ page }) => {
  const state = await setup(page),
    modal = await picker(page)
  await modal.getByLabel('目标技能树', { exact: true }).click()
  await page.getByRole('option', { name: '其他课程', exact: true }).click()
  await modal.getByLabel('目标技能', { exact: true }).click()
  await page.getByRole('option', { name: '类别 / 目标技能', exact: true }).click()
  state.fail = 409
  await modal.getByRole('button', { name: '移动到此技能', exact: true }).click()
  await expect(modal.getByRole('button', { name: '移动到此技能', exact: true })).toBeDisabled()
  await modal.getByRole('button', { name: '取消', exact: true }).click()
  await expect(outline(page).locator('[data-outline-id="q"]')).toBeVisible()
  expect(state.source.document.categories[0].skills[0].contents).toHaveLength(1)
})
test('mobile has the same menu without drag dependency', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await setup(page)
  await page.getByRole('button', { name: '打开课程目录', exact: true }).click()
  const drawer = page.getByRole('dialog')
  await drawer.getByRole('button', { name: '更多操作 题目Q', exact: true }).click()
  await page.getByRole('menuitem', { name: '移动或引用到…' }).click()
  await expect(page.getByRole('dialog', { name: '移动或引用内容' })).toBeVisible()
})

test('an accidental drag can be undone before saving', async ({ page }) => {
  const state = await setup(page),
    nav = outline(page)
  await nav.locator('[data-outline-id="q"]').dragTo(nav.locator('[data-outline-id="t"]'))
  await page.getByRole('button', { name: '撤销整理', exact: true }).click()
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect.poll(() => state.saves).toBe(1)
  expect(state.source.document.categories[0].skills.map((s) => s.contents.length)).toEqual([1, 0])
})
test('opening a cross-tree picker saves local edits first; failed save never opens it', async ({ page }) => {
  const state = await setup(page)
  await page.getByRole('textbox', { name: '名称', exact: true }).fill('修改过的课程')
  await page.route(`**/api/admin/skill-trees/${tree}/workspace`, (r) =>
    r.request().method() === 'PUT'
      ? r.fulfill({ status: 409, json: { message: '版本冲突' } })
      : r.fulfill({ json: state.source })
  )
  await outline(page).getByRole('button', { name: '更多操作 题目Q', exact: true }).click()
  await page.getByRole('menuitem', { name: '移动或引用到…' }).click()
  await expect(page.getByRole('alert').filter({ hasText: '草稿版本已变化' })).toBeVisible()
  await expect(page.getByRole('dialog', { name: '移动或引用内容' })).not.toBeVisible()
  await expect(page.getByRole('textbox', { name: '名称', exact: true })).toHaveValue('修改过的课程')
  expect(state.commands).toHaveLength(0)
})
test('same-skill drag still sorts mixed lessons and questions', async ({ page }) => {
  const state = await setup(page)
  state.source.document.categories[0].skills[0].contents.push({
    ...question,
    contentId: 'r',
    kind: 'lesson',
    title: '课节R',
  })
  await page.reload()
  const nav = outline(page)
  await nav.locator('[data-outline-id="q"]').dragTo(nav.locator('[data-outline-id="r"]'))
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect.poll(() => state.saves).toBe(1)
  expect(state.source.document.categories[0].skills[0].contents.map((c) => c.contentId)).toEqual(['r', 'q'])
})

for (const scheme of ['light', 'dark'] as const) {
  test(`destination picker follows ${scheme} theme and fits mobile`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: scheme })
    await page.setViewportSize({ width: 390, height: 844 })
    await setup(page)
    await page.getByRole('button', { name: '打开课程目录', exact: true }).click()
    await page.getByRole('dialog').getByRole('button', { name: '更多操作 题目Q', exact: true }).click()
    await page.getByRole('menuitem', { name: '移动或引用到…' }).click()
    const modal = page.getByRole('dialog', { name: '移动或引用内容' })
    await expect(modal).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
    const box = await modal.boundingBox()
    expect(box!.width).toBeLessThanOrEqual(390)
    await page.screenshot({ path: `/tmp/outline-picker-${scheme}.png`, fullPage: true })
  })
}
