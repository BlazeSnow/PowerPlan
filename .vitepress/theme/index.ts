import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import './color.css'
import DownloadLinks from '../custom/DownloadLinks.vue'

export default {
    extends: DefaultTheme,
    enhanceApp({ app }) {
        app.component('DownloadLinks', DownloadLinks)
    },
} satisfies Theme
