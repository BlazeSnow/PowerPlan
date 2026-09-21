import { defineConfig } from 'vitepress'

//Deutsch（/de/ 路径）语言配置
export const de = defineConfig({
    //网页语言
    lang: 'de-DE',
    //网页描述
    description: 'Verwenden Sie PowerPlan, um schnell zwischen Windows-Energiesparplänen zu wechseln!',
    //主题配置
    themeConfig: {
        //语言切换按钮提示
        langMenuLabel: 'Sprache wechseln',
        //切换深色或浅色模式提示
        darkModeSwitchLabel: 'Zwischen dunklem und hellem Modus wechseln',
        //切换至浅色模式提示
        lightModeSwitchTitle: 'Zum hellen Modus wechseln',
        //切换至深色模式提示
        darkModeSwitchTitle: 'Zum dunklen Modus wechseln',
        //目录按钮文字
        sidebarMenuLabel: 'Menü',
        //回到顶部文字
        returnToTopLabel: 'Nach oben',
        //右边的小目录标题
        outlineTitle: 'Auf dieser Seite',
        //上一篇下一篇
        docFooter: {
            prev: 'Vorherige',
            next: 'Nächste'
        },
        nav: [
            {
                text: 'Startseite',
                link: '/de/'
            },
            {
                text: 'Änderungsprotokoll',
                link: '/de/CHANGELOG',
                activeMatch: '/de/CHANGELOG'
            }
        ],
        notFound: {
            title: 'Seite nicht gefunden',
            quote: 'Entschuldigung, die gesuchte Seite wurde nicht gefunden',
            linkLabel: 'Zurück zur Startseite',
            linkText: 'Zurück zur Startseite',
            code: '404',
        },
        //主页页脚
        footer: {
            message: 'Diese Software ist unter der <a href="https://www.gnu.org/licenses/agpl-3.0.html" target="_blank">GNU AGPL v3.0</a> lizenziert.',
            copyright: 'Copyright © 2026 <a href="https://github.com/BlazeSnow" target="_blank">BlazeSnow</a>.'
        }
    }
})
