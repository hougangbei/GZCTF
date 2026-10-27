import { expect, test, type Page } from '@playwright/test'

async function setup(
  page: Page,
  options: {
    failList?: boolean
    failSubmit?: boolean
    delay?: number
    empty?: boolean
    multiple?: boolean
    official?: boolean
  } = {}
) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))

  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'learner', userName: 'Learner', role: 'User' } })
  )
  const treeId = '01990000-0000-7000-8000-000000000001'
  const categoryId = '01990000-0000-7000-8000-000000000002'
  const challengeId = '01990000-0000-7000-8000-000000000003'
  await page.route(`**/api/skill-trees/${treeId}`, (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        skillTreeId: treeId,
        name: 'Web 学习',
        summary: '',
        iconKey: 'web',
        categories: [
          {
            categoryId,
            name: 'Web 基础',
            summary: '从 HTTP 开始',
            iconKey: 'web',
            sortOrder: 0,
            contents: [
              {
                contentId: challengeId,
                kind: 'challenge',
                sortOrder: 0,
                title: '第一道题',
                summary: '题目摘要',
                expectedMinutes: 30,
                difficulty: 'Normal',
              },
            ],
          },
        ],
      }),
    })
  )
  await page.route(`**/api/challenges/${challengeId}?locale=*`, (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        id: challengeId,
        title: '第一道题',
        summary: '题目摘要',
        body: '阅读这段题目正文',
        type: 'StaticAttachment',
        ctfCategory: 'Web',
        hintLocaleCount: 0,
        hasWriteup: options.official ?? false,
        hasAttachment: false,
        hasContainer: false,
      }),
    })
  )

  const submissions: Buffer[] = []
  await page.route(`**/api/challenges/${challengeId}/community-writeups`, async (route) => {
    if (route.request().method() === 'POST') {
      submissions.push(route.request().postDataBuffer()!)
      if (options.delay) await new Promise((resolve) => setTimeout(resolve, options.delay))
      return route.fulfill({ status: options.failSubmit ? 500 : 201, json: { id: 'pending-id' } })
    }
    return route.fulfill({
      status: options.failList ? 500 : 200,
      json: options.failList
        ? {}
        : options.empty
          ? []
          : [
              {
                id: 'wp-one',
                title: 'Solution A',
                authorName: 'Alice',
                fileSize: 2048,
                createdAtUtc: '2026-10-19T00:00:00Z',
              },
              ...(options.multiple
                ? [
                    {
                      id: 'wp-two',
                      title: 'Solution B',
                      authorName: 'Bob',
                      fileSize: 2048,
                      createdAtUtc: '2026-10-19T00:00:00Z',
                    },
                  ]
                : []),
            ],
    })
  })
  await page.route(`**/api/challenges/${challengeId}/writeup*`, (route) =>
    route.fulfill({
      json: { locale: 'zh', content: '官方解题步骤：先分析 HTTP 请求。', firstViewedAtUtc: new Date().toISOString() },
    })
  )
  await page.goto(`/skill-trees/${treeId}/${categoryId}/challenge/${challengeId}`)
  return submissions
}

const pdf = { name: 'solution.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4\nexample\n%%EOF') }
async function draft(page: Page) {
  await page.getByRole('tab', { name: '提交 WP', exact: true }).click()
  await page.getByRole('textbox', { name: 'WP 标题', exact: true }).fill('  HTTP 解题过程  ')
  await page.getByRole('textbox', { name: '作者', exact: true }).fill('  Visitor  ')
  await page.locator('input[type=file]').setInputFiles(pdf)
}

test('separates browsing from submission and posts a trimmed PDF draft once', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1100 })
  const posted = await setup(page, { delay: 700 })
  await expect(page.getByRole('link', { name: 'Solution A' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'WP 标题' })).toBeHidden()
  await draft(page)
  await page.getByRole('region', { name: '社区 WP' }).screenshot({ path: '../../../.local-run/wp-desktop.png' })
  await page.getByRole('tab', { name: '查看解题' }).click()
  await page.getByRole('tab', { name: '提交 WP', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'WP 标题' })).toHaveValue('  HTTP 解题过程  ')
  await page.getByRole('region', { name: '社区 WP' }).getByRole('button', { name: '提交 WP', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '作者', exact: true })).toBeDisabled()
  await expect(
    page.getByRole('region', { name: '社区 WP' }).getByRole('button', { name: '提交 WP', exact: true })
  ).toBeDisabled()
  await page.locator('form[aria-busy="true"]').evaluate((form) => (form as HTMLFormElement).requestSubmit())
  await expect(page.getByText('提交成功，等待管理员审核。', { exact: true })).toBeVisible()
  expect(posted).toHaveLength(1)
  const body = posted[0].toString()
  expect(body).toContain('\r\nHTTP 解题过程\r\n')
  expect(body).toContain('\r\nVisitor\r\n')
  expect(body).toContain('%PDF-1.4')
  await expect(page.getByRole('textbox', { name: 'WP 标题' })).toHaveValue('')
})

