# 06 - 对接 Everything 做文件快查

Status: done

## 问题

M1 的 `FileIndex` 是浅层走查 Desktop / Documents / Downloads，只有几千个路径。全盘文件查不到——「那个报告在哪」这种问题它答不了。

本机装了 Everything **1.4.1.1026 x64**（便携版 `G:\Everything\Everything.exe`），索引 `S:\Users\hdty\AppData\Local\Everything\Everything.db`，**4,225,137 条**，常驻运行。它已经把全盘扫完了，我们自己再扫一遍是重复劳动，而且慢。

用户授权自助实现、不提问、直接开发。以下设计决策由我自行拍板。

## 修法

走 Everything 自己的 IPC（`WM_COPYDATA` + QUERY2W）拿候选，**不引任何 DLL**。Everything 是加速器不是唯一来源：它不在就退回浅走查，缺依赖只掉速度不掉功能。

## 验收标准

- [x] 打字时能查到全盘文件（不只 Desktop/Documents/Downloads）
- [x] 前缀/精确命中能在大候选集里排到文件首位
- [x] 应用和文件共用一个列表，应用不被文件挤掉
- [x] 查询提交不阻塞输入
- [x] Everything 不可用时静默退回浅走查，不弹窗

## Comments

### 2026-10-18 — 设计决策（自助拍板）

1. **Everything 是加速器不是唯一来源**。`FileIndex` 内部按「Everything 在不在」选数据源，**一个 provider 内部切换**而不是两个并列 provider——并列会让同一条文件出两行。
2. **查询时取，不预先同步索引**。Everything 的价值就是把百万行筛成几个，预先把它的库搬过来等于自己重建一遍索引。为此 `IResultProvider` 加了默认空实现的 `Prime(string query)`：即将打分的查询先给 provider 看一眼，provider 借此去取候选。`AppIndex` / `CommandProvider` 是固定集合，`Prime` 对它们是无操作，一行不改。
3. **候选窗口 2000**（不是最初设想的 200）。见下「候选窗口」。
4. **按名称升序取**。这是 Everything 免费从索引答得出、且同一查询两次结果相同的唯一排序。排序交给本地阶梯，不在 Everything 里做相关性。
5. **只匹配文件名**，不匹配路径（Everything 的 `match_path=0` 默认即是）。用户想找「名字叫 X 的文件」，不是「路径里带 X 的文件」。
6. **文件和文件夹都算**，都用 `ResultKind.File`。
7. **Subtitle 是所在目录**，与 M1 一致。
8. **Everything 缺席静默降级**，不弹窗、不提示。缺一个外部工具不该打断输入。
9. **不加「文件让应用」的排序特例**。阶梯保持纯粹：精确 > 前缀 > 词首 > 词中。真出现应用被文件压住，那是阶梯的问题，不是文件源的问题。
10. **查询串原样透传**，不加引号。Everything 的搜索语法（`ext:js`、`size:>10mb`、通配符）因此直接可用，算个免费的高级特性。

### 候选窗口：200 是不够的，2000 是够的

原设计是「取前 200 条、按名升序，然后本地阶梯排」。算一下就发现：**按名升序会把前缀命中埋掉**——`report` 的前缀命中在第 424 位，200 的窗口里根本没有。

`.scratch/everything-probe/max-probe/` 实测：

| 查询 | 匹配总数 | 前缀命中的位置 | 结论 |
|---|---|---|---|
| `report` | 7,925 | max=200 **不在窗口**；max≥500 起稳定在 **424** | 200 必须加 |
| `note` | 5,547 | 94 | 200 够，但没余量 |
| `readme` | 79,722 | 0 | 任何窗口都够 |
| `a` | 4,226,927 | — | 任何选择器都只是任意采样 |
| `re` | 890,702 | — | 同上 |

**窗口大小几乎不影响延迟**：100 和 2000 都是 46–94ms，`SendMessage` 本身 0.1–1ms。只有编组开销随大小涨。

关键不是**位置**而是**在不在窗口里**——只要进了窗口，本地阶梯就会把它排到第一。最坏实测是 424，取 **2000 给了 4 倍余量**。超宽查询（`a`）返回的是按名排序的采样，这在文档里写明，不假装是相关性结果。

### 实现

