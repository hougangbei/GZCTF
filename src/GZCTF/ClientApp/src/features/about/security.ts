import DOMPurify from 'dompurify'

const CONTENT_TAGS = ['p', 'br', 'h1', 'h2', 'h3', 'h4', 'strong', 'b', 'em', 'i', 'u', 's', 'ul', 'ol', 'li', 'blockquote', 'a', 'table', 'thead', 'tbody', 'tr', 'th', 'td', 'span', 'div', 'hr', 'pre', 'code', 'sub', 'sup', 'del']
const PRESET_CLASSES = new Set([
  'about-align-left', 'about-align-center', 'about-align-right', 'about-align-justify',
  'about-color-cyan', 'about-color-blue', 'about-color-green', 'about-color-orange', 'about-color-red', 'about-color-purple',
])
const PRESET_COLORS: Record<string, string> = {
  '#22d3ee': 'about-color-cyan', 'rgb(34, 211, 238)': 'about-color-cyan',
  '#60a5fa': 'about-color-blue', 'rgb(96, 165, 250)': 'about-color-blue',
  '#34d399': 'about-color-green', 'rgb(52, 211, 153)': 'about-color-green',
  '#fb923c': 'about-color-orange', 'rgb(251, 146, 60)': 'about-color-orange',
  '#f87171': 'about-color-red', 'rgb(248, 113, 113)': 'about-color-red',
  '#c084fc': 'about-color-purple', 'rgb(192, 132, 252)': 'about-color-purple',
}

export const sanitizeAboutHtml = (html: string) => {
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  for (const element of parsed.body.querySelectorAll('*')) {
    const styledElement = element as HTMLElement
    const align = styledElement.style.textAlign.toLowerCase()
    if (['left', 'center', 'right', 'justify'].includes(align)) element.classList.add(`about-align-${align}`)
    const color = styledElement.style.color.toLowerCase()
    if (PRESET_COLORS[color]) element.classList.add(PRESET_COLORS[color])
    element.removeAttribute('style')
    const classes = [...element.classList].filter((value) => PRESET_CLASSES.has(value))
    if (classes.length) element.className = classes.join(' ')
    else element.removeAttribute('class')
  }
  return DOMPurify.sanitize(parsed.body.innerHTML, {
    ALLOWED_TAGS: CONTENT_TAGS,
    ALLOWED_ATTR: ['href', 'title', 'class'],
    ALLOWED_URI_REGEXP: /^https?:/i,
    FORBID_TAGS: ['script', 'style', 'iframe', 'svg', 'math', 'object', 'embed', 'template'],
    FORBID_ATTR: ['style', 'id', 'target', 'onclick', 'onerror'],
  })
}

export const safeAboutImageUrl = (value?: string) => {
  if (!value) return undefined
  if (/^\/assets\/[0-9a-f]{64}\//i.test(value)) return value
  try {
    return new URL(value).protocol === 'https:' ? value : undefined
  } catch {
    return undefined
  }
}
