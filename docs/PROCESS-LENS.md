# Process Lens

左は「誰が」、右は「誰を」。両側は同じ型・同じ再帰規則。詳細は選択した記録に対応するグラフ上のPopupだけで、下部パネルは置かない。

## 分担

| 所有者 | 担うこと |
| --- | --- |
| 提供側 | ビジネスプロセス、対応、ズレ、根拠の意味 |
| visions | 入力検証、相対Lens、uiのSceneへの変換、元refのPopup |
| ui | 再帰配置、輪郭／全文の表示尺度、Renderer、Camera、pan/pinch |

`ui`は`deps/ui`のGit submodule（commit固定）。独自Rendererへのfallbackはない。`scripts/build-ui.mjs`は固定版を検証し、上流のmodule-closure工具で必要なモジュールを集め、正確なGit bytesとライセンスを生成資産へ出力する。生成資産はGitの正本にしない。実行時の外部取得・CDN・Node/.NET追加インストールを利用者に求めない構成とする。

開発時はsubmoduleの取得後に`./scripts/build.ps1`を実行する。uiを取得できないときはbuildを失敗させる。リポジトリの公開・認証設定はこのPRでは変更しない。

## 閲覧

Windowsアプリの「Process Lens」→「合成デモ」。または管理側から提供されたWorld JSONをローカルで読み込む。供給された`views`で視点を切り替え、戻るとLens・選択・Cameraが復帰する。Popupは供給された状態・根拠参照を表示するだけで、証拠の真偽を保証しない。

1つのui Rendererと1つのグラフ面を使う。ui内部の補助SVGは新たなプロセス画面ではない。ノード・辺の正本IDと、左右に現れる表示上のIDを分ける。内部の業務遷移も左右間の関係も元の向きを保持する。

## 未確認の扱い

focusログから業務のPlan、Case、判断完了を生成しない。`unknown`を`mismatch`へ変換しない。壊れた参照・包含の循環・表示契約が未対応の複数包含親を拒否する。名前は役職を決めず、権威のフィールドも利用権限を与えない。

この版のローカル検査は契約20件と、Git blob一致を確認したuiの実配置モジュールによるScene検査8件。Rendererの実ブラウザ操作、Windows配布物、実業務の管理側ログとの対応は別途検証が必要。ui取得で止まったCIを検査合格として扱わない。
