# 03 - 拼音首字母搜索

Status: done

## 需求

中文启动器刚需：输 `jsb` 命中「记事本」，输 `sz` 命中「设置」，输 `jishiben` / `jsb` 都能命中。当前 Ranking 只做汉字/ASCII 精确、前缀、子串匹配，中文名必须打全。

## 修法

引 NuGet 拼音库（不自造轮子），为索引条目生成拼音元数据（全拼 + 首字母），Ranking 匹配链扩展：

- 全拼匹配（`jishiben`）与首字母匹配（`jsb`）纳入既有确定性打分序列（精确 > 前缀 > 词首 > 子串，拼音命中排在汉字同级命中之后）
- 保持「无模糊、结果确定」的设计原则，不做编辑距离模糊

## 验收标准

- [ ] `jsb` 命中「记事本」（依赖 02 入库）
- [ ] `sz` 能命中含「设置」的结果
- [ ] 全拼 `jishiben` 与首字母 `jsb` 均可用
- [ ] 汉字直接输入的既有排序不回归
- [ ] 拼音元数据生成逻辑有单测（含多音字至少覆盖「重/行」级别的用例）

## Comments

### 2026-10-17 — 实现

**选型**（Q12/Q13/Q14/Q15/Q20/Q21/Q22/Q28/Q29/Q34/Q36）：

- **库**：`ToolGood.Words.Pinyin` 3.1.0.4（MIT、net10.0 原生、词典内嵌离线）。调研实测只有它靠约 8 万词词库做词境消歧——`重庆→ChongQing`、`银行→YinHang`、`重复→ChongFu`、`行长→HangZhang` 全对；BlueBird / BaoXia / TinyPinyin 都是 `重庆→zhongqing`、`银行→yinxing`。代价是常驻约 40MB + 145ms 冷启动，只在 `Rebuild()` 用一次。调研产物在 `.scratch/nuget-pinyin-research/`。
- **元数据**：`SearchResult.PinyinFull` / `PinyinInitials`，`Rebuild()` 时一次算好，不是每键现算。所有 provider 都生成（应用 / 文件 / 命令），和 `Ranking` 中心化匹配的现状一致。
- **只取汉字段**：`Chrome 浏览器` → `liulanqi` / `llq`，ASCII 段不参与。原样存库输出（`chrome llq`）的话 `llq` 只是词首子串，扣分后约 500；切成汉字段后 `llq` 是精确命中，扣分后约 900。`TitlePinyin` 按汉字连续段切分再调库。
- **多音字只入消歧后的单一读音**（`重庆` 只出 `chongqing`，不并列 `zhongqing`）。验收写的是「消歧正确」，不是穷举读音；并列会让 `zjs` 同时命中两串。
- **触发**：仅纯 ASCII 字母查询（`jsb` / `jishiben`）。中文查询走原有阶梯；混合查询属于模糊匹配，M1 排除。
- **排序**：同级减固定分 **100**。档位间距 200，扣 100 让「拼音精确(900) > Han 前缀(≈800) > 拼音前缀(≈700) > Han 词首(600) > 拼音词首(≈500)」层级清晰。拼音只对 `Title` 生效，`Subtitle` 不算。

**测试**（`ZTool.Tests`，xUnit，`dotnet test ZTool.sln`）：28 条，覆盖元数据生成（多音字 `重`/`行` 全组 + 真实应用名 + 混排）、`Ranking` 扣分阶梯、`SearchService` 端到端（`jsb`→记事本、`sz`→设置、`jsq`→计算器、`jishiben`→记事本，以及「真叫 JSB 的行排在拼音命中之前」）。

**实测**（`.scratch/appsfolder-probe/`）：`jsb`→记事本 ✅、`jishiben`→记事本 ✅、`jsq`→计算器 ✅、`ht`/`huatu`→画图 ✅、`chrome`→Google Chrome ✅、汉字直输 `设置` 排序不回归 ✅。

**`sz` 的实况**：结果里含「设置」（验收达标），但第一名是「时钟」——两者首字母都是 `sz`、都是两字名，扣分后同为 900，按 `SearchService` 的次级序（短名优先、再字母序）「时钟」在前。这是拼音首字母的固有歧义，不是排序缺陷。

**遗留**：Windows 自带「设置」被搜狗同名快捷方式挡在索引外（见 02 Comments）。所以 `sz` 里的「设置」是搜狗设置，不是 Windows 设置。验收原文只要求「命中含设置的结果」，达标；但日常用起来不对劲，等 02 那边的取舍定下再修。
