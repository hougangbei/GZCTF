import { expect, test } from '@playwright/test'

test('admin can edit QQ solve broadcasts and send a test message', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) => route.fulfill({
    json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'admin', role: 'Admin' },
  }))
  const settings = {
    enabled: false, baseUrl: '', accessTokenConfigured: false, groupId: '',
    messageTemplate: '🎉 {member} 解出了 {challenge}（{source}）',
    notifyLearningSolves: true, notifyGameSolves: true,
  }
  let saved = false
  let tested = false
  await page.route('**/api/admin/qq-bot/settings', (route) => {
    if (route.request().method() === 'PUT') {
      Object.assign(settings, route.request().postDataJSON())
      saved = true
    }
    return route.fulfill({ json: settings })
  })
  await page.route('**/api/admin/qq-bot/test', (route) => {
    tested = true
    return route.fulfill({ json: { sent: true } })
  })

  await page.goto('/admin/settings')
  await expect(page.getByRole('heading', { name: 'QQ 解题播报' })).toBeVisible()
  await page.getByRole('textbox', { name: 'NapCat HTTP 地址' }).fill('http://napcat:3000')
  await page.getByRole('textbox', { name: '目标 QQ 群号' }).fill('123456')
  await page.getByRole('textbox', { name: '解题消息模板' }).fill('{member} 攻克了 {challenge}')
  await page.getByRole('button', { name: '保存 QQ 设置' }).click()
  await expect(page.getByText('QQ 设置已保存')).toBeVisible()
  expect(saved).toBe(true)
  await page.getByRole('button', { name: '保存并发送测试消息' }).click()
  await expect(page.getByText('测试消息已发送到 QQ 群')).toBeVisible()
  expect(tested).toBe(true)
})
