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
  await expect(page.getByRole('heading', { name: '积分走势' })).toBeVisible()
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


test('competition board puts the trend first and remains dark under a light site theme', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('language', JSON.stringify('zh-CN'))
    localStorage.setItem('mantine-color-scheme-value', 'light')
  })
  await page.route('**/api/learning-leaderboard?*', route => route.fulfill({ json: score }))
  await page.goto('/leaderboard')
  await expect(page.getByRole('table')).toBeVisible()
  const trend = await page.getByRole('heading', { name: '积分走势' }).boundingBox()
  const table = await page.getByRole('table').boundingBox()
  expect(trend && table && trend.y < table.y).toBe(true)
  await expect(page.getByText('每 15 秒自动更新')).toBeVisible()
  const board = page.locator('[data-competition-board]')
  expect(await board.evaluate(el => getComputedStyle(el).backgroundColor)).toBe('rgb(20, 20, 20)')
  expect(await board.evaluate(el => getComputedStyle(el).color)).toBe('rgb(238, 238, 238)')
  await expect(page.locator('tbody tr[data-rank="1"]')).toContainText('cipher')
})

test('long and hostile usernames stay plain text at 320px', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 })
  const username = '<img src=x onerror=alert(1)>' + '用户名'.repeat(20)
  await page.route('**/api/learning-leaderboard?*', route => route.fulfill({ json: {
    ...score, entries: [{ rank: 1, userName: username, score: 99999999, solvedCount: 1200 }],
    series: [{ userName: username, points: [{ date: '2026-10-25', score: 99999999, solvedCount: 1200 }] }],
  } }))
  await page.goto('/leaderboard')
  await expect(page.getByRole('table')).toContainText(username)
  await expect(page.locator('tbody img')).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})

test('resizing from desktop to phone keeps the chart inside its section', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.route('**/api/learning-leaderboard?*', route => route.fulfill({ json: score }))
  await page.goto('/leaderboard')
  const chart = page.getByRole('img').filter({ has: page.locator('canvas') })
  await expect(chart.locator('canvas')).toBeVisible()
  await page.setViewportSize({ width: 390, height: 844 })
  await expect.poll(async () => {
    const outer = await chart.boundingBox()
    const canvas = await chart.locator('canvas').boundingBox()
    return outer && canvas ? Math.abs(canvas.height - outer.height) : 999
  }).toBeLessThan(2)
})

test('approved Figma table has distinct neutral stripes and identifies the signed-in learner', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/account/profile', route => route.fulfill({ json: { userName: 'learner', role: 'User' } }))
  await page.route('**/api/learning-leaderboard?*', route => route.fulfill({ json: score }))
  await page.goto('/leaderboard')
  const rows = page.locator('tbody tr')
  await expect(rows).toHaveCount(4)
  expect(await rows.nth(0).evaluate(el => getComputedStyle(el).backgroundColor)).toBe('rgb(32, 32, 32)')
  expect(await rows.nth(1).evaluate(el => getComputedStyle(el).backgroundColor)).toBe('rgb(48, 48, 48)')
  await expect(rows.nth(3)).toHaveAttribute('data-self', 'true')
  await expect(rows.nth(3)).toContainText('你的位置')
  await expect(rows.nth(3)).toHaveAttribute('aria-current', 'true')
  expect(await page.locator('.mantine-AppShell-header').evaluate(el => getComputedStyle(el).backgroundColor)).toBe('rgb(20, 20, 20)')
  expect(await page.locator('footer p.mantine-Text-root').evaluate(el => getComputedStyle(el).color)).toBe('rgb(181, 181, 181)')
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(rows.nth(3)).toContainText('9 题')
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})
