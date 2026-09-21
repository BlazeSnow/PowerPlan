import { defineConfig } from 'vitepress'

//简体中文（根路径）语言配置
export const zh = defineConfig({
    //网页语言
    lang: 'zh-CN',
    //网页描述
    description: '使用 PowerPlan，快速切换 Windows 电源计划！',
    //主题配置
    themeConfig: {
        //语言切换按钮提示
        langMenuLabel: '切换语言',
        //切换深色或浅色模式提示
        darkModeSwitchLabel: '切换深色或浅色模式',
        //切换至浅色模式提示
        lightModeSwitchTitle: '切换至浅色模式',
        //切换至深色模式提示
        darkModeSwitchTitle: '切换至深色模式',
        //目录按钮文字
        sidebarMenuLabel: '目录',
        //回到顶部文字
        returnToTopLabel: '回到顶部',
        //右边的小目录标题
        outlineTitle: '本篇目录',
        //上一篇下一篇
        docFooter: {
            prev: '上一篇',
            next: '下一篇'
        },
        nav: [
            {
                text: '首页',
                link: '/'
            },
            {
                text: '更新日志',
                link: '/CHANGELOG',
                activeMatch: '/CHANGELOG'
            }
        ],
        notFound: {
            title: '页面未找到',
            quote: '抱歉，没有找到您需要的页面',
            linkLabel: '回到首页',
            linkText: '回到首页',
            code: '404',
        },
        //主页页脚
        footer: {
            message: '软件使用 <a href="https://www.gnu.org/licenses/agpl-3.0.html" target="_blank">GNU AGPL v3.0</a> 协议。',
            copyright: 'Copyright © 2026 <a href="https://github.com/BlazeSnow" target="_blank">BlazeSnow</a>.'
        }
    }
})
