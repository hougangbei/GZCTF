# 归档：GZCTF 前端的 ARTEX 风格改造

此文档记录上一版方案。当前界面已按用户确认的好靶场视觉方向重做；请以 [HBC-REDESIGN.md](HBC-REDESIGN.md) 为准。

本实现保留 GZCTF 的 Vite/React 路由、权限、API 与业务组件，以 ARTEX `artex-web-2026-10-11` 快照的浅色侧边栏、细边框、紧凑顶栏和单色表面为视觉依据。它是基于快照的改造，并非 ARTEX 源码的逐字节复刻。

## 组件结构

```text
App (Mantine、i18n、SWR、路由)
├── WithNavBar (全站页面外壳、跳转链接、响应式导航)
│   ├── AppHeader (手机导航开关、语言和账户菜单)
│   ├── AppNavbar (有文字标签的主导航、权限过滤)
│   ├── AppShell.Main (各业务页面)
│   │   └── PageState (页面内容的加载、空、错误、成功状态)
│   └── AppFooter
└── AccountView (登录、注册等账户页面的响应式容器)
```

`PageState` 只管理展示状态，数据请求与重试由使用它的页面负责。颜色和表面样式由 `src/styles/App.css` 的 `--artex-*` 变量统一提供；深色模式仍可通过现有设置切换。

## Props 和类型

`PageStateProps`：

| 属性 | 类型 | 作用 |
| --- | --- | --- |
| `status` | `'loading' \| 'empty' \| 'error' \| 'success'` | 唯一必填状态，避免多个布尔值冲突。 |
| `children` | `ReactNode` | 仅在成功状态显示。 |
| `loadingMessage`、`emptyMessage`、`errorMessage` | `string` | 对应状态的可读说明；页面应传入本地化文本。 |
| `onRetry` | `() => void` | 错误状态的重试操作；不提供则不显示按钮。 |
| `retryLabel` | `string` | 重试按钮的本地化标签。 |

`getCollectionStatus<T>(data: readonly T[] | undefined, error: unknown)` 为列表页提供常见状态推导。`WithNavBar` 沿用 `width`、`minWidth`、`isLoading`、`withFooter`、`withHeader`、`stickyHeader` 等现有 Props，避免业务页面改动。

## 完整实现与使用示例

实现位于 `src/GZCTF/ClientApp/src/components/PageState.tsx`，样式位于同目录的 `PageState.module.css`。首页 `src/GZCTF/ClientApp/src/pages/Index.tsx` 与帖子列表页 `src/GZCTF/ClientApp/src/pages/posts/Index.tsx` 都已接入原有 API 和该组件。首页用法如下：

```tsx
const { data: posts, error, mutate } = api.info.useInfoGetLatestPosts()

<PageState
  status={getCollectionStatus(posts, error)}
  loadingMessage={t('learning:loading')}
  emptyMessage={t('common.content.no_data')}
  errorMessage={t('common.error.encountered')}
  retryLabel={t('common.button.try_again')}
  onRetry={() => { void mutate() }}
>
  <PostList posts={posts ?? []} />
</PageState>
```

示例中的 `PostList` 表示任意列表组件；实际首页按屏幕宽度选择现有的 `PostCard` 或 `MobilePostCard`。

## 可访问性

- 加载和空状态使用 `role="status"`；错误使用 `role="alert"`。加载图形和装饰图形不会被屏幕阅读器重复朗读。
- 重试使用原生 `button`，支持 Enter、Space 和可见焦点。按钮标签由页面本地化。
- 导航项显示文字，并给当前路由设置 `aria-current="page"`。页面顶部提供键盘可聚焦的“跳转到主要内容”链接。
- 手机导航可由按钮和 Escape 键关闭；收起时设置 `aria-hidden` 与 `inert`，展开时主内容设置 `inert`。导航标签已加入所有现有语言文件。
- 手机首页帖子的标题使用原生链接，键盘用户可用 Enter 打开帖子。
- 加载图形在 `prefers-reduced-motion: reduce` 下停止旋转。

## 测试和重要边界情况

运行 `pnpm check`、`pnpm test:unit`、`pnpm build`；浏览器回归测试使用 `E2E_BASE_URL=<预览地址> pnpm exec playwright test tests/e2e/artex-shell.spec.ts`。

单元测试覆盖四种状态、错误时重试按钮、后台刷新失败时保留已有内容，以及 `undefined`、空数组与首次请求失败的区别。浏览器测试覆盖手机导航开关和 Escape、收起时的可访问树、桌面跳转链接与横向溢出、手机帖子标题的键盘打开，并模拟首页请求失败后重试得到空数组的完整流程。

首次请求尚未返回时为加载状态；返回空数组时为空状态；没有可用数据且请求失败时为错误状态。已有帖子时，后台刷新失败仍展示帖子，避免屏幕闪回错误状态。没有重试回调时，错误状态仍可阅读但不显示不可用的按钮。用户原有的 `.gitignore` 和 `.zcodeignore` 改动不属于本次实现。