- `Services/EverythingClient.cs` — 纯 IPC 客户端。自建隐藏消息窗口收回复，`WM_COPYDATA` 缓冲区当场拷走（它随调用销毁）。`ChangeWindowMessageFilterEx` 放行 UIPI。**`WNDCLASSEX` 的 `CharSet.Unicode` 是承重墙**：不标它类名按 ANSI 封送，`RegisterClassEx` 静默什么都不注册。
- `Services/FileIndex.cs` — 一个 provider，内部选源。`Prime` 同查询只问一次；回复带自己的查询串，过期回复丢弃。Everything 中途消失则退回浅走查。
- `Services/SearchService.cs` — 打分前先 `Prime` 全部 provider（两趟循环）。
- `Services/SearchPlugin.cs` — 文件回复到了要让面板重画：`_files.Changed += () => Changed?.Invoke();`；`Dispose` 交还 IPC 窗口。
- 丢掉了文件名的点号前缀过滤（只留 `Naming.IsSystemFile`），否则 `.env` 这类永远搜不到。

### 实测（`.scratch/everything-probe/ztool-probe/`，端到端打真 Everything）

```
EverythingClient.IsRunning = True

=== 1. FileIndex.Prime -> Candidates, with the ladder on top ===
  jsb        first=   4.8ms  candidates=468
  report     first=   0.6ms  candidates=2000    top = report   (node_modules/istanbul/lib)
  readme     first=   0.8ms  candidates=2000    top = README
  note       first=   0.6ms  candidates=2000    top = note     (go sumdb)
  invoice    first=   0.8ms  candidates=587     top = invoice
  ztool      first=   0.5ms  candidates=241     top = ztool    (F:\)

=== 2. does the ladder surface a prefix match inside a huge match set? ===
  report     2000 candidates | ladder top file = report      score= 1000.0
  note       2000 candidates | ladder top file = note        score= 1000.0
  readme     2000 candidates | ladder top file = README      score= 1000.0
  setting    2000 candidates | ladder top file = setting     score= 1000.0

=== 3. apps and files share one list, apps are not displaced ===
  jsb        -> [A] 记事本  |  [F] jsbi  |  [F] jsbn  |  ...
  ztool      -> [F] ztool  |  [F] ZTool  |  [F] ZTools  |  ...

=== 4. latency of a warm query ===
  submit path: min=0.6ms median=1.0ms max=1.3ms (does not block on the reply)

=== 5. a query with no matches ===
  candidates=0  results=0
```

| 验收 | 结果 |
|---|---|
| 能查到全盘文件 | ✅ 241–2000 候选/查询，跨 F:/G:/S: |
| 大候选集里前缀命中排到文件首位 | ✅ 4/4 查询阶梯 top 分数 1000（窗口 2000） |
| 应用不被文件挤掉 | ✅ `jsb` → `记事本` 在 468 条文件候选之前 |
| 提交不阻塞输入 | ✅ 0.6–1.3ms，只堵在 Everything 接受包那一刻 |
| 无匹配不报错 | ✅ 空列表，干净退出 |

**`chrome` 全是文件不是 bug**：本机 Chrome 根本没有开始菜单快捷键（`Get-ChildItem -Filter *chrome*` 在两个 Start Menu 根下都空），`AppIndex` 里就没有它。阶梯行为正确。

**未验证**：Everything 不在时的浅走查回退。本机 Everything 常驻，构造函数永远走 IPC 分支。回退分支就是 M1 那条浅走查代码，风险低，但如实记为未跑过。

### 实现期补的一个口子

`FileIndex.Prime` 是公开接缝。空查询下 Everything 会回「全索引的头几千行」——2000 行噪声，面板还要花一帧去打分。`SearchService` 上游已经挡了空查询，但接缝自身不该依赖调用方自觉，加了显式判空。

`SearchService.Search` 对空查询直接返回空、从不 `Prime`，所以面板打开时不打 Everything，只出命令 + 应用。这是对的。

### 试用期要盯

1. **超宽查询（`a` / `re`）出的是按名排序的采样**，不是相关性结果。真人会不会误以为「搜不到」？
2. **打字过程中会有一瞬间拿上一次查询的候选去打分**（回复要 25–80ms）。正常打字下「上一次的候选集」包含「这一次要的」，所以看起来像渐进精化。若出现明显闪错行，改成查询变更即清空。
3. **Everything 的搜索语法是特性不是 bug**，但打 `size:` 这种会被当语法。真人被它咬到再决定要不要默认加引号。
