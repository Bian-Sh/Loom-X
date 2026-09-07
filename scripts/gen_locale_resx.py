"""Generate Strings.zh-TW.resx and Strings.ja-JP.resx from Strings.resx.

zh-TW: uses opencc Simplified→Traditional conversion.
ja-JP: hand-written Japanese translations (dictionary below).
"""
import re
import sys
import os
from xml.etree import ElementTree as ET

import opencc

# ── Path setup ──────────────────────────────────────────────────
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RESOURCES = os.path.join(REPO, "LoomX", "Resources")
BASE = os.path.join(RESOURCES, "Strings.resx")

# ── Japanese translations (key → value) ─────────────────────────
JA = {
    # app
    "app.loading.title": "読み込み中",
    "app.loading.description": "Loom-X を読み込んでいます。",
    "app.loading.failed": "読み込み失敗",
    "app.loading.failed.description": "データセンターの読み込みに失敗しました：{0}。ページを更新して再試行してください。",

    # nav
    "nav.overview": "概要",
    "nav.overview.description": "ローカルサービスの健全性を確認し、ゲートウェイとモデル設定を簡単に確認します。",
    "nav.gateway": "ゲートウェイ",
    "nav.gateway.description": "Endpoint のモデルルートを組み合わせ、優先順位に基づいて自動フェイルオーバーします。",
    "nav.providers": "Provider",
    "nav.providers.description": "アップストリーム接続、リクエストプロトコル、認証キー、利用可能なモデルを管理します。",
    "nav.activity": "アクティビティ",
    "nav.activity.title": "リクエストアクティビティ",
    "nav.activity.description": "プロトコル変換、アップストリーム遅延、HTTP エラーを特定し、プライバシー保護された追跡可能なコンテキストを保持します。",
    "nav.console": "コンソール",
    "nav.console.description": "ローカルゲートウェイ、プロトコル変換、アップストリームリクエストのマスク済み実行ログを表示します。",
    "nav.settings": "設定",
    "nav.settings.description": "Loom-X の表示、接続、更新、プライバシー設定を調整します。",

    # settings tabs
    "settings.tab.general": "一般",
    "settings.tab.proxy": "プロキシ",
    "settings.tab.updates": "更新",
    "settings.tab.data": "データとプライバシー",
    "settings.tab.about": "About",

    # settings general
    "settings.language.label": "インターフェース言語",
    "settings.language.hint": "インターフェースの表示言語を変更します。",
    "settings.theme.label": "テーマ",
    "settings.theme.hint": "現在の作業環境に適した配色を選択します。",
    "settings.transparency.label": "半透明ウィンドウ",
    "settings.transparency.hint": "システムマテリアルを使用してウィンドウにわずかな奥行きを与えます。",
    "settings.opacity.label": "不透明度",
    "settings.opacity.hint": "値が大きいほど、コンテンツ領域は不透明になります。",
    "settings.blur.label": "ぼかし具合",
    "settings.blur.hint": "マテリアルのシステムぼかし強度を制御します（0〜64）。",

    # settings proxy
    "settings.proxy.mode.label": "プロキシモード",
    "settings.proxy.mode.hint": "アップストリームリクエストがプロキシ経由でネットワークにアクセスする方法を制御します。",
    "settings.proxy.host.label": "プロキシアドレス",
    "settings.proxy.port.label": "ポート",
    "settings.proxy.username.label": "ユーザー名（任意）",
    "settings.proxy.password.label": "パスワード（任意）",
    "settings.proxy.password.saved.hint": "プロキシパスワードが保存されています。新しいパスワードを入力して保存すると置き換わります。",
    "settings.proxy.password.clear": "保存済みのプロキシパスワードを削除",
    "settings.proxy.status.label": "接続状態",
    "settings.proxy.test.button": "接続テスト",

    # settings updates
    "settings.update.version.label": "現在のバージョン",
    "settings.update.version.hint": "ローカルデスクトップアプリのバージョン情報。",
    "settings.update.auto.check": "自動更新チェック",
    "settings.update.auto.check.hint": "起動時にチェックし、アプリ実行中は24時間ごとにチェックします。自動インストールはされません。",
    "settings.update.use.proxy": "更新時にプロキシを使用",
    "settings.update.use.proxy.hint": "既定では上部のプロキシモードに従います。無効にすると更新リクエストは直接接続します。",
    "settings.update.check.button": "更新を確認",

    # settings data & privacy
    "settings.diagnostic.send": "匿名診断データを送信",
    "settings.diagnostic.send.hint": "API キー、リクエスト本文、モデル内容には含まれません。",
    "settings.log.stacktrace": "ログスタックトレースを記録",
    "settings.log.stacktrace.hint": "既定で無効です。有効にすると異常の特定が容易になりますが、ログに更完全な呼び出しパスが含まれます。",
    "settings.log.retention.label": "ログ保持期間",
    "settings.log.retention.hint": "コンソールログは期間終了後に自動でクリーンアップされます。",
    "settings.local.data": "ローカルデータ",
    "settings.local.data.path.label": "データディレクトリ：",
    "settings.local.data.open": "データディレクトリを開く",
    "settings.local.data.clear": "ログを削除",

    # settings about
    "settings.about.description": "Loom-X は、OpenAI、Anthropic、Ollama エンドポイントに基づくローカルおよびクラウド AI サービス向けの統合アクセスレイヤーを提供します。IDE や GitHub Copilot で選択したモデルを使用できます。モデルの選択権を開発者に返し、日常のコーディングをもっと自由で、もっと効率的にします。",
    "settings.about.homepage": "プロジェクトホームページ",
    "settings.about.issues": "問題を報告",
    "settings.about.export.label": "診断情報をエクスポート",
    "settings.about.export.hint": "認証キーやリクエスト本文を含まない診断サマリーを生成します。",
    "settings.about.export.button": "診断をエクスポート",

    # settings status
    "settings.status.loading": "設定を読み込んでいます…",
    "settings.status.loaded": "設定を読み込みました",
    "settings.status.load.failed": "設定の読み込みに失敗しました：{0}",
    "settings.status.saving": "設定を保存しています…",
    "settings.status.saved": "設定を保存しました · {0}",
    "settings.status.save.failed": "設定の保存に失敗しました：{0}",
    "settings.status.waiting": "自動保存を待機中…",

    # settings proxy test
    "settings.proxy.test.direct.success": "直接接続モードの設定は有効です。",
    "settings.proxy.test.system.success": "システムプロキシモードが選択されました。Windows 設定に従います。",
    "settings.proxy.test.invalid": "プロキシテストに失敗しました：有効な HTTP/HTTPS アドレスとポートを入力してください。",
    "settings.proxy.test.invalid.toast": "プロキシテストの設定が無効です",
    "settings.proxy.test.running": "プロキシ接続をテストしています…",
    "settings.proxy.test.success": "プロキシ接続正常 · {0}",
    "settings.proxy.test.response": "プロキシ応答あり · {0}",
    "settings.proxy.test.toast.success": "プロキシ接続テストに成功しました",
    "settings.proxy.test.toast.response": "プロキシに応答がありました。設定を確認してください",
    "settings.proxy.test.toast.failed": "プロキシ接続テストに失敗しました",
    "settings.proxy.test.exception": "プロキシテストに失敗しました：{0}",

    # settings update messages
    "settings.update.check.status.running": "更新を確認しています…",
    "settings.update.check.status.latest": "現在のバージョン {0} は最新です。",
    "settings.update.check.status.found": "新しいバージョン v{0} が見つかりました。右下の更新通知を確認してください。",
    "settings.update.check.toast.latest": "最新バージョンです",
    "settings.update.check.toast.found": "新しいバージョン v{0} が見つかりました",

    # settings data/log operations
    "settings.local.data.opened": "ローカルデータディレクトリを開きました。",
    "settings.local.data.open.failed": "データディレクトリの開きに失敗しました：{0}",
    "settings.local.data.logs.empty": "クリーンアップ可能なログがありません。",
    "settings.local.data.logs.cleared": "{0} 個のログファイルを削除しました。",
    "settings.local.data.logs.clear.failed": "ログのクリーンアップに失敗しました：{0}",
    "settings.local.data.export.done": "診断サマリーをエクスポートしました。",
    "settings.local.data.export.failed": "診断のエクスポートに失敗しました：{0}",
    "settings.local.data.export.content": "Loom-X 診断サマリー\nバージョン：{0}\nシステム：{1}\nデータディレクトリ：{2}\nプロキシモード：{3}\nログ保持：{4} 日",

    # settings option display names
    "settings.option.language.zh-CN": "簡体字中国語",
    "settings.option.language.zh-TW": "繁体字中国語",
    "settings.option.language.en-US": "英語",
    "settings.option.language.ja-JP": "日本語",
    "settings.option.theme.system": "システムに従う",
    "settings.option.theme.dark": "ダーク",
    "settings.option.theme.light": "ライト",
    "settings.option.proxy.direct": "直接接続",
    "settings.option.proxy.system": "システムプロキシ",
    "settings.option.proxy.custom": "カスタムプロキシ",
    "settings.option.retention.7": "7 日",
    "settings.option.retention.30": "30 日",
    "settings.option.retention.90": "90 日",
    "settings.option.retention.365": "365 日",
    "settings.option.retention.3650": "永久保持",

    # window chrome
    "window.minimize": "最小化",
    "window.maximize": "最大化または元に戻す",
    "window.close": "閉じる",

    # sidebar
    "sidebar.service": "ローカルサービス",
    "sidebar.version": "サービスバージョン",

    # update card
    "update.card.title": "新しいバージョンが見つかりました",
    "update.card.release.notes": "リリースノート",
    "update.card.later": "後で",
    "update.card.download": "今すぐダウンロード",
    "update.card.later.tip": "後で処理",
    "update.card.close": "閉じる",

    # overview view
    "overview.graph.hint": "ホイールでズーム · drag to pan",
    "overview.endpoint.focus.tip": "Endpoint にフォーカス",
    "overview.graph.fit.tooltip": "キャンバスに適合",
    "overview.gateway.label": "ローカルゲートウェイ",
    "overview.gateway.endpoint.label": "リスニングアドレス",
    "overview.gateway.version.label": "インターフェース状態",
    "overview.gateway.lastchecked.label": "最終確認",
    "overview.recent.label": "最近のリクエスト",
    "overview.recent.count": "最近 {0} 件",
    "overview.recent.empty": "リクエストアクティビティなし",

    # overview status
    "overview.gateway.status.not_running": "実行されていない",
    "overview.gateway.status.running": "実行中",
    "overview.gateway.status.starting": "起動中",
    "overview.gateway.status.stopping": "停止中",
    "overview.gateway.status.failed": "エラー：{0}",
    "overview.endpoint.unconfigured": "未設定",
    "overview.version.unknown": "不明",
    "overview.version.online": "Loom-X API オンライン",
    "overview.version.disconnected": "未接続",
    "overview.lastchecked.none": "未確認",
    "overview.graph.waiting": "ゲートウェイ起動を待機中",
    "overview.graph.connected": "リアルタイムトポロジー接続済み",
    "overview.graph.failed": "概要の読み込みに失敗しました",
    "overview.gateway.action.start": "ゲートウェイを起動",
    "overview.gateway.action.stop": "ゲートウェイを停止",
    "overview.gateway.action.starting": "起動中",
    "overview.gateway.action.stopping": "停止中",
    "overview.request.status.success": "成功",
    "overview.request.status.failed": "失敗",
    "overview.request.model.unknown": "不明なモデル",
    "overview.endpoint.status.disabled": "無効",
    "overview.endpoint.status.no_routes": "ルートなし",
    "overview.endpoint.status.active": "リクエストあり",
    "overview.endpoint.status.ready": "準備完了",
    "overview.route.status.active": "アクティブ",
    "overview.route.status.idle": "待機中",
    "overview.lateness.dash": "—",
    "overview.latency.format": "{0} ms",

    # activity view
    "activity.stat.requestcount.label": "リクエスト数",
    "activity.stat.requestcount.sub": "現在のフィルタ結果",
    "activity.stat.conversion.label": "プロトコル変換",
    "activity.stat.conversion.sub": "OpenAI → Anthropic",
    "activity.stat.failures.label": "失敗リクエスト",
    "activity.stat.failures.sub": "HTTP 5xx",
    "activity.stat.p95.label": "P95 遅延",
    "activity.stat.p95.sub": "最近のアクティビティ",
    "activity.search.placeholder": "モデル、Provider、またはリクエスト ID を検索",
    "activity.column.time": "時刻 / リクエスト ID",
    "activity.column.model": "モデル / Provider",
    "activity.column.route": "入口と変換パス",
    "activity.column.status": "状態",
    "activity.column.latency": "遅延",
    "activity.detail.title": "選択されたリクエスト",
    "activity.detail.fallback.model": "選択されたリクエストなし",
    "activity.detail.fallback.requestid": "左側のリクエストを選択して詳細を表示",
    "activity.detail.copy.requestid": "リクエスト ID をコピー",
    "activity.detail.field.protocol": "入口プロトコル",
    "activity.detail.field.route": "変換パス",
    "activity.detail.field.upstream": "アップストリーム応答",
    "activity.detail.field.size": "応答サイズ",
    "activity.detail.field.error": "エラー分類",
    "activity.detail.summary.label": "プライバシー保護済み診断サマリー",
    "activity.detail.summary.fallback": "診断サマリーなし",
    "activity.route.openai.passthrough": "OpenAI 透過",
    "activity.route.anthropic.passthrough": "Anthropic 透過",
    "activity.route.ollama.passthrough": "Ollama 透過",

    # activity VM
    "activity.filter.status.all": "すべての状態",
    "activity.filter.status.success": "成功",
    "activity.filter.status.failed": "失敗",
    "activity.filter.status.warning": "警告",
    "activity.filter.protocol.all": "すべての入口プロトコル",
    "activity.status.loading": "アクティビティを読み込んでいます…",
    "activity.status.empty": "リクエストアクティビティなし",
    "activity.status.loaded": "{0} 件のアクティビティを読み込みました",
    "activity.status.load.failed": "アクティビティの読み込みに失敗しました：{0}",
    "activity.status.history.more": "{0} 件のアクティビティを読み込みました。履歴を続けて表示できます",
    "activity.status.history.end": "{0} 件のアクティビティを読み込みました。履歴の末尾に達しました",
    "activity.status.history.failed": "履歴アクティビティの読み込みに失敗しました：{0}",
    "activity.status.returned": "最新アクティビティに戻りました",
    "activity.status.return.failed": "最新アクティビティに戻ることに失敗しました：{0}",
    "activity.result.count": "{0} 件のアクティビティを表示中",
    "activity.pending.count": "{0} 件の新しいアクティビティがあります。最新に戻す",
    "activity.pending.back": "最新に戻る",
    "activity.loadmore.loading": "履歴アクティビティを読み込んでいます…",
    "activity.loadmore.continue": "上方向にスワイプして古いアクティビティを読み込む",
    "activity.loadmore.end": "アクティビティ履歴の末尾に達しました",
    "activity.item.model.unknown": "未認識モデル",
    "activity.item.provider.unknown": "未マッピング Provider",
    "activity.latency.format": "{0} ms",
    "activity.bytes.format": "{0:N0} B",
    "activity.dash": "—",

    # console
    "console.search.placeholder": "リクエスト ID、モデル、またはエラー情報を検索",
    "console.toggle.info": "通常ログ",
    "console.toggle.warning": "警告ログ",
    "console.toggle.error": "エラーログ",
    "console.copy.tip": "ログをコピー",
    "console.empty": "現在のフィルタ条件に一致するログがありません。",
    "console.jump.tip": "下へジャンプ",
    "console.clear": "消去",
    "console.count.format": "全 {0} 件",
    "console.copied": "ログをコピーしました",

    # providers view
    "providers.header.list": "プロバイダー一覧",
    "providers.header.list.hint": "現在のルート順に表示",
    "providers.button.new": "新規 Provider",
    "providers.search.watermark": "Provider を検索",
    "providers.toggle.tooltip.enabled": "緑色は有効を示します",
    "providers.toggle.tooltip": "Provider を有効化",
    "providers.delete.tooltip": "Provider を削除",
    "providers.count.enabled": " 件有効",
    "providers.count.models": " 件のモデル",
    "providers.count.models.suffix": " 件のモデル ·",
    "providers.model.count.suffix": " ·",
    "providers.key.configured": "認証キー設定済み",
    "providers.key.protected": "認証キー保護済み",
    "providers.card.providers.label": "Provider",
    "providers.card.models.label": "利用可能なモデル",
    "providers.card.models.status": "リモートモデル検出済み",
    "providers.card.keys.label": "保護済み認証キー",
    "providers.card.keys.status": "DPAPI CurrentUser",
    "providers.card.health.label": "接続健全性",
    "providers.card.health.status": "検証待ち",
    "providers.tab.basic": "基本",
    "providers.tab.request": "リクエスト",
    "providers.tab.models": "モデル",
    "providers.basic.header": "アイデンティティとアップストリーム",
    "providers.request.header": "認証とネットワーク",
    "providers.name.label": "表示名",
    "providers.name.watermark": "例：DeepSeek",
    "providers.id.label": "Provider ID",
    "providers.id.watermark": "一意な識別子",
    "providers.type.label": "Provider タイプ",
    "providers.baseurl.label": "Base URL",
    "providers.baseurl.tooltip": "v1 を追加するかどうかに迷ったら、プロバイダーのドキュメントに従ってください",
    "providers.endpointformat.label": "リクエスト形式",
    "providers.proxy.checkbox": "プロキシを使用",
    "providers.headers.label": "カスタムリクエストヘッダー",
    "providers.headers.add": "追加",
    "providers.headers.name.watermark": "ヘッダー名",
    "providers.headers.value.watermark": "ヘッダー値",
    "providers.headers.remove.tooltip": "リクエストヘッダーを削除",
    "providers.headers.incomplete": "{0} 件のリクエストヘッダーが未完了です。名前と値を入力すると保存されます。",
    "providers.headers.incomplete.prefix": "・",
    "providers.headers.incomplete.suffix": " 件のリクエストヘッダーが未完了です。名前と値を入力すると保存されます。",
    "providers.headers.empty": "カスタムリクエストヘッダーが追加されていません",
    "providers.connection.label": "接続状態",
    "providers.connection.test": "接続テスト",
    "providers.models.url.label": "モデルリスト URL（任意）",
    "providers.models.url.watermark": "例：https://api.example.com/v1/models",
    "providers.models.url.hint": "空の場合は Base URL から自動推論します。入力後はこのアドレスのみでモデルを更新し、チャットリクエストには影響しません。",
    "providers.models.search.watermark": "モデルを検索",
    "providers.models.sync.tooltip": "モデルを同期",
    "providers.models.sync.button": "モデルを同期",
    "providers.models.toggleAll.tooltip": "すべてのモデルを有効または無効にする",
    "providers.models.sort.drag.tooltip": "ドラッグしてモデルの順序を調整",
    "providers.models.sort.toggle.tooltip": "モデルを下流に公開するか制御",
    "providers.models.sort.toggle.automation": "モデルを有効化",
    "providers.models.vision.tooltip": "画像入力をサポート",
    "providers.models.ctx.suffix": " ctx · ",
    "providers.models.delete.tooltip": "モデルを削除",
    "providers.dragging": "ドラッグ中",
    "providers.models.empty": "一致するモデルがありません",
    "providers.empty.title": "Provider なし",
    "providers.empty.hint": "まず左側のディレクトリから Provider を追加してください。",

    # providers VM
    "providers.edit.new.displayname": "新しい Provider",
    "providers.status.edit.new": "新しい Provider を編集中",
    "providers.status.loaded": "{0} 件の Provider を読み込みました",
    "providers.loading.failure": "読み込み失敗：{0}",
    "providers.save.success": "Provider を保存しました",
    "providers.save.success.pendingHeaders": "Provider を保存しました · {0} 件のヘッダーが未完了",
    "providers.save.failure": "保存に失敗しました：{0}",
    "providers.delete.success": "Provider を削除しました",
    "providers.delete.failure": "削除に失敗しました：{0}",
    "providers.headers.pending": "{0} 件のカスタムリクエストヘッダーを補完してください",
    "providers.sync.beforeProvider": "まず Provider を保存してからモデルを追加してください",
    "providers.sync.beforeModel": "まず Provider を保存してからモデルを同期してください",
    "providers.sync.invalidUrl": "モデルリスト URL は HTTP または HTTPS の絶対アドレスである必要があります",
    "providers.sync.running": "モデルを同期しています…",
    "providers.sync.failure.http": "モデル同期に失敗しました · {0} {1}",
    "providers.sync.failure.toast.http": "モデル同期に失敗しました · {0}",
    "providers.sync.failure.empty": "モデル同期に失敗しました · 応答内に利用可能なモデルがありません",
    "providers.sync.failure.parse": "モデル同期に失敗しました · 応答形式を解析できません",
    "providers.sync.failure.exception": "モデル同期に失敗しました · {0}",
    "providers.sync.failure.toast.exception": "モデル同期に失敗しました",
    "providers.sync.success": "モデル同期完了 · 検出 {0} 件、新規 {1} 件、更新 {2} 件",
    "providers.test.baseurl.required": "まず Base URL を入力してください",
    "providers.test.baseurl.required.toast": "まず Provider の Base URL を入力してください",
    "providers.test.running": "接続をテストしています…",
    "providers.test.success": "接続正常 · {0} · {1} ms",
    "providers.test.failure": "接続に失敗しました · {0} {1}",
    "providers.test.failure.exception": "接続に失敗しました · {0}",
    "providers.test.toast.success": "Provider 接続テストに成功しました",
    "providers.test.toast.failure": "Provider 接続テストに失敗しました",
    "providers.model.edit.new": "新しいモデルを編集中",
    "providers.model.save.success": "モデルを保存しました",
    "providers.model.save.failure": "モデルの保存に失敗しました：{0}",
    "providers.model.delete.success": "モデルを削除しました",
    "providers.model.delete.failure": "モデルの削除に失敗しました：{0}",
    "providers.model.order.saved": "モデルの順序を保存しました",
    "providers.model.order.save.failure": "モデルソート保存に失敗しました：{0}",
    "providers.model.order.save.failure.toast": "モデルソート保存に失敗しました",
    "providers.model.enable.all": "すべてのモデルを有効化しました",
    "providers.model.disable.all": "すべてのモデルを無効化しました",
    "providers.model.summary": "有効 {0} / {1} 件のモデル",
    "providers.model.unknown": "不明なモデル",
    "providers.model.context.missing": "AI プロバイダーのモデルインターフェースがコンテキスト設定を返していません",
    "providers.model.capability.vision": "ビジョン",
    "providers.model.capability.text": "テキスト",
    "providers.apikey.visibility.show": "API Key を表示",
    "providers.apikey.visibility.hide": "API Key を隠す",
    "providers.apikey.watermark": "API Key を入力してください",
    "providers.apikey.configured": "API Key 設定済み",
    "providers.apikey.notGenerated": "未生成",

    # gateway view
    "gateway.endpoint.header": "Endpoint",
    "gateway.endpoint.header.hint": "モデルの組み合わせとフェイルオーバー順序を独立して管理",
    "gateway.endpoint.copyUrl.tooltip": "Base URL をコピー",
    "gateway.endpoint.rotateKey.tooltip": "API Key を再生成",
    "gateway.endpoint.copyKey.tooltip": "API Key をコピー",
    "gateway.endpoint.comboCount": "公開された Combo {0} 件",
    "gateway.endpoint.comboCount.prefix": "公開済み ",
    "gateway.endpoint.comboCount.suffix": " 件の Combo",
    "gateway.combo.header": "グローバル Combo",
    "gateway.combo.header.hint": "Endpoint 間で共有し、メンバー順にフェイルオーバー",
    "gateway.combo.add.tooltip": "Combo モデルを追加",
    "gateway.combo.expand.tooltip": "Combo を展開または折りたたむ",
    "gateway.combo.toggle.tooltip": "Combo を有効または無効にする",
    "gateway.combo.remove.tooltip": "Combo モデルを削除",
    "gateway.route.sort.drag.tooltip": "ドラッグしてフェイルオーバー順序を調整",
    "gateway.route.sort.toggle.tooltip": "メンバーを有効または無効にする",
    "gateway.route.sort.remove.tooltip": "Combo からモデルを削除",
    "gateway.route.sort.dragging": "ドラッグ中",
    "gateway.combo.memberOrder.hint": "メンバーモデルは順番にフェイルオーバー",
    "gateway.combo.addModel.tooltip": "Combo にモデルを追加",
    "gateway.combo.search.watermark": "モデルを検索...",

    # gateway VM
    "gateway.sort.ascending.tooltip": "アルファベット昇順でソート",
    "gateway.sort.descending.tooltip": "アルファベット降順でソート",
    "gateway.url.copied": "アドレスをコピーしました",
    "gateway.apikey.copied": "API Key をコピーしました",
    "gateway.status.loaded": "{0} 件の Provider、{1} 件のモデル、{2} 件の Endpoint、{3} 件の Combo を読み込みました",
    "gateway.status.load.failure": "ゲートウェイの読み込みに失敗しました：{0}",
    "gateway.combo.new.name": "新しい Combo",
    "gateway.combo.new.name.dup": "新しい Combo {0}",
    "gateway.combo.add.success": "グローバル Combo を追加しました",
    "gateway.combo.add.failure": "Combo モデルの追加に失敗しました：{0}",
    "gateway.combo.save.success": "Combo モデルを保存しました",
    "gateway.combo.save.failure": "Combo モデルの保存に失敗しました：{0}",
    "gateway.combo.remove.success": "グローバル Combo を削除しました",
    "gateway.combo.remove.failure": "Combo モデルの削除に失敗しました：{0}",
    "gateway.endpoint.comboScope.saved": "{0} の Combo 公開範囲を保存しました",
    "gateway.endpoint.comboScope.failure": "Endpoint Combo の更新に失敗しました：{0}",
    "gateway.endpoint.comboScope.failure.toast": "Endpoint Combo の更新に失敗しました",
    "gateway.route.reorder.saved": "フェイルオーバー順序を保存しました",
    "gateway.route.reorder.failure": "ソート保存に失敗しました：{0}",
    "gateway.model.available.empty": "追加可能な有効化モデルがありません。まず Provider でモデルを有効化してください",
    "gateway.model.available.loaded": "{0} 件の追加可能モデルを読み込みました",
    "gateway.model.available.load.failure": "追加可能モデルの読み込みに失敗しました：{0}",
    "gateway.model.combo.add.success": "モデルを Combo に追加しました",
    "gateway.model.combo.add.failure": "Combo への追加に失敗しました：{0}",
    "gateway.model.combo.remove.success": "モデルを Combo から削除しました",
    "gateway.model.combo.remove.failure": "メンバーの削除に失敗しました：{0}",
    "gateway.route.member.save.success": "メンバー状態を保存しました",
    "gateway.route.member.save.failure": "メンバーの保存に失敗しました：{0}",
    "gateway.endpoint.toggle.success": "{0} は{1}しました",
    "gateway.endpoint.toggle.failure": "Endpoint の更新に失敗しました：{0}",
    "gateway.endpoint.enable": "有効",
    "gateway.endpoint.disable": "無効",
    "gateway.endpoint.apikey.regenerated": "{0} の API Key を再生成しました",
    "gateway.endpoint.apikey.regenerated.toast": "API Key を再生成しました",
    "gateway.endpoint.apikey.regenerate.failure": "API Key 再生成に失敗しました：{0}",
    "gateway.endpoint.apikey.regenerate.failure.toast": "API Key 再生成に失敗しました",
    "gateway.endpoint.reasoning.saved": "{0} の Reasoning effort を保存しました",
    "gateway.endpoint.reasoning.saved.toast": "Reasoning effort を保存しました",
    "gateway.endpoint.reasoning.failure": "Reasoning effort 保存に失敗しました：{0}",
    "gateway.endpoint.reasoning.failure.toast": "Reasoning effort 保存に失敗しました",
    "gateway.combo.summary.empty": "Combo 未公開",
    "gateway.combo.summary.separator": "、",
    "gateway.combo.disabled": "グローバル無効",

    # gateway derived state
    "gateway.endpoint.noRoutes": "ルートなし",
    "gateway.endpoint.hasRequests": "リクエストあり",
    "gateway.endpoint.ready": "準備完了",
    "gateway.endpoint.active": "アクティブ",
    "gateway.endpoint.standby": "待機中",
    "gateway.request.success": "成功",
    "gateway.request.failure": "失敗",
    "providers.connection.status.pending": "接続未テスト",

    # placeholder
    "placeholder.empty.body": "このページはナビゲーションに接続済みです。業務機能は今後のイテレーションで段階的に実装されます。",
}


