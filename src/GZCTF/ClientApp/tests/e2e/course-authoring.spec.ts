import { expect, test, type Page } from '@playwright/test'

const treeId = '01990000-0000-7000-8000-000000000001'
const categoryId = '01990000-0000-7000-8000-000000000002'
const skillId = '01990000-0000-7000-8000-000000000003'
const lessonId = '01990000-0000-7000-8000-000000000004'
const base = {
  skillTreeId: treeId,
  version: '1',
  hasPublishedVersion: false,
  document: { name: 'Web 课程', summary: '', iconKey: 'web', categories: [] as any[] },
}
async function setup(page: Page) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } })
  )
  let workspace = structuredClone(base)
  let saves = 0
  let creates = 0
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace`, (route) => {
    if (route.request().method() === 'PUT') {
      saves++
      workspace = { ...workspace, version: String(saves + 1), document: route.request().postDataJSON().document }
    }
    return route.fulfill({ json: workspace })
  })
  await page.route('**/api/admin/skill-categories', (route) => {
    if (route.request().method() === 'POST') {
      creates++
      return route.fulfill({
        json: { categoryId, name: route.request().postDataJSON().name, summary: '', iconKey: 'flag', skills: [] },
      })
    }
    return route.fulfill({ json: [] })
  })
  await page.route('**/api/admin/skills', (route) =>
    route.fulfill({
      json:
        route.request().method() === 'POST'
          ? {
              skillId,
              name: route.request().postDataJSON().name,
              summary: '',
              iconKey: 'flag',
              isEnabled: true,
              contents: [],
            }
          : [],
    })
  )
  await page.route('**/api/admin/lessons', (route) =>
    route.fulfill({ json: { id: lessonId, title: route.request().postDataJSON().localizations[0].title } })
  )
  const operations = new Map<string, unknown>()
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/nodes`, (route) => {
    const command = route.request().postDataJSON()
    let resource = operations.get(command.operationId)
    if (!resource) {
      creates++
      resource = {
        id: command.kind === 'category' ? categoryId : command.kind === 'skill' ? skillId : lessonId,
        name: command.name,
        summary: '',
        iconKey: 'flag',
        skills: [],
        contents: [],
        isEnabled: true,
        created: true,
      }
      operations.set(command.operationId, resource)
    }
    return route.fulfill({ json: resource })
  })
  return {
    get workspace() {
      return workspace
    },
    get saves() {
      return saves
    },
    get creates() {
      return creates
    },
  }
}
test('builds the four levels inline and returns to the full editor with context', async ({ page }) => {
  const state = await setup(page)
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '新建类别', exact: true }).click()
  await page.getByRole('textbox', { name: '名称', exact: true }).last().fill('Web 入门')
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Web 入门', exact: true })).toBeVisible()
  await page.getByRole('button', { name: '新建技能', exact: true }).click()
  await page.getByRole('textbox', { name: '名称', exact: true }).last().fill('HTTP 基础')
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'HTTP 基础', exact: true })).toBeVisible()
  await page.getByRole('button', { name: '新建课节', exact: true }).click()
  await page.getByRole('textbox', { name: '名称', exact: true }).last().fill('请求与响应')
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page).toHaveURL(
    new RegExp(`/admin/library/lessons/${lessonId}\\?course=${treeId}&category=${categoryId}&skill=${skillId}`)
  )
  expect(state.workspace.document.categories[0].skills[0].contents).toMatchObject([
    { contentId: lessonId, kind: 'lesson' },
  ])
})
test('an association retry reuses the created identity instead of creating a duplicate', async ({ page }) => {
  const state = await setup(page)
  let failed = false
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace`, async (route) => {
    if (route.request().method() === 'PUT' && !failed) {
      failed = true
      return route.fulfill({ status: 503, json: { message: 'Temporarily unavailable' } })
    }
    await route.fallback()
  })
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '新建类别', exact: true }).click()
  await page.getByRole('textbox', { name: '名称', exact: true }).last().fill('Web 入门')
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByText(/已创建，关联尚未保存/)).toBeVisible()
  await page.getByRole('button', { name: '重试保存关联', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Web 入门', exact: true })).toBeVisible()
  expect(state.creates).toBe(1)
})
test('stale saves preserve local edits and never proceed to publication', async ({ page }) => {
  await setup(page)
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace`, async (route) =>
    route.request().method() === 'PUT'
      ? route.fulfill({ status: 409, json: { code: 'course_workspace_conflict' } })
      : route.fallback()
  )
  let published = 0
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/publish`, (route) => {
    published++
    return route.fulfill({ status: 204 })
  })
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('textbox', { name: '名称', exact: true }).fill('保留我的草稿')
  await page.getByRole('button', { name: '检查并发布', exact: true }).click()
  await expect(page.getByRole('alert').filter({ hasText: '版本已变化' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: '名称', exact: true })).toHaveValue('保留我的草稿')
  expect(published).toBe(0)
})

test('incomplete editor context never enables a global content editor', async ({ page }) => {
  await setup(page)
  let globalReads = 0
  await page.route(`**/api/admin/challenges/${lessonId}/edit?*`, (route) => {
    globalReads++
    return route.fulfill({ json: {} })
  })
  await page.goto(`/admin/library/challenges/${lessonId}?category=${categoryId}&skill=${skillId}`)
  await expect(page.getByRole('alert')).toContainText('上下文无效')
  expect(globalReads).toBe(0)
  await expect(page.getByRole('button', { name: /保存草稿|Save draft/ })).toHaveCount(0)
})

test('course lesson saves only to its workspace, then returns with the fresh outline version', async ({ page }) => {
  const state = await setup(page)
  state.workspace.document.categories.push({
    categoryId,
    name: 'Web 入门',
    summary: '',
    iconKey: 'web',
    skills: [
      {
        skillId,
        name: 'HTTP',
        summary: '',
        iconKey: 'web',
        isEnabled: true,
        contents: [{ contentId: lessonId, kind: 'lesson', title: 'Old title', summary: '', state: 'Draft' }],
      },
    ],
  })
  let savedPath = '',
    putBody: any
  await page.route(`**/api/admin/lessons/${lessonId}?*`, async (route) => {
    if (route.request().method() === 'PUT') {
      savedPath = route.request().url()
      putBody = route.request().postDataJSON()
      state.workspace.version = '2'
      state.workspace.document.categories[0].skills[0].contents[0].title = 'New title'
    }
    return route.fulfill({
      json: {
        id: lessonId,
        locale: 'zh',
        title: 'Old title',
        body: 'Body',
        localizations: [{ locale: 'zh', title: putBody ? 'New title' : 'Old title', body: 'Body' }],
        publication: { rowVersion: putBody ? 2 : 1, publicationState: 'Draft', categoryIds: [categoryId] },
      },
    })
  })
  await page.goto(`/admin/skill-trees/${treeId}?category=${categoryId}&skill=${skillId}`)
  await page.getByRole('region', { name: '节点编辑' }).getByRole('button', { name: '编辑', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '课节标题' })).toHaveValue('Old title')
  await page.getByRole('textbox', { name: '课节标题' }).fill('New title')
  await page.getByRole('button', { name: '保存并返回课程' }).click()
  await expect(page).toHaveURL(new RegExp(`/admin/skill-trees/${treeId}\\?category=${categoryId}&skill=${skillId}`))
  await expect(page.getByText('New title').first()).toBeVisible()
  expect(savedPath).toContain(`course=${treeId}`)
  expect(putBody.rowVersion).toBe(1)
})

test('review invalidates on edits, blocks errors, and publishes the checked version', async ({ page }) => {
  const state = await setup(page)
  let checks = 0,
    published: any
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/check`, (route) => {
    checks++
    return route.fulfill({
      json: {
        version: route.request().postDataJSON().version,
        categoryCount: 0,
        skillCount: 0,
        challengeCount: 0,
        lessonCount: 0,
        issues: checks === 1 ? [{ severity: 'error', code: 'missing_content', message: '请先完善题目配置' }] : [],
      },
    })
  })
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/publish`, (route) => {
    published = route.request().postDataJSON()
    return route.fulfill({ status: 204 })
  })
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '检查并发布' }).click()
  await expect(page.getByRole('button', { name: '确认统一发布' })).toBeDisabled()
  await page.getByRole('button', { name: '继续编辑' }).click()
  await page.getByRole('textbox', { name: '名称', exact: true }).fill('Updated route')
  await page.getByRole('button', { name: '检查并发布' }).click()
  await page.getByRole('button', { name: '确认统一发布' }).click()
  expect(published).toEqual({ version: state.workspace.version })
})

for (const scheme of ['light', 'dark'] as const) {
  test(`mobile authoring directory and themed inputs remain usable (${scheme})`, async ({ page }) => {
    await setup(page)
    await page.setViewportSize({ width: 390, height: 844 })
    await page.emulateMedia({ colorScheme: scheme })
    await page.goto(`/admin/skill-trees/${treeId}`)
    await page.getByRole('button', { name: '打开课程目录' }).click()
    await expect(page.getByRole('dialog').getByRole('navigation', { name: '课程编排目录' })).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('button', { name: '打开课程目录' })).toBeFocused()
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
  })
}

test('reusing a skill from the library keeps this course draft definition and its stable identity', async ({
  page,
}) => {
  const state = await setup(page)
  state.workspace.document.categories.push(
    {
      categoryId,
      name: 'A',
      summary: '',
      iconKey: 'web',
      skills: [{ skillId, name: 'Edited skill', summary: 'Edited', iconKey: 'web', isEnabled: true, contents: [] }],
    },
    { categoryId: '01990000-0000-7000-8000-000000000005', name: 'B', summary: '', iconKey: 'web', skills: [] }
  )
  await page.route('**/api/admin/skills', (route) =>
    route.fulfill({
      json: [
        {
          skillId,
          name: 'Old library skill',
          summary: 'Old',
          iconKey: 'flag',
          isEnabled: true,
          categories: [{ categoryId }],
          contents: [],
        },
      ],
    })
  )
  await page.goto(`/admin/skill-trees/${treeId}?category=01990000-0000-7000-8000-000000000005`)
  await page.getByRole('button', { name: '从库添加技能', exact: true }).click()
  await page.getByRole('combobox', { name: '选择已有资源' }).click()
  await page.getByRole('option', { name: 'Old library skill' }).click()
  await page.getByRole('button', { name: '添加到当前目录' }).click()
  await expect(page.getByRole('heading', { name: 'Edited skill', exact: true })).toBeVisible()
  expect(state.workspace.document.categories.map((c) => c.skills[0].skillId)).toEqual([skillId, skillId])
  expect(state.workspace.document.categories.map((c) => c.skills[0].name)).toEqual(['Edited skill', 'Edited skill'])
})

test('learner preview uses the safe prepared snapshot and shows its body without edit controls', async ({ page }) => {
  await setup(page)
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/preview?*`, (route) =>
    route.fulfill({
      json: {
        skillTreeId: treeId,
        name: 'Prepared preview',
        summary: 'Public view',
        categories: [
          {
            categoryId,
            name: 'Category',
            skills: [
              { skillId, name: 'Skill', contents: [{ kind: 'lesson', contentId: lessonId, title: 'Prepared lesson' }] },
            ],
          },
        ],
      },
    })
  )
  await page.route(`**/api/admin/lessons/${lessonId}?*`, (route) =>
    route.fulfill({
      json: {
        id: lessonId,
        localizations: [{ locale: 'zh', title: 'Prepared lesson', body: '## Safe preview body\n\nPreview text' }],
      },
    })
  )
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '学员预览', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('heading', { name: 'Prepared preview' })).toBeVisible()
  await dialog.getByRole('button', { name: '课节 · Prepared lesson' }).click()
  await expect(dialog.getByRole('heading', { name: 'Safe preview body' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: /上移|下移/ })).toHaveCount(0)
})

