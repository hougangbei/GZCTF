import { expect, test, type Page } from '@playwright/test'

const source = () => ({ kind: 'github', location: 'https://github.com/example/platform', ref: 'main', sha256: '' })
const ready = () => ({
  configured: true,
  mode: 'native',
  phase: 'ready',
  currentSha: 'abc123',
  validation: {
    id: 'validation-1',
    sourceDigest: 'digest-1',
    version: 'v2.0.0',
    artifactHash: 'a'.repeat(64),
    expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
    canApply: true,
    checks: [{ key: 'manifest', label: '更新包清单', status: 'passed', message: '兼容当前部署' }],
  },
})
async function mock(page: Page, initial: any = ready()) {
  const state = { source: source(), status: initial, failed: false, saves: [] as any[], applied: [] as any[] }
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'admin', userName: 'admin', role: 'Admin' } })
  )
  await page.route('**/api/admin/updates/source', (route) => {
    if (route.request().method() === 'PUT') {
      state.source = route.request().postDataJSON()
      state.saves.push(state.source)
    }
    return route.fulfill({ json: state.source })
  })
  await page.route('**/api/admin/updates', (route) => {
    if (route.request().method() === 'POST') {
      state.applied.push(route.request().postDataJSON())
      return route.fulfill({ status: 202, json: {} })
    }
    return route.fulfill(state.failed ? { status: 503, json: {} } : { json: state.status })
  })
  await page.route('**/api/admin/updates/validate', (route) => {
    state.status = { ...state.status, phase: 'validating', validation: undefined }
    return route.fulfill({ status: 202, json: {} })
  })
  await page.goto('/admin/updates')
  return state
}

test('persists all source choices and invalidates the previous validation on save', async ({ page }) => {
  const state = await mock(page)
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeEnabled()
  await page.getByRole('textbox', { name: '分支或标签', exact: true }).fill('stable')
  await page.getByRole('button', { name: '保存来源', exact: true }).click()
  await expect.poll(() => state.saves.length).toBe(1)
  expect(state.saves[0]).toEqual({ ...source(), ref: 'stable' })
  await page.getByRole('combobox', { name: '更新来源' }).click()
  await page.getByRole('option', { name: '更新包链接' }).click()
  await page.getByRole('textbox', { name: '更新包 URL', exact: true }).fill('https://example.com/release.tar.gz')
  await page.getByRole('textbox', { name: 'SHA256（可选）' }).fill('a'.repeat(64))
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: '保存来源', exact: true }).click()
  await expect(page.getByText('更新来源已保存')).toBeVisible()
  expect(state.saves[1]).toEqual({
    kind: 'package',
    location: 'https://example.com/release.tar.gz',
    ref: '',
    sha256: 'a'.repeat(64),
  })
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
  await page.getByRole('combobox', { name: '更新来源' }).click()
  await page.getByRole('option', { name: '本机目录' }).click()
  await expect(page.getByText('目录位于部署服务器上，不是浏览器所在的电脑。')).toBeVisible()
  await page.getByRole('textbox', { name: '服务器目录', exact: true }).fill('/srv/releases/v2')
  await page.getByRole('button', { name: '保存来源', exact: true }).click()
  await expect.poll(() => state.saves.length).toBe(3)
  expect(state.saves[2]).toEqual({ kind: 'directory', location: '/srv/releases/v2', ref: '', sha256: '' })
  await page.reload()
  await expect(page.getByRole('textbox', { name: '服务器目录', exact: true })).toHaveValue('/srv/releases/v2')
})

test('shows validation report and only applies after explicit confirmation', async ({ page }) => {
  const state = await mock(page)
  await expect(page.getByText('v2.0.0', { exact: true })).toBeVisible()
  await expect(page.getByText('兼容当前部署')).toBeVisible()
  await expect(page.getByText('a'.repeat(64), { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '应用更新', exact: true }).click()
  expect(state.applied).toEqual([])
  await page.getByRole('dialog').getByRole('button', { name: '应用更新', exact: true }).click()
  await expect.poll(() => state.applied).toEqual([{ validationId: 'validation-1' }])
})

test('rejects expired reports and disables stale reports when polling fails', async ({ page }) => {
  const status = ready()
  status.validation.expiresAtUtc = new Date(Date.now() + 2000).toISOString()
  const state = await mock(page, status)
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled({ timeout: 6000 })
  state.status = ready()
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeEnabled({ timeout: 10000 })
  state.failed = true
  await expect(page.getByText('无法加载更新状态，请稍后重试。')).toBeVisible({ timeout: 10000 })
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
})

test('allows source saving without an agent and fits a mobile screen', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await mock(page, { configured: false, phase: 'idle' })
  await expect(page.getByText(/在部署服务器上安装并启动更新服务/)).toBeVisible()
  await page.getByRole('textbox', { name: '分支或标签', exact: true }).fill('stable')
  await expect(page.getByRole('button', { name: '保存来源', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: '验证来源', exact: true })).toBeDisabled()
  const input = await page.getByRole('textbox', { name: 'GitHub 仓库', exact: true }).boundingBox()
  expect(input!.x + input!.width).toBeLessThanOrEqual(390)
})

