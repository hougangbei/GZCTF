import { expect, test, type Page } from '@playwright/test'

const editorPath = '/admin/library/lessons/editor-one'
const draftKey = 'lesson-draft:v1:editor-admin:editor-one:en'
async function setup(page: Page) {
  await page.addInitScript(() => localStorage.setItem('language', JSON.stringify('en-US')))
  await page.route('**/api/**', (route) => route.fulfill({ json: [] }))
  await page.route('**/api/config', (route) => route.fulfill({ json: { title: 'Test platform' } }))
  await page.route('**/api/account/profile', (route) =>
    route.fulfill({ json: { userId: 'editor-admin', userName: 'Editor', role: 'Admin' } })
  )
  const lesson = {
    id: 'editor-one',
    localizations: [
      { locale: 'en', title: 'English lesson', body: 'Original body' },
      { locale: 'ja', title: '既存', body: '保持' },
    ],
    publication: { publicationState: 'Published', rowVersion: 4, categoryIds: [] },
  }
  const updates: { locale: string; localizations: { locale: string; title: string; body: string }[] }[] = []
  await page.route('**/api/admin/lessons/editor-one*', async (route) => {
    if (route.request().method() === 'PUT') {
      const update = route.request().postDataJSON()
      updates.push(update)
      lesson.localizations = update.localizations
      lesson.publication.rowVersion++
    }
    return route.fulfill({ json: lesson })
  })
  return { lesson, updates }
}

test('failed manual save retains content and local recovery; keyboard saves do not overlap', async ({ page }) => {
  const state = await setup(page)
  let releaseSave: (() => void) | undefined
  let failSave = true
  let requests = 0
  await page.route('**/api/admin/lessons/editor-one*', async (route) => {
    if (route.request().method() !== 'PUT') return route.fulfill({ json: state.lesson })
    requests++
    if (failSave) return route.fulfill({ status: 503, json: { title: 'Unavailable' } })
    await new Promise<void>((resolve) => {
      releaseSave = resolve
    })
    return route.fulfill({ json: { ...state.lesson, publication: { ...state.lesson.publication, rowVersion: 5 } } })
  })
  await page.goto(editorPath)
  const body = page.getByRole('textbox', { name: 'Markdown body', exact: true })
  await body.fill('## Keep my edits')
  expect(requests).toBe(0)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByRole('alert').filter({ hasText: 'Could not save' })).toBeVisible()
  await expect(body).toHaveValue('## Keep my edits')
  expect(JSON.parse((await page.evaluate((key) => localStorage.getItem(key), draftKey))!)).toMatchObject({
    body: '## Keep my edits',
  })
  failSave = false
  await body.press('Control+s')
  await expect.poll(() => requests).toBe(2)
  await body.press('Control+s')
  expect(requests).toBe(2)
  releaseSave!()
  await expect(page.getByText('Saved', { exact: true })).toBeVisible()
  expect(await page.evaluate((key) => localStorage.getItem(key), draftKey)).toBeNull()
})

test('locale switches preserve separate drafts and saving keeps every other localization', async ({ page }) => {
  const { updates } = await setup(page)
  await page.goto(editorPath)
  const body = page.getByRole('textbox', { name: 'Markdown body', exact: true })
  await body.fill('English local draft')
  page.once('dialog', (dialog) => dialog.accept())
  await page.getByRole('combobox', { name: 'Content language', exact: true }).click()
  await page.getByRole('option', { name: 'ja', exact: true }).click()
  await expect(body).toHaveValue('保持')
  await body.fill('日本語の更新')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByText('Saved', { exact: true })).toBeVisible()
  expect(updates[0].locale).toBe('ja')
  expect(updates[0].localizations).toContainEqual({ locale: 'en', title: 'English lesson', body: 'Original body' })
  await page.getByRole('combobox', { name: 'Content language', exact: true }).click()
  await page.getByRole('option', { name: 'en', exact: true }).click()
  await page.getByRole('button', { name: 'Restore local draft', exact: true }).click()
  await expect(body).toHaveValue('English local draft')
  page.once('dialog', (dialog) => dialog.dismiss())
  await page.locator('a[href="/admin/skill-categories"]').click()
  await expect(page).toHaveURL(new RegExp(editorPath + '$'))
  await expect(body).toHaveValue('English local draft')
})

