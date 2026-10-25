import { expect, test } from '@playwright/test'

test('mobile navigation is keyboard accessible and hidden when closed', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/')

  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  const open = page.getByRole('button', { name: 'Open navigation' })
  await expect(open).toBeVisible()
  await expect(navigation).toHaveCount(0)

  await open.click()
  await expect(navigation).toBeVisible()
  await expect(page.getByRole('main')).toHaveCount(0)

  await page.keyboard.press('Escape')
  await expect(navigation).toHaveCount(0)
  await expect(page.getByRole('main')).toBeVisible()
})

test('desktop content has a keyboard skip link and no horizontal overflow', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/')
  await page.getByRole('link', { name: 'Skip to content' }).waitFor()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused()
  await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(1440)
})

test('home retries an initial request error and announces an empty result', async ({ page }) => {
  let requests = 0
  await page.route('**/api/posts/latest', async (route) => {
    requests += 1
    await route.fulfill(requests === 1
      ? { status: 503, contentType: 'application/json', body: '{}' }
      : { status: 200, contentType: 'application/json', body: '[]' })
  })

  await page.goto('/')
  await expect(page.getByRole('alert')).toContainText('An error has occurred')
  await page.getByRole('button', { name: 'Try Again' }).click()
  await expect(page.getByRole('status')).toContainText('No data')
  expect(requests).toBeGreaterThanOrEqual(2)
})

test('mobile post title can be opened with the keyboard', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.route('**/api/posts/latest', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify([{ id: 'keyboard-post', title: 'Keyboard post', summary: 'A post summary', isPinned: false, time: 0 }]),
  }))
  await page.goto('/')

  const postLink = page.getByRole('link', { name: 'Keyboard post' })
  await expect(postLink).toBeVisible()
  await postLink.focus()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/posts\/keyboard-post$/)
})

test('about content uses one readable light surface', async ({ page }) => {
  await page.route('**/api/about', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ published: true, document: {
      schemaVersion: 1,
      title: 'About',
      sections: [{ id: 'section', title: 'History', items: [{
        id: 'timeline', type: 'timeline', width: 12, title: 'Timeline',
        events: [{ time: '2026', title: 'Milestone', description: '<p>Event details</p>' }],
      }] }],
    } }),
  }))
  await page.goto('/about')

  const section = page.getByRole('heading', { name: 'History' }).locator('..').locator('..')
  const block = page.getByRole('heading', { name: 'Timeline' }).locator('..')
  await expect(section).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)')
  await expect(block).toHaveCSS('background-color', 'rgb(255, 255, 255)')
  await expect(page.getByText('Event details')).toHaveCSS('color', 'rgb(30, 41, 59)')
})

test('desktop navigation links form a horizontal row', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const home = nav.getByRole('link', { name: 'Home' })
  const skills = nav.getByRole('link', { name: 'Skill trees' })
  await expect(home).toBeVisible()
  const homeBox = await home.boundingBox()
  const skillsBox = await skills.boundingBox()
  expect(homeBox && skillsBox && skillsBox.x > homeBox.x && Math.abs(skillsBox.y - homeBox.y) < 2).toBe(true)
})
