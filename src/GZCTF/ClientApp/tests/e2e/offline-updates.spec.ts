import { expect, test, type Page } from '@playwright/test'

const id = 'a'.repeat(32),
  validationId = 'b'.repeat(32)
async function setup(page: Page) {
  const item = {
    id,
    name: 'gzctf-release.tar.gz',
    size: 267000000,
    modifiedAtUtc: new Date().toISOString(),
    source: 'directory',
    checksumProvided: false,
  }
  const state: any = {
    status: { configured: true, mode: 'offline', phase: 'idle', currentSha: '12345678' },
    directory: '/root/nynuctf/update-packages',
    items: [item],
    applies: [],
    uploads: 0,
    checks: 0,
    checksums: [],
  }
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('zh-CN')))
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } })
  )
  await page.route('**/api/admin/updates', (route) => route.fulfill({ json: state.status }))
  await page.route('**/api/admin/updates/packages/settings', (route) => {
    if (route.request().method() === 'PUT') state.directory = route.request().postDataJSON().directory
    return route.fulfill({
      json: {
        mode: 'offline',
        directory: state.directory,
        allowedRoots: ['/root/nynuctf'],
        maxUploadBytes: 1073741824,
      },
    })
  })
  await page.route('**/api/admin/updates/packages', (route) =>
    route.fulfill({ json: { directory: state.directory, items: state.items } })
  )
  await page.route(`**/api/admin/updates/packages/${id}/validate`, (route) => {
    state.checks++
    state.status = {
      ...state.status,
      phase: 'ready',
      validation: {
        id: validationId,
        packageId: id,
        sourceDigest: 'd'.repeat(64),
        artifactHash: 'c'.repeat(64),
        version: 'reviewed-ui',
        expiresAtUtc: new Date(Date.now() + 60000).toISOString(),
        canApply: true,
        checks: [{ key: 'manifest', label: '清单', status: 'passed', message: '更新包完整' }],
      },
    }
    return route.fulfill({ status: 202, json: state.status })
  })
  await page.route(`**/api/admin/updates/packages/${id}/apply`, (route) => {
    state.applies.push(route.request().postDataJSON())
    state.status = { ...state.status, phase: 'backingUp', message: '正在备份' }
    return route.fulfill({ status: 202, json: state.status })
  })
  await page.route(`**/api/admin/updates/packages/${id}/checksum`, (route) => {
    state.checksums.push(route.request().postDataJSON())
    return route.fulfill({ json: {} })
  })
  await page.route('**/api/admin/updates/packages/upload', (route) => {
    state.uploads++
    return route.fulfill({ json: { ...item, source: 'upload' } })
  })
  await page.goto('/admin/updates')
  return state
}