test('rejects forged and oversized PDFs before uploading', async ({ page }) => {
  const posted = await setup(page)
  await draft(page)
  await page.locator('input[type=file]').setInputFiles({ ...pdf, buffer: Buffer.from('plain text') })
  await page.getByRole('region', { name: '社区 WP' }).getByRole('button', { name: '提交 WP', exact: true }).click()
  await expect(page.getByText('请选择有效的 PDF 文件。', { exact: true })).toBeVisible()
  expect(posted).toHaveLength(0)
  await page.locator('input[type=file]').setInputFiles({ ...pdf, buffer: Buffer.alloc(20 * 1024 * 1024 + 1) })
  await expect(page.getByText('PDF 文件不能超过 20 MB。', { exact: true })).toBeVisible()
  await expect(
    page.getByRole('region', { name: '社区 WP' }).getByRole('button', { name: '提交 WP', exact: true })
  ).toBeDisabled()
  expect(posted).toHaveLength(0)
})

test('retains draft after failure and retries without losing inputs', async ({ page }) => {
  const options = { failSubmit: true }
  const posted = await setup(page, options)
  await draft(page)
  await page.getByRole('region', { name: '社区 WP' }).getByRole('button', { name: '提交 WP', exact: true }).click()
  await expect(page.getByText('提交失败，请检查 PDF 文件后重试。', { exact: true })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'WP 标题' })).toHaveValue('  HTTP 解题过程  ')
  options.failSubmit = false
  await page.getByRole('region', { name: '社区 WP' }).getByRole('button', { name: '提交 WP', exact: true }).click()
  await expect(page.getByText('提交成功，等待管理员审核。', { exact: true })).toBeVisible()
  expect(posted).toHaveLength(2)
})

test('can submit when listing fails and retries the list independently', async ({ page }) => {
  const options = { failList: true }
  await setup(page, options)
  await expect(page.getByText('WP 加载失败', { exact: true })).toBeVisible()
  options.failList = false
  await page.getByRole('button', { name: '重新加载' }).click()
  await expect(page.getByRole('link', { name: 'Solution A' })).toBeVisible()
  options.failList = true
  await draft(page)
  await page.getByRole('region', { name: '社区 WP' }).getByRole('button', { name: '提交 WP', exact: true }).click()
  await expect(page.getByText('提交成功，等待管理员审核。', { exact: true })).toBeVisible()
  await expect(page.getByText('提交失败，请检查 PDF 文件后重试。', { exact: true })).toBeHidden()
})

test('mobile layout keeps fields and actions inside the dialog', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await setup(page)
  await draft(page)
  for (const name of ['WP 标题', '作者']) {
    const box = await page.getByRole('textbox', { name, exact: true }).boundingBox()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(390)
  }
  const title = await page.getByRole('textbox', { name: 'WP 标题' }).boundingBox()
  const author = await page.getByRole('textbox', { name: '作者', exact: true }).boundingBox()
  expect(author!.y).toBeGreaterThan(title!.y)
  await page
    .getByRole('region', { name: '社区 WP' })
    .getByRole('button', { name: '提交 WP', exact: true })
    .scrollIntoViewIfNeeded()
  await page.screenshot({ path: '../../../.local-run/wp-mobile.png', fullPage: true })
})

test('opens an available PDF from the single writeup section without solving first', async ({ page }) => {
  await setup(page)
  const view = page.getByRole('link', { name: 'Solution A', exact: true })
  await expect(view).toBeVisible()
  await expect(view).toHaveAttribute('href', /\/community-writeups\/wp-one\/pdf$/)
  await expect(view).toHaveAttribute('target', '_blank')
  await page
    .context()
    .route('**/community-writeups/wp-one/pdf', (route) =>
      route.fulfill({ contentType: 'application/pdf', body: pdf.buffer })
    )
  const opened = page.context().waitForEvent('page')
  const requested = page
    .context()
    .waitForEvent('request', { predicate: (request) => request.url().endsWith('/community-writeups/wp-one/pdf') })
  await view.click()
  const preview = await opened
  await requested
  await preview.close()
  await page.getByRole('tab', { name: '提交 WP', exact: true }).click()
  await expect(page.getByRole('tab', { name: '提交 WP', exact: true })).toHaveAttribute('aria-selected', 'true')
})

test('offers submission when no approved WP exists', async ({ page }) => {
  await setup(page, { empty: true })
  await expect(page.getByText('暂无通过审核的 WP', { exact: true })).toBeVisible()
  await expect(page.getByRole('group', { name: 'WP 快捷操作' })).toHaveCount(0)
  await page.getByRole('tab', { name: '提交 WP', exact: true }).click()
  await expect(page.getByRole('textbox', { name: 'WP 标题' })).toBeVisible()
})

test('chooses among multiple WPs and can still submit another approach', async ({ page }) => {
  await setup(page, { multiple: true })
  await expect(page.getByRole('link', { name: 'Solution A' })).toHaveAttribute('href', /wp-one\/pdf$/)
  await expect(page.getByRole('link', { name: 'Solution B' })).toHaveAttribute('href', /wp-two\/pdf$/)
  await page.getByRole('tab', { name: '提交 WP', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '作者', exact: true })).toBeVisible()
})

test('can view the official WP before submitting any flag', async ({ page }) => {
  await setup(page, { empty: true, official: true })
  const response = page.waitForResponse((r) => r.url().includes('/writeup?') && r.request().method() === 'GET')
  await page.getByRole('button', { name: '查看官方题解', exact: true }).click()
  await response
  await expect(page.getByText('官方解题步骤：先分析 HTTP 请求。', { exact: true })).toBeVisible()
  await expect(page.getByRole('textbox', { name: '输入 Flag' })).toHaveValue('')
})
