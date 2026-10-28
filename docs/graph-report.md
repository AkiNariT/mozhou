# 人物关系图检查记录

2026-10-28，在 Windows / .NET SDK 8.0.422 下执行：

```powershell
dotnet run --project Mozhou.Graph.Tests/Mozhou.Graph.Tests.csproj --configuration Release -p:BuildInParallel=false -m:1
```

结果：退出码 0，`PASS: 19 focused graph checks`。

检查直接在 STA 线程实例化实际 WPF `GraphView`，覆盖空状态、全部人物与一阶邻居聚焦、有向箭头、同向/反向多重关系、非法坐标和颜色、交叉关系标签避让、刷新稳定性，以及 36 条拥挤关系下的标签互不重叠、避开人物卡片、引线、绘制顺序和画布边界。

交叉关系回归使用示例人物的四角布局；原实现的两个对角线标签均在中点，检查先复现失败，修复后通过。当前标签优先沿所属曲线寻找空位；空间不足时使用带引线的空位，并扩展画布。

此检查不模拟系统鼠标、保存文件或关系编辑对话框；这些需要桌面交互验证。测试工程通过相对路径引用核心项目和生产 `GraphView.cs`，无外部测试包，不依赖临时目录。
