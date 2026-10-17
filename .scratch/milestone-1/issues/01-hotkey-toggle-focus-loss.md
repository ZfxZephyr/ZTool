# 01 - hotkey-toggle 失焦修复

Status: done（2026-10-16 物理验收通过）

## 问题

焦点被其他窗口抢走（如全屏弹窗）后，面板的可见状态残留：下一次按热键变成「关闭」（无可见反应），要按第二次才能唤起。2026-10-16 实测复现。

## 修法

窗口失焦（Deactivate / LostFocus）即视为 dismiss：

- 隐藏面板
- 清空查询框（已确认语义：失焦 = 隐藏 + 清空）
- 再次热键呼出 = 空态面板

## 验收标准

- [x] 面板显示中用鼠标点击其他窗口 / 其他窗口弹出抢焦 → 面板立即隐藏
- [x] 隐藏后按一次热键即唤起空态面板（不再需要按两次）
- [x] 托盘「显示」入口与热键行为一致
- [x] Esc 清空 → Esc 关闭的既有行为不回归

## Comments

- 2026-10-16 实现：三处小改。
  1. `App.axaml.cs` 热键 toggle 条件改为 `IsVisible && IsActive` 才 dismiss，其余状态一律 Summon（自愈：被盖住/被最小化/状态脱节时一按必到前台）
  2. `MainWindow.Summon()` 补 `WindowState` 恢复（minimize-all 残留 → Normal）
  3. `MainWindow.Dismiss()` 清空查询（落实「失焦 = 隐藏 + 清空」语义）
- 根因：原 toggle 以 `IsVisible` 为准，与用户所见可脱节（被 minimize-all 后 `IsVisible` 仍 true，第一按走了 Dismiss 看似没反应）。
- 2026-10-16 物理验收（真人按键）：①组合键 toggle 正常 ②点走面板立即消失 ③再唤起为空查询。全部通过。
- **自动化备注**：windows-mcp 合成按键可打进普通输入框，但不触发 `RegisterHotKey` 全局热键（多因窗口激活/UIPI 干扰），且合成环境下面板 Activate/Deactivate 行为不可靠。后续 UI 验收优先真人按键或考虑插桩日志。
