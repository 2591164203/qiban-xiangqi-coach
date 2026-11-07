# 第三方来源

本项目的应用源码以 GPL-3.0 发布，见 LICENSE。下列二进制资源保留各自的许可，不能用应用源码的许可替代。

| 组件 | 固定版本或来源 | 使用方式 |
| --- | --- | --- |
| [VinXiangQi](https://github.com/Vincentzyx/VinXiangQi) | v1.4.0，提交 a5a5d487ecd06acc951cd32c865ef20e8e5551b8 | 链接 YoloXiangQiModel.cs，复用标签映射、模型与棋盘对齐思路；GPL-3.0 |
| [VinXiangQi 模型发行包](https://github.com/Vincentzyx/VinXiangQi/releases/tag/v1.4.0) | VinXiangQi.v1.4.0.zip | 小模型 → vin-fast.onnx；中模型 → vin-accurate.onnx；万能带旋转 → vin-rotated.onnx；未更改权重 |
| [YOLOv5 .NET](https://github.com/mentalstack/yolov5-net) | Yolov5Net 1.0.9，来自 VinXiangQi 仓库的 NuGet 包 | MIT，识别预处理和后处理；保留 © Mentalstack 2016-2021 |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime/tree/v1.20.1) | Microsoft.ML.OnnxRuntime 1.20.1，NuGet 官方源 | CPU 推理；MIT 及其 ThirdPartyNotices |
| [Pikafish](https://github.com/official-pikafish/Pikafish/tree/Pikafish-2026-09-06) | Pikafish-2026-09-06 官方发行包 | Windows x86-64 universal 引擎未修改；GPL-3.0，完整对应源码可从链接取得 |
| Pikafish NNUE | 与该引擎同包的 pikafish.nnue | 独立许可见 licenses/Pikafish-NNUE-License.md，其中规定未经许可不得商用 |
| [.NET](https://github.com/dotnet/runtime) / [Windows Desktop](https://github.com/dotnet/winforms) | 构建 SDK 8.0.427；随包运行时 8.0.33 | 微软官方运行时，保留 MIT 和 ThirdPartyNotices |

测试图片 `assets/fixtures/vin-screen.png` 来自 VinXiangQi 同一提交的 `assets/image-20220711145437807.png`，原样保留，用于固定样本回归测试。它不是用户的实时网页截图。

`tests/fixtures/qq-h5-board.png` 是裁剪后的 QQ H5 棋盘兼容测试样本，已检查不包含账号、昵称、头像或登录参数。相关游戏界面美术归腾讯及相关权利人所有，未按本项目源码许可重新授权；该样本仅供识别回归测试。`docs/ui-*` 中的图片由本项目使用固定测试棋局离线渲染。

下载来源与 SHA256 记录在 `assets/sources.json`，构建依赖下载锁定值记录在 `scripts/dependencies.lock.json`。这些哈希用于复现本次取得的内容，不表示另行取得了作者签名或独立安全审计。