test('browser Back cannot silently discard unsaved workspace edits', async ({ page }) => {
  await setup(page)
  await page.goto('/skill-trees')
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('textbox', { name: '名称', exact: true }).fill('Keep this draft')
  page.once('dialog', (dialog) => dialog.dismiss())
  const dialogEvent = page.waitForEvent('dialog')
  await page.evaluate(() => window.history.back())
  await dialogEvent
  await expect(page).toHaveURL(new RegExp(`/admin/skill-trees/${treeId}$`))
  await expect(page.getByRole('textbox', { name: '名称', exact: true })).toHaveValue('Keep this draft')
})

test('two-admin content conflicts preserve the local draft and require an explicit comparison', async ({ page }) => {
  await setup(page)
  let stale = true
  await page.route(`**/api/admin/lessons/${lessonId}?*`, (route) => {
    if (route.request().method() === 'PUT' && stale) {
      stale = false
      return route.fulfill({ status: 409, json: { code: 'skill_tree_revision_conflict' } })
    }
    return route.fulfill({
      json: {
        id: lessonId,
        locale: 'zh',
        title: 'Server title',
        body: 'Server body',
        localizations: [{ locale: 'zh', title: 'Server title', body: 'Server body' }],
        publication: { rowVersion: stale ? 1 : 2, publicationState: 'Draft' },
      },
    })
  })
  await page.goto(`/admin/library/lessons/${lessonId}?course=${treeId}&category=${categoryId}&skill=${skillId}`)
  await page.getByRole('textbox', { name: '课节标题' }).fill('My local title')
  await page.getByRole('button', { name: '保存并返回课程' }).click()
  await expect(page.getByRole('alert', { name: '课节版本冲突' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: '课节标题' })).toHaveValue('My local title')
  await page.getByRole('button', { name: '读取最新版本并比较' }).click()
  await expect(page.getByRole('textbox', { name: '服务端版本（只读）' })).toHaveValue('Server body')
  await page.getByRole('button', { name: '已比较，保留我的输入继续编辑' }).click()
  await expect(page.getByRole('textbox', { name: '课节标题' })).toHaveValue('My local title')
  await expect(page.getByRole('alert', { name: '课节版本冲突' })).toHaveCount(0)
})

