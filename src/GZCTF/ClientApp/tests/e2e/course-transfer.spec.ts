import { expect, test, type Page } from '@playwright/test'

const sourceId = '10000000-0000-0000-0000-000000000001',
  targetId = '10000000-0000-0000-0000-000000000002'
const categoryId = '20000000-0000-0000-0000-000000000001',
  skillId = '30000000-0000-0000-0000-000000000001'
const targetCategory = '20000000-0000-0000-0000-000000000002',
  targetSkill = '30000000-0000-0000-0000-000000000002'
const contentId = '40000000-0000-0000-0000-000000000001'
const content = { contentId, kind: 'challenge', title: '迁入Pwn题', summary: '保留原题', state: 'Draft' }
async function setup(page: Page) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  const make = (id: string, name: string, c: string, s: string, contents: any[]) => ({
    skillTreeId: id,
    version: id,
    hasPublishedVersion: true,
    document: {
      name,
      summary: '',
      iconKey: 'pwn',
      categories: [
        {
          categoryId: c,
          name: 'MISC',
          summary: '',
          iconKey: 'pwn',
          skills: [{ skillId: s, name: '基础技能', summary: '', iconKey: 'pwn', isEnabled: true, contents }],
        },
      ],
    },
  })
  const state = {
    source: make(sourceId, 'ExSolo个人赛', categoryId, skillId, [content]),
    target: make(targetId, 'NYNUCTF', targetCategory, targetSkill, []),
    requests: [] as any[],
    conflict: false,
  }
  await page.route('**/api/**', (r) => r.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (r) =>
    r.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } })
  )
  await page.route('**/api/admin/skill-trees', (r) =>
    r.fulfill({
      json: [
        { skillTreeId: sourceId, name: 'ExSolo个人赛' },
        { skillTreeId: targetId, name: 'NYNUCTF' },
      ],
    })
  )
  for (const key of ['source', 'target'] as const)
    await page.route(`**/api/admin/skill-trees/${state[key].skillTreeId}/workspace`, (r) =>
      r.fulfill({ json: state[key] })
    )
  await page.route('**/api/admin/course-transfers', (r) => {
    state.requests.push(r.request().postDataJSON())
    if (state.conflict) return r.fulfill({ status: 409, json: { message: '草稿已变化' } })
    const mode = r.request().postDataJSON().mode
    state.target = {
      ...state.target,
      version: '50000000-0000-0000-0000-000000000002',
      document: {
        ...state.target.document,
        categories: [
          {
            ...state.target.document.categories[0],
            skills: [{ ...state.target.document.categories[0].skills[0], contents: [content] }],
          },
        ],
      },
    }
    if (mode === 'move')
      state.source = {
        ...state.source,
        version: '50000000-0000-0000-0000-000000000001',
        document: {
          ...state.source.document,
          categories: [
            {
              ...state.source.document.categories[0],
              skills: [{ ...state.source.document.categories[0].skills[0], contents: [] }],
            },
          ],
        },
      }
    return r.fulfill({
      json: {
        source: state.source,
        target: state.target,
        addedCount: 1,
        removedCount: mode === 'move' ? 1 : 0,
        duplicateCount: 0,
        noOp: false,
      },
    })
  })
  await page.goto(`/admin/content-transfer?source=${sourceId}&target=${targetId}`)
  return state
}
async function select(page: Page) {
  await page.getByRole('checkbox', { name: '选择 迁入Pwn题' }).check()
  await page.getByRole('button', { name: '选择目标 MISC / 基础技能' }).click()
}
test('moves selected content through explicit preview without publishing', async ({ page }) => {
  const state = await setup(page)
  await select(page)
  await page.getByRole('button', { name: '移动到所选技能', exact: true }).click()
  await expect(page.getByRole('dialog').getByText(/ExSolo个人赛/)).toBeVisible()
  expect(state.requests).toEqual([])
  await page.getByRole('dialog').getByRole('button', { name: '确认移动', exact: true }).click()
  await expect.poll(() => state.requests.length).toBe(1)
  expect(state.requests[0]).toMatchObject({
    mode: 'move',
    sourceTreeId: sourceId,
    targetTreeId: targetId,
    sourceVersion: sourceId,
    targetVersion: targetId,
    contents: [{ kind: 'challenge', contentId }],
  })
  await expect(page.getByText('已保存两边草稿，尚未发布。')).toBeVisible()
})
test('reference mode retains source and keyboard target selection', async ({ page }) => {
  const state = await setup(page)
  await select(page)
  await page.getByRole('radio', { name: '添加引用（保留原位置）' }).check()
  await page.getByRole('button', { name: '添加到所选技能', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '确认添加引用', exact: true }).click()
  await expect.poll(() => state.requests.length).toBe(1)
  expect(state.requests[0].mode).toBe('reference')
  expect(state.source.document.categories[0].skills[0].contents).toHaveLength(1)
})
test('dragging into target opens the same confirmation', async ({ page }) => {
  const state = await setup(page)
  await page.getByTestId(`transfer-content-${contentId}`).dragTo(page.getByTestId(`transfer-target-${targetSkill}`))
  await expect(page.getByRole('dialog')).toBeVisible()
  expect(state.requests).toEqual([])
  await page.getByRole('dialog').getByRole('button', { name: '确认移动', exact: true }).click()
  await expect.poll(() => state.requests.length).toBe(1)
})
test('version conflict preserves selection and requires refresh', async ({ page }) => {
  const state = await setup(page)
  state.conflict = true
  await select(page)
  await page.getByRole('button', { name: '移动到所选技能', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: '确认移动', exact: true }).click()
  await expect(page.getByText(/草稿版本已变化/)).toBeVisible()
  await expect(page.getByRole('checkbox', { name: '选择 迁入Pwn题' })).toBeChecked()
})
test('mobile transfer has no horizontal overflow', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await setup(page)
  await expect(page.getByRole('heading', { name: '整理课程内容', exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})

test('same-name global categories show source and course-specific locations', async ({ page }) => {
  await setup(page)
  await page.route('**/api/admin/skill-categories', (r) =>
    r.fulfill({
      json: [
        { categoryId, name: 'MISC', summary: '', iconKey: 'flag', trees: [], contents: [] },
        { categoryId: targetCategory, name: 'MISC', summary: '', iconKey: 'flag', trees: [], contents: [] },
      ],
    })
  )
  await page.route('**/api/admin/course-resources?kind=category', (r) =>
    r.fulfill({
      json: [
        {
          id: categoryId,
          kind: 'category',
          name: 'MISC',
          sourceLabels: ['ExSolo [gzctf-s7]'],
          locations: [
            {
              treeId: sourceId,
              treeName: 'ExSolo个人赛',
              categoryId,
              categoryName: 'misc（26-ExSolo）',
              state: 'draft',
            },
          ],
          unassociated: false,
          contentCount: 3,
        },
        {
          id: targetCategory,
          kind: 'category',
          name: 'MISC',
          sourceLabels: ['NYNUCTF [campus-ctfd]'],
          locations: [
            {
              treeId: targetId,
              treeName: 'NYNUCTF',
              categoryId: targetCategory,
              categoryName: 'ctfd-misc',
              state: 'published',
            },
          ],
          unassociated: false,
          contentCount: 5,
        },
      ],
    })
  )
  await page.goto('/admin/skill-trees?tab=categories')
  await expect(page.getByText('ExSolo [gzctf-s7]', { exact: true })).toBeVisible()
  await expect(page.getByText('NYNUCTF [campus-ctfd]', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '查看位置（1）' }).first().click()
  await expect(page.getByRole('dialog').getByText('ExSolo个人赛 / misc（26-ExSolo）', { exact: true })).toBeVisible()
  await expect(page.getByRole('dialog').getByText('资源库名称：MISC')).toBeVisible()
})

test('unassociated filter finds removed content without deleting its identity', async ({ page }) => {
  await setup(page)
  await page.route('**/api/admin/challenges**', (r) =>
    r.fulfill({
      json: [
        {
          id: contentId,
          title: '移出的题目',
          type: 'StaticAttachment',
          ctfCategory: 'Pwn',
          publicationState: 'Draft',
          isEnabled: true,
        },
        {
          id: '40000000-0000-0000-0000-000000000002',
          title: '仍在课程的题目',
          type: 'StaticAttachment',
          ctfCategory: 'Pwn',
          publicationState: 'Draft',
          isEnabled: true,
        },
      ],
    })
  )
  await page.route('**/api/admin/course-resources?kind=challenge', (r) =>
    r.fulfill({
      json: [
        {
          id: contentId,
          kind: 'challenge',
          name: '移出的题目',
          sourceLabels: ['CTFd'],
          locations: [],
          unassociated: true,
          contentCount: 1,
        },
        {
          id: '40000000-0000-0000-0000-000000000002',
          kind: 'challenge',
          name: '仍在课程的题目',
          sourceLabels: ['CTFd'],
          locations: [
            {
              treeId: sourceId,
              treeName: 'ExSolo',
              categoryId,
              categoryName: 'PWN',
              skillId,
              skillName: 'Pwn',
              state: 'published',
            },
          ],
          unassociated: false,
          contentCount: 1,
        },
      ],
    })
  )
  await page.goto('/admin/skill-trees?tab=challenges')
  await page.getByRole('switch', { name: '只看未关联到课程' }).check()
  await expect(page.getByText('移出的题目', { exact: true })).toBeVisible()
  await expect(page.getByText('仍在课程的题目', { exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: '未关联到课程', exact: true }).click()
  await expect(page.getByRole('dialog').getByText(/内容仍在资源库中，没有被删除/)).toBeVisible()
})

test('open confirmation does not adopt a newly selected tree', async ({ page }) => {
  const state = await setup(page)
  await select(page)
  await page.getByRole('button', { name: '移动到所选技能', exact: true }).click()
  await page.goBack()
  expect(state.requests).toEqual([])
})
