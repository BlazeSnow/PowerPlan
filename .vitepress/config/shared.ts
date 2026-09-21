import { defineConfig } from 'vitepress'

//各语言共享的站点配置
export const shared = defineConfig({
    //网页标题
    title: 'PowerPlan',
    //使用无扩展名的干净链接
    cleanUrls: true,
    //排除仓库自带的 README，不参与站点构建
    srcExclude: ['README*.md'],
    //网页地图
    //TODO: 站点正式域名确定后，修改此处 hostname
    sitemap: {
        hostname: 'https://powerplan.blazesnow.com/'
    },
    //头文件配置
    head: [
        //网页logo（将应用图标 logo.ico 放入 public 目录后取消注释）
        // ['link',
        //     { rel: 'icon', href: '/logo.ico' }
        // ]
    ],
    //markdown配置
    markdown: {
        //显示行号
        lineNumbers: true,
    },
    //主题配置
    themeConfig: {
        //左上角logo（将应用图标 logo.ico 放入 public 目录后取消注释）
        // logo: '/logo.ico',
        //右边的小目录
        aside: true,
        //右边的小目录层级
        outline: [2, 4],
        //是否在markdown中的外部链接旁显示外部链接图标
        externalLinkIcon: false,
        //搜索内容显示本地化
        search: {
            //用自带的搜索功能
            provider: 'local',
            //其他选项
            options: {
                locales: {
                    root: {
                        translations: {
                            button: {
                                buttonText: '搜索文档',
                                buttonAriaLabel: '搜索文档',
                            },
                            modal: {
                                displayDetails: '显示文章的详细内容',
                                resetButtonTitle: '清除内容',
                                backButtonTitle: '返回',
                                noResultsText: '没有找到',
                                footer: {
                                    selectText: '选择',
                                    selectKeyAriaLabel: '选择',
                                    navigateText: '切换',
                                    navigateUpKeyAriaLabel: '向上',
                                    navigateDownKeyAriaLabel: '向下',
                                    closeText: '关闭',
                                    closeKeyAriaLabel: '关闭',
                                }
                            }
                        }
                    }
                }
            }
        },
        socialLinks: [
            { icon: 'github', link: 'https://github.com/BlazeSnow/PowerPlan' },
        ],
    }
})