test('an ambiguous creation response retries the same operation id', async ({ page }) => {
  await setup(page)
  let operation: string | undefined
  let requests = 0
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/nodes`, async (route) => {
    requests++
    const body = route.request().postDataJSON()
    if (requests === 1) {
      operation = body.operationId
      return route.abort('failed')
    }
    expect(body.operationId).toBe(operation)
    return route.fulfill({
      json: { id: categoryId, name: body.name, summary: '', iconKey: 'flag', skills: [], created: true },
    })
  })
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '新建类别', exact: true }).click()
  await page.getByRole('dialog').getByRole('textbox', { name: '名称', exact: true }).fill('Recover once')
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByText('本次创建结果尚未确认。重试会查询同一创建操作，不会再创建一份。')).toBeVisible()
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Recover once', exact: true })).toBeVisible()
  expect(requests).toBe(2)
})

test('pending creation survives closing and reopening the dialog without a new identity', async ({ page }) => {
  await setup(page)
  let firstId: string | undefined,
    calls = 0
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/nodes`, (route) => {
    calls++
    const body = route.request().postDataJSON()
    if (calls === 1) {
      firstId = body.operationId
      return route.abort('failed')
    }
    expect(body.operationId).toBe(firstId)
    return route.fulfill({
      json: { id: categoryId, name: body.name, summary: '', iconKey: 'flag', skills: [], created: true },
    })
  })
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '新建类别', exact: true }).click()
  await page.getByRole('dialog').getByRole('textbox', { name: '名称', exact: true }).fill('Restore operation')
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByText('本次创建结果尚未确认。重试会查询同一创建操作，不会再创建一份。')).toBeVisible()
  await page.getByRole('button', { name: '取消', exact: true }).click()
  await page.getByRole('button', { name: '新建类别', exact: true }).click()
  await expect(page.getByRole('dialog').getByRole('textbox', { name: '名称', exact: true })).toHaveValue(
    'Restore operation'
  )
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Restore operation', exact: true })).toBeVisible()
  expect(calls).toBe(2)
})

