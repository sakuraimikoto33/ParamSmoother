# Parameter Smoother

`okitsu.net.ndparamsmoother` is a VPM package that applies [OSCmooth](https://github.com/regzo2/OSCmooth)-style smoothing to VRChat avatar parameters non-destructively.

- [日本語](#日本語)
- [English](#english)

## 日本語

### 概要

Parameter Smoother は、[OSCmooth](https://github.com/regzo2/OSCmooth) の処理を NDMF で非破壊的に実装するツールです。

アバターにコンポーネントを追加しておくと、ビルド時に Animator Controller へ OSCmooth 相当のレイヤーを生成します。元の Animator Controller アセットは直接変更しません。

OSCmooth の導入は必須ではありません。OSCmooth が導入されている場合は、OSCmooth で作成した config をコンポーネントへ読み込むことができます。

### インストール

VCC / ALCOM に以下の VPM リポジトリを追加し、`Parameter Smoother` をプロジェクトに追加してください。

```text
https://vpm.okitsu.net/
```

### 使い方

1. アバターに `Oktnet/Parameter Smoother` コンポーネントを追加します。
2. 対象の Layer、パラメーター名、Smoothness などを設定します。
3. アバターのビルド時に設定が非破壊で適用されます。

### ライセンス

MIT License

Copyright (c) 2022 Mitchell Taylor  
Copyright (c) 2026 sakuraimikoto33

## English

### Overview

Parameter Smoother is a non-destructive NDMF implementation of [OSCmooth](https://github.com/regzo2/OSCmooth) for VRChat avatars.

Add the component to your avatar, and the generates OSCmooth-style layers on Animator Controller during avatar build. The original Animator Controller asset is not modified.

OSCmooth is optional. If OSCmooth is installed, configs created with OSCmooth can be imported into the Parameter Smoother component.

### Installation

Add this VPM repository to VCC / ALCOM, then add `Parameter Smoother` to your project.

```text
https://vpm.okitsu.net/
```

### Usage

1. Add the `Oktnet/Parameter Smoother` component to your avatar.
2. Configure the target Layer, parameter name, Smoothness, and other options.
3. The settings are applied non-destructively when the avatar is built.

### License

MIT License

Copyright (c) 2022 Mitchell Taylor  
Copyright (c) 2026 sakuraimikoto33
