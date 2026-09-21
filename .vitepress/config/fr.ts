import { defineConfig } from 'vitepress'

//Français（/fr/ 路径）语言配置
export const fr = defineConfig({
    //网页语言
    lang: 'fr-FR',
    //网页描述
    description: "Basculez rapidement d'un mode d'alimentation à un autre avec PowerPlan !",
    //主题配置
    themeConfig: {
        //语言切换按钮提示
        langMenuLabel: 'Changer de langue',
        //切换深色或浅色模式提示
        darkModeSwitchLabel: 'Basculer entre mode sombre et mode clair',
        //切换至浅色模式提示
        lightModeSwitchTitle: 'Passer en mode clair',
        //切换至深色模式提示
        darkModeSwitchTitle: 'Passer en mode sombre',
        //目录按钮文字
        sidebarMenuLabel: 'Menu',
        //回到顶部文字
        returnToTopLabel: 'Retour en haut',
        //右边的小目录标题
        outlineTitle: 'Sur cette page',
        //上一篇下一篇
        docFooter: {
            prev: 'Précédent',
            next: 'Suivant'
        },
        nav: [
            {
                text: 'Accueil',
                link: '/fr/'
            },
            {
                text: 'Journal des modifications',
                link: '/fr/CHANGELOG',
                activeMatch: '/fr/CHANGELOG'
            }
        ],
        notFound: {
            title: 'Page introuvable',
            quote: 'Désolé, la page que vous recherchez est introuvable',
            linkLabel: "Retour à l'accueil",
            linkText: "Retour à l'accueil",
            code: '404',
        },
        //主页页脚
        footer: {
            message: 'Ce logiciel est publié sous la licence <a href="https://www.gnu.org/licenses/agpl-3.0.html" target="_blank">GNU AGPL v3.0</a>.',
            copyright: 'Copyright © 2026 <a href="https://github.com/BlazeSnow" target="_blank">BlazeSnow</a>.'
        }
    }
})
