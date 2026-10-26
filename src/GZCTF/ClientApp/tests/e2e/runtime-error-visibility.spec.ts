import { expect, test, type Page } from '@playwright/test'

const challengeId = '10000000-0000-0000-0000-000000000010'
const message = 'Challenge runtime is unavailable. Ask an administrator to configure or check the challenge host Docker connection. Harbor only stores images.'
const mockViewer = async (page: Page, role = 'Admin') => {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('en-US')))
  await page.route('**/api/**', route => route.fulfill({ json: [] }))
  await page.route('**/api/account/profile', route => route.fulfill({ json: {
    userId: '20000000-0000-0000-0000-000000000001', userName: 'viewer', role,
  } }))
}
const unavailable = { code: 'challenge.runtime_unavailable' }

test('admin test-container start translates a runtime503 and remains retryable', async ({ page }) => {
  await mockViewer(page)
  const edit = {
    challenge: { id: challengeId, title: 'Test container', type: 'StaticContainer', ctfCategory: 'Web', difficulty: 'Normal', isEnabled: true, publicationState: 'Draft' },
    localizations: [{ locale: 'en', title: 'Test container', summary: '', body: 'Body' }],
    runtimeConfigurationJson: JSON.stringify({ ContainerImage: 'registry.test/box:1', ExposedPort: 8080 }),
    flags: [{ kind: 'Static', value: 'flag{test}' }], hints: [], writeups: [],
    publication: { rowVersion: 1, categoryIds: [] },
  }
  await page.route(`**/api/admin/challenges/${challengeId}/edit**`, route => route.fulfill({ json: edit }))
  await page.route(`**/api/admin/challenges/${challengeId}`, route => route.fulfill({ json: edit }))
  await page.route(`**/api/challenges/${challengeId}/instances`, route => route.fulfill({ status: 503, json: unavailable }))
  await page.goto(`/admin/library/challenges/${challengeId}`)
  await page.getByRole('button', { name: 'Start test container' }).click()
  await expect(page.getByText(message, { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Start test container' })).toBeEnabled()
  await expect(page.getByText('translation:common.error.challenge_runtime_unavailable', { exact: true })).toHaveCount(0)
})

test('admin instance stop translates a runtime503 without removing the running instance', async ({ page }) => {
  await mockViewer(page)
  await page.route('**/api/admin/challenge-instances', route => route.fulfill({ json: [{
    id: challengeId, userId: 'member-1', userName: 'Alice', challengeTitle: 'Running target', status: 'Running',
  }] }))
  await page.route('**/api/admin/challenge-instances/settings', route => route.fulfill({ json: { maxConcurrentPerUser: 2, lifetimeMinutes: 120 } }))
  await page.route(`**/api/admin/challenge-instances/${challengeId}`, route => route.fulfill({ status: 503, json: unavailable }))
  await page.goto('/admin/skill-trees?tab=instances')
  await page.getByRole('button', { name: 'Stop instance' }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Stop instance' }).click()
  await expect(page.getByRole('alert')).toContainText(message)
  await expect(page.getByText('Running target', { exact: true })).toBeVisible()
  await expect(page.getByRole('table').getByRole('button', { name: 'Stop instance' })).toBeEnabled()
})

test('learner start translates a runtime503 without a false running state', async ({ page }) => {
  await mockViewer(page, 'User')
  await page.route(`**/api/challenges/${challengeId}?locale=*`, route => route.fulfill({ json: {
    id: challengeId, title: 'Learner container', body: 'Challenge body', type: 'StaticContainer',
    ctfCategory: 'Web', hasContainer: true, hasAttachment: false, hasWriteup: false,
  } }))
  await page.route(`**/api/challenges/${challengeId}/instances`, route => route.request().method() === 'POST'
    ? route.fulfill({ status: 503, json: unavailable }) : route.fulfill({ status: 404, json: {} }))
  await page.goto(`/challenges/${challengeId}`)
  await page.getByRole('button', { name: 'Start', exact: true }).click()
  await expect(page.getByText(message, { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Start', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Stop', exact: true })).toHaveCount(0)
})