test('offline workspace has folder and upload entries and requires validation and maintenance confirmation', async ({
  page,
}) => {
  const state = await setup(page)
  await expect(page.getByRole('heading', { name: '离线更新包', exact: true })).toBeVisible()
  await expect(page.getByRole('textbox', { name: '服务器扫描目录' })).toHaveValue(state.directory)
  await expect(page.getByText('未提供外部校验值', { exact: true })).toBeVisible()
  await page.getByRole('radio', { name: 'gzctf-release.tar.gz' }).check()
  await expect(page.getByRole('button', { name: '备份并更新', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: '检查更新包', exact: true }).click()
  await expect(page.getByText('更新包完整')).toBeVisible()
  await page.getByRole('button', { name: '备份并更新', exact: true }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  expect(state.applies).toEqual([])
  await expect(page.getByRole('dialog').getByRole('button', { name: '确认更新', exact: true })).toBeDisabled()
  await page.getByRole('checkbox', { name: '我已暂停平台入口并通知学员维护' }).check()
  await page.getByRole('dialog').getByRole('button', { name: '确认更新', exact: true }).click()
  await expect.poll(() => state.applies.length).toBe(1)
  expect(state.applies[0]).toMatchObject({ validationId, maintenanceConfirmed: true })
  expect(state.applies[0].operationId).toMatch(/^[0-9a-f-]{36}$/)
})

test('upload and checksum never apply the package automatically', async ({ page }) => {
  const state = await setup(page)
  await page
    .getByLabel('选择更新包', { exact: true })
    .setInputFiles({ name: 'release.tar.gz', mimeType: 'application/gzip', buffer: Buffer.from('synthetic package') })
  await expect.poll(() => state.uploads).toBe(1)
  await page
    .getByLabel('选择校验文件（可选）', { exact: true })
    .setInputFiles({
      name: 'release.tar.gz.sha256',
      mimeType: 'text/plain',
      buffer: Buffer.from('c'.repeat(64) + '  release.tar.gz\n'),
    })
  await expect.poll(() => state.checksums.length).toBe(1)
  expect(state.checksums[0]).toEqual({ sha256: 'c'.repeat(64) })
  expect(state.checks).toBe(0)
  expect(state.applies).toEqual([])
})

test('a pending confirmation cannot use a stale expired validation', async ({ page }) => {
  const state = await setup(page)
  await page.getByRole('radio', { name: 'gzctf-release.tar.gz' }).check()
  await page.getByRole('button', { name: '检查更新包', exact: true }).click()
  await page.getByRole('button', { name: '备份并更新', exact: true }).click()
  state.status.validation.expiresAtUtc = new Date(Date.now() - 1000).toISOString()
  await expect(page.getByText('检查已过期，请重新检查。')).toBeVisible({ timeout: 10000 })
  await page.getByRole('checkbox', { name: '我已暂停平台入口并通知学员维护' }).check()
  await expect(page.getByRole('dialog').getByRole('button', { name: '确认更新', exact: true })).toBeDisabled()
  expect(state.applies).toEqual([])
})

test('directory save refreshes the list and rejects malformed checksum files', async ({ page }) => {
  const state = await setup(page)
  await page.getByRole('textbox', { name: '服务器扫描目录' }).fill('/root/nynuctf/updates')
  await page.getByRole('button', { name: '保存目录', exact: true }).click()
  await expect.poll(() => state.directory).toBe('/root/nynuctf/updates')
  await page.getByRole('radio', { name: 'gzctf-release.tar.gz' }).check()
  await page
    .getByLabel('选择校验文件（可选）', { exact: true })
    .setInputFiles({ name: 'bad.sha256', mimeType: 'text/plain', buffer: Buffer.from('not a hash') })
  await expect(page.getByText('校验文件格式无效，请选择仅包含一个SHA-256值的文件。')).toBeVisible()
  expect(state.checksums).toEqual([])
})

test('an open confirmation stays bound to its original validation', async ({ page }) => {
  const state = await setup(page)
  await page.getByRole('radio', { name: 'gzctf-release.tar.gz' }).check()
  await page.getByRole('button', { name: '检查更新包', exact: true }).click()
  await page.getByRole('button', { name: '备份并更新', exact: true }).click()
  state.status.validation = { ...state.status.validation, id: 'f'.repeat(32), version: 'different-version' }
  await expect(page.getByRole('dialog').getByText('different-version')).toBeVisible({ timeout: 10000 })
  await page.getByRole('checkbox', { name: '我已暂停平台入口并通知学员维护' }).check()
  await expect(page.getByRole('dialog').getByRole('button', { name: '确认更新', exact: true })).toBeDisabled()
  expect(state.applies).toEqual([])
})

test('upload cancellation never registers or applies a package', async ({ page }) => {
  const state = await setup(page)
  let finish!: () => void
  const pending = new Promise<void>((resolve) => {
    finish = resolve
  })
  await page.route('**/api/admin/updates/packages/upload', async (route) => {
    await pending
    await route.fulfill({ json: {} }).catch(() => undefined)
  })
  await page
    .getByLabel('选择更新包', { exact: true })
    .setInputFiles({ name: 'release.tar.gz', mimeType: 'application/gzip', buffer: Buffer.from('pending upload') })
  await page.getByRole('button', { name: '取消上传', exact: true }).click()
  await expect(page.getByText('上传已取消，没有执行更新。')).toBeVisible()
  finish()
  expect(state.applies).toEqual([])
  expect(state.checks).toBe(0)
})

test('update service disconnect disables an already validated package', async ({ page }) => {
  await setup(page)
  await page.getByRole('radio', { name: 'gzctf-release.tar.gz' }).check()
  await page.getByRole('button', { name: '检查更新包', exact: true }).click()
  await expect(page.getByRole('button', { name: '备份并更新', exact: true })).toBeEnabled()
  await page.route('**/api/admin/updates', (route) => route.fulfill({ status: 503, json: { message: 'unavailable' } }))
  await expect(page.getByText(/暂时无法连接更新服务/)).toBeVisible({ timeout: 10000 })
  await expect(page.getByRole('button', { name: '备份并更新', exact: true })).toBeDisabled()
})

for (const theme of ['light', 'dark'])
  test(`offline update workspace fits mobile in ${theme} theme`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await page.addInitScript((theme) => localStorage.setItem('mantine-color-scheme-value', theme), theme)
    await setup(page)
    await expect(page.getByRole('heading', { name: '离线更新包', exact: true })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    expect(await page.locator('html').getAttribute('data-mantine-color-scheme')).toBe(theme)
    await page.screenshot({ path: `/tmp/offline-updates-${theme}-mobile.png`, fullPage: true })
  })
