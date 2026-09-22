<script setup lang="ts">
defineProps<{
    /** 浅色模式下的图片地址 */
    light: string
    /** 深色模式下的图片地址 */
    dark: string
    /** 图片的替代文本 */
    alt?: string
}>()
</script>

<template>
    <span class="theme-image">
        <img class="theme-image__light" :src="light" :alt="alt" loading="lazy" />
        <img class="theme-image__dark" :src="dark" :alt="alt" loading="lazy" />
    </span>
</template>

<style scoped>
.theme-image {
    display: block;
}

.theme-image img {
    display: block;
    max-width: 100%;
    height: auto;
}
</style>

<style>
/* 图片深浅色变体切换：浅色模式显示浅色图，深色模式显示深色图 */
html:not(.dark) .theme-image__dark {
    display: none;
}

html.dark .theme-image__light {
    display: none;
}
</style>
