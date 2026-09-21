import { defineConfig } from 'vitepress'

//English（/en/ 路径）语言配置
export const en = defineConfig({
    //网页语言
    lang: 'en-US',
    //网页描述
    description: 'Switch power plans on Windows quickly with PowerPlan!',
    //主题配置
    themeConfig: {
        //语言切换按钮提示
        langMenuLabel: 'Change language',
        //切换深色或浅色模式提示
        darkModeSwitchLabel: 'Toggle dark or light mode',
        //切换至浅色模式提示
        lightModeSwitchTitle: 'Switch to light mode',
        //切换至深色模式提示
        darkModeSwitchTitle: 'Switch to dark mode',
        //目录按钮文字
        sidebarMenuLabel: 'Menu',
        //回到顶部文字
        returnToTopLabel: 'Back to top',
        //右边的小目录标题
        outlineTitle: 'On this page',
        //上一篇下一篇
        docFooter: {
            prev: 'Previous',
            next: 'Next'
        },
        nav: [
            {
                text: 'Home',
                link: '/en/'
            },
            {
                text: 'Changelog',
                link: '/en/CHANGELOG',
                activeMatch: '/en/CHANGELOG'
            }
        ],
        notFound: {
            title: 'Page not found',
            quote: 'Sorry, the page you are looking for could not be found',
            linkLabel: 'Back to home',
            linkText: 'Back to home',
            code: '404',
        },
        //主页页脚
        footer: {
            message: 'This software is licensed under the <a href="https://www.gnu.org/licenses/agpl-3.0.html" target="_blank">GNU AGPL v3.0</a> license.',
            copyright: 'Copyright © 2026 <a href="https://github.com/BlazeSnow" target="_blank">BlazeSnow</a>.'
        }
    }
})
