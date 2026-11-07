import { expect, test, type Page } from '@playwright/test'

const treeId = 'preview-tree'
const categories = ['A', 'B'].map((name) => ({
  categoryId: `category-${name}`, name: `Category ${name}`, summary: '', iconKey: 'flag', rowVersion: 1,
  trees: [{ skillTreeId: treeId }],
}))

async function mockAdmin(page: Page) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('en-US')))
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } }))
  await page.route('**/api/info/config', (route) => route.fulfill({ json: { title: 'GZ' } }))
  await page.route('**/api/admin/skill-trees', (route) => route.fulfill({ json: [
    { skillTreeId: treeId, name: 'Route A', rowVersion: 1 },
    { skillTreeId: 'other-tree', name: 'Route B', rowVersion: 1 },
  ] }))
  await page.route('**/api/admin/skill-categories', (route) => route.fulfill({ json: categories }))
}

test('preview saves unsaved category changes before loading the preview', async ({ page }) => {
  await mockAdmin(page)
  let draft = { revisionId: 'revision', name: 'Route A', summary: '', iconKey: 'flag', rowVersion: 1, categories: [] as typeof categories }
  const requests: string[] = []
  let releaseSave!: () => void
  const saveGate = new Promise<void>((resolve) => { releaseSave = resolve })
  await page.route(`**/api/admin/skill-trees/${treeId}/draft`, async (route) => {
    if (route.request().method() === 'PUT') {
      requests.push('save')
      const payload = route.request().postDataJSON()
      draft = { ...draft, ...payload, rowVersion: 2, categories: payload.categories.map((item: { categoryId: string }) => categories.find((category) => category.categoryId === item.categoryId)) }
      await saveGate
    }
    await route.fulfill({ json: draft })
  })
  await page.route(`**/api/admin/skill-trees/${treeId}/draft/preview`, (route) => {
    requests.push('preview')
    return route.fulfill({ json: { categories: draft.categories } })
  })
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: 'Add category', exact: true }).first().click()
  await page.getByRole('button', { name: 'Preview draft', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Preview draft', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Publish', exact: true })).toBeDisabled()
  await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toBeDisabled()
  await expect(page.getByRole('textbox', { name: 'Summary', exact: true })).toBeDisabled()
  releaseSave()
  await expect(page.getByRole('dialog')).toContainText('Category A')
  expect(requests).toEqual(['save', 'preview'])
})

test('preview keeps local changes and shows the existing conflict when saving fails', async ({ page }) => {
  await mockAdmin(page)
  let previews = 0
  await page.route(`**/api/admin/skill-trees/${treeId}/draft`, (route) => route.request().method() === 'PUT'
    ? route.fulfill({ status: 409, json: { code: 'skill_tree_revision_conflict' } })
    : route.fulfill({ json: { revisionId: 'revision', name: 'Route A', summary: '', iconKey: 'flag', rowVersion: 1, categories: [] } }))
  await page.route(`**/api/admin/skill-trees/${treeId}/draft/preview`, (route) => {
    previews += 1
    return route.fulfill({ json: { categories: [] } })
  })
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('textbox', { name: 'Name', exact: true }).fill('My unsaved route')
  await page.getByRole('button', { name: 'Preview draft', exact: true }).click()
  await expect(page.getByRole('alert', { name: /changed|conflict/i })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('My unsaved route')
  expect(previews).toBe(0)
})

for (const change of ['category', 'route'] as const) {
  test(`changing the publish ${change} clears the previous inline skill draft`, async ({ page }) => {
    await mockAdmin(page)
    const lesson = { id: 'lesson', locale: 'en', title: 'Lesson', body: 'Body', localizations: [{ locale: 'en', title: 'Lesson', body: 'Body' }], publication: { rowVersion: 1, publicationState: 'Draft', categoryIds: [] } }
    await page.route('**/api/admin/lessons/lesson?*', (route) => route.fulfill({ json: lesson }))
    await page.route('**/api/admin/lessons/lesson', (route) => route.fulfill({ json: lesson }))
    await page.goto('/admin/library/lessons/lesson')
    await page.getByRole('button', { name: /^(Publish lesson|Publication settings)$/ }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: '选择路线', exact: true }).click()
    await page.getByRole('option', { name: 'Route A', exact: true }).click()
    await dialog.getByRole('combobox', { name: '选择类别', exact: true }).click()
    await page.getByRole('option', { name: 'Category A', exact: true }).click()
    await dialog.getByRole('textbox', { name: '新技能名称', exact: true }).fill('Belongs to A')
    await dialog.getByRole('textbox', { name: '新技能简介', exact: true }).fill('Draft for category A')
    if (change === 'route') {
      await dialog.getByRole('combobox', { name: '选择路线', exact: true }).click()
      await page.getByRole('option', { name: 'Route B', exact: true }).click()
      await dialog.getByRole('combobox', { name: '选择路线', exact: true }).click()
      await page.getByRole('option', { name: 'Route A', exact: true }).click()
    }
    await dialog.getByRole('combobox', { name: '选择类别', exact: true }).click()
    await page.getByRole('option', { name: 'Category B', exact: true }).click()
    await expect(dialog.getByRole('textbox', { name: '新技能名称', exact: true })).toHaveValue('')
    await expect(dialog.getByRole('textbox', { name: '新技能简介', exact: true })).toHaveValue('')
  })
}
