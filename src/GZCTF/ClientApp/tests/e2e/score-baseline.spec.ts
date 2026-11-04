import { expect, test, type Page } from '@playwright/test'

async function setup(page: Page) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/**', route => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', route => route.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } }))
  await page.route('**/api/config', route => route.fulfill({ json: { title: 'NYNUCTF' } }))
}

test('score migration is explicit, binds preview, and requires accounts and history', async ({ page }) => {
  await setup(page)
  const posted: boolean[] = []
  await page.route('**/api/admin/platform-imports/preview', route => {
    const body = route.request().postDataBuffer()!.toString()
    posted.push(body.includes('"mergeScores":true'))
    return route.fulfill({ json: { format: 'CTFd', title: 'CTFd', canImport: true, planFingerprint: 'plan', challengeCount: 1, fileCount: 0, userCount: 1, teamCount: 0, submissionCount: 1, issues: [] } })
  })
  await page.goto('/admin/imports')
  const merge = page.getByRole('checkbox', { name: '合并 CTFd 旧成绩到训练排行榜', exact: true })
  await expect(merge).not.toBeChecked()
  await page.getByLabel('导出 ZIP').setInputFiles({ name: 'ctfd.zip', mimeType: 'application/zip', buffer: Buffer.from('fixture') })
  await page.getByRole('textbox', { name: '来源标识', exact: true }).fill('campus-ctfd')
  await page.getByRole('button', { name: '检查导出包', exact: true }).click()
  await expect(page.getByRole('button', { name: '确认导入', exact: true })).toBeEnabled()
  await merge.check()
  await expect(page.getByRole('button', { name: '确认导入', exact: true })).toHaveCount(0)
  await expect(page.getByRole('checkbox', { name: '导入账号（保留受支持的旧密码，首次成功登录自动升级）', exact: true })).toBeChecked()
  await expect(page.getByRole('checkbox', { name: '保留原比赛历史成绩、队伍和提交记录', exact: true })).toBeChecked()
  await page.getByRole('button', { name: '检查导出包', exact: true }).click()
  expect(posted).toEqual([false, true])
  await expect(page.getByText('旧题通关同步保留，重做不重复加分。', { exact: false })).toBeVisible()
  await page.getByRole('checkbox', { name: '保留原比赛历史成绩、队伍和提交记录', exact: true }).uncheck()
  await expect(merge).not.toBeChecked()
  await expect(page.getByRole('button', { name: '确认导入', exact: true })).toHaveCount(0)
})

test('leaderboard shows one cumulative total without a migration breakdown on a phone', async ({ page }) => {
  await setup(page)
  await page.route('**/api/learning-leaderboard?*', route => route.fulfill({ json: { metric: 'score', hasImportedScores: true, entries: [{ rank: 1, userName: 'Student', score: 350, solvedCount: 3, importedScore: 250, earnedScore: 100, importedSolvedCount: 2, earnedSolvedCount: 1 }], series: [] } }))
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/leaderboard')
  await expect(page.getByRole('cell', { name: 'Student', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: '350', exact: true })).toBeVisible()
  await expect(page.getByText('旧成绩 250 · 新增 100', { exact: true })).toHaveCount(0)
  await expect(page.getByText('积分持续累计，同一题目重复完成不重复加分。', { exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})
