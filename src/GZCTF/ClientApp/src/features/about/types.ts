export type AboutItem = { id: string; type: 'text' | 'image' | 'timeline'; width: 3 | 4 | 6 | 8 | 12; title?: string; html?: string; url?: string; href?: string; alt?: string; caption?: string; events?: { time: string; title: string; description: string; image?: string }[] }
export type AboutSection = { id: string; title: string; items: AboutItem[] }
export type AboutDocument = { schemaVersion: 1; title: string; sections: AboutSection[] }
export const emptyDocument: AboutDocument = { schemaVersion: 1, title: '关于实验室', sections: [] }
