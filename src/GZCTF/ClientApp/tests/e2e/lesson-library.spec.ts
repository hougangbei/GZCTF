import { expect, test, type Page } from '@playwright/test'

const lessonId = '10000000-0000-0000-0000-000000000001'
const setup = async (page: Page) => {
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'admin', userName: 'admin', role: 'Admin' } })
  )
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('en-US')))
  await page.route('**/api/admin/skill-categories', (route) =>
    route.fulfill({ json: [{ categoryId: 'web', name: 'Web security' }] })
  )
}

test('lesson library filters metadata and keeps edit actions usable on mobile', async ({ page }) => {
  await setup(page)
  await page.route('**/api/admin/lessons?locale=en', (route) =>
    route.fulfill({
      json: [
        {
          id: lessonId,
          title: 'HTTP fundamentals',
          publication: { publicationState: 'Published', categoryIds: ['web'] },
        },
        { id: 'draft', title: 'Binary fundamentals', publication: { publicationState: 'Draft', categoryIds: [] } },
      ],
    })
  )
  await page.setViewportSize({ width: 375, height: 812 })
  await page.goto('/admin/library/lessons')
  await expect(page.getByText('Web security', { exact: true })).toBeVisible()
  await page.getByRole('textbox', { name: 'Search lessons' }).fill('Web security')
  await expect(page.getByRole('link', { name: 'Edit HTTP fundamentals' })).toBeVisible()
  await expect(page.getByText('Binary fundamentals', { exact: true })).toHaveCount(0)
  await page.getByRole('textbox', { name: 'Search lessons' }).fill('')
  await page.getByRole('combobox', { name: 'Publication status' }).selectOption('Draft')
  await expect(page.getByText('Binary fundamentals', { exact: true })).toBeVisible()
  await expect(page.getByText('HTTP fundamentals', { exact: true })).toHaveCount(0)
  await page.getByRole('textbox', { name: 'Search lessons' }).fill('no-match')
  await expect(page.getByText('No matching lessons')).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
})

test('creating a lesson opens its editor and trims the title', async ({ page }) => {
  await setup(page)
  let created: unknown
  await page.route(/\/api\/admin\/lessons(?:\?|$)/, (route) => {
    if (route.request().method() === 'POST') {
      created = route.request().postDataJSON()
      return route.fulfill({ json: { id: lessonId, title: 'New lesson' } })
    }
    return route.fulfill({ json: [] })
  })
  await page.goto('/admin/library/lessons')
  await expect(page.getByText('No lessons yet')).toBeVisible()
  await page.getByRole('textbox', { name: 'Lesson title', exact: true }).fill('  New lesson  ')
  await page.getByRole('button', { name: 'Create lesson', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/admin/library/lessons/${lessonId}$`))
  expect(created).toEqual({ locale: 'en', localizations: [{ locale: 'en', title: 'New lesson', body: '' }] })
})

test('lesson load failures offer a retry', async ({ page }) => {
  await setup(page)
  let failed = true
  await page.route('**/api/admin/lessons?locale=en', (route) =>
    failed ? route.fulfill({ status: 500, json: { title: 'Unavailable' } }) : route.fulfill({ json: [] })
  )
  await page.goto('/admin/library/lessons')
  await expect(page.getByRole('alert').filter({ hasText: 'Unable to load lessons' })).toBeVisible()
  failed = false
  await page.getByRole('button', { name: 'Retry', exact: true }).click()
  await expect(page.getByText('No lessons yet')).toBeVisible()
})
