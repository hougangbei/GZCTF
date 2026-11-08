import { expect, test } from '@playwright/test'

// Exercise the production bundle: Vite's preloadError event does not exist in dev mode.
const preview = process.env.E2E_CHUNK_RECOVERY_URL
test.skip(!preview, 'Requires a built SPA preview (E2E_CHUNK_RECOVERY_URL).')

for (const persistent of [false, true]) {
  test(persistent ? 'a persistent chunk failure does not cause a reload loop' : 'an obsolete lazy chunk automatically recovers at the same page', async ({ page }) => {
    await page.goto(`${preview}/leaderboard`)
    await expect(page.locator('[data-competition-board]')).toBeVisible()
    let failedUrl = ''
    let reloads = 0
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) reloads++ })
    await page.route('**/static/*.js', async route => {
      if (!failedUrl) { failedUrl = route.request().url(); await route.abort('failed'); return }
      if (persistent && route.request().url() === failedUrl) { await route.abort('failed'); return }
      await route.continue()
    })
    await page.locator('a[href="/about"]').first().click()
    await expect.poll(() => reloads).toBe(1)
    await expect(page).toHaveURL(/\/about$/)
    if (persistent) {
      await expect(page.locator('textarea').first()).toBeVisible()
    } else {
      await expect(page.locator('textarea')).toHaveCount(0)
      await expect(page.getByRole('banner')).toBeVisible()
    }
    expect(failedUrl).toContain('/static/')
    expect(reloads).toBe(1)
  })
}
