# v1.0.0 - クロスフィルター for YMM4

YukkuriMovieMaker4 向けのクロスフィルターエフェクトプラグインの初回リリースです。
Direct2D カスタムピクセルシェーダーが、しきい値以上の明るさを持つ画素を光源として検出し、その明るさを放射状の光の筋へ広げます。
距離に応じた減衰と波長ごとの分散で、筋の先端を虹色に色づけます。
光の色による着色、映像を消して筋だけを出力する光のみ表示に対応します。
8 言語リソース構成の UI を備えます。

---

## 新機能

### 1. ピクセルシェーダー

`CrossFilter.hlsl` の `main` は、各出力画素から放射状の各方向へ入力映像をたどり、しきい値以上の明るさを持つ画素の色を距離減衰付きで積算して光の筋を作ります。追加テクスチャは使用しません。`strength <= 0` かつ光のみ表示が無効のときはソースをそのまま返します。

#### サンプリング

光条の本数 `rays`（1〜16）ごとに方向 `angleRad + 2π × k / rays` を求め、`dir` と垂直方向 `perp` を計算します。各方向で `samples`（1〜64）回、距離 `t = len × (i + 0.5) / samples` の位置を `posScene.xy` からたどります。`thickness > 0.25` のときは `perp` 方向へずらした 3 点を `0.5 : 0.25 : 0.25` で加重平均し、筋に太さを与えます。座標のずれは `uv0.zw` で UV へ変換してサンプリングします。

| 値 | 説明 |
|---|---|
| `rays` | 1〜16 に制限した光条の本数 |
| `samples` | 1〜64 に制限した 1 本あたりのサンプル数 |
| `len` | 1px 以上に制限した筋の長さ |
| `dir` / `perp` | 各光条の方向と垂直方向 |

#### しきい値と減衰

サンプル色の輝度を Rec.601 係数 `(0.299, 0.587, 0.114)` で求め、`knee = max(1 − threshold, 0.05)` を用いた `mask = saturate((luma − threshold) / knee)` の 2 乗でしきい値を境に光源を選びます。距離 `u = 3t / len` に対して、緑を基準とした逆 2 乗の減衰 `wG = 1 / (1 + u²)` で重み付けし、`k == 0` のサンプルで `wG` を合計して正規化係数 `norm` を求めます。

#### 分散

赤と青の減衰距離を分散 `dispersion` でずらします。`fR = 1 + 0.4 × dispersion`、`fB = max(1 − 0.35 × dispersion, 0.1)` を用いて、赤 `wR = 1 / (1 + (u / fR)²)` は遠くまで届き、青 `wB = 1 / (1 + (u / fB)²)` は根元へ寄ります。この差で筋の先端が虹色に色づきます。

| 値 | 説明 |
|---|---|
| `fR` | 赤の減衰距離を伸ばす係数 |
| `fB` | 青の減衰距離を縮める係数 |
| `wR` / `wG` / `wB` | 各チャンネルの距離減衰の重み |

#### 合成

積算した光を `norm` で正規化し、光の色と `strength × 3` を掛けて光の筋 `light` を求めます。光のみ表示が有効なときは最大チャンネルからアルファを作り、プリマルチプライドで筋だけを返します。無効なときはソース色へ加算します。

| 項目 | 式 |
|---|---|
| 光の筋 | `acc / max(norm, 1e-4) × lightColor × (strength × 3)` |
| 光のみ表示の出力 | `float4(min(light, aL), aL)`（`aL = saturate(max(light))`） |
| 通常の出力アルファ | `max(source.a, saturate(max(source.rgb + light)))` |
| 通常の出力色 | `min(source.rgb + light, a)` |

出力はいずれもプリマルチプライドを保ちます。

---

### 2. カスタムシェーダーエフェクト

`CrossFilterCustomEffect` は `[CustomEffect(1)]` の 1 入力エフェクトです。公開プロパティは `SetValue` を介して定数バッファーへ転送します。各プロパティは代入時にシェーダーが前提とする範囲へ制限します。

| プロパティ | 型 | 範囲 |
|---|---|---|
| `Strength` | `float` | 0〜20 |
| `Threshold` | `float` | 0〜0.999 |
| `Length` | `float` | 0〜2000 |
| `RayCount` | `float` | 1〜16 |
| `Angle` | `float` | ラジアン |
| `Dispersion` | `float` | 0〜1 |
| `Thickness` | `float` | 0〜50 |
| `LightOnly` | `int` | 0 または 1 |
| `LightR` | `float` | 0〜1 |
| `LightG` | `float` | 0〜1 |
| `LightB` | `float` | 0〜1 |
| `Samples` | `float` | 1〜64 |

`ConstantBuffer` のレイアウトは以下のとおりです。12 個の 4 バイト値で合計 48 バイトとなり、16 バイトの倍数に揃います。

