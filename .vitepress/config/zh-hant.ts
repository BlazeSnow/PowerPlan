import { defineConfig } from 'vitepress'

//繁體中文（/zh-hant/ 路径）语言配置
export const zhHant = defineConfig({
    //网页语言
    lang: 'zh-Hant',
    //网页描述
    description: '使用 PowerPlan，快速切換 Windows 電源計劃！',
    //主题配置
    themeConfig: {
        //语言切换按钮提示
        langMenuLabel: '切換語言',
        //切换深色或浅色模式提示
        darkModeSwitchLabel: '切換深色或淺色模式',
        //切换至浅色模式提示
        lightModeSwitchTitle: '切換至淺色模式',
        //切换至深色模式提示
        darkModeSwitchTitle: '切換至深色模式',
        //目录按钮文字
        sidebarMenuLabel: '目錄',
        //回到顶部文字
        returnToTopLabel: '回到頂部',
        //右边的小目录标题
        outlineTitle: '本篇目錄',
        //上一篇下一篇
        docFooter: {
            prev: '上一篇',
            next: '下一篇'
        },
        nav: [
            {
                text: '首頁',
                link: '/zh-hant/'
            },
            {
                text: '更新日誌',
                link: '/zh-hant/CHANGELOG',
                activeMatch: '/zh-hant/CHANGELOG'
            }
        ],
        notFound: {
            title: '頁面未找到',
            quote: '抱歉，沒有找到您需要的頁面',
            linkLabel: '回到首頁',
            linkText: '回到首頁',
            code: '404',
        },
        //主页页脚
        footer: {
            message: '軟體使用 <a href="https://www.gnu.org/licenses/agpl-3.0.html" target="_blank">GNU AGPL v3.0</a> 協議。',
            copyright: 'Copyright © 2026 <a href="https://github.com/BlazeSnow" target="_blank">BlazeSnow</a>.'
        }
    }
})
