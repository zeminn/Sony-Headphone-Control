- # Sony 耳机浮窗（SonyHeadphonesFlyout）

  SonyHeadphonesFlyout 是一款基于 FluentFlyout 设计风格的 Windows 11 系统托盘应用，用于在电脑上快速控制兼容的 Sony 蓝牙耳机。

  应用采用 Windows 11 Fluent 风格界面，支持从任务栏系统托盘打开浮窗，无需使用手机即可调整耳机的降噪、环境声和虚拟环绕（VPT）等功能。

  程序使用 C# / WPF 构建界面，并通过 C++ 实现 Sony 蓝牙控制协议。

  Windows x64 发布版本为自包含程序，普通用户无需另外安装 .NET Runtime。

  ## 功能特性

  - Windows 11 Fluent 风格界面。
  - 系统托盘快捷控制。
  - 自动发现 Windows 中已配对或已连接的 Sony 耳机。
  - 支持手动选择和切换设备。
  - 主动降噪和环境声控制。
  - 环境声等级调节。
  - 聚焦人声（Focus on Voice）。
  - 虚拟环绕 VPT 控制。
  - 跟随 Windows 系统的明暗主题。
  - 保存每副耳机的部分控制状态。
  - 支持读取当前媒体封面并显示 Cover Glow 背景。
  - 蓝牙控制通道异常时提供错误提示和重连处理。

  部分功能取决于耳机型号及其固件支持情况。

  ## 系统要求

  ### 使用软件

  - Windows 11。
  - x64 处理器。
  - 支持蓝牙的 Windows 电脑。
  - 兼容的 Sony 蓝牙耳机。

  ### 开发和编译

  - Windows 11 x64。
  - .NET SDK 10。
  - LLVM-MinGW Windows x64 工具链（UCRT）。
  - PowerShell。
  - 网络连接，用于首次下载 NuGet 依赖。

  ## 支持的设备

  目前主要针对 Sony WH-1000XM3 进行开发和测试。

  其他 Sony 蓝牙耳机可能支持部分功能，但不同型号的功能集合和控制协议可能存在差异。

  尤其需要注意：

  - 部分耳机不支持 VPT。
  - 部分耳机不支持 Sound Position。
  - 不同型号可能具有不同的环境声等级。
  - 部分设备可能无法建立第三方控制通道。

  本项目不是 Sony 官方软件，也不保证兼容所有 Sony 耳机。

  ## 下载与安装

  ### 1. 下载程序

  进入本仓库的 **Releases** 页面，下载最新的 Windows x64 发布版本：

  ```
  SonyHeadphonesFlyout-win-x64.zip
  ```

  ### 2. 解压程序

  将 ZIP 文件解压到任意目录。

  ### 3. 启动程序

  运行：

  ```
  SonyHeadphonesFlyout.exe
  ```

  发布版本自带所需的 .NET 运行时组件，无需用户另外安装对应版本的 .NET Runtime。

  ### 4. 打开控制浮窗

  运行后，程序会在 Windows 系统托盘显示蓝色耳机图标。

  - 左键单击图标：打开控制浮窗。
  - 右键单击图标：退出程序。

  如果没有看到图标，请检查任务栏右侧的 `^` 隐藏图标区域。

  ## 使用说明

  ### 1. 设备选择

  一级浮窗会列出 Windows 中已配对或已连接的 Sony 耳机。

  刷新按钮位于窗口标题栏，可手动重新搜索设备。

  当设备列表只有一项且该设备已经连接时，程序会直接打开二级控制浮窗。

  已配对但尚未建立音频连接的耳机仍可能显示在设备列表中，用户可以手动选择。

  点击左上角的返回箭头，可以返回设备选择界面。

  ### 2. 降噪与环境声控制

  二级浮窗通过 Sony 蓝牙控制通道向耳机发送设置命令。

  环境声控制包括：

  - 主动降噪。
  - 环境声等级调节。
  - 聚焦人声（Focus on Voice）。

  环境声滑块使用原版协议对应的 0–19 档。

  其中：

  - 0、1 对应原版的双级和单级降噪控制。
  - 其他档位用于环境声等级调节。
  - 聚焦人声在 3–19 档可用。

  拖动滑块时，程序会合并待发送的等级变更，避免产生过多重复控制命令。

  具体功能以所连接耳机的实际支持情况为准。

  ### 3. 虚拟环绕 VPT

  VPT（Virtualphones Technology）是 Sony 的虚拟环绕音效技术。

  在支持 VPT 的耳机上，可以通过软件选择不同的虚拟声场。

  程序支持原版协议中的以下 VPT 预设：

  - Off：关闭。
  - Outdoor Festival：户外音乐节。
  - Arena：竞技场。
  - Concert Hall：音乐厅。
  - Club：俱乐部。

  当前程序的 VPT 开关在开启时默认选择 Arena（竞技场），关闭时退出虚拟环绕。

  VPT 设置会通过 Sony 控制协议发送给耳机。

  需要注意，实际音频处理由兼容耳机完成，本程序本身不包含 Sony VPT 音频处理算法。

  因此，该功能不能直接让非 Sony 耳机获得 Sony 原生 VPT 效果。

  相关项目：

  [SonyHeadphonesClient v1.3.2](https://github.com/Plutoberth/SonyHeadphonesClient/releases/tag/v1.3.2)

  ### 4. 控制状态保存

  每副耳机的环境声与 VPT 设置，会在收到控制命令应答后保存到本机。

  重新打开浮窗或重启程序后，应用会恢复对应选项的高亮状态。

  需要注意，软件保存的控制状态不一定始终等于耳机当前的真实状态。例如，耳机可能在连接手机后被其他软件修改设置。

  ### 5. Cover Glow 背景效果

  两级浮窗使用内嵌的 Noto Sans SC 简体中文字形。

  打开浮窗时，Cover Glow 会尝试从 Windows 当前媒体会话读取正在播放的曲目封面。

  取得封面后，程序会对图片进行模糊处理，并从窗口左侧渐隐铺开，形成柔和的背景效果。

  如果播放器没有提供封面，程序会使用 Windows 当前主题色生成背景柔光。

  设计参考：

  [FluentFlyout 背景样式](https://github.com/unchihugo/FluentFlyout/discussions/118)

  ## 蓝牙连接说明

  Windows 蓝牙音频连接与 Sony 设置控制通道是两个相互独立的连接。

  因此，耳机能够正常播放声音，并不代表第三方软件一定能够建立设置控制通道。

  当 Sony 控制通道被拒绝或连接超时时，应用会尝试重新读取设备公布的 Sony 服务，并根据查询结果重新建立连接。

  如果连接持续失败，可以尝试：

  1. 确认耳机已在 Windows 中正确配对。
  2. 确认耳机已经开机。
  3. 暂时断开耳机与手机等其他设备的连接。
  4. 断开并重新连接 Windows 蓝牙。
  5. 在程序中刷新设备列表。
  6. 重新选择耳机并尝试连接。

  相关技术资料：

  [Microsoft Windows Bluetooth RFCOMM](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-connect)

  # 从源代码编译

  以下内容面向希望自行修改、编译或调试本项目的开发者。

  普通用户只需下载 Releases 中的可执行版本，无需安装开发环境。

  ## 开发环境准备

  ### 1. 安装 .NET SDK 10

  官方下载地址：

  [Microsoft .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)

  选择 Windows x64 版本的 **SDK**。

  注意，需要安装 SDK，而不仅仅是 .NET Runtime。

  也可以打开 PowerShell，执行：

  ```
  winget install --id Microsoft.DotNet.SDK.10 --exact
  ```

  安装完成后，重新打开 PowerShell。

  执行：

  ```
  dotnet --version
  ```

  如果显示类似以下版本号：

  ```
  10.0.404
  ```

  说明 .NET SDK 已经安装成功。

  本项目开发时使用的 SDK 版本为 10.0.404。

  其他兼容的 .NET 10 SDK 版本也可以尝试使用。

  ### 2. 安装 LLVM-MinGW

  本项目包含 C++ 原生通信模块，需要 LLVM-MinGW 编译器。

  官方下载地址：

  [LLVM-MinGW Releases](https://github.com/mstorsjo/llvm-mingw/releases)

  选择适用于 Windows x86_64 的 UCRT 版本。

  开发时使用的版本为：

  ```
  llvm-mingw-20261104-ucrt-x86_64
  ```

  下载后，将工具链解压到本地目录。

  例如：

  ```
  C:\Tools\llvm-mingw
  ```

  确保编译器文件存在：

  ```
  C:\Tools\llvm-mingw\bin\clang++.exe
  ```

  打开 PowerShell，执行：

  ```
  & "C:\Tools\llvm-mingw\bin\clang++.exe" --version
  ```

  如果正常显示 Clang 版本信息，说明编译器可以使用。

  如果使用其他安装位置，需要在后续构建命令中修改编译器路径。

  ### 3. 获取源代码

  下载或克隆本项目的 GitHub 仓库。

  确保项目目录中至少包含：

  ```
  SonyHeadphonesFlyout/
  ├── README.md
  ├── app/
  │   ├── LICENSE
  │   └── SonyHeadphonesFlyout/
  │       ├── SonyHeadphonesFlyout.csproj
  │       ├── App.xaml
  │       ├── App.xaml.cs
  │       ├── Native/
  │       └── Resources/
  └── prep/
      └── sony-transport-test.cpp
  ```

  后续所有构建命令均在项目根目录执行，也就是包含 `README.md` 的目录。

  首次编译需要联网，以便 .NET 自动下载并还原项目引用的 NuGet 依赖。

  ## 构建

  ### 1. 设置 LLVM-MinGW 路径

  打开 PowerShell，进入项目根目录。

  设置编译器路径：

  ```
  $clang = "C:\Tools\llvm-mingw\bin\clang++.exe"
  ```

  如果 LLVM-MinGW 安装在其他位置，请修改为实际路径。

  检查文件是否存在：

  ```
  Test-Path $clang
  ```

  正常情况下应返回：

  ```
  True
  ```

  ### 2. 编译 Debug 版本

  执行：

  ```
  dotnet build .\app\SonyHeadphonesFlyout\SonyHeadphonesFlyout.csproj -c Debug "-p:SonyClang=$clang"
  ```

  编译成功后，Debug 版本通常位于：

  ```
  app/SonyHeadphonesFlyout/bin/Debug/net10.0-windows10.0.22000.0/
  ```

  可以运行其中的：

  ```
  SonyHeadphonesFlyout.exe
  ```

  进行功能测试。

  ### 3. 编译 Release 版本

  执行：

  ```
  dotnet publish .\app\SonyHeadphonesFlyout\SonyHeadphonesFlyout.csproj -c Release -r win-x64 --self-contained true "-p:SonyClang=$clang" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o .\dist\SonyHeadphonesFlyout-win-x64
  ```

  发布完成后，输出目录为：

  ```
  dist/SonyHeadphonesFlyout-win-x64/
  ```

  其中：

  ```
  SonyHeadphonesFlyout.exe
  ```

  为自包含的 Windows x64 可执行程序。

  发布命令启用了单文件发布及压缩选项，以减少最终程序需要携带的文件数量。

  ### 4. 构建过程说明

  项目主要由两个部分组成：

  **C# / WPF**

  负责：

  - 系统托盘。
  - 设备选择界面。
  - 耳机控制界面。
  - 窗口定位。
  - 主题和背景效果。
  - 本地设置保存。

  **C++**

  负责：

  - Sony 控制协议封包。
  - Windows 蓝牙设备枚举。
  - RFCOMM 通信。
  - 命令发送与接收。
  - 应答校验与异常处理。

  构建时，MSBuild 会调用 LLVM-MinGW，将 `Native` 目录内的 C++ 源码编译为：

  ```
  SonyTransport.dll
  ```

  随后将原生模块作为应用程序依赖一同发布。

  ### 5. 常见编译问题

  **问题：找不到 dotnet 命令**

  请确认安装的是 .NET SDK 10。

  安装后重新打开 PowerShell，再执行：

  ```
  dotnet --version
  ```

  **问题：找不到 LLVM-MinGW**

  如果出现：

  ```
  Install LLVM-MinGW x64 or set SonyClang to its clang++.exe path.
  ```

  说明项目无法找到 C++ 编译器。

  请检查：

  ```
  Test-Path $clang
  ```

  并确认构建命令中的 `SonyClang` 参数指向实际存在的 `clang++.exe`。

  **问题：NuGet 依赖还原失败**

  请检查网络连接，并尝试：

  ```
  dotnet restore .\app\SonyHeadphonesFlyout\SonyHeadphonesFlyout.csproj
  ```

  之后重新执行构建命令。

  ## 调试

  推荐使用 Visual Studio Code 或 Visual Studio 开发本项目。

  主要代码位于：

  ```
  app/SonyHeadphonesFlyout/
  ```

  ### 主要文件

  | 文件                        | 作用                    |
  | --------------------------- | ----------------------- |
  | `App.xaml`                  | 应用程序资源与配置      |
  | `App.xaml.cs`               | 程序启动与生命周期      |
  | `DevicesWindow.xaml`        | 一级设备选择窗口        |
  | `ControlsWindow.xaml`       | 二级耳机控制窗口        |
  | `FlyoutController.cs`       | 浮窗控制                |
  | `FlyoutWindow.cs`           | 浮窗行为与窗口定位      |
  | `Headphones.cs`             | 耳机控制逻辑            |
  | `HeadphoneSettingsStore.cs` | 耳机设置保存            |
  | `MediaCover.cs`             | Windows 媒体封面读取    |
  | `Native/SonyTransport.cpp`  | Sony Bluetooth 原生通信 |

  ### 命令行调试

  1. 安装 .NET SDK 和 LLVM-MinGW。
  2. 编译 Debug 版本。
  3. 运行 Debug 输出目录中的程序。
  4. 修改 C# 或 XAML 文件后，重新构建。

  程序支持以下启动参数：

  ```
  --show
  ```

  可用于启动时直接显示设备浮窗。

  ### VS Code 调试

  如果希望使用 `F5` 进行调试，需要安装适当的 C# 调试扩展，并配置项目启动方式。

  独立源码发布包不保证包含原开发环境的 `.vscode/launch.json` 和 `tasks.json`。

  因此，若编辑器中没有出现预设的启动选项，请先按照上述命令行方式编译运行，或者自行配置 VS Code 调试任务。

  ## 上游项目与技术来源

  本项目参考或复用了以下开源项目的相关实现。

  ### FluentFlyout

  项目地址：

  [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout)

  本项目使用与 FluentFlyout 相关的 Mica/WPF-UI 设计资源及部分窗口处理实现，构建独立的 Sony 耳机控制浮窗。

  开发时参考的上游提交：

  ```
  4fb7254e9b23229b1caa989523c0ada7402e6969
  ```

  FluentFlyout 使用 GPL-3.0 许可证。

  ### SonyHeadphonesClient

  项目地址：

  [Plutoberth/SonyHeadphonesClient](https://github.com/Plutoberth/SonyHeadphonesClient)

  本项目参考 SonyHeadphonesClient 的 Sony 耳机控制协议实现。

  开发时参考的版本为 v1.3.2。

  对应提交：

  ```
  7e58d77f0e172f2352870287749dfe2c6e5459d2
  ```

  相关的协议封包源码和 MIT 许可证保留在：

  ```
  app/SonyHeadphonesFlyout/Native/Upstream/
  ```

  ### Noto Sans SC

  项目地址：

  [Google Noto CJK](https://github.com/notofonts/noto-cjk)

  本项目使用 Noto Sans SC 字体。

  参考提交：

  ```
  f8d157532fbfaeda587e826d4cd5b21a49186f7c
  ```

  字体按照对应的 SIL Open Font License 使用和分发。

  ## 协议测试

  项目包含以下测试源码：

  ```
  prep/sony-transport-test.cpp
  ```

  该测试用于检查 Sony 控制协议的封包及应答处理逻辑。

  测试项目包括：

  - 数据封包。
  - 数据拆包。
  - 粘包处理。
  - 错序列处理。
  - 校验和。
  - 连接断开。
  - 超时处理。

  测试使用本机回环通信，不直接访问 Sony 耳机。

  独立发布的源码包仅包含构建当前应用所需的代码及部分测试源码。

  原开发工作区中用于构建上游基线的其他脚本和测试数据，不属于独立应用发布包的必要组成部分。

  ## 开源许可证

  本项目包含来自多个开源项目的代码及资源。

  项目自身及复用的 FluentFlyout 相关代码遵守 GPL-3.0-or-later 相关许可声明；SonyHeadphonesClient 的相关代码保留原 MIT 许可证。

  其他第三方组件与字体依照其各自许可证使用。

  相关许可证文件位于：

  - `app/LICENSE`
  - `app/SonyHeadphonesFlyout/Native/Upstream/LICENSE`
  - `app/SonyHeadphonesFlyout/Resources/Fonts/OFL.txt`

  Windows 发布版本的 `licenses` 目录包含随软件分发的第三方许可信息。

  使用、修改或重新分发本项目时，请遵守各组件对应的许可证要求，并保留必要的版权声明。

  ## 免责声明

  本项目为独立开发的第三方开源软件，与 Sony Corporation 及其关联公司没有官方合作或隶属关系。

  本项目并非 Sony 官方 Sound Connect 客户端。

  Sony、WH-1000XM3 及其他相关产品名称是其各自权利人的商标。

  本软件按现状提供，不保证兼容所有设备或固件版本。

  使用本软件前，请确认已了解第三方蓝牙控制软件可能存在的兼容性风险。
