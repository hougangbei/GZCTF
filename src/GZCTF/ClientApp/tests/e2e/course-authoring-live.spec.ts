import { expect, test } from '@playwright/test'
import { createRoleContext } from './support/auth'

test('administrator builds, edits and uniformly publishes a complete course in context', async ({ browser }) => {
  test.skip(!process.env.E2E_ADMIN_PASSWORD, 'Requires isolated real backend and explicit QA administrator')
  test.setTimeout(180000)
  const context = await createRoleContext(browser, 'administrator')
  const page = await context.newPage()
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  const suffix = Date.now().toString().slice(-7)
  const created = await page.request.post('/api/admin/skill-trees', {
    data: { name: `课程编排验收 ${suffix}`, summary: '', iconKey: 'web' },
  })
  expect(created.ok()).toBeTruthy()
  const treeId = (await created.json()).skillTreeId
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('button', { name: '新建类别', exact: true }).click()
  await page.getByRole('dialog').getByRole('textbox', { name: '名称', exact: true }).fill(`类别 ${suffix}`)
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByRole('heading', { name: `类别 ${suffix}`, exact: true })).toBeVisible()
  await page.getByRole('button', { name: '新建技能', exact: true }).click()
  await page.getByRole('dialog').getByRole('textbox', { name: '名称', exact: true }).fill(`技能 ${suffix}`)
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page.getByRole('heading', { name: `技能 ${suffix}`, exact: true })).toBeVisible()
  await page.getByRole('button', { name: '新建课节', exact: true }).click()
  await page.getByRole('dialog').getByRole('textbox', { name: '名称', exact: true }).fill(`课节 ${suffix}`)
  await page.getByRole('button', { name: '创建并添加', exact: true }).click()
  await expect(page).toHaveURL(/\/admin\/library\/lessons\/.*course=/)
  await page.getByRole('textbox', { name: '课节标题' }).fill(`课节正文 ${suffix}`)
  await page.locator('textarea:not([aria-hidden=true])').first().fill(`## 课程内容 ${suffix}\n\n只有统一发布后才可见。`)
  await page.getByRole('button', { name: '保存并返回课程' }).click()
  await expect(page).toHaveURL(new RegExp(`/admin/skill-trees/${treeId}\\?`))
  expect((await page.request.get(`/api/skill-trees/${treeId}`)).status()).toBe(404)
  await page.getByRole('button', { name: '检查并发布' }).click()
  await expect(page.getByRole('dialog').getByText('1 篇课节')).toBeVisible()
  await page.getByRole('button', { name: '确认统一发布' }).click()
  await expect(page.getByRole('alert').filter({ hasText: '课程已统一发布' })).toBeVisible()
  const published = await page.request.get(`/api/skill-trees/${treeId}`)
  expect(published.ok()).toBeTruthy()
  const publicTree = await published.json(),
    category = publicTree.categories[0],
    skill = category.skills[0],
    lesson = skill.contents[0]
  expect(lesson.title).toBe(`课节正文 ${suffix}`)
  await page.goto(`/skill-trees/${treeId}/${category.categoryId}/skills/${skill.skillId}/lesson/${lesson.contentId}`)
  await expect(page.locator('article h1')).toHaveText(`课节正文 ${suffix}`)
  await expect(page.getByRole('heading', { name: `课程内容 ${suffix}` })).toBeVisible()
  await page.goto(`/admin/skill-trees/${treeId}`)
  await page.getByRole('textbox', { name: '名称', exact: true }).fill(`未发布修改 ${suffix}`)
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  await expect(page.getByRole('alert').filter({ hasText: '草稿已保存' })).toBeVisible()
  expect((await (await page.request.get(`/api/skill-trees/${treeId}`)).json()).name).toBe(`课程编排验收 ${suffix}`)
  await context.close()
})