test('clears the old report immediately when validating and locks active operations', async ({ page }) => {
  await mock(page)
  await page.getByRole('button', { name: '验证来源', exact: true }).click()
  await expect(page.getByText('兼容当前部署')).toHaveCount(0)
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '保存来源', exact: true })).toBeDisabled()
  await expect(page.getByRole('button', { name: '验证来源', exact: true })).toBeDisabled()
})

test('shows save and validation errors without retaining an applicable report', async ({ page }) => {
  await mock(page)
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeEnabled()
  await page.route('**/api/admin/updates/validate', (route) => route.fulfill({ status: 503, json: {} }))
  await page.getByRole('button', { name: '验证来源', exact: true }).click()
  await expect(page.getByText('无法发起验证，请检查更新服务后重试。')).toBeVisible()
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
  await page.route('**/api/admin/updates/source', (route) => route.fulfill({ status: 400, json: {} }))
  await page.getByRole('textbox', { name: '分支或标签', exact: true }).fill('stable')
  await page.getByRole('button', { name: '保存来源', exact: true }).click()
  await expect(page.getByText('无法保存更新来源，请检查来源后重试。')).toBeVisible()
  await expect(page.getByRole('textbox', { name: '分支或标签', exact: true })).toHaveValue('stable')
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
})

const legacyStatus = () => ({
  configured: true,
  phase: 'idle',
  currentSha: 'a'.repeat(40),
  latestSha: 'b'.repeat(40),
  latestUrl: 'https://github.com/example/platform/actions/runs/1',
  updateAvailable: true,
  checkError: '暂时无法检查最新构建',
  publishedAtUtc: '2026-10-17T00:00:00Z',
})

test('preserves legacy image checks and explicit targetSha updates while disabling source validation', async ({
  page,
}) => {
  const state = await mock(page, legacyStatus())
  await expect(page.getByText('b'.repeat(12), { exact: true })).toBeVisible()
  await expect(page.getByText('暂时无法检查最新构建')).toBeVisible()
  await expect(page.getByRole('link', { name: '查看 GitHub 构建' })).toHaveAttribute('href', state.status.latestUrl)
  await expect(page.getByText(/当前更新服务仅支持原有镜像更新/)).toBeVisible()
  await expect(page.getByRole('button', { name: '验证来源', exact: true })).toBeDisabled()
  let refreshed = false
  await page.route('**/api/admin/updates?refresh=true', (route) => {
    refreshed = true
    state.status = { ...state.status, latestSha: 'c'.repeat(40), checkError: '' }
    return route.fulfill({ json: state.status })
  })
  await page.getByRole('button', { name: '重新检查', exact: true }).click()
  await expect(page.getByText('c'.repeat(12), { exact: true })).toBeVisible()
  expect(refreshed).toBe(true)
  await page.getByRole('textbox', { name: '分支或标签', exact: true }).fill('stable')
  await expect(page.getByRole('button', { name: '保存来源', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: '应用更新', exact: true }).click()
  expect(state.applied).toEqual([])
  await page.getByRole('dialog').getByRole('button', { name: '应用更新', exact: true }).click()
  await expect.poll(() => state.applied).toEqual([{ targetSha: 'c'.repeat(40) }])
})

test('blocks legacy updates during active operations and recovery states', async ({ page }) => {
  const state = await mock(page, { ...legacyStatus(), phase: 'interrupted' })
  for (const status of [
    { phase: 'interrupted' },
    { phase: 'recoveryRequired' },
    { phase: 'failed', backupPath: '/srv/backups/last' },
    { phase: 'accepted' },
    { phase: 'backingUp' },
    { phase: 'starting' },
    { phase: 'pulling' },
    { phase: 'idle', updateAvailable: false },
    { phase: 'idle', currentSha: '' },
    { phase: 'idle', latestSha: '' },
  ]) {
    state.status = { ...legacyStatus(), ...status }
    await page.reload()
    await expect(page.getByRole('heading', { name: '更新管理', exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
  }
  expect(state.applied).toEqual([])
})

test('shows running validation without an expiry warning and preserves the complete current version', async ({
  page,
}) => {
  const status = ready()
  const state = await mock(page, {
    ...status,
    phase: 'validating',
    currentSha: '1.8.7+updates-native-release',
    validation: {
      ...status.validation,
      expiresAtUtc: undefined,
      canApply: false,
      checks: [{ key: 'build', label: '构建候选版本', status: 'running', message: '正在构建候选版本' }],
    },
  })
  await expect(page.getByText('正在构建候选版本', { exact: true })).toBeVisible()
  await expect(page.getByText('验证已过期，请重新验证来源后再应用更新。')).toHaveCount(0)
  await expect(page.getByText('当前版本 / 提交: 1.8.7+updates-native-release', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
  state.status = { ...status, validation: { ...status.validation, expiresAtUtc: undefined } }
  await page.reload()
  await expect(page.getByText('请先保存并验证来源，再应用更新。')).toBeVisible()
  await expect(page.getByText('验证已过期，请重新验证来源后再应用更新。')).toHaveCount(0)
  await expect(page.getByRole('button', { name: '应用更新', exact: true })).toBeDisabled()
})