test('deleting a renamed draft confirms the server identity and reviewed version',async({page})=>{
 await setup(page);let deletion:any
 await page.route(`**/api/admin/skill-trees/${treeId}/delete-impact`,route=>route.fulfill({json:{name:'Original published name',requiresTypedConfirmation:true,rowVersion:42,categoryCount:0,challengeCount:0,lessonCount:0,enrollmentCount:1}}))
 await page.route(`**/api/admin/skill-trees/${treeId}`,route=>{deletion=route.request().postDataJSON();return route.fulfill({status:204})})
 await page.goto(`/admin/skill-trees/${treeId}`)
 await page.getByRole('button',{name:'删除技能树',exact:true}).click()
 const dialog=page.getByRole('dialog')
 await dialog.getByRole('textbox').fill('Original published name')
 await dialog.getByRole('button',{name:/删除|Delete/,exact:true}).click()
 await expect(page).toHaveURL(/\/admin\/skill-trees$/)
 expect(deletion).toEqual({confirmationName:'Original published name',rowVersion:42})
})

test('publication errors identify the content and explain missing description in Chinese', async ({ page }) => {
  const state = await setup(page)
  state.workspace.document.categories.push({ categoryId, name: '资料', summary: '', iconKey: 'flag', skills: [{ skillId, name: '参考资料', summary: '', iconKey: 'flag', isEnabled: true, contents: [{ contentId: lessonId, kind: 'challenge', title: 'WP模板', summary: '', state: 'Draft' }] }] })
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/check`, route => route.fulfill({ json: { version: '1', categoryCount: 1, skillCount: 1, challengeCount: 1, lessonCount: 0, issues: [{ severity: 'error', code: 'challenge_invalid', message: 'The challenge is missing, retired, or needs a title and description.', contentId: lessonId }] } }))
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '检查并发布', exact: true }).click()
  const error = page.getByRole('dialog').getByRole('alert')
  await expect(error).toContainText('WP模板')
  await expect(error).toContainText('请补全题目标题和各语言版本的正文')
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeDisabled()
  await error.getByRole('button', { name: '定位问题', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'WP模板', exact: true })).toBeVisible()
})

test('cover and disabled-content warnings explain that they do not prevent publication', async ({ page }) => {
  await setup(page)
  await page.route(`**/api/admin/skill-trees/${treeId}/workspace/check`, route => route.fulfill({ json: { version: '1', categoryCount: 0, skillCount: 0, challengeCount: 0, lessonCount: 0, issues: [{ severity: 'warning', code: 'missing_cover', message: 'The course has no cover.' }, { severity: 'warning', code: 'disabled_challenge', message: 'This challenge is disabled.' }] } }))
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '检查并发布', exact: true }).click()
  await expect(page.getByRole('dialog')).toContainText('尚未设置课程封面，不影响发布。')
  await expect(page.getByRole('dialog')).toContainText('该题目已停用，发布后学员无法使用它；不影响其他内容发布。')
  await expect(page.getByRole('button', { name: '确认统一发布', exact: true })).toBeEnabled()
})
