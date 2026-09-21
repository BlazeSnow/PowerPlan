import { defineConfig } from 'vitepress'

//Español（/es/ 路径）语言配置
export const es = defineConfig({
    //网页语言
    lang: 'es-ES',
    //网页描述
    description: '¡Cambia rápidamente de plan de energía en Windows con PowerPlan!',
    //主题配置
    themeConfig: {
        //语言切换按钮提示
        langMenuLabel: 'Cambiar idioma',
        //切换深色或浅色模式提示
        darkModeSwitchLabel: 'Cambiar entre modo oscuro y claro',
        //切换至浅色模式提示
        lightModeSwitchTitle: 'Cambiar al modo claro',
        //切换至深色模式提示
        darkModeSwitchTitle: 'Cambiar al modo oscuro',
        //目录按钮文字
        sidebarMenuLabel: 'Menú',
        //回到顶部文字
        returnToTopLabel: 'Volver arriba',
        //右边的小目录标题
        outlineTitle: 'En esta página',
        //上一篇下一篇
        docFooter: {
            prev: 'Anterior',
            next: 'Siguiente'
        },
        nav: [
            {
                text: 'Inicio',
                link: '/es/'
            },
            {
                text: 'Registro de cambios',
                link: '/es/CHANGELOG',
                activeMatch: '/es/CHANGELOG'
            }
        ],
        notFound: {
            title: 'Página no encontrada',
            quote: 'Lo sentimos, no se ha encontrado la página que busca',
            linkLabel: 'Volver al inicio',
            linkText: 'Volver al inicio',
            code: '404',
        },
        //主页页脚
        footer: {
            message: 'Este software se publica bajo la licencia <a href="https://www.gnu.org/licenses/agpl-3.0.html" target="_blank">GNU AGPL v3.0</a>.',
            copyright: 'Copyright © 2026 <a href="https://github.com/BlazeSnow" target="_blank">BlazeSnow</a>.'
        }
    }
})
