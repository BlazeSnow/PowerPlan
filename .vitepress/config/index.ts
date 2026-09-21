import { defineConfig } from 'vitepress'
import { shared } from './shared'
import { zh } from './zh'
import { en } from './en'
import { de } from './de'
import { es } from './es'
import { fr } from './fr'
import { it } from './it'
import { zhHant } from './zh-hant'

export default defineConfig({
    ...shared,
    locales: {
        root: { label: '简体中文', ...zh },
        en: { label: 'English', link: '/en/', ...en },
        de: { label: 'Deutsch', link: '/de/', ...de },
        es: { label: 'Español', link: '/es/', ...es },
        fr: { label: 'Français', link: '/fr/', ...fr },
        it: { label: 'Italiano', link: '/it/', ...it },
        'zh-hant': { label: '繁體中文', link: '/zh-hant/', ...zhHant }
    }
})
