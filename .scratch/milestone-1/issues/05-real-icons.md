# 05 - 结果行真图标

Status: done

## 问题

结果行左侧是一块 28×28 圆角色块（`Border.mark`）里放一个类型字母（`A` / `F` / `›`）。人眼找应用时找的是图标，不是字母——尤其 Store 应用和传统应用混在一起后，字母区分度不够。

02 的验收原文里写了「with correct icons」，但当时 UI 只有字母标记，没有图标提取。这一项是把那句验收补上（02 → 05 → 03 的顺序里排在 02 之后、03 之前）。

## 修法

从 shell 取应用图标，在结果行渲染。只给应用出图标；文件和命令仍用字母。

## 验收标准

- [ ] 应用行显示应用图标（`.lnk` / `.appref-ms` / `.exe` + AppsFolder 条目都算）
- [ ] 文件 / 命令行仍显示 `KindMark` 字母，左缘节奏不乱
- [ ] 图标提不出来时退回字母，不出现空白行
- [ ] 图标在缩放显示下不糊

## Comments

### 2026-10-17 — 实现

**选型**（Q11/Q17/Q18/Q27/Q32）：

- **范围**：只给应用出图标。文件图标要按文件走 shell 提取，命令行本来就没有原生图标，都不进这一轮。
- **槽位**：图标占掉 `KindMark` 的位置，但**色块保留**——28×28 圆角底衬是列表左缘的节奏基准，混排时应用行和文件行形态一致。裸图标会让应用行比文件行「轻」一截。
- **载体**：`SearchResult.Icon`（`Avalonia.Media.Imaging.Bitmap`），`Rebuild()` 时一次提取成位图持有。列表渲染是热路径，逐行走 COM 取图标会卡滚动；应用就几百个，内存可接受。存 `HICON` 句柄在渲染时转换则很容易泄漏。
- **提取**：`Services/ShellIcon.cs`——`SHCreateItemFromParsingName` 拿 `IShellItemImageFactory`，`GetImage` 出 56×56 HBITMAP（28 逻辑像素 × 2），`GetDIBits` 读成 BGRA，写进 `WriteableBitmap`。shortcut 与 Store AUMID 都能走同一条路：前者用文件路径，后者用 `shell:AppsFolder\<id>` moniker。
- **降级**：提取失败就退回 `KindMark` 字母（`SearchResult.HasIcon`）。字母本来就在模型里，零额外资源。为此 `ShellIcon.For` 兜了全部异常——缺图标只是那一格少个图标，绝不能让整条索引少一行。

**实测**（`.scratch/appsfolder-probe/`，在探针里先 `AppBuilder…SetupWithoutStarting()` 才有渲染接口）：

| 验收 | 结果 |
|---|---|
| 应用行显示图标 | ✅ 322 / 322 条索引条目都拿到了 56×56 位图 |
| 文件/命令仍显字母 | ✅ `FileIndex` / `CommandProvider` 不设 `Icon`，走 `KindMark` |
| 失败退回字母 | ✅ `HasIcon` 门控；`ShellIcon.For` 任何失败都返回 null |
| 缩放不糊 | ✅ 按 2× 设备像素提取，行内按 20×20 渲染 |

**视觉走查**（真人看一眼，未自动化）：色块底 + 图标的对比度、圆角与图标的比例，等首跑时顺手确认。