test('upload blocks saving until inserted and upload failure keeps lesson text', async ({ page }) => {
  const { updates } = await setup(page)
  let releaseUpload: (() => void) | undefined
  let failUpload = false
  await page.route('**/api/assets', async (route) => {
    if (failUpload) return route.fulfill({ status: 503, json: {} })
    await new Promise<void>((resolve) => {
      releaseUpload = resolve
    })
    return route.fulfill({ json: [{ hash: 'lesson-image', name: 'test.png' }] })
  })
  await page.goto(editorPath)
  const body = page.getByRole('textbox', { name: 'Markdown body', exact: true })
  const title = page.getByRole('textbox', { name: 'Lesson title', exact: true })
  await title.fill('Updated title')
  const file = {
    name: 'test.png',
    mimeType: 'image/png',
    buffer: Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
      'base64'
    ),
  }
  await page.locator('input[type="file"]').setInputFiles(file)
  await expect.poll(() => !!releaseUpload).toBe(true)
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
  await expect(title).toBeDisabled()
  await page.keyboard.press('Control+s')
  expect(updates).toHaveLength(0)
  releaseUpload!()
  await expect.poll(() => body.inputValue()).toContain('/assets/lesson-image/test.png')
  await expect(title).toHaveValue('Updated title')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByText('Saved', { exact: true })).toBeVisible()
  expect(updates[0].localizations.find((item) => item.locale === 'en')?.body).toContain('/assets/lesson-image/test.png')
  const savedBody = await body.inputValue()
  failUpload = true
  await page.locator('input[type="file"]').setInputFiles(file)
  await expect(page.getByRole('alert').filter({ hasText: 'Image upload failed' })).toBeVisible()
  await expect(body).toHaveValue(savedBody)
  expect(updates).toHaveLength(1)
})

test('local storage failures are visible and blank titles never save', async ({ page }) => {
  const { updates } = await setup(page)
  await page.addInitScript(() => {
    const original = Storage.prototype.setItem
    Storage.prototype.setItem = function (key, value) {
      if (key.startsWith('lesson-draft:')) throw new DOMException('Quota exceeded', 'QuotaExceededError')
      return original.call(this, key, value)
    }
  })
  await page.goto(editorPath)
  const title = page.getByRole('textbox', { name: 'Lesson title', exact: true })
  await title.fill('   ')
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeDisabled()
  await page.keyboard.press('Control+s')
  expect(updates).toHaveLength(0)
  await expect(page.getByRole('alert').filter({ hasText: 'Local draft recovery is unavailable' })).toBeVisible()
  await title.fill('  Trimmed title  ')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByText('Saved', { exact: true })).toBeVisible()
  expect(updates[0].localizations.find((item) => item.locale === 'en')?.title).toBe('Trimmed title')
})

test('browser Back offers explicit recovery and discard never touches another account or language', async ({
  page,
}) => {
  const { updates } = await setup(page)
  const otherKey = 'lesson-draft:v1:another-admin:editor-one:en'
  const chineseKey = 'lesson-draft:v1:editor-admin:editor-one:zh-CN'
  await page.addInitScript(
    ({ otherKey, chineseKey }) => {
      for (const key of [otherKey, chineseKey])
        localStorage.setItem(key, JSON.stringify({ title: 'Private draft', body: key, updatedAt: 1 }))
    },
    { otherKey, chineseKey }
  )
  await page.goto(editorPath)
  await expect(page.getByRole('button', { name: 'Restore local draft', exact: true })).toHaveCount(0)
  const body = page.getByRole('textbox', { name: 'Markdown body', exact: true })
  await body.fill('Return to this draft')
  page.once('dialog', (dialog) => dialog.accept())
  await page.locator('a[href="/admin/skill-categories"]').click()
  await expect(page).toHaveURL(/\/admin\/skill-trees\?tab=categories$/)
  await page.goBack()
  await page.getByRole('button', { name: 'Restore local draft', exact: true }).click()
  await expect(body).toHaveValue('Return to this draft')
  page.once('dialog', (dialog) => dialog.accept())
  await page.reload()
  await page.getByRole('button', { name: 'Discard local draft', exact: true }).click()
  await expect(body).toHaveValue('Original body')
  expect(updates).toHaveLength(0)
  expect(await page.evaluate((key) => localStorage.getItem(key), draftKey)).toBeNull()
  expect(await page.evaluate((key) => localStorage.getItem(key), otherKey)).not.toBeNull()
  expect(await page.evaluate((key) => localStorage.getItem(key), chineseKey)).not.toBeNull()
})