| フィールド | 型 | 説明 |
|---|---|---|
| `Strength` | `float` | 強度 |
| `Threshold` | `float` | しきい値 |
| `Length` | `float` | 長さ |
| `RayCount` | `float` | 本数 |
| `Angle` | `float` | 角度（ラジアン） |
| `Dispersion` | `float` | 分散 |
| `Thickness` | `float` | 太さ |
| `LightOnly` | `int` | 光のみ表示 |
| `LightR` | `float` | 光色 R |
| `LightG` | `float` | 光色 G |
| `LightB` | `float` | 光色 B |
| `Samples` | `float` | サンプル数 |

`MapInputRectsToOutputRect` は光の筋が素材の外側へ広がる分だけ出力矩形を拡張します。`MapOutputRectToInputRects` は同じ分だけ入力矩形を拡張し、外側の明るい画素も参照できるようにします。拡張量は `ceil(min(length + thickness + 2, 4096))` です。退化した入力矩形はそのまま返します。

シェーダーリソース: `pack://application:,,,/CrossFilter;component/Shaders/CrossFilter.cso`（ps_5_0、`ShaderResourceUri.Get` が生成）

---

### 3. エフェクト定義

`CrossFilterEffect` は YMM4 の映像エフェクトとして宣言されます。

`[VideoEffect]` 属性は以下のパラメーターで宣言されます。

- 表示名：`Texts.CrossFilterEffectName`（ローカライズキー、日本語では「光条」）
- カテゴリー：`VideoEffectCategories.Drawing`
- 検索タグ：`TagCrossFilter`・`TagSparkle`・`TagLightStreak`
- `IsAviUtlSupported = false` により AviUtl 向け EXO 出力は非対応
- `ResourceType = typeof(Texts)` でローカライズリソースを指定

`Label` プロパティは `Texts.CrossFilterEffectName` を返します。

公開プロパティは以下のとおりです。

| プロパティ | 型 | デフォルト | 内部範囲 | アニメーション |
|---|---|---|---|---|
| `Strength` | `Animation` | 100 | 0〜1000 | あり |
| `Threshold` | `Animation` | 60 | 0〜100 | あり |
| `Length` | `Animation` | 80 | 0〜1000 | あり |
| `RayCount` | `Animation` | 4 | 1〜16 | あり |
| `Angle` | `Animation` | 45 | -36000〜36000 | あり |
| `Thickness` | `Animation` | 1 | 0〜50 | あり |
| `Dispersion` | `Animation` | 30 | 0〜100 | あり |
| `LightColor` | `Color` | `#FFFFFFFF` | — | なし |
| `Samples` | `Animation` | 24 | 1〜64 | あり |
| `LightOnly` | `bool` | false | — | なし |

`GetAnimatables` は `Strength`・`Threshold`・`Length`・`RayCount`・`Angle`・`Thickness`・`Dispersion`・`Samples` を返します。

`CreateExoVideoFilters` は空のシーケンスを返します（EXO 非対応）。`CreateVideoEffect` は映像処理用のインスタンスを生成します。

---

### 4. フレームごとの更新

各フレームで YMM4 の `EffectDescription` からフレーム位置、アイテム長、FPS を取得し、アニメーション値を評価します。前フレームと値が異なる項目だけをカスタムシェーダーへ転送します。

| パラメータ | 変換 |
|---|---|
| `Strength` | `value / 100` |
| `Threshold` | `value / 100` |
| `Length` | px のまま |
| `RayCount` | 四捨五入して整数へ |
| `Angle` | 度からラジアンへ変換 |
| `Dispersion` | `value / 100` |
| `Thickness` | px のまま |
| `Samples` | 四捨五入して整数へ |
| `LightColor` | `R/G/B` を 0〜1 の float へ変換し、いずれも不透明度を掛ける |
| `LightOnly` | 真偽値を 1 または 0 へ |

入力は `SetInput(0, input, true)` でカスタムシェーダーへ接続します。エフェクトチェーンのクリア時は入力 0 を `null` に戻します。

---

### 5. ローカライズ

`Texts` クラスは `[AutoGenLocalizer]` 属性を持つ `partial` クラスとして宣言されます。
`YukkuriMovieMaker.Generator` のソースジェネレーターが `Texts.csv` を処理し、各ロケールのリソースファイルを自動生成します。

対応リソース：日本語（`ja-jp`）・英語（`en-us`）・中国語簡体字（`zh-cn`）・中国語繁体字（`zh-tw`）・韓国語（`ko-kr`）・スペイン語（`es-es`）・アラビア語（`ar-sa`）・インドネシア語（`id-id`）

ローカライズキーの一覧は以下のとおりです。

| キー | ja-jp |
|---|---|
| `CrossFilterEffectName` | 光条 |
| `TagCrossFilter` | クロスフィルター |
| `TagSparkle` | きらめき |
| `TagLightStreak` | 光条 |
| `CrossFilterStrength` | 強度 |
| `CrossFilterThreshold` | しきい値 |
| `CrossFilterLength` | 長さ |
| `CrossFilterRayCount` | 本数 |
| `CrossFilterAngle` | 角度 |
| `CrossFilterThickness` | 太さ |
| `CrossFilterDispersion` | 分散 |
| `CrossFilterLightColor` | 光の色 |
| `CrossFilterSamples` | サンプル数 |
| `CrossFilterLightOnly` | 光のみ表示 |
