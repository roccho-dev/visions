# 依存と検証境界

| 部品 | 依存 | 理由 |
| --- | --- | --- |
| Visions.Core | .NET 10標準のみ | OS非依存のRecord・検査・追記・明示選択ファイルの取得 |
| Visions.Viewer | Core + ASP.NET Core | 消費側でだけ投影。読取専用のlocalhost表示 |
| Visions.Windows | Core + Viewer + FlaUI.UIA3 5.0.0 | WinForms入口と、別プロセスでのUIA観測 |
| Tests | Core + Viewer | Windowsなしで同じ契約を検証 |

前のPR本文にあるFlaUI 6の記述は未確認だった。公式Release APIとv5.0.0のcsprojで確認できた5.0.0を固定する。FlaUI.CoreはWindowsのOSS名であり、Visions.Coreへは参照しない。

一次情報: https://github.com/FlaUI/FlaUI/releases/tag/v5.0.0 / https://github.com/FlaUI/FlaUI/blob/v5.0.0/src/FlaUI.UIA3/FlaUI.UIA3.csproj / https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-eventsforclients / https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview

## 小さなCI

`pwsh scripts/build.ps1`で契約・秘匿・投影・HTTPを検査。Windowsで`-Publish`を付けると自己完結exeを生成し、**生成したexeそのもの**の起動・JSONL・localhost読出しを試験した後だけZIPを作る。1 workflow、Linux/Windowsの2 job。CIの単体・起動合格は、対象業務アプリのUIA捕捉率や非干渉の実証ではない。

## 今回固定する補足

Recordに`v:1`を付け、未知の版・項目・自由文字列を拒否する。sourceとcapture_idは仮名ID、seqは同一source内だけ。破損行より後は投影せず、欠けた末尾・重複ID・順序逆転を明示する。投影規則は`focus-sequence/1`。同じRecordと規則の版から同じ結果を作る。

## 未実証・対象外

実業務GUIでの捕捉率・負荷・非干渉、実機での停止復帰、SmartScreen/EDR、配布署名、Linux取得Adapter、業務別エクスポート、全件性照合、保存期限・暗号化は未完了。UIA非通知を欠損として必ず検出できるという主張はしない。実測してから採用範囲を広げる。

CIの配布物には、復元した依存パッケージのライセンス・nuspec・依存一覧を同梱する。