def parse_resx(path):
    """Parse a .resx file into an ordered list of (name, value, xml_space) tuples."""
    tree = ET.parse(path)
    root = root = tree.getroot()
    entries = []
    for data in root.findall("data"):
        name = data.get("name")
        value_el = data.find("value")
        value = value_el.text if value_el is not None else ""
        xml_space = data.get("{http://www.w3.org/XML/1998/namespace}space")
        entries.append((name, value, xml_space))
    return entries


def xml_escape(value):
    """Minimal XML escaping for text content."""
    return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def write_resx(path, entries, header_comment):
    """Write a minimal .resx file from (name, value, xml_space) entries."""
    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<root>',
        '  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>',
        '  <resheader name="version"><value>2.0</value></resheader>',
        '  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>',
        '  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>',
    ]
    if header_comment:
        lines.append(f'  <!-- {header_comment} -->')
    for name, value, xml_space in entries:
        space_attr = ' xml:space="preserve"' if xml_space == "preserve" else ""
        lines.append(f'  <data name="{name}"{space_attr}>')
        lines.append(f'    <value>{xml_escape(value)}</value>')
        lines.append('  </data>')
    lines.append('</root>')
    lines.append('')
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


def main():
    # Parse base resx
    entries = parse_resx(BASE)
    print(f"Parsed {len(entries)} entries from {BASE}")

    # ── zh-TW: opencc Simplified → Traditional ──
    s2t = opencc.OpenCC("s2t")
    zh_tw_entries = []
    for name, value, xml_space in entries:
        translated = s2t.convert(value)
        zh_tw_entries.append((name, translated, xml_space))
    zh_tw_path = os.path.join(RESOURCES, "Strings.zh-TW.resx")
    write_resx(zh_tw_path, zh_tw_entries, "Traditional Chinese (zh-TW)")
    print(f"Wrote {zh_tw_path}")

    # ── ja-JP: hand-written translations ──
    ja_entries = []
    missing = []
    for name, value, xml_space in entries:
        if name in JA:
            ja_entries.append((name, JA[name], xml_space))
        else:
            missing.append(name)
            ja_entries.append((name, value, xml_space))
    if missing:
        print(f"WARNING: {len(missing)} keys missing from JA dictionary:")
        for m in missing:
            print(f"  - {m}")
        sys.exit(1)
    ja_path = os.path.join(RESOURCES, "Strings.ja-JP.resx")
    write_resx(ja_path, ja_entries, "Japanese (ja-JP)")
    print(f"Wrote {ja_path}")
    print("Done.")


if __name__ == "__main__":
    main()
