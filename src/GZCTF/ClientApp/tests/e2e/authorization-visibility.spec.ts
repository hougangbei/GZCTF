import { expect, test } from '@playwright/test'
import { ungzip } from 'pako'

test('admin content stays unmounted until the viewer role is known', async ({ page }) => {
  let releaseProfile: (() => void) | undefined
  const profileGate = new Promise<void>(resolve => { releaseProfile = resolve })
  let adminRequests = 0

  await page.route('**/api/account/profile', async route => {
    await profileGate
    await route.fulfill({ status: 401, json: { message: 'Login required' } })
  })
  await page.route('**/api/admin/**', async route => {
    adminRequests += 1
    await route.fulfill({ json: [{ id: 'secret', name: 'Private admin content' }] })
  })

  await page.goto('/admin/skill-trees')
  await page.waitForTimeout(350)
  expect(adminRequests).toBe(0)
  await expect(page.getByRole('navigation', { name: 'Administration navigation' })).toHaveCount(0)
  await expect(page.getByText('Private admin content')).toHaveCount(0)

  releaseProfile?.()
  await expect(page).toHaveURL(/\/account\/login\?from=/)
})

test('ordinary users cannot view admin workspace or its navigation links', async ({ page }) => {
  let adminRequests = 0
  await page.route('**/api/account/profile', route => route.fulfill({
    json: { userId: '30000000-0000-0000-0000-000000000002', userName: 'learner', role: 'User' },
  }))
  await page.route('**/api/admin/**', route => {
    adminRequests += 1
    return route.fulfill({ json: [{ id: 'secret', name: 'Private admin content' }] })
  })

  await page.goto('/admin/skill-trees')
  await expect(page).toHaveURL(/\/404$/)
  await expect(page.getByText('Private admin content')).toHaveCount(0)
  await expect(page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: 'Admin' })).toHaveCount(0)
  expect(adminRequests).toBe(0)
  await expect(page.getByRole('navigation', { name: 'Administration navigation' })).toHaveCount(0)
})

test('profile request errors show a retry without mounting admin data', async ({ page }) => {
  let profileRequests = 0
  let adminRequests = 0
  await page.route('**/api/account/profile', route => {
    profileRequests += 1
    return route.fulfill(profileRequests === 1
      ? { status: 503, json: { message: 'Unavailable' } }
      : { json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'admin', role: 'Admin' } })
  })
  await page.route('**/api/admin/**', route => {
    adminRequests += 1
    return route.fulfill({ json: [] })
  })

  await page.goto('/admin/skill-trees')
  await expect(page.getByRole('alert')).toContainText('An error has occurred')
  expect(adminRequests).toBe(0)
  await expect(page.getByRole('navigation', { name: 'Administration navigation' })).toHaveCount(0)
  await page.getByRole('button', { name: 'Try Again' }).click()
  await expect(page.getByRole('heading', { name: 'Skill trees', exact: true })).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Administration navigation' }).getByRole('link', { name: 'Skill trees', exact: true })).toHaveAttribute('aria-current', 'page')
})

test('disk cache excludes profile and admin API responses', async ({ page }) => {
  await page.route('**/api/config', route => route.fulfill({ json: { title: 'GZ' } }))
  await page.route('**/api/account/profile', route => route.fulfill({
    json: { userId: '30000000-0000-0000-0000-000000000001', userName: 'admin', role: 'Admin' },
  }))
  await page.route('**/api/admin/**', route => route.fulfill({ json: [] }))
  await page.goto('/admin/skill-trees')
  await expect(page.getByRole('heading', { name: 'Skill trees', exact: true })).toBeVisible()
  await expect(page.getByRole('navigation', { name: 'Administration navigation' }).getByRole('link', { name: 'Skill trees', exact: true })).toHaveAttribute('aria-current', 'page')
  await page.waitForTimeout(3500)

  const stored = await page.evaluate(async () => {
    const db = await new Promise<IDBDatabase>((resolve, reject) => {
      const request = indexedDB.open('gzctf-cache')
      request.onsuccess = () => resolve(request.result)
      request.onerror = () => reject(request.error)
    })
    const request = db.transaction('swr', 'readonly').objectStore('swr').get('cache-map')
    return await new Promise<number[]>((resolve, reject) => {
      request.onsuccess = () => resolve(Array.from(new Uint8Array(request.result ?? [])))
      request.onerror = () => reject(request.error)
    })
  })
  const entries = JSON.parse(new TextDecoder().decode(ungzip(new Uint8Array(stored)))) as [string, unknown][]
  expect(entries.some(([key]) => key === '/api/config')).toBe(true)
  expect(entries.some(([key]) => key.includes('/api/account/') || key.includes('/api/admin/'))).toBe(false)
})


test('legacy admin layout hides navigation while profile is pending or failed', async ({ page }) => {
  let release: (() => void) | undefined
  const gate = new Promise<void>(resolve => { release = resolve })
  await page.route('**/api/account/profile', async route => {
    await gate
    await route.fulfill({ status: 503, json: { message: 'Unavailable' } })
  })
  await page.goto('/admin/settings')
  await page.waitForTimeout(250)
  await expect(page.getByRole('navigation', { name: 'Administration navigation' })).toHaveCount(0)
  release?.()
  await expect(page.getByRole('alert')).toContainText('An error has occurred')
  await expect(page.getByRole('navigation', { name: 'Administration navigation' })).toHaveCount(0)
})
