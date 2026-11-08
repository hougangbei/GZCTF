import { expect, test, type Page } from '@playwright/test'

const id = '10000000-0000-0000-0000-000000000099'
const instanceId = '50000000-0000-0000-0000-000000000099'
const edit = {
  challenge: {
    id,
    title: 'Editor session test',
    type: 'DynamicContainer',
    ctfCategory: 'Web',
    difficulty: 'Normal',
    isEnabled: true,
    publicationState: 'Draft',
  },
  localizations: [{ locale: 'en', title: 'Editor session test', summary: '', body: 'Synthetic description' }],
  runtimeConfigurationJson: JSON.stringify({
    Image: 'fixture/image:latest',
    FlagMode: 'gzctf',
    ImportSourceInstance: 'synthetic',
    FlagTemplate: 'nynuctf{[GUID]}',
    ExposedPort: 8080,
    Cpu: 1,
    MemoryMb: 128,
    StorageMb: 256,
    NetworkMode: 'open',
  }),
  flags: [{ kind: 'Template', template: 'nynuctf{[GUID]}' }],
  hints: [],
  writeups: [],
  publication: { rowVersion: 1, categoryIds: [] },
}
async function setup(page: Page, delayStart = false, delaySave = false) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('en-US')))
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'admin', userName: 'Admin', role: 'Admin' } })
  )
  await page.route(`**/api/admin/challenges/${id}/edit**`, (route) => route.fulfill({ json: edit }))
  let saveComplete!: () => void
  const savePending = new Promise<void>((done) => {
    saveComplete = done
  })
  let saves = 0
  await page.route(`**/api/admin/challenges/${id}`, async (route) => {
    saves++
    if (delaySave) await savePending
    return route.fulfill({ json: edit })
  })
  const starts: string[] = [],
    stops: unknown[] = []
  let complete!: () => void
  const pending = new Promise<void>((done) => {
    complete = done
  })
  await page.route(new RegExp(`/api/challenges/${id}/instances(?:\\?|$)`), async (route) => {
    starts.push(route.request().url())
    if (delayStart) await pending
    await route.fulfill({ json: { id: instanceId, status: 1, publicIp: '127.0.0.1', publicPort: 33001 } })
  })
  await page.route(`**/api/challenges/${id}/instances/test-stop`, (route) => {
    stops.push(route.request().postDataJSON())
    return route.fulfill({ status: 204 })
  })
  await page.goto(`/admin/library/challenges/${id}`)
  return {
    starts,
    stops,
    complete,
    saveComplete,
    get saves() {
      return saves
    },
  }
}

test('leaving the saved editor releases only the exact test session', async ({ page }) => {
  const state = await setup(page)
  await page.getByRole('button', { name: 'Start test container' }).click()
  await expect(page.getByRole('button', { name: 'Stop test container' })).toBeVisible()
  const session = new URL(state.starts[0]).searchParams.get('testSessionId')
  expect(session).toMatch(/^[0-9a-f-]{36}$/)
  await page.locator('a[href="/admin/skill-trees?tab=challenges"]').first().click()
  await expect.poll(() => state.stops).toEqual([{ instanceId, testSessionId: session }])
})

test('a cancelled dirty navigation keeps the test instance running', async ({ page }) => {
  const state = await setup(page)
  await page.getByRole('button', { name: 'Start test container' }).click()
  await expect(page.getByRole('button', { name: 'Stop test container' })).toBeVisible()
  await page.getByRole('textbox', { name: 'Challenge description' }).fill('Unsaved editing')
  page.once('dialog', (dialog) => dialog.dismiss())
  await page.locator('a[href="/admin/skill-trees?tab=challenges"]').first().click()
  await expect(page).toHaveURL(new RegExp(`/admin/library/challenges/${id}$`))
  expect(state.stops).toHaveLength(0)
})

test('a start completing after navigating away is released', async ({ page }) => {
  const state = await setup(page, true)
  await page.getByRole('button', { name: 'Start test container' }).click()
  await expect.poll(() => state.starts.length).toBe(1)
  await page.locator('a[href="/admin/skill-trees?tab=challenges"]').first().click()
  state.complete()
  await expect.poll(() => state.stops.length).toBe(1)
  expect(state.stops[0]).toEqual({
    instanceId,
    testSessionId: new URL(state.starts[0]).searchParams.get('testSessionId'),
  })
})

test('pagehide releases the running test without waiting for component unmount', async ({ page }) => {
  const state = await setup(page)
  await page.getByRole('button', { name: 'Start test container' }).click()
  await expect(page.getByRole('button', { name: 'Stop test container' })).toBeVisible()
  await page.evaluate(() => window.dispatchEvent(new Event('pagehide')))
  await expect.poll(() => state.stops.length).toBe(1)
  await expect(page.getByRole('button', { name: 'Start test container' })).toBeVisible()
})

test('obsolete userId templates are explained and cannot be saved or started', async ({ page }) => {
  const state = await setup(page)
  await page.getByRole('textbox', { name: 'Flag template' }).fill('flag{{userId}}')
  await expect(
    page.getByText('The {userId} placeholder is no longer supported. Replace it with [GUID].', { exact: true })
  ).toBeVisible()
  await page.getByRole('button', { name: 'Start test container' }).click()
  expect(state.starts).toHaveLength(0)
})

test('a pending save cannot start a test after pagehide and restoration', async ({ page }) => {
  const state = await setup(page, false, true)
  await page.getByRole('button', { name: 'Start test container' }).click()
  await expect.poll(() => state.saves).toBe(1)
  await page.evaluate(() => {
    window.dispatchEvent(new Event('pagehide'))
    window.dispatchEvent(new Event('pageshow'))
  })
  state.saveComplete()
  await expect(page.getByRole('button', { name: 'Start test container' })).toBeEnabled()
  expect(state.starts).toHaveLength(0)
})
