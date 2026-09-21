import { defineConfig } from 'vitepress'

//Italiano（/it/ 路径）语言配置
export const it = defineConfig({
    //网页语言
    lang: 'it-IT',
    //网页描述
    description: 'Usa PowerPlan per cambiare rapidamente le combinazioni di risparmio energia di Windows!',
    //主题配置
    themeConfig: {
        //语言切换按钮提示
        langMenuLabel: 'Cambia lingua',
        //切换深色或浅色模式提示
        darkModeSwitchLabel: 'Attiva o disattiva la modalità scura o chiara',
        //切换至浅色模式提示
        lightModeSwitchTitle: 'Passa alla modalità chiara',
        //切换至深色模式提示
        darkModeSwitchTitle: 'Passa alla modalità scura',
        //目录按钮文字
        sidebarMenuLabel: 'Menu',
        //回到顶部文字
        returnToTopLabel: "Torna all'inizio",
        //右边的小目录标题
        outlineTitle: 'In questa pagina',
        //上一篇下一篇
        docFooter: {
            prev: 'Precedente',
            next: 'Successivo'
        },
        nav: [
            {
                text: 'Home',
                link: '/it/'
            },
            {
                text: 'Registro delle modifiche',
                link: '/it/CHANGELOG',
                activeMatch: '/it/CHANGELOG'
            }
        ],
        notFound: {
            title: 'Pagina non trovata',
            quote: 'Siamo spiacenti, la pagina che stai cercando non è stata trovata',
            linkLabel: 'Torna alla pagina iniziale',
            linkText: 'Torna alla pagina iniziale',
            code: '404',
        },
        //主页页脚
        footer: {
            message: 'Questo software è rilasciato sotto la licenza <a href="https://www.gnu.org/licenses/agpl-3.0.html" target="_blank">GNU AGPL v3.0</a>.',
            copyright: 'Copyright © 2026 <a href="https://github.com/BlazeSnow" target="_blank">BlazeSnow</a>.'
        }
    }
})
