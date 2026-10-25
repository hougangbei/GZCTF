import { expect, test } from '@playwright/test'

const score = {
  metric: 'score',
  generatedAtUtc: '2026-10-11T12:00:00Z',
  entries: [
    { rank: 1, userName: 'cipher', solvedCount: 18, score: 1720 },
    { rank: 2, userName: 'long_username_sample', solvedCount: 15, score: 1460 },
    { rank: 3, userName: 'pwncat', solvedCount: 12, score: 1080 },
    { rank: 4, userName: 'learner', solvedCount: 9, score: 780 },
  ],
  series: [
    { userName: 'cipher', points: [{ date: '2026-10-08', solvedCount: 5, score: 480 }, { date: '2026-10-10', solvedCount: 18, score: 1720 }] },
    { userName: 'long_username_sample', points: [{ date: '2026-10-09', solvedCount: 8, score: 760 }, { date: '2026-10-11', solvedCount: 15, score: 1460 }] },
  ],
}

test('empty leaderboard explains how to appear and offers a route into practice', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/learning-leaderboard?*', route => route.fulfill({
    json: { metric: 'score', entries: [], series: [] },
  }))
  await page.goto('/leaderboard')

  await expect(page.getByRole('heading', { name: '还没有人上榜' })).toBeVisible()
  await expect(page.getByText('完成已发布技能树中的题目后，成绩会自动出现在这里。')).toBeVisible()
  await expect(page.getByRole('link', { name: '浏览技能树' })).toHaveAttribute('href', '/skill-trees')
  const selector = await page.locator('.mantine-SegmentedControl-root').boundingBox()
  expect(selector?.width).toBeLessThan(400)
})

test('ranking stays readable on phones and switches to the requested metric', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/learning-leaderboard?*', async route => {
    const metric = new URL(route.request().url()).searchParams.get('metric')
    if (metric === 'solves') {
      await new Promise(resolve => setTimeout(resolve, 500))
      await route.fulfill({ json: { ...score, metric: 'solves', entries: [
        { rank: 1, userName: 'pwncat', solvedCount: 20, score: 1080 },
        ...score.entries.filter(entry => entry.userName !== 'pwncat'),
      ] } })
    } else {
      await route.fulfill({ json: score })
    }
  })
  await page.goto('/leaderboard')
  await expect(page.getByRole('heading', { name: '学习者排行' })).toBeVisible()
  await expect(page.getByRole('table')).toBeVisible()
  await expect(page.locator('tbody tr').first()).toContainText('cipher')
  await expect(page.getByRole('heading', { name: '进步轨迹' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)

  await page.getByText('累计解题数', { exact: true }).click()
  await expect(page.getByRole('status')).toContainText('正在更新排行榜')
  await expect(page.locator('tbody tr').first()).toContainText('pwncat')
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})

test('leaderboard error offers retry and recovers into the empty state', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  let calls = 0
  await page.route('**/api/learning-leaderboard?*', route => {
    calls += 1
    return route.fulfill(calls === 1
      ? { status: 503, contentType: 'application/json', body: '{}' }
      : { json: { metric: 'score', entries: [], series: [] } })
  })
  await page.goto('/leaderboard')
  await expect(page.getByRole('alert')).toContainText('排行榜加载失败')
  await page.getByRole('button', { name: '重新加载' }).click()
  await expect(page.getByRole('heading', { name: '还没有人上榜' })).toBeVisible()
  expect(calls).toBeGreaterThanOrEqual(2)
})
