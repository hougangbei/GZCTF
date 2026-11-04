import { expect, test, type Page } from '@playwright/test'
const user = { userId: 'student', userName: 'Student', bio: '持续学习', role: 'User', hidden: false, sourceHidden: false, score: 1000, importedScore: 900, earnedScore: 100, adjustmentScore: 0, solvedCount: 2, importedSolvedCount: 1, earnedSolvedCount: 1, level: 2, version: 'v1' }
async function setup(page: Page) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/**', route => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', route => route.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } }))
  await page.route('**/api/config', route => route.fulfill({ json: { title: 'NYNUCTF' } }))
  await page.route('**/api/admin/learning-rankings?*', route => route.fulfill({ json: { items: [user], total: 1, skip: 0, take: 20 } }))
  const detail = { user: { ...user }, challenges: [{ challengeId: 'old', title: '旧题目', score: 100, solved: true, imported: true }, { challengeId: 'new', title: '新题目', score: 200, solved: false, imported: false }], operations: [] as unknown[] }
  await page.route('**/api/admin/learning-rankings/student', route => route.fulfill({ json: detail }))
  await page.route('**/api/admin/learning-rankings/operations?*', route => route.fulfill({ json: { items: route.request().url().includes('userId=') ? detail.operations : [], total: route.request().url().includes('userId=') ? detail.operations.length : 0, skip: 0, take: 20 } }))
  await page.route('**/api/admin/learning-rankings/levels', route => route.fulfill({ json: { version: 'levels1', thresholds: [0, 1000, 3000] } }))
  return detail
}
test('admin adjusts points only with a reason and preserves version and operation id', async ({ page }) => {
  const detail = await setup(page); let sent: Record<string, unknown> | undefined
  await page.route('**/api/admin/learning-rankings/student/adjustments', route => { sent = route.request().postDataJSON(); detail.user.score = 1100; detail.user.version = 'v2'; return route.fulfill({ json: detail }) })
  await page.goto('/admin/rankings'); await page.getByRole('button', { name: '管理 Student', exact: true }).click()
  await page.getByRole('button', { name: '加分 / 扣分', exact: true }).click()
  const dialog = page.getByRole('dialog').filter({ has: page.getByRole('textbox', { name: '调整原因', exact: true }) })
  await expect(dialog.getByRole('button', { name: '确认加减分', exact: true })).toBeDisabled()
  await dialog.getByRole('textbox', { name: '积分差额（扣分填负数）', exact: true }).fill('100')
  await dialog.getByRole('textbox', { name: '调整原因', exact: true }).fill('课堂奖励')
  await expect(dialog.getByText('调整后：1,100 分', { exact: true })).toBeVisible()
  await dialog.getByRole('button', { name: '确认加减分', exact: true }).click()
  await expect.poll(() => sent).toMatchObject({ kind: 'delta', value: 100, reason: '课堂奖励', version: 'v1' })
  expect(sent!.operationId).toMatch(/^[a-f0-9-]{36}$/)
  await expect(page.getByText('1,100 分', { exact: true })).toBeVisible()
})
test('conflicting score change stays editable and retry uses the same operation id', async ({ page }) => {
  await setup(page); const payloads: Record<string, unknown>[] = []
  await page.route('**/api/admin/learning-rankings/student/adjustments', route => { payloads.push(route.request().postDataJSON()); return route.fulfill({ status: 409, json: { message: '成绩已变化，请刷新后核对再操作。' } }) })
  await page.goto('/admin/rankings'); await page.getByRole('button', { name: '管理 Student', exact: true }).click(); await page.getByRole('button', { name: '设置目标总分', exact: true }).click()
  const dialog = page.getByRole('dialog').filter({ has: page.getByRole('textbox', { name: '调整原因', exact: true }) })
  await dialog.getByRole('textbox', { name: '目标总分', exact: true }).fill('1500'); await dialog.getByRole('textbox', { name: '调整原因', exact: true }).fill('核对后更正')
  await dialog.getByRole('button', { name: '确认设置总分', exact: true }).click(); await expect(dialog.getByRole('alert')).toContainText('成绩已变化')
  await dialog.getByRole('button', { name: '确认设置总分', exact: true }).click()
  await expect.poll(() => payloads.length).toBe(2); expect(payloads[0].operationId).toEqual(payloads[1].operationId)
})
test('revoke, restore and visibility commands require confirmation and a reason', async ({ page }) => {
  const detail = await setup(page); const sent: { method: string; body: unknown }[] = []
  await page.route('**/api/admin/learning-rankings/student/challenges/old/solve', route => { sent.push({ method: route.request().method(), body: route.request().postDataJSON() }); detail.challenges[0].solved = route.request().method() === 'POST'; return route.fulfill({ json: detail }) })
  await page.route('**/api/admin/learning-rankings/student/visibility', route => { sent.push({ method: route.request().method(), body: route.request().postDataJSON() }); return route.fulfill({ json: detail }) })
  await page.goto('/admin/rankings'); await page.getByRole('button', { name: '管理 Student', exact: true }).click()
  await page.getByRole('button', { name: '撤销通关', exact: true }).click()
  let dialog = page.getByRole('dialog').filter({ has: page.getByRole('textbox', { name: '调整原因', exact: true }) })
  await dialog.getByRole('textbox', { name: '调整原因', exact: true }).fill('成绩复核'); await dialog.getByRole('button', { name: '确认撤销通关', exact: true }).click()
  await expect.poll(() => sent.length).toBe(1); expect(sent[0].method).toBe('DELETE')
  await page.getByRole('button', { name: '补记通关', exact: true }).first().click()
  dialog = page.getByRole('dialog').filter({ has: page.getByRole('textbox', { name: '调整原因', exact: true }) })
  await dialog.getByRole('textbox', { name: '调整原因', exact: true }).fill('已重新完成'); await dialog.getByRole('button', { name: '确认补记通关', exact: true }).click()
  await expect.poll(() => sent.length).toBe(2); expect(sent[1].method).toBe('POST')
  await page.getByRole('button', { name: '隐藏上榜', exact: true }).click()
  dialog = page.getByRole('dialog').filter({ has: page.getByRole('textbox', { name: '调整原因', exact: true }) })
  await dialog.getByRole('textbox', { name: '调整原因', exact: true }).fill('测试账号不参与排名'); await dialog.getByRole('button', { name: '确认上榜可见性', exact: true }).click()
  await expect.poll(() => sent.length).toBe(3); expect(sent[2].body).toMatchObject({ hidden: true, version: 'v1' })
})
test('levels require impact preview before applying and reject unsorted thresholds', async ({ page }) => {
  await setup(page); let sent: unknown
  await page.route('**/api/admin/learning-rankings/levels/preview', route => route.fulfill({ json: { affectedUserCount: 12 } }))
  await page.route('**/api/admin/learning-rankings/levels', route => { if (route.request().method() === 'PUT') { sent = route.request().postDataJSON(); return route.fulfill({ json: { version: 'levels2', thresholds: [0, 2000, 3000] } }) } return route.fulfill({ json: { version: 'levels1', thresholds: [0, 1000, 3000] } }) })
  await page.goto('/admin/rankings'); await page.getByRole('tab', { name: '等级设置', exact: true }).click()
  await expect(page.getByRole('button', { name: '确认应用等级', exact: true })).toBeDisabled()
  await page.getByRole('textbox', { name: 'Lv.2 最低积分', exact: true }).fill('4000'); await expect(page.getByRole('button', { name: '预览等级变化', exact: true })).toBeDisabled()
  await page.getByRole('textbox', { name: 'Lv.2 最低积分', exact: true }).fill('2000'); await page.getByRole('button', { name: '预览等级变化', exact: true }).click()
  await expect(page.getByText('应用后将有 12 个账号的等级变化。积分不会改变。', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '确认应用等级', exact: true }).click()
  await expect.poll(() => sent).toMatchObject({ version: 'levels1', thresholds: [0, 2000, 3000] })
})
test('admin page fits phones and leaderboard renders level and bio as plain text', async ({ page }) => {
  await setup(page); await page.setViewportSize({ width: 390, height: 844 }); await page.goto('/admin/rankings')
  await expect(page.getByRole('button', { name: '管理 Student', exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
  await page.route('**/api/learning-leaderboard?*', route => route.fulfill({ json: { metric: 'score', entries: [{ rank: 1, userName: 'Student', score: 1000, solvedCount: 2, level: 2, bio: '<img src=x onerror=alert(1)>持续学习' }], series: [] } }))
  await page.goto('/leaderboard'); await expect(page.getByText('Lv.2', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('<img src=x onerror=alert(1)>持续学习', { exact: true }).first()).toBeVisible()
  await expect(page.locator('tbody img')).toHaveCount(0)
  await page.getByRole('button', { name: '查看 Student 的个性签名', exact: true }).first().click()
  await expect(page.getByRole('dialog').getByText('<img src=x onerror=alert(1)>持续学习', { exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1)
})
test('operation ledger reversal and CSV export are available', async ({ page }) => {
  const detail = await setup(page); let sent: unknown
  detail.operations = [{ id: 'award', userId: 'student', kind: 'delta', delta: 100, beforeScore: 900, afterScore: 1000, reason: '课堂奖励', actorName: 'Admin', createdAtUtc: '2026-11-04T00:00:00Z', canReverse: true, reversed: false }]
  await page.route('**/api/admin/learning-rankings/adjustments/award/reverse', route => { sent = route.request().postDataJSON(); return route.fulfill({ json: detail }) })
  await page.route('**/api/admin/learning-rankings/export', route => route.fulfill({ contentType: 'text/csv', body: '用户名,积分\nStudent,1000' }))
  await page.goto('/admin/rankings'); const download = page.waitForEvent('download'); await page.getByRole('button', { name: '导出排行榜 CSV', exact: true }).click(); expect((await download).suggestedFilename()).toBe('training-rankings.csv')
  await page.getByRole('button', { name: '管理 Student', exact: true }).click(); await page.getByRole('tab', { name: '该学员调整记录', exact: true }).click(); await page.getByRole('button', { name: '撤销这次调整', exact: true }).click()
  const dialog = page.getByRole('dialog').filter({ has: page.getByRole('textbox', { name: '调整原因', exact: true }) })
  await dialog.getByRole('textbox', { name: '调整原因', exact: true }).fill('重复奖励需冲销'); await dialog.getByRole('button', { name: '确认撤销调整', exact: true }).click()
  await expect.poll(() => sent).toMatchObject({ version: 'v1', reason: '重复奖励需冲销' })
})


test('switching learners never leaves the previous learner editable while loading', async ({ page }) => {
  const detail = await setup(page)
  await page.route('**/api/admin/learning-rankings?*', route => route.fulfill({ json: { items: [user, { ...user, userId: 'second', userName: 'Second' }], total: 2, skip: 0, take: 20 } }))
  await page.route('**/api/admin/learning-rankings/second', async route => { await new Promise(resolve => setTimeout(resolve, 1500)); return route.fulfill({ json: { ...detail, user: { ...user, userId: 'second', userName: 'Second' } } }) })
  await page.goto('/admin/rankings'); await page.getByRole('button', { name: '管理 Student', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Student', exact: true })).toBeVisible()
  await page.keyboard.press('Escape')
  await page.getByRole('button', { name: '管理 Second', exact: true }).click()
  await expect(page.getByRole('button', { name: '加分 / 扣分', exact: true })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Student', exact: true })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Second', exact: true })).toBeVisible()
})

test('older learner adjustments remain paged and reversible and global history identifies its target', async ({ page }) => {
  const detail = await setup(page)
  const award = { id: 'older', userId: 'student', targetUserName: 'Student', kind: 'delta', delta: 50, beforeScore: 950, afterScore: 1000, reason: '早期奖励', actorName: 'Admin', createdAtUtc: '2024-01-01T00:00:00Z', reversed: false, canReverse: true }
  await page.route('**/api/admin/learning-rankings/operations?*', route => {
    const url = new URL(route.request().url()); const perUser = !!url.searchParams.get('userId'); const skip = Number(url.searchParams.get('skip'))
    return route.fulfill({ json: { items: perUser && skip === 0 ? [{ ...award, id: 'recent', reason: '近期奖励', reversed: true, canReverse: false }] : [award], total: perUser ? 21 : 1, skip, take: 20 } })
  })
  let sent: unknown
  await page.route('**/api/admin/learning-rankings/adjustments/older/reverse', route => { sent = route.request().postDataJSON(); return route.fulfill({ json: detail }) })
  await page.goto('/admin/rankings'); await page.getByRole('tab', { name: '调整记录', exact: true }).click()
  await page.getByRole('button', { name: '学员：Student', exact: true }).click()
  await page.getByRole('tab', { name: '该学员调整记录', exact: true }).click()
  await expect(page.getByText('近期奖励', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '下一页学员记录', exact: true }).click()
  await expect(page.getByText('早期奖励', { exact: true }).last()).toBeVisible()
  await page.getByRole('button', { name: '撤销这次调整', exact: true }).click()
  const dialog = page.getByRole('dialog').filter({ has: page.getByRole('textbox', { name: '调整原因', exact: true }) })
  await dialog.getByRole('textbox', { name: '调整原因', exact: true }).fill('历史奖励更正')
  await dialog.getByRole('button', { name: '确认撤销调整', exact: true }).click()
  await expect.poll(() => sent).toMatchObject({ version: 'v1', reason: '历史奖励更正' })
})


test('learner history never shows another learner operations while loading', async ({ page }) => {
  const detail = await setup(page)
  const award = { id: 'award-a', userId: 'student', kind: 'delta', delta: 100, beforeScore: 900, afterScore: 1000, reason: 'A账号奖励', actorName: 'Admin', createdAtUtc: '2026-11-04T00:00:00Z', canReverse: true, reversed: false }
  await page.route('**/api/admin/learning-rankings?*', route => route.fulfill({ json: { items: [user, { ...user, userId: 'second', userName: 'Second' }], total: 2, skip: 0, take: 20 } }))
  await page.route('**/api/admin/learning-rankings/second', route => route.fulfill({ json: { ...detail, user: { ...user, userId: 'second', userName: 'Second' } } }))
  await page.route('**/api/admin/learning-rankings/operations?*', async route => {
    const id = new URL(route.request().url()).searchParams.get('userId')
    if (id === 'second') await new Promise(resolve => setTimeout(resolve, 1500))
    return route.fulfill({ json: { items: id === 'student' ? [award] : [], total: id === 'student' ? 1 : 0, skip: 0, take: 20 } })
  })
  await page.goto('/admin/rankings'); await page.getByRole('button', { name: '管理 Student', exact: true }).click(); await page.getByRole('tab', { name: '该学员调整记录', exact: true }).click()
  await expect(page.getByText('A账号奖励', { exact: true })).toBeVisible(); await page.keyboard.press('Escape')
  await page.getByRole('button', { name: '管理 Second', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Second', exact: true })).toBeVisible()
  await page.getByRole('tab', { name: '该学员调整记录', exact: true }).click()
  await expect(page.getByText('A账号奖励', { exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '撤销这次调整', exact: true })).toHaveCount(0)
})

test('an unverifiable imported score cannot be revoked from the admin screen', async ({ page }) => {
  const detail = await setup(page)
  await page.route('**/api/admin/learning-rankings/student', route => route.fulfill({ json: { ...detail, challenges: [{ ...detail.challenges[0], score: 0, frozenScoreAvailable: false }] } }))
  await page.goto('/admin/rankings'); await page.getByRole('button', { name: '管理 Student', exact: true }).click()
  await expect(page.getByText('迁入题目 · 冻结分值待核实 · 已通关', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '撤销通关', exact: true })).toBeDisabled()
})
