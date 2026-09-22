import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import './color.css'
import DownloadLinks from '../custom/DownloadLinks.vue'
import ThemeImage from '../custom/ThemeImage.vue'

export default {
    extends: DefaultTheme,
    enhanceApp({ app }) {
        app.component('DownloadLinks', DownloadLinks)
        app.component('ThemeImage', ThemeImage)
    },
} satisfies Theme
