FarmTogether2.AutoSellMod 1.1.3 安装说明

兼容性
1. 本发布包的兼容目标为 Steam build 24184988。
2. 插件加载时会校验 GameAssembly.dll 与 global-metadata.dat。文件缺失或
   指纹不符时，自动出售保持停用，并在 BepInEx 日志中记录原因。

前置条件
1. 安装 BepInEx Unity IL2CPP 6.0.0-be.755+3fab71a。
   下载地址：
   https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip
2. 按官方说明安装：
   https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html
3. 先启动游戏一次，让 BepInEx 生成所需文件，然后退出游戏。

安装
1. 解压发布包。
2. 将包内的 BepInEx 目录合并到 Farm Together 2 游戏根目录。
3. 确认最终文件为：
   BepInEx/plugins/FarmTogether2.AutoSellMod/FarmTogether2.AutoSellMod.dll
4. 启动游戏，并在 BepInEx 日志中确认：
   Loading [FarmTogether2.AutoSellMod 1.1.3]

运行说明
1. 联网出售请求在 15 秒内没有收到匹配确认时，会进入结果未知状态。
2. 结果未知的资源不会自动重试，以免重复出售。只有农场、当前玩家或
   插件生命周期重置后，才会解除阻止状态。

卸载
删除整个 BepInEx/plugins/FarmTogether2.AutoSellMod 目录。

发布包只需要上述插件目录中的 DLL。不要把 reference assembly、stub、
IL2CPP interop DLL 或 PDB 文件复制进游戏插件目录。
