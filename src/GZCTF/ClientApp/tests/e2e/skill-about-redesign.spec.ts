import { expect, test } from '@playwright/test'

const treeId = '01990000-0000-7000-8000-000000000001'
const categoryId = '01990000-0000-7000-8000-000000000002'
const challengeId = '01990000-0000-7000-8000-000000000003'

test('empty skill tree catalog gives visitors a next step on phones', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/skill-trees', route => route.fulfill({ json: [] }))
  await page.goto('/skill-trees')

  await expect(page.getByRole('heading', { name: '新的学习路径正在准备中' })).toBeVisible()
  await expect(page.getByText('技能树发布后会出现在这里。')).toBeVisible()
  await expect(page.getByRole('link', { name: '浏览文章' })).toHaveAttribute('href', '/posts')
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})

test('published trees and category contents form a readable learning path', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/skill-trees', route => route.fulfill({ json: [{
    skillTreeId: treeId, name: 'Web 工程师', summary: '从 HTTP 到漏洞利用',
    iconKey: 'web', categoryCount: 1, challengeCount: 1, lessonCount: 0,
  }] }))
  await page.route(`**/api/skill-trees/${treeId}`, route => route.fulfill({ json: {
    skillTreeId: treeId, name: 'Web 工程师', summary: '从 HTTP 到漏洞利用', iconKey: 'web',
    categories: [{ categoryId, name: 'Web 基础', summary: '从 HTTP 开始', iconKey: 'web', sortOrder: 0,
      contents: [{ contentId: challengeId, kind: 'challenge', title: '第一道题',
        summary: '题目摘要', difficulty: 'Normal', expectedMinutes: 30 }],
    }],
  } }))
  await page.goto('/skill-trees')
  await expect(page.getByText('从一个方向开始，沿着类别逐步练习。')).toBeVisible()
  await page.getByRole('link', { name: /Web 工程师/ }).click()
  await expect(page.getByRole('link', { name: /Web 基础/ })).toBeVisible()
  await page.getByRole('link', { name: /Web 基础/ }).click()
  await expect(page.getByRole('heading', { name: 'Web 基础' })).toBeVisible()
  await expect(page.getByRole('link', { name: /第一道题/ })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})

test('a single published timeline uses the About page width without nested cards', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 })
  await page.route('**/api/about', route => route.fulfill({ json: {
    published: true, document: { schemaVersion: 1, title: 'About the lab', sections: [{
      id: 'history', title: 'History', items: [{ id: 'timeline', type: 'timeline', width: 4,
        title: 'Development timeline', events: [{ time: '2026', title: 'Milestone', description: '<p>Event details</p>' }],
      }],
    }] },
  } }))
  await page.goto('/about')
  const timeline = page.getByRole('heading', { name: 'Development timeline' }).locator('..')
  await expect(page.getByRole('heading', { name: 'About the lab', level: 1 })).toBeVisible()
  expect((await timeline.boundingBox())?.width).toBeGreaterThan(650)
  await expect(page.getByText('Event details')).toBeVisible()
})

test('About page errors can be retried and unpublished content has a useful state', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  let calls = 0
  await page.route('**/api/about', route => {
    calls += 1
    return route.fulfill(calls === 1
      ? { status: 503, contentType: 'application/json', body: '{}' }
      : { json: { published: false } })
  })
  await page.goto('/about')
  await expect(page.getByRole('alert')).toContainText('关于页面暂时无法加载')
  await page.getByRole('button', { name: '重新加载' }).click()
  await expect(page.getByRole('heading', { name: '内容正在整理' })).toBeVisible()
  await expect(page.getByRole('link', { name: '浏览文章' })).toHaveAttribute('href', '/posts')
})
