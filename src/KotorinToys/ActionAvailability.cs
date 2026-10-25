namespace KotorinToys;

internal sealed record ActionAvailability(bool Enabled, string Reason)
{
    public static ActionAvailability Allow(string text) => new(true, text);
    public static ActionAvailability Deny(string text) => new(false, text);
}

// 表示と実行直前で同じ条件を使う。開発配置の起動と正式導入の管理は区別する。
internal static class ToolActionPolicy
{
    internal static bool Transition(ToolState state) => state.State is "確認中" or "起動処理中" or "停止処理中" or "処理中";
    public static ActionAvailability Evaluate(string operation, ToolDefinition tool, InstallationState installed,
        ToolState execution, PublicRelease? release = null, bool executableExists = false, bool documentExists = false,
        bool uninstallerExists = false, bool busy = false, bool checkingReleases = false)
    {
        if (busy) return ActionAvailability.Deny("処理中です。完了するまでお待ちください。");
        bool Capability(string name) => tool.Capabilities?.Contains(name) == true;
        if (operation == "manual") return documentExists ? ActionAvailability.Allow("選択したアプリの使い方を開きます。") : ActionAvailability.Deny("使い方のファイルがありません。GitHubの説明を確認してください。");
        if (operation == "start")
        {
            if (Transition(execution)) return ActionAvailability.Deny("状態を確認中、または起動・停止の処理中です。");
            if (execution.State is not ("実行中" or "停止中")) return ActionAvailability.Deny("実行状態を確認できません。「状態を更新」で再確認してください。");
            if (tool.Msix != null && installed.State != "導入済み") return ActionAvailability.Deny("署名付きMSIXの正式導入・対応版を確認してください。");
            if (!executableExists) return ActionAvailability.Deny("実行ファイルが見つかりません。先にインストール、または修復してください。");
            if (execution.State == "実行中" ? !Capability("show") : !Capability("silent-start") && !Capability("start")) return ActionAvailability.Deny("このアプリは本体からの起動・画面表示に対応していません。");
            return ActionAvailability.Allow(execution.State == "実行中" ? "起動済みのアプリの設定画面を開きます。" : "アプリを起動します。");
        }
        if (operation == "stop")
        {
            if (execution.State == "停止中") return ActionAvailability.Deny("すでに停止しています。");
            if (execution.State != "実行中") return ActionAvailability.Deny("正常に実行中と確認できる場合だけ停止できます。状態を確認してください。");
            if (!executableExists || !Capability("stop")) return ActionAvailability.Deny("実行ファイルまたは正常停止への対応を確認できません。");
            return ActionAvailability.Allow("アプリに正常終了を依頼します。作業中は終了を拒否することがあります。");
        }
        if (Transition(execution) || execution.State != "実行中" && execution.State != "停止中")
            return ActionAvailability.Deny("アプリの起動・停止または状態確認が完了してから操作してください。");
        if (operation == "ffmpeg") return installed.State != "導入済み" ? ActionAvailability.Deny("先にSSCaptureを正式インストールしてください。") :
            !executableExists ? ActionAvailability.Deny("SSCaptureの実行ファイルが見つかりません。修復してください。") :
            execution.State != "停止中" ? ActionAvailability.Deny("先にSSCaptureを停止してください。") : ActionAvailability.Allow("SSCaptureの録画用FFmpegを導入・修復します。");
        if (operation == "uninstall")
            return ToolInstallationRunner.CanUninstall(tool, installed) && (tool.Msix != null || uninstallerExists)
                ? ActionAvailability.Allow("設定・履歴を保持してアンインストールします。")
                : ActionAvailability.Deny("正式導入または削除プログラムを確認できません。");
        if (operation is not ("install" or "update" or "repair")) return ActionAvailability.Deny("未対応の操作です。");
        if (checkingReleases) return ActionAvailability.Deny("公開版を確認しています。完了するまでお待ちください。");
        var candidate = release is { Confirmed: true, Package: not null } ? new ReleaseCatalog(1, [release.Package]) : null;
        if (ToolInstallationRunner.PublicReason(tool, candidate, release) is { } reason) return ActionAvailability.Deny(reason);
        if (operation == "repair")
        {
            if (tool.Msix != null || !installed.Repairable || installed.State != "修復が必要" || string.IsNullOrEmpty(installed.Directory))
                return ActionAvailability.Deny("登録先・製品定義を照合できるEXE欠落だけ修復できます。MSIXの破損はWindowsのアプリ設定で確認してください。");
            if (execution.State != "停止中") return ActionAvailability.Deny("修復の前にアプリを正常終了してください。");
            if (!Version.TryParse(installed.Version, out var current) || !Version.TryParse(release?.Package?.Package.Version, out var published) || published < current)
                return ActionAvailability.Deny("同じ版以上の照合済み公開版が必要です。古い版へ戻す修復は行いません。");
            return ActionAvailability.Allow("登録済みの導入先へ再導入し、設定・履歴を保持して実行ファイルを修復します。");
        }
        if (operation == "install") return installed.State == "未導入" && execution.State == "停止中"
            ? ActionAvailability.Allow("GitHub Releasesからインストールします。")
            : ActionAvailability.Deny(installed.State == "未導入" ? "実行中の開発版を先に停止してください。" : "導入済み、または導入状態を確認できません。更新・修復を確認してください。");
        if (!executableExists) return ActionAvailability.Deny("導入先の実行ファイルが見つかりません。状態を更新し、修復を確認してください。");
        var view = ToolVersionView.Create(installed, release!);
        if (!view.CanUpdate) return ActionAvailability.Deny(installed.State is "導入済み" or "更新が必要" ? "新しい公開版が確認できません。同じ版や古い版へは更新しません。" : "正式導入済みのアプリだけアップデートできます。");
        return ActionAvailability.Allow("新しい公開版へアップデートします。設定・履歴は保持します。");
    }
}

internal static class ActionHints
{
    public static void Apply(Button button, ActionAvailability availability, ToolTip tips)
    {
        button.Enabled = availability.Enabled; button.AccessibleDescription = availability.Reason;
        tips.SetToolTip(button, availability.Reason);
    }
    // 標準ToolTipが表示しない無効ボタンも、親のマウス移動で理由を案内する。
    public static void Attach(Control parent, ToolTip tips)
    {
        Control? last = null;
        Control? Find(Control container, Point point)
        {
            foreach (Control child in container.Controls)
            {
                if (!child.Visible || !child.Bounds.Contains(point)) continue;
                var nested = Find(child, new(point.X - child.Left, point.Y - child.Top));
                if (nested != null) return nested;
                if (!child.Enabled && !string.IsNullOrEmpty(child.AccessibleDescription)) return child;
            }
            return null;
        }
        parent.MouseMove += (_, e) =>
        {
            var target = Find(parent, e.Location);
            if (target == last) return; last = target; tips.Hide(parent);
            if (!string.IsNullOrEmpty(target?.AccessibleDescription)) tips.Show(target.AccessibleDescription, parent, e.X + 12, e.Y + 18, 8000);
        };
        parent.MouseLeave += (_, _) => { last = null; tips.Hide(parent); };
    }
}