test('an older save cannot delete a newer draft after leaving and reopening the lesson', async ({ page }) => {
  const { lesson } = await setup(page)
  let releaseSave: (() => void) | undefined
  await page.route('**/api/admin/lessons/editor-one*', async (route) => {
    if (route.request().method() !== 'PUT') return route.fulfill({ json: lesson })
    const update = route.request().postDataJSON()
    await new Promise<void>((resolve) => {
      releaseSave = resolve
    })
    return route.fulfill({ json: { ...lesson, localizations: update.localizations } })
  })
  await page.goto(editorPath)
  const body = page.getByRole('textbox', { name: 'Markdown body', exact: true })
  await body.fill('Older pending save')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect.poll(() => !!releaseSave).toBe(true)
  page.once('dialog', (dialog) => dialog.accept())
  await page.locator('a[href="/admin/skill-categories"]').click()
  await expect(page).toHaveURL(/\/admin\/skill-trees\?tab=categories$/)
  await page.goBack()
  await page.getByRole('button', { name: 'Restore local draft', exact: true }).click()
  await body.fill('Newer unsaved changes')
  const response = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.url().includes('/api/admin/lessons/editor-one')
  )
  releaseSave!()
  await response
  await expect.poll(() => page.evaluate((key) => localStorage.getItem(key), draftKey)).not.toBeNull()
  expect(JSON.parse((await page.evaluate((key) => localStorage.getItem(key), draftKey))!)).toMatchObject({
    body: 'Newer unsaved changes',
  })
  page.once('dialog', (dialog) => dialog.accept())
  await page.reload()
  await page.getByRole('button', { name: 'Restore local draft', exact: true }).click()
  await expect(body).toHaveValue('Newer unsaved changes')
})

test('Chinese content uses the same locale code requested by the learner', async ({ page }) => {
  const { updates } = await setup(page)
  await page.goto(editorPath)
  await page.getByRole('combobox', { name: 'Content language', exact: true }).click()
  await page.getByRole('option', { name: 'zh', exact: true }).click()
  await page.getByRole('textbox', { name: 'Lesson title', exact: true }).fill('中文课节')
  await page.getByRole('textbox', { name: 'Markdown body', exact: true }).fill('中文正文')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page.getByText('Saved', { exact: true })).toBeVisible()
  expect(updates[0].locale).toBe('zh')
  expect(updates[0].localizations).toContainEqual({ locale: 'zh', title: '中文课节', body: '中文正文' })
})

test('format tools wrap selections and images insert through paste and drop', async ({ page }) => {
  await setup(page)
  let images = 0
  await page.route('**/api/assets', (route) =>
    route.fulfill({ json: [{ hash: `image-${++images}`, name: 'lesson.png' }] })
  )
  await page.goto(editorPath)
  const body = page.getByRole('textbox', { name: 'Markdown body', exact: true })
  await body.fill('Selected words')
  await body.evaluate((element: HTMLTextAreaElement) => element.setSelectionRange(0, 8))
  await page.getByRole('button', { name: 'Bold', exact: true }).click()
  await expect(body).toHaveValue('**Selected** words')
  for (const type of ['paste', 'drop']) {
    await body.evaluate((element: HTMLTextAreaElement, type) => {
      element.setSelectionRange(element.value.length, element.value.length)
      const transfer = new DataTransfer()
      transfer.items.add(new File(['test-image-bytes'], 'lesson.png', { type: 'image/png' }))
      const event =
        type === 'paste'
          ? new ClipboardEvent('paste', { clipboardData: transfer, bubbles: true, cancelable: true })
          : new DragEvent('drop', { dataTransfer: transfer, bubbles: true, cancelable: true })
      element.dispatchEvent(event)
    }, type)
    await expect(body).toHaveValue(new RegExp(`/assets/image-${type === 'paste' ? 1 : 2}/lesson.png`))
  }
  expect(images).toBe(2)
})
