import { expect, test } from '@playwright/test'

test.describe('skill tree discovery', () => {
  test('opens a category card and shows its challenge in a modal without joining', async ({ page }) => {
    const treeId = '01990000-0000-7000-8000-000000000001'
    const categoryId = '01990000-0000-7000-8000-000000000002'
    const challengeId = '01990000-0000-7000-8000-000000000003'
    await page.route(`**/api/skill-trees/${treeId}`, (route) => route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        skillTreeId: treeId,
        name: 'Web 学习',
        summary: '',
        iconKey: 'web',
        categories: [{
          categoryId,
          name: 'Web 基础',
          summary: '从 HTTP 开始',
          iconKey: 'web',
          sortOrder: 0,
          contents: [{
            contentId: challengeId,
            kind: 'challenge',
            sortOrder: 0,
            title: '第一道题',
            summary: '题目摘要',
            expectedMinutes: 30,
            difficulty: 'Normal',
          }],
        }],
      }),
    }))
    await page.route(`**/api/challenges/${challengeId}?locale=*`, (route) => route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        id: challengeId,
        title: '第一道题',
        summary: '题目摘要',
        body: '阅读这段题目正文',
        type: 'StaticAttachment',
        ctfCategory: 'Web',
        hintLocaleCount: 0,
        hasWriteup: false,
        hasAttachment: false,
        hasContainer: false,
      }),
    }))
    await page.route(`**/api/challenges/${challengeId}/community-writeups`, (route) =>
      route.request().method() === 'POST'
        ? route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 'new-wp' }) })
        : route.fulfill({
          contentType: 'application/json',
          body: JSON.stringify([
            { id: 'wp-one', title: 'Solution A', authorName: 'Alice', fileSize: 500, createdAtUtc: new Date().toISOString() },
            { id: 'wp-two', title: 'Solution B', authorName: 'Bob', fileSize: 600, createdAtUtc: new Date().toISOString() },
          ]),
        })
    )

    await page.goto(`/skill-trees/${treeId}`)
    await expect(page.getByRole('link', { name: /Web 基础/ })).toBeVisible()
    await page.getByRole('link', { name: /Web 基础/ }).click({ position: { x: 260, y: 90 } })
    await expect(page.getByRole('link', { name: /第一道题/ })).toBeVisible()
    await page.getByRole('link', { name: /第一道题/ }).click()
    await expect(page.getByRole('dialog', { name: '第一道题' })).toBeVisible()
    await expect(page.getByRole('dialog').getByText('阅读这段题目正文')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Join' })).toHaveCount(0)
    await page.getByRole('button', { name: /查看 WP|View WP/ }).click()
    await expect(page.getByRole('link', { name: /Solution A/ })).toBeVisible()
    await expect(page.getByRole('link', { name: /Solution B/ })).toBeVisible()
    await page.getByRole('textbox', { name: /WP 标题|WP title/ }).fill('My solution')
    await page.getByRole('textbox', { name: /作者|Author/ }).fill('Visitor')
    await page.locator('input[type="file"]').setInputFiles({
      name: 'solution.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\nexample\n%%EOF'),
    })
    await page.getByRole('button', { name: /提交 WP|Submit WP/ }).click()
    await expect(page.getByText(/待审核|pending review/i)).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(page).toHaveURL(new RegExp(`category=${categoryId}$`))
  })

  test('shows public counts and never renders aggregate progress', async ({ page }) => {
    await page.route('**/api/skill-trees', (route) =>
      route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify([
          {
            skillTreeId: '01990000-0000-7000-8000-000000000001',
            name: 'Web 工程师',
            summary: '从 HTTP 到漏洞利用',
            iconKey: 'web',
            categoryCount: 3,
            challengeCount: 12,
            lessonCount: 6,
          },
        ]),
      }),
    )

    await page.goto('/skill-trees')
    await expect(page.getByText('Web 工程师')).toBeVisible()
    await page.getByRole('link', { name: /Web 工程师/ }).click({ position: { x: 260, y: 90 } })
    await expect(page).toHaveURL(/\/skill-trees\/01990000-0000-7000-8000-000000000001$/)
    await page.goBack()
    await expect(page.getByText(/3\s+categories|3\s+个类别/)).toBeVisible()
    await expect(page.getByText(/12\s+challenges|12\s+道题目/)).toBeVisible()
    await expect(page.getByText(/6\s+lessons|6\s+篇课节/)).toBeVisible()
    await expect(page.getByText(/%|路线进度|完成模块|完成课时/i)).toHaveCount(0)
  })
})
