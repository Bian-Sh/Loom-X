# 修复设计

Provider/Model 编辑器为每次持久化属性变更分配递增编辑版本。保存操作在共享锁内读取最新输入，响应只确认本次请求捕获的版本；若请求期间发生新编辑，则保留脏状态并由后续事件保存。数据库响应应用继续使用 `suppressDirtyTracking`，因此 UI 回填不会产生保存事件。

Settings 移除防抖保存器，属性变更事件直接进入串行保存锁；加载期间使用现有 `suppressAutoSave`，保存回读只更新密钥状态等服务端元数据。Providers 的本机保存事件继续携带 `LocalSave`，各页面不重建编辑控件。

所有编辑型 TextBox 显式使用 `Mode=TwoWay, UpdateSourceTrigger=PropertyChanged`。Gateway 组合名称保留 `LostFocus` 作为编辑完成提交点，但源值先在每次输入时更新；Providers、Settings 的属性变更事件直接自动保存。只读显示和搜索框不进入持久化保存路径。

同步、拖拽、批量切换等已有操作在进入数据库写入前使用同一保存锁，避免并发操作读取旧编辑快照。保留 `DebouncedAutoSaver` 类及其独立单元测试供兼容代码使用，但桌面配置 ViewModel 不再依赖它。
