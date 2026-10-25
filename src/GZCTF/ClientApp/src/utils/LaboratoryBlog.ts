/** Return a safe absolute web URL, or null when unconfigured or invalid. */
export const getLaboratoryBlogUrl = (value?: string | null): string | null => {
  const url = value?.trim()
  if (!url || !/^https?:\/\/[^/\s?#]+[^\s]*$/i.test(url)) return null
  try {
    const parsed = new URL(url)
    return parsed.hostname && ['http:', 'https:'].includes(parsed.protocol) ? url : null
  } catch {
    return null
  }
}
